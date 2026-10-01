using System.Security.Cryptography;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Core.Ai;

/// <summary>Where the Anthropic key in use came from, for the settings screen. Never the key.</summary>
public sealed class ApiKeyStatus
{
    /// <summary>"app" (entered by an admin), "configuration" (App Service setting / .env), or "none".</summary>
    public string Source { get; set; } = "none";

    public string? LastFour { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// A key is stored but cannot be decrypted — the Data Protection key ring that encrypted it is
    /// gone (for example, the App Service was recreated). The app falls back to configuration; the
    /// admin should enter the key again.
    /// </summary>
    public bool StoredKeyUnreadable { get; set; }

    /// <summary>Whether configuration also holds a key, i.e. what clearing the app key falls back to.</summary>
    public bool ConfigurationHasKey { get; set; }
}

/// <summary>The key the model client should use right now.</summary>
public interface IApiKeySource
{
    string? Current { get; }

    /// <summary>Drop the cached key. Called after an admin changes it.</summary>
    void Invalidate();
}

/// <summary>Checks a key with Anthropic before it is saved.</summary>
public interface IApiKeyVerifier
{
    /// <summary>Null when the key works; otherwise a user-facing reason.</summary>
    Task<string?> VerifyAsync(string key, CancellationToken ct = default);
}

internal static class ApiKeyProtection
{
    /// <summary>Data Protection purpose. Versioned so a future format change cannot misread old values.</summary>
    public const string Purpose = "JDWriter.AppSecrets.v1";

    public static string? Configured(IConfiguration config)
    {
        var fromConfig = config["ANTHROPIC_API_KEY"];
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig.Trim();
        }

        // The SDK accepts the ambient variable too; a developer who exported it is configured.
        var fromEnv = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv.Trim();
    }
}

/// <summary>
/// Resolves the key: one entered in the app wins; otherwise the configured one (the template's
/// standard path — a GitHub Environment secret applied as an App Service setting).
///
/// Singleton, because the model client is. The resolved key is cached briefly and dropped
/// explicitly when an admin changes it, so a rotation takes effect immediately on this instance.
/// </summary>
public sealed class ApiKeySource : IApiKeySource
{
    private const string CacheKey = "jdw:anthropic-key";

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly IDataProtector _protector;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ApiKeySource> _logger;

    public ApiKeySource(
        IServiceScopeFactory scopes,
        IConfiguration config,
        IDataProtectionProvider dataProtection,
        IMemoryCache cache,
        ILogger<ApiKeySource> logger)
    {
        _scopes = scopes;
        _config = config;
        _protector = dataProtection.CreateProtector(ApiKeyProtection.Purpose);
        _cache = cache;
        _logger = logger;
    }

    public string? Current => _cache.GetOrCreate(CacheKey, entry =>
    {
        // A backstop for a change made by another instance; this instance's own changes
        // invalidate explicitly.
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        return Load();
    });

    public void Invalidate() => _cache.Remove(CacheKey);

    private string? Load()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = db.AppSecrets.AsNoTracking()
                .FirstOrDefault(s => s.Name == AppSecretNames.AnthropicApiKey);
            if (stored != null)
            {
                try
                {
                    return _protector.Unprotect(stored.ProtectedValue);
                }
                catch (CryptographicException)
                {
                    _logger.LogWarning(
                        "The Anthropic key entered in the app cannot be decrypted (the Data Protection "
                        + "key ring changed). Falling back to configuration; re-enter it in Settings.");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No database yet (first boot, migrations pending) must not take model features down
            // when a key is configured.
            _logger.LogWarning(ex, "Could not read the app-entered Anthropic key; using configuration.");
        }

        return ApiKeyProtection.Configured(_config);
    }
}

/// <summary>Verifies a key with the free token-counting endpoint, so checking costs nothing.</summary>
public sealed class AnthropicKeyVerifier : IApiKeyVerifier
{
    public async Task<string?> VerifyAsync(string key, CancellationToken ct = default)
    {
        var client = new AnthropicClient { ApiKey = key };
        try
        {
            await client.Messages.CountTokens(new MessageCountTokensParams
            {
                Model = StructuredLlm.Model,
                Messages = [new() { Role = Role.User, Content = "ping" }],
            }, cancellationToken: ct);
            return null;
        }
        catch (AnthropicUnauthorizedException)
        {
            return "Anthropic rejected this key. Check that it was copied completely and has not been revoked.";
        }
        catch (AnthropicForbiddenException)
        {
            return "Anthropic accepted the key but it is not permitted to use the API. Check its workspace and permissions.";
        }
    }
}

/// <summary>The settings-screen operations on the app-entered key.</summary>
public sealed class ApiKeySettings
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IDataProtector _protector;
    private readonly IApiKeySource _source;
    private readonly IApiKeyVerifier _verifier;

    public ApiKeySettings(
        AppDbContext db,
        IConfiguration config,
        IDataProtectionProvider dataProtection,
        IApiKeySource source,
        IApiKeyVerifier verifier)
    {
        _db = db;
        _config = config;
        _protector = dataProtection.CreateProtector(ApiKeyProtection.Purpose);
        _source = source;
        _verifier = verifier;
    }

    public async Task<ApiKeyStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var configured = ApiKeyProtection.Configured(_config);
        var stored = await _db.AppSecrets.AsNoTracking()
            .Where(s => s.Name == AppSecretNames.AnthropicApiKey)
            .Select(s => new
            {
                s.ProtectedValue,
                s.LastFour,
                s.UpdatedAt,
                UpdatedBy = s.UpdatedBy != null ? (s.UpdatedBy.DisplayName ?? s.UpdatedBy.LoginId) : null,
            })
            .FirstOrDefaultAsync(ct);

        var status = new ApiKeyStatus { ConfigurationHasKey = configured != null };
        if (stored != null)
        {
            if (CanDecrypt(stored.ProtectedValue))
            {
                status.Source = "app";
                status.LastFour = stored.LastFour;
                status.UpdatedBy = stored.UpdatedBy;
                status.UpdatedAt = stored.UpdatedAt;
                return status;
            }

            status.StoredKeyUnreadable = true;
        }

        if (configured != null)
        {
            status.Source = "configuration";
            status.LastFour = LastFour(configured);
        }

        return status;
    }

    /// <summary>
    /// Verify, encrypt and store a key. Throws <see cref="ArgumentException"/> with a user-facing
    /// message when the key is malformed or Anthropic rejects it — nothing is stored in that case.
    /// </summary>
    public async Task SetAsync(string key, int? updatedByUserId, CancellationToken ct = default)
    {
        key = (key ?? "").Trim();
        if (key.Length < 20 || key.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("That doesn’t look like an Anthropic API key.");
        }

        var problem = await _verifier.VerifyAsync(key, ct);
        if (problem != null)
        {
            throw new ArgumentException(problem);
        }

        var secret = await _db.AppSecrets.FirstOrDefaultAsync(s => s.Name == AppSecretNames.AnthropicApiKey, ct);
        if (secret == null)
        {
            secret = new AppSecret { Name = AppSecretNames.AnthropicApiKey };
            _db.AppSecrets.Add(secret);
        }

        secret.ProtectedValue = _protector.Protect(key);
        secret.LastFour = LastFour(key);
        secret.UpdatedByUserId = updatedByUserId;
        secret.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        _source.Invalidate();
    }

    /// <summary>Remove the app-entered key; the configured one, if any, takes over.</summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        var secret = await _db.AppSecrets.FirstOrDefaultAsync(s => s.Name == AppSecretNames.AnthropicApiKey, ct);
        if (secret != null)
        {
            _db.AppSecrets.Remove(secret);
            await _db.SaveChangesAsync(ct);
        }

        _source.Invalidate();
    }

    private bool CanDecrypt(string protectedValue)
    {
        try
        {
            _protector.Unprotect(protectedValue);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string LastFour(string key) => key.Length <= 4 ? key : key[^4..];
}

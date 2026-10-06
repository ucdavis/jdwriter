using System.Net;
using System.Net.Http.Headers;
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

/// <summary>
/// Which AI provider is active and where its key came from, for the settings screen. Never the key.
/// </summary>
public sealed class ApiKeyStatus
{
    /// <summary>The active provider (anthropic, azureOpenAi, openAiCompatible) — set by configuration.</summary>
    public LlmProvider Provider { get; set; }

    /// <summary>Model id or deployment name in use.</summary>
    public string Model { get; set; } = "";

    /// <summary>Endpoint for Azure OpenAI or an OpenAI-compatible server; empty for Anthropic.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>"app" (entered by an admin), "configuration" (App Service setting / Key Vault / .env), or "none".</summary>
    public string Source { get; set; } = "none";

    public string? LastFour { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// A key is stored but cannot be decrypted — the Data Protection key ring that encrypted it is
    /// gone (for example, the App Service was recreated). The app falls back to configuration.
    /// </summary>
    public bool StoredKeyUnreadable { get; set; }

    /// <summary>Whether configuration also holds a key, i.e. what clearing the app key falls back to.</summary>
    public bool ConfigurationHasKey { get; set; }

    /// <summary>False for a local OpenAI-compatible server that needs no key.</summary>
    public bool KeyRequired { get; set; } = true;

    /// <summary>Whether this environment lets admins enter keys in the app (Llm:AllowKeyEntryInApp).</summary>
    public bool KeyEntryAllowed { get; set; } = true;

    /// <summary>Recent key changes, newest first — who and when, never the value.</summary>
    public List<KeyAuditEntry> Audit { get; set; } = [];
}

public sealed class KeyAuditEntry
{
    public string Action { get; set; } = "";
    public string? LastFour { get; set; }
    public string? By { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>The key the model client should use right now, for the active provider.</summary>
public interface IApiKeySource
{
    string? Current { get; }

    /// <summary>Drop the cached key. Called after an admin changes it.</summary>
    void Invalidate();
}

/// <summary>Checks a key with the active provider before it is saved.</summary>
public interface IApiKeyVerifier
{
    /// <summary>Null when the key works; otherwise a user-facing reason.</summary>
    Task<string?> VerifyAsync(string key, CancellationToken ct = default);
}

internal static class ApiKeyProtection
{
    /// <summary>Data Protection purpose. Versioned so a future format change cannot misread old values.</summary>
    public const string Purpose = "JDWriter.AppSecrets.v1";

    /// <summary>
    /// The configured key for the active provider. In Azure this setting is meant to be a Key Vault
    /// reference (@Microsoft.KeyVault(...)), which App Service resolves before the app sees it.
    /// </summary>
    public static string? Configured(IConfiguration config, LlmOptions options)
    {
        var fromConfig = config[options.KeySetting];
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig.Trim();
        }

        // The SDKs accept the ambient variable too; a developer who exported it is configured.
        var fromEnv = Environment.GetEnvironmentVariable(options.KeySetting);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv.Trim();
    }
}

/// <summary>
/// Resolves the active provider's key: one entered in the app (when this environment allows it)
/// wins; otherwise the configured one. Singleton, because the model clients are; cached briefly
/// and dropped explicitly when an admin changes it, so a rotation applies immediately.
/// </summary>
public sealed class ApiKeySource : IApiKeySource
{
    private const string CacheKey = "jdw:llm-key";

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly LlmOptions _options;
    private readonly IDataProtector _protector;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ApiKeySource> _logger;

    public ApiKeySource(
        IServiceScopeFactory scopes,
        IConfiguration config,
        LlmOptions options,
        IDataProtectionProvider dataProtection,
        IMemoryCache cache,
        ILogger<ApiKeySource> logger)
    {
        _scopes = scopes;
        _config = config;
        _options = options;
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
        if (_options.AllowKeyEntryInApp)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var name = AppSecretNames.ApiKeyFor(_options.Provider);
                var stored = db.AppSecrets.AsNoTracking().FirstOrDefault(s => s.Name == name);
                if (stored != null)
                {
                    try
                    {
                        return _protector.Unprotect(stored.ProtectedValue);
                    }
                    catch (CryptographicException)
                    {
                        _logger.LogWarning(
                            "The {Provider} key entered in the app cannot be decrypted (the Data Protection "
                            + "key ring changed). Falling back to configuration; re-enter it in Settings.",
                            _options.Provider);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // No database yet (first boot, migrations pending) must not take model features down
                // when a key is configured.
                _logger.LogWarning(ex, "Could not read the app-entered AI key; using configuration.");
            }
        }

        return ApiKeyProtection.Configured(_config, _options);
    }
}

/// <summary>Verifies an Anthropic key with the free token-counting endpoint, so checking costs nothing.</summary>
public sealed class AnthropicKeyVerifier : IApiKeyVerifier
{
    private readonly LlmOptions? _options;

    public AnthropicKeyVerifier(LlmOptions? options = null)
    {
        _options = options;
    }

    public async Task<string?> VerifyAsync(string key, CancellationToken ct = default)
    {
        var client = new AnthropicClient { ApiKey = key };
        try
        {
            await client.Messages.CountTokens(new MessageCountTokensParams
            {
                Model = _options?.EffectiveModel is { Length: > 0 } m ? m : StructuredLlm.Model,
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

/// <summary>
/// Verifies an Azure OpenAI or OpenAI-compatible key by listing models — a read that costs
/// nothing and fails fast on a bad key.
/// </summary>
public sealed class OpenAiKeyVerifier : IApiKeyVerifier
{
    private readonly IHttpClientFactory _http;
    private readonly LlmOptions _options;

    public OpenAiKeyVerifier(IHttpClientFactory http, LlmOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<string?> VerifyAsync(string key, CancellationToken ct = default)
    {
        if (_options.Endpoint.Length == 0)
        {
            return "No endpoint is configured for this provider (Llm:Endpoint).";
        }

        var url = _options.Provider == LlmProvider.AzureOpenAi
            ? $"{_options.Endpoint}/openai/models?api-version={Uri.EscapeDataString(_options.ApiVersion)}"
            : $"{_options.Endpoint}/models";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (_options.Provider == LlmProvider.AzureOpenAi)
        {
            request.Headers.Add("api-key", key);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.CreateClient(OpenAiStructuredLlm.HttpClientName).SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            return "Could not reach the AI provider to check the key. Check Llm:Endpoint and the network.";
        }

        using (response)
        {
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    "The provider rejected this key. Check that it was copied completely and is for this resource.",
                _ when response.IsSuccessStatusCode => null,
                var status => $"The provider could not confirm the key ({(int)status}). Nothing was saved.",
            };
        }
    }
}

/// <summary>The settings-screen operations on the app-entered key for the active provider.</summary>
public sealed class ApiKeySettings
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly LlmOptions _options;
    private readonly IDataProtector _protector;
    private readonly IApiKeySource _source;
    private readonly IApiKeyVerifier _verifier;

    public ApiKeySettings(
        AppDbContext db,
        IConfiguration config,
        LlmOptions options,
        IDataProtectionProvider dataProtection,
        IApiKeySource source,
        IApiKeyVerifier verifier)
    {
        _db = db;
        _config = config;
        _options = options;
        _protector = dataProtection.CreateProtector(ApiKeyProtection.Purpose);
        _source = source;
        _verifier = verifier;
    }

    private string SecretName => AppSecretNames.ApiKeyFor(_options.Provider);

    public async Task<ApiKeyStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var configured = ApiKeyProtection.Configured(_config, _options);
        var status = new ApiKeyStatus
        {
            Provider = _options.Provider,
            Model = _options.EffectiveModel,
            Endpoint = _options.Endpoint,
            ConfigurationHasKey = configured != null,
            KeyRequired = _options.KeyRequired,
            KeyEntryAllowed = _options.AllowKeyEntryInApp,
            Audit = await _db.AppSecretAudits.AsNoTracking()
                .Where(a => a.SecretName == SecretName)
                .OrderByDescending(a => a.At)
                .Take(10)
                .Select(a => new KeyAuditEntry
                {
                    Action = a.Action,
                    LastFour = a.LastFour,
                    By = a.User != null ? (a.User.DisplayName ?? a.User.LoginId) : null,
                    At = a.At,
                })
                .ToListAsync(ct),
        };

        if (_options.AllowKeyEntryInApp)
        {
            var stored = await _db.AppSecrets.AsNoTracking()
                .Where(s => s.Name == SecretName)
                .Select(s => new
                {
                    s.ProtectedValue,
                    s.LastFour,
                    s.UpdatedAt,
                    UpdatedBy = s.UpdatedBy != null ? (s.UpdatedBy.DisplayName ?? s.UpdatedBy.LoginId) : null,
                })
                .FirstOrDefaultAsync(ct);
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
        }

        if (configured != null)
        {
            status.Source = "configuration";
            status.LastFour = LastFour(configured);
        }

        return status;
    }

    /// <summary>
    /// Verify, encrypt, store and audit a key. Throws <see cref="ArgumentException"/> with a
    /// user-facing message when the key is malformed or rejected — nothing is stored — and
    /// <see cref="InvalidOperationException"/> when this environment does not allow in-app entry.
    /// </summary>
    public async Task SetAsync(string key, int? updatedByUserId, CancellationToken ct = default)
    {
        if (!_options.AllowKeyEntryInApp)
        {
            throw new InvalidOperationException(
                "Keys cannot be entered in the app in this environment; they are managed in Key Vault.");
        }

        key = (key ?? "").Trim();
        if (key.Length < 20 || key.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("That doesn’t look like an API key.");
        }

        var problem = await _verifier.VerifyAsync(key, ct);
        if (problem != null)
        {
            throw new ArgumentException(problem);
        }

        var secret = await _db.AppSecrets.FirstOrDefaultAsync(s => s.Name == SecretName, ct);
        if (secret == null)
        {
            secret = new AppSecret { Name = SecretName };
            _db.AppSecrets.Add(secret);
        }

        var now = DateTimeOffset.UtcNow;
        secret.ProtectedValue = _protector.Protect(key);
        secret.LastFour = LastFour(key);
        secret.UpdatedByUserId = updatedByUserId;
        secret.UpdatedAt = now;
        _db.AppSecretAudits.Add(new AppSecretAudit
        {
            SecretName = SecretName, Action = "set", LastFour = secret.LastFour, UserId = updatedByUserId, At = now,
        });
        await _db.SaveChangesAsync(ct);
        _source.Invalidate();
    }

    /// <summary>Remove the app-entered key (audited); the configured key, if any, takes over.</summary>
    public async Task ClearAsync(int? userId = null, CancellationToken ct = default)
    {
        var secret = await _db.AppSecrets.FirstOrDefaultAsync(s => s.Name == SecretName, ct);
        if (secret != null)
        {
            _db.AppSecrets.Remove(secret);
            _db.AppSecretAudits.Add(new AppSecretAudit
            {
                SecretName = SecretName, Action = "cleared", UserId = userId, At = DateTimeOffset.UtcNow,
            });
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

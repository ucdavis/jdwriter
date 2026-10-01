using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core.Ai;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Tests.Ai;

/// <summary>
/// The Anthropic key an admin enters in Settings: stored encrypted and write-only, preferred over
/// configuration while set, and never able to break the app when it goes bad.
/// </summary>
public class ApiKeyTests
{
    private const string GoodKey = "sk-ant-api03-ABCDEFGHIJKLMNOPQRSTUVWX-wxyz";
    private const string ConfigKey = "sk-ant-api03-CONFIGURED-KEY-0000000000-cfg1";

    private sealed class FakeVerifier : IApiKeyVerifier
    {
        public string? Problem { get; init; }
        public List<string> Checked { get; } = [];

        public Task<string?> VerifyAsync(string key, CancellationToken ct = default)
        {
            Checked.Add(key);
            return Task.FromResult(Problem);
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _provider;

        public Harness(string? configuredKey, IDataProtectionProvider? protection = null, FakeVerifier? verifier = null, string? dbName = null)
        {
            dbName ??= $"keys_{Guid.NewGuid():N}";
            Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ANTHROPIC_API_KEY"] = configuredKey,
            }).Build();
            Protection = protection ?? new EphemeralDataProtectionProvider();
            Verifier = verifier ?? new FakeVerifier();

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
            _provider = services.BuildServiceProvider();

            Db = _provider.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
            Source = new ApiKeySource(
                _provider.GetRequiredService<IServiceScopeFactory>(), Config, Protection,
                new MemoryCache(new MemoryCacheOptions()), NullLogger<ApiKeySource>.Instance);
            Settings = new ApiKeySettings(Db, Config, Protection, Source, Verifier);
        }

        public IConfiguration Config { get; }
        public IDataProtectionProvider Protection { get; }
        public FakeVerifier Verifier { get; }
        public AppDbContext Db { get; }
        public ApiKeySource Source { get; }
        public ApiKeySettings Settings { get; }

        public void Dispose() => _provider.Dispose();
    }

    [Fact]
    public async Task A_saved_key_is_stored_encrypted_and_reported_by_its_last_four_only()
    {
        using var h = new Harness(configuredKey: null);

        await h.Settings.SetAsync(GoodKey, updatedByUserId: null);

        var row = await h.Db.AppSecrets.SingleAsync();
        row.ProtectedValue.Should().NotContain(GoodKey).And.NotContain("ABCDEFGHIJ");
        row.LastFour.Should().Be("wxyz");

        var status = await h.Settings.GetStatusAsync();
        status.Source.Should().Be("app");
        status.LastFour.Should().Be("wxyz");
        JsonSerializer.Serialize(status).Should().NotContain(GoodKey, "the status is all a browser ever receives");
    }

    [Fact]
    public async Task The_app_key_wins_over_configuration_and_clearing_falls_back()
    {
        using var h = new Harness(ConfigKey);
        h.Source.Current.Should().Be(ConfigKey);

        await h.Settings.SetAsync(GoodKey, null);
        h.Source.Current.Should().Be(GoodKey, "a rotation takes effect immediately, not after the cache expires");

        await h.Settings.ClearAsync();
        h.Source.Current.Should().Be(ConfigKey);
        (await h.Settings.GetStatusAsync()).Source.Should().Be("configuration");
    }

    [Fact]
    public async Task A_key_anthropic_rejects_is_not_stored()
    {
        using var h = new Harness(ConfigKey, verifier: new FakeVerifier { Problem = "Anthropic rejected this key." });

        var set = () => h.Settings.SetAsync(GoodKey, null);

        await set.Should().ThrowAsync<ArgumentException>().WithMessage("Anthropic rejected this key.");
        (await h.Db.AppSecrets.CountAsync()).Should().Be(0);
        h.Source.Current.Should().Be(ConfigKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("sk-ant-api03 with spaces in the middle of it")]
    public async Task Something_that_is_not_a_key_is_refused_without_calling_anthropic(string input)
    {
        using var h = new Harness(null);

        var set = () => h.Settings.SetAsync(input, null);

        await set.Should().ThrowAsync<ArgumentException>();
        h.Verifier.Checked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_stored_key_that_can_no_longer_be_decrypted_degrades_to_configuration()
    {
        // Saved under one key ring, read under another: what happens if the App Service is
        // recreated and its Data Protection keys are lost.
        var dbName = $"keys_{Guid.NewGuid():N}";
        using (var before = new Harness(ConfigKey, dbName: dbName))
        {
            await before.Settings.SetAsync(GoodKey, null);
        }

        using var after = new Harness(ConfigKey, protection: new EphemeralDataProtectionProvider(), dbName: dbName);

        after.Source.Current.Should().Be(ConfigKey);
        var status = await after.Settings.GetStatusAsync();
        status.StoredKeyUnreadable.Should().BeTrue();
        status.Source.Should().Be("configuration");
    }

    [Fact]
    public async Task With_no_key_anywhere_the_status_says_so()
    {
        using var h = new Harness(null);

        var status = await h.Settings.GetStatusAsync();

        status.Source.Should().Be("none");
        status.ConfigurationHasKey.Should().BeFalse();
        h.Source.Current.Should().BeNull();
    }
}

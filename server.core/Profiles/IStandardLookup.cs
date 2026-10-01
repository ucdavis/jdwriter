using Server.Core.Standards;

namespace Server.Core.Profiles;

/// <summary>
/// Resolves the official UC job standard for a class title, or null when none exists.
///
/// Declared here as a narrow port rather than depending on the standards store directly: envelope
/// synthesis needs exactly one question answered, and the store that answers it is a separate
/// concern with its own ingest and caching. The DI wiring adapts one to the other.
///
/// Standards COMPLEMENT the corpus rather than replacing it. The corpus drives the
/// % time / function / duties, because reflecting what UC Davis JDs actually say is the point of
/// this system; the standard supplies authoritative KSAs, education, scope and fixed attributes.
/// </summary>
public interface IStandardLookup
{
    Task<ClassStandardRecord?> ForTitleAsync(string title, CancellationToken ct = default);
}

/// <summary>
/// Used when no standards store is wired, or when a caller deliberately wants corpus-only
/// synthesis. Returning null is a valid answer — most classes have no published standard.
/// </summary>
public sealed class NoStandardLookup : IStandardLookup
{
    public Task<ClassStandardRecord?> ForTitleAsync(string title, CancellationToken ct = default) =>
        Task.FromResult<ClassStandardRecord?>(null);
}

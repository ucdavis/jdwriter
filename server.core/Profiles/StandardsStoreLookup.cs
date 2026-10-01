using Server.Core.Standards;

namespace Server.Core.Profiles;

/// <summary>
/// Answers <see cref="IStandardLookup"/> from the real standards store.
///
/// This is a seam, not logic. Envelope synthesis needs exactly one question answered — "is there an
/// official standard for this class title?" — and declaring that as a narrow port kept synthesis
/// from depending on the store's ingest and caching concerns. This class is the wiring that makes
/// the port point at the real thing.
///
/// Without it the container resolves <see cref="NoStandardLookup"/>, which answers null to
/// everything. That is a legitimate configuration (corpus-only synthesis) and an easy accident:
/// every envelope would silently lose its authoritative KSAs, education and scope, and nothing
/// would fail — the envelopes would just quietly be worse. Register this explicitly.
/// </summary>
public sealed class StandardsStoreLookup : IStandardLookup
{
    private readonly IStandardsStore _store;

    public StandardsStoreLookup(IStandardsStore store)
    {
        _store = store;
    }

    public async Task<ClassStandardRecord?> ForTitleAsync(string title, CancellationToken ct = default)
    {
        var index = await _store.GetIndexAsync(ct);
        return index.ForTitle(title);
    }
}

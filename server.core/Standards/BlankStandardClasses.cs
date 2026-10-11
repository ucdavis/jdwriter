using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Core.Standards;

/// <summary>A class bootstrapped from a blank standard.</summary>
public sealed class BlankStandardClass
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string UcJobCode { get; set; } = "";

    /// <summary>Saved JDs written against it. A class with any is kept: removing it would orphan them.</summary>
    public int SavedJds { get; set; }
}

public sealed class BlankStandardCleanup
{
    public List<BlankStandardClass> Removed { get; set; } = [];

    /// <summary>Built from a blank standard, but kept because saved JDs hang off them.</summary>
    public List<BlankStandardClass> Kept { get; set; } = [];
}

/// <summary>
/// Starter classes bootstrapped from blank standards — a title and perhaps a scope, no
/// responsibilities — before blank standards were refused. Little more than a title went into their
/// envelopes, so they are not usable classes: they are removed, and the job waits for real JDs (or
/// the full standard). A class that has learned from JDs, been edited by hand, or has saved JDs
/// against it is never touched.
/// </summary>
public sealed class BlankStandardClasses
{
    private readonly AppDbContext _db;
    private readonly IStandardsStore _standards;

    public BlankStandardClasses(AppDbContext db, IStandardsStore standards)
    {
        _db = db;
        _standards = standards;
    }

    public async Task<List<BlankStandardClass>> PreviewAsync(CancellationToken ct = default)
    {
        var standards = await _standards.GetIndexAsync(ct);
        var starters = await _db.ClassProfiles.AsNoTracking()
            .Where(p => p.EnvelopeSource == EnvelopeSource.Standard && p.CorpusSize == 0)
            .Select(p => new { p.Id, p.Slug, p.Title, p.UcJobCode })
            .ToListAsync(ct);
        var saved = await _db.AuthoredJds.AsNoTracking()
            .GroupBy(a => a.ClassProfileId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return starters
            .Where(p => standards.For(p.Title, p.UcJobCode) is { IsBlank: true })
            .Select(p => new BlankStandardClass
            {
                Slug = p.Slug,
                Title = p.Title,
                UcJobCode = p.UcJobCode,
                SavedJds = saved.GetValueOrDefault(p.Id),
            })
            .OrderBy(p => p.Title, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Remove them, keeping any with saved JDs. Idempotent: a second run finds nothing.</summary>
    public async Task<BlankStandardCleanup> RemoveAsync(CancellationToken ct = default)
    {
        var found = await PreviewAsync(ct);
        var result = new BlankStandardCleanup
        {
            Removed = [.. found.Where(c => c.SavedJds == 0)],
            Kept = [.. found.Where(c => c.SavedJds > 0)],
        };

        var slugs = result.Removed.Select(c => c.Slug).ToHashSet(StringComparer.Ordinal);
        _db.ClassProfiles.RemoveRange(await _db.ClassProfiles.Where(p => slugs.Contains(p.Slug)).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
        return result;
    }
}

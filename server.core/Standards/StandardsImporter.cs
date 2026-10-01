using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Titles;

namespace Server.Core.Standards;

/// <summary>What an import produced, in the POC's report shape.</summary>
public sealed class StandardsImportReport
{
    public int Count { get; set; }
    public int Coded { get; set; }
    /// <summary>Uncoded because the title is not a UC Davis title at all.</summary>
    public List<string> Unknown { get; set; } = [];
    /// <summary>Uncoded because the title resolves to more than one code — needs a human.</summary>
    public List<string> Ambiguous { get; set; } = [];
    public List<string> Titles { get; set; } = [];
}

public sealed class StandardsImportResult
{
    /// <summary>Deduplicated standards, ordered by strict title key.</summary>
    public List<ClassStandardRecord> Store { get; set; } = [];
    public StandardsImportReport Report { get; set; } = new();
}

/// <summary>
/// Rebuilds the standards table from a directory of Job Builder workbooks. Ported from
/// <c>ingestStandards()</c> in the POC's src/lib/standards/store.ts.
///
/// Split in two so the part worth checking can be checked: <see cref="Build"/> is pure and is
/// held to the <c>standards.store.json</c> fixture; <see cref="ImportAsync"/> only persists it.
/// </summary>
public sealed class StandardsImporter
{
    private readonly AppDbContext _db;
    private readonly ITitleCodeService _titleCodes;
    private readonly IStandardsStore _standards;

    public StandardsImporter(AppDbContext db, ITitleCodeService titleCodes, IStandardsStore standards)
    {
        _db = db;
        _titleCodes = titleCodes;
        _standards = standards;
    }

    /// <summary>
    /// Parse, backfill and dedup, in exactly the reference's order: code from the title when the
    /// layout did not state one, then grade from the code, then last-wins on the strict key.
    /// </summary>
    public static StandardsImportResult Build(string directory, TitleCodeIndex titles)
    {
        // Ordinal, matching the reference's plain .sort(). Order matters: on a duplicate strict
        // key the LAST workbook wins.
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(directory, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var parsed = new List<ClassStandardRecord>();
        foreach (var file in files)
        {
            using var stream = File.OpenRead(Path.Combine(directory, file));
            parsed.AddRange(StandardsWorkbookParser.Parse(stream));
        }

        return Build(parsed, titles);
    }

    /// <summary>
    /// The dedup and backfill over already-parsed standards, in workbook order. Separate because
    /// two of its branches never fire on the real workbooks — every one states a grade, and their
    /// only duplicates are identical — so they can only be checked with constructed input.
    /// </summary>
    public static StandardsImportResult Build(IEnumerable<ClassStandardRecord> parsedInOrder, TitleCodeIndex titles)
    {
        var byKey = new Dictionary<string, ClassStandardRecord>(StringComparer.Ordinal);
        foreach (var std in parsedInOrder)
        {
            // Only the single-family layout states a job code; resolve it from the title in
            // both cases so every standard carries the one field that joins it to a profile.
            if (string.IsNullOrEmpty(std.Code))
            {
                std.Code = titles.FindTitleCode(std.LongTitle)?.Code;
            }

            // Neither layout is reliable for grade, and the title reference is authoritative.
            if (string.IsNullOrEmpty(std.Grade) && !string.IsNullOrEmpty(std.Code))
            {
                std.Grade = titles.FindByCode(std.Code)?.Grade ?? "";
            }

            // Dedup on the STRICT key. The loose key drops variant suffixes, which would merge
            // "Analyst 3 RP" into "Analyst 3 RP GF" and silently discard a real standard.
            byKey[TitleNormalizer.TitleCodeKey(std.LongTitle)] = std;
        }

        var store = byKey
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Value)
            .ToList();

        var uncoded = store.Where(s => string.IsNullOrEmpty(s.Code)).ToList();
        List<string> UncodedAs(TitleCodeResolution why) =>
            [.. uncoded.Where(s => titles.ExplainTitleCode(s.LongTitle) == why)
                .Select(s => s.LongTitle)
                .OrderBy(t => t, StringComparer.Ordinal)];

        return new StandardsImportResult
        {
            Store = store,
            Report = new StandardsImportReport
            {
                Count = store.Count,
                Coded = store.Count - uncoded.Count,
                Unknown = UncodedAs(TitleCodeResolution.Unknown),
                Ambiguous = UncodedAs(TitleCodeResolution.Ambiguous),
                Titles = [.. store.Select(s => s.LongTitle)],
            },
        };
    }

    /// <summary>
    /// Replace every stored standard with what the workbooks say now. Wholesale and inside one
    /// transaction: a failed import leaves the previous standards in place rather than none.
    /// </summary>
    public async Task<StandardsImportReport> ImportAsync(string directory, CancellationToken ct = default)
    {
        var titles = await _titleCodes.GetAsync(ct);
        var result = Build(directory, titles);

        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            await _db.JobStandards.ExecuteDeleteAsync(ct);
            _db.JobStandards.AddRange(result.Store.Select(StandardsStore.ToEntity));
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        _standards.Invalidate();
        return result.Report;
    }
}

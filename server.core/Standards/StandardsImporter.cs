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

/// <summary>What happened to one uploaded workbook.</summary>
public sealed class WorkbookOutcome
{
    public string FileName { get; set; } = "";

    /// <summary>"added", "duplicate" (this exact workbook was uploaded before), or "failed".</summary>
    public string Result { get; set; } = "";

    public int Standards { get; set; }
    public string? Error { get; set; }
}

public sealed class StandardsMergeReport
{
    public List<WorkbookOutcome> Files { get; set; } = [];

    /// <summary>Standards that were new to the store.</summary>
    public int Added { get; set; }

    /// <summary>Standards that replaced one with the same exact title.</summary>
    public int Updated { get; set; }

    /// <summary>Standards in the store after the merge.</summary>
    public int Total { get; set; }

    public List<string> Uncoded { get; set; } = [];
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
    /// Merge uploaded workbooks into the store. Each standard they contain is added, or replaces
    /// the stored one with the same STRICT title key; every other standard is left alone — so
    /// uploading one family's workbook never wipes the rest. Workbooks are kept as the original
    /// bytes, once each.
    /// </summary>
    public async Task<StandardsMergeReport> MergeAsync(
        IEnumerable<(string Name, byte[] Bytes)> files, int? userId, CancellationToken ct = default)
    {
        var report = new StandardsMergeReport();
        var parsed = new List<ClassStandardRecord>();

        foreach (var (name, bytes) in files)
        {
            var outcome = new WorkbookOutcome { FileName = name };
            report.Files.Add(outcome);

            if (!name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                outcome.Result = "failed";
                outcome.Error = "Not a Job Builder export — expected an .xlsx workbook.";
                continue;
            }

            var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
            if (await _db.StandardsWorkbooks.AnyAsync(w => w.Sha256 == sha, ct)
                || _db.StandardsWorkbooks.Local.Any(w => w.Sha256 == sha))
            {
                outcome.Result = "duplicate";
                continue;
            }

            List<ClassStandardRecord> found;
            try
            {
                using var stream = new MemoryStream(bytes);
                found = StandardsWorkbookParser.Parse(stream);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcome.Result = "failed";
                outcome.Error = "Could not be read as an Excel workbook.";
                continue;
            }

            if (found.Count == 0)
            {
                outcome.Result = "failed";
                outcome.Error = "No job standards found — expected a Job Builder standards export.";
                continue;
            }

            outcome.Result = "added";
            outcome.Standards = found.Count;
            parsed.AddRange(found);
            _db.StandardsWorkbooks.Add(new StandardsWorkbook
            {
                FileName = name,
                Sha256 = sha,
                Content = bytes,
                SizeBytes = bytes.Length,
                StandardsFound = found.Count,
                UploadedByUserId = userId,
                UploadedAt = DateTimeOffset.UtcNow,
            });
        }

        var titles = await _titleCodes.GetAsync(ct);
        var merged = Build(parsed, titles);
        var keys = merged.Store.Select(s => TitleNormalizer.TitleCodeKey(s.LongTitle)).ToList();
        var existing = await _db.JobStandards
            .Include(s => s.Items)
            .Where(s => keys.Contains(s.TitleCodeKey))
            .ToListAsync(ct);

        foreach (var record in merged.Store)
        {
            var fresh = StandardsStore.ToEntity(record);
            var current = existing.FirstOrDefault(s => s.TitleCodeKey == fresh.TitleCodeKey);
            if (current == null)
            {
                _db.JobStandards.Add(fresh);
                report.Added++;
                continue;
            }

            _db.RemoveRange(current.Items);
            current.LongTitle = fresh.LongTitle;
            current.Code = fresh.Code;
            current.PersProg = fresh.PersProg;
            current.Grade = fresh.Grade;
            current.Flsa = fresh.Flsa;
            current.Union = fresh.Union;
            current.GenericScope = fresh.GenericScope;
            current.CustomScope = fresh.CustomScope;
            current.TitleKey = fresh.TitleKey;
            current.Items = fresh.Items;
            report.Updated++;
        }

        await _db.SaveChangesAsync(ct);
        _standards.Invalidate();

        report.Total = await _db.JobStandards.CountAsync(ct);
        report.Uncoded = [.. merged.Report.Unknown.Concat(merged.Report.Ambiguous)];
        return report;
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

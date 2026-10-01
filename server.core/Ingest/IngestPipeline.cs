using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Server.Core.Ai;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Profiles;
using Server.Core.Titles;

namespace Server.Core.Ingest;

/// <summary>A class present in the corpus that has no profile yet.</summary>
public sealed class PendingClass
{
    public string Code { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public int FileCount { get; set; }
}

/// <summary>
/// The ingest pipeline: scan the corpus, find classes without a profile, and ingest one class
/// end to end — parse, aggregate, consolidate, synthesize an envelope, compute coverage, persist.
/// Ported from the POC's src/lib/ingest/pipeline.ts, with SQL in place of JSON files.
/// </summary>
public sealed partial class IngestPipeline
{
    private readonly AppDbContext _db;
    private readonly ITitleCodeService _titleCodes;
    private readonly IStructuredLlm _llm;
    private readonly Consolidator _consolidator;
    private readonly EnvelopeSynthesizer _envelopes;
    private readonly ILogger<IngestPipeline> _logger;

    public IngestPipeline(
        AppDbContext db,
        ITitleCodeService titleCodes,
        IStructuredLlm llm,
        Consolidator consolidator,
        EnvelopeSynthesizer envelopes,
        ILogger<IngestPipeline> logger)
    {
        _db = db;
        _titleCodes = titleCodes;
        _llm = llm;
        _consolidator = consolidator;
        _envelopes = envelopes;
        _logger = logger;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChar();

    [GeneratedRegex("^-|-$")]
    private static partial Regex EdgeDashes();

    public static string SlugForClass(string code, string title) =>
        $"{code}-{EdgeDashes().Replace(NonSlugChar().Replace(title.ToLowerInvariant(), "-"), "")}";

    /// <summary>
    /// Parse every JD under the corpus root, recursively, and group by UC job code.
    ///
    /// A JD exported under a superseded code belongs to the SUCCESSOR class, and the remap happens
    /// here at scan time rather than later. Doing it here means every downstream step — grouping,
    /// slug, aggregate, profile identity — follows automatically, and a re-ingest cannot resurrect
    /// the deprecated class.
    /// </summary>
    public async Task<Dictionary<string, List<HrtmsRecord>>> ScanCorpusAsync(
        string corpusDir, CancellationToken ct = default)
    {
        var byCode = new Dictionary<string, List<HrtmsRecord>>(StringComparer.Ordinal);
        if (!Directory.Exists(corpusDir))
        {
            return byCode;
        }

        var index = await _titleCodes.GetAsync(ct);

        var files = Directory.EnumerateFiles(corpusDir, "*", SearchOption.AllDirectories)
            .Where(p => p.ToLowerInvariant().EndsWith(".html", StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(corpusDir, p).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(p => p, StringComparer.Ordinal);

        foreach (var rel in files)
        {
            ct.ThrowIfCancellationRequested();

            var rec = HrtmsParser.Parse(
                await File.ReadAllTextAsync(Path.Combine(corpusDir, rel), ct), rel);

            var sup = index.SupersededBy(rec.UcJobCode);
            if (sup is not null)
            {
                // Keep what the export actually said, so the rewrite is auditable rather than
                // invisible.
                rec.OriginalUcJobCode = rec.UcJobCode;
                rec.UcJobCode = sup.ToCode;
                rec.UcJobTitle = sup.ToTitle;
            }

            if (!byCode.TryGetValue(rec.UcJobCode, out var list))
            {
                list = [];
                byCode[rec.UcJobCode] = list;
            }

            list.Add(rec);
        }

        return byCode;
    }

    /// <summary>Classes present in the corpus with no profile in the database yet.</summary>
    public async Task<List<PendingClass>> PendingClassesAsync(string corpusDir, CancellationToken ct = default)
    {
        var done = await _db.ClassProfiles.AsNoTracking()
            .Select(p => p.UcJobCode)
            .ToListAsync(ct);
        var doneCodes = done.Select(TitleCodeIndex.Pad).ToHashSet(StringComparer.Ordinal);

        var pending = new List<PendingClass>();
        foreach (var (code, recs) in await ScanCorpusAsync(corpusDir, ct))
        {
            if (doneCodes.Contains(TitleCodeIndex.Pad(code)))
            {
                continue;
            }

            pending.Add(new PendingClass
            {
                Code = code,
                Slug = SlugForClass(code, recs[0].UcJobTitle),
                Title = ProfileAggregator.Titleize(recs[0].UcJobTitle),
                FileCount = recs.Count,
            });
        }

        return pending.OrderBy(p => p.Title, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Ingest one class from its parsed records.
    ///
    /// A partner-edited envelope is PRESERVED: if the stored profile was marked manual, the human's
    /// wording survives re-ingest and only the statistics are refreshed. Overwriting it would
    /// silently discard curation work.
    /// </summary>
    public async Task<ClassProfile> IngestRecordsAsync(
        IReadOnlyList<HrtmsRecord> records, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(records.Count);

        var slug = SlugForClass(records[0].UcJobCode, records[0].UcJobTitle);
        var profile = ProfileAggregator.Aggregate(records, slug);

        var existing = await _db.ClassProfiles
            .Include(p => p.Envelope!).ThenInclude(e => e.KeyResponsibilities).ThenInclude(r => r.Duties)
            .Include(p => p.Envelope!).ThenInclude(e => e.Items)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);

        if (_llm.HasApiKey)
        {
            try
            {
                var consolidated = await _consolidator.ConsolidateAsync(records, ct);
                profile.ConsolidatedFunctions = consolidated.FunctionGroups;
                profile.ConsolidatedQuals = [.. consolidated.Certifications, .. consolidated.MinQualifications];
                profile.DroppedItems =
                [
                    .. consolidated.DroppedFunctions.Select((t, i) =>
                        new ProfileDroppedItem { Kind = DroppedItemKind.Function, Ordinal = i, Text = t }),
                    .. consolidated.DroppedQualifications.Select((t, i) =>
                        new ProfileDroppedItem { Kind = DroppedItemKind.Qualification, Ordinal = i, Text = t }),
                ];

                if (existing?.EnvelopeSource == Domain.EnvelopeSource.Manual && existing.Envelope is not null)
                {
                    profile.Envelope = Detach(existing.Envelope);
                    profile.EnvelopeSource = Domain.EnvelopeSource.Manual;
                }
                else
                {
                    profile.Envelope = await _envelopes.SynthesizeAsync(profile, ct);
                    profile.EnvelopeSource = Domain.EnvelopeSource.Claude;
                }
            }
            catch (Exception ex)
            {
                // An ingest that fails on a model call should still produce a usable class rather
                // than leaving the corpus unrepresented. The fallback is marked as such so it is
                // obvious the envelope was not synthesized.
                _logger.LogWarning(ex, "Envelope synthesis failed for {Slug}; falling back to deterministic", slug);
                profile.Envelope = EnvelopeSynthesizer.Deterministic(profile);
                profile.EnvelopeSource = Domain.EnvelopeSource.Deterministic;
            }
        }
        else
        {
            profile.Envelope = EnvelopeSynthesizer.Deterministic(profile);
            profile.EnvelopeSource = Domain.EnvelopeSource.Deterministic;
        }

        // Backwards coverage needs the consolidated groups set above, so it runs last.
        profile.Coverage = CoverageCalculator.ComputeCoverage(profile, records);

        if (existing is not null)
        {
            // Replace rather than merge: every child is derived from the records, so a partial
            // update would leave stale rows from the previous corpus behind. Cascades handle the
            // children.
            _db.ClassProfiles.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }

        _db.ClassProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
        return profile;
    }

    /// <summary>Scan and ingest a single class by code.</summary>
    public async Task<ClassProfile> IngestClassAsync(
        string corpusDir, string code, CancellationToken ct = default)
    {
        var corpus = await ScanCorpusAsync(corpusDir, ct);
        if (!corpus.TryGetValue(code, out var recs) || recs.Count == 0)
        {
            throw new InvalidOperationException($"No JDs found for code {code}");
        }

        return await IngestRecordsAsync(recs, ct);
    }

    /// <summary>One parsed JD by class code and source file, for the review viewer.</summary>
    public async Task<HrtmsRecord?> FindJdAsync(
        string corpusDir, string code, string sourceFile, CancellationToken ct = default)
    {
        var corpus = await ScanCorpusAsync(corpusDir, ct);
        return corpus.TryGetValue(code, out var recs)
            ? recs.FirstOrDefault(r => r.SourceFile == sourceFile)
            : null;
    }

    /// <summary>All parsed JDs for a class.</summary>
    public async Task<List<HrtmsRecord>> RecordsForClassAsync(
        string corpusDir, string code, CancellationToken ct = default)
    {
        var corpus = await ScanCorpusAsync(corpusDir, ct);
        return corpus.TryGetValue(code, out var recs) ? recs : [];
    }

    /// <summary>
    /// Copy an envelope free of its database identity, so a preserved manual envelope can be
    /// re-attached to a freshly built profile instead of being re-parented in place.
    /// </summary>
    private static JobEnvelope Detach(JobEnvelope e)
    {
        var copy = new JobEnvelope { Summary = e.Summary, ScopeStatement = e.ScopeStatement };

        foreach (var r in e.KeyResponsibilities.OrderBy(x => x.Ordinal))
        {
            var resp = new EnvelopeResponsibility
            {
                Ordinal = r.Ordinal,
                FunctionName = r.FunctionName,
                PctTime = r.PctTime,
            };

            foreach (var d in r.Duties.OrderBy(x => x.Ordinal))
            {
                resp.Duties.Add(new EnvelopeDuty { Ordinal = d.Ordinal, Text = d.Text });
            }

            copy.KeyResponsibilities.Add(resp);
        }

        foreach (var item in e.Items.OrderBy(x => x.Kind).ThenBy(x => x.Ordinal))
        {
            copy.Items.Add(new EnvelopeListItem { Kind = item.Kind, Ordinal = item.Ordinal, Text = item.Text });
        }

        return copy;
    }
}

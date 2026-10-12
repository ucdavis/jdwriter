using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Profiles;
using Server.Core.Titles;

namespace Server.Core.Standards;

/// <summary>
/// Standard-derived envelopes, carried from one environment to another — bootstrap on a laptop,
/// where model calls are cheap to retry and nothing is live, then load the results into production.
///
/// Only the envelope travels, keyed by the standard's title and the job code it resolved to. That is
/// the one part a model produced; every other field of the class is rebuilt by the receiving
/// environment from its own standard and title reference, through the same create path and guards
/// as a bootstrap there.
///
/// Two things travel with the envelopes because the receiving environment can't rebuild them: the
/// admin's standard–code match decisions, and the standards the envelopes were built from — one
/// entered by hand exists nowhere else. Both are added before the envelopes, and never overwrite
/// what the receiving environment already has.
/// </summary>
public sealed class EnvelopeBundle
{
    public const string FormatName = "jdwriter.standard-envelopes";

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = 1;
    public DateTimeOffset ExportedAt { get; set; }
    public List<BundledEnvelope> Envelopes { get; set; } = [];

    /// <summary>The standards the envelopes were built from. Added within version 1.</summary>
    public List<ClassStandardRecord> Standards { get; set; } = [];

    /// <summary>Every standard–code match decision. Added within version 1.</summary>
    public List<BundledMatchDecision> StandardMatches { get; set; } = [];

    /// <summary>Codes an admin marked active or not at UC Davis, over the payroll reference. Added within version 1.</summary>
    public List<BundledCodeActivity> ActiveCodes { get; set; } = [];
}

public sealed class BundledCodeActivity
{
    public string UcJobCode { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class BundledMatchDecision
{
    public string StandardTitle { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public bool IsMatch { get; set; }
}

public sealed class BundledEnvelope
{
    /// <summary>The standard's long title, which is also the class title a bootstrap gives it.</summary>
    public string Title { get; set; } = "";

    /// <summary>The job code the standard resolved to where the envelope was made; "" for none.</summary>
    public string UcJobCode { get; set; } = "";

    public EnvelopeWire Envelope { get; set; } = new();
}

public sealed class ImportedClass
{
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string UcJobCode { get; set; } = "";
}

public sealed class SkippedEnvelope
{
    public string Title { get; set; } = "";
    public BootstrapRefusal Reason { get; set; }
    public string Message { get; set; } = "";
}

public sealed class EnvelopeImportResult
{
    public List<ImportedClass> Created { get; set; } = [];
    public List<SkippedEnvelope> Skipped { get; set; } = [];

    /// <summary>Standards this environment didn't have, added before the envelopes.</summary>
    public int StandardsAdded { get; set; }

    /// <summary>Match decisions this environment didn't have.</summary>
    public int MatchesAdded { get; set; }
}

public sealed class EnvelopeTransfer
{
    private readonly AppDbContext _db;
    private readonly IBootstrapper _bootstrapper;
    private readonly ITitleCodeService _titleCodes;
    private readonly IStandardsStore _standards;
    private readonly ILogger<EnvelopeTransfer> _logger;

    public EnvelopeTransfer(
        AppDbContext db, IBootstrapper bootstrapper, ITitleCodeService titleCodes, IStandardsStore standards,
        ILogger<EnvelopeTransfer> logger)
    {
        _db = db;
        _standards = standards;
        _bootstrapper = bootstrapper;
        _titleCodes = titleCodes;
        _logger = logger;
    }

    /// <summary>
    /// Every class still built from its standard alone. A hand-edited envelope is excluded (its
    /// source is Manual), and so is any class that has since learned from real JDs, or whose code a
    /// union successor has superseded — a dead class awaiting retirement is not worth carrying.
    /// </summary>
    public async Task<EnvelopeBundle> ExportAsync(CancellationToken ct = default)
    {
        var titleCodes = await _titleCodes.GetAsync(ct);

        var profiles = await _db.ClassProfiles.AsNoTracking()
            .Where(p => p.EnvelopeSource == EnvelopeSource.Standard && p.CorpusSize == 0 && p.Envelope != null)
            .Include(p => p.Envelope!).ThenInclude(e => e.KeyResponsibilities).ThenInclude(r => r.Duties)
            .Include(p => p.Envelope!).ThenInclude(e => e.Items)
            .AsSplitQuery()
            .OrderBy(p => p.Title)
            .ToListAsync(ct);

        var live = profiles.Where(p => !titleCodes.IsSuperseded(p.UcJobCode)).ToList();
        var standards = await _standards.GetIndexAsync(ct);

        return new EnvelopeBundle
        {
            ExportedAt = DateTimeOffset.UtcNow,
            Envelopes =
            [
                .. live.Select(p => new BundledEnvelope
                {
                    Title = p.Title,
                    UcJobCode = p.UcJobCode,
                    Envelope = EnvelopeWire.From(p.Envelope!),
                }),
            ],
            Standards =
            [
                .. live.Select(p => standards.For(p.Title, p.UcJobCode))
                    .OfType<ClassStandardRecord>()
                    .DistinctBy(s => s.LongTitle),
            ],
            StandardMatches =
            [
                .. await _db.StandardMatchDecisions.AsNoTracking()
                    .OrderBy(d => d.Id)
                    .Select(d => new BundledMatchDecision { StandardTitle = d.StandardTitle, UcJobCode = d.UcJobCode, IsMatch = d.IsMatch })
                    .ToListAsync(ct),
            ],
            ActiveCodes =
            [
                .. await _db.CodeActiveOverrides.AsNoTracking()
                    .OrderBy(o => o.Id)
                    .Select(o => new BundledCodeActivity { UcJobCode = o.UcJobCode, IsActive = o.IsActive })
                    .ToListAsync(ct),
            ],
        };
    }

    /// <summary>Adds what this environment lacks; never replaces a standard or a decision it already has.</summary>
    private async Task<(int Standards, int Matches)> AddStandardsAndMatchesAsync(EnvelopeBundle bundle, CancellationToken ct)
    {
        var haveTitles = (await _db.JobStandards.Select(s => s.LongTitle).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var standards = bundle.Standards.Where(s => !haveTitles.Contains(s.LongTitle)).DistinctBy(s => s.LongTitle).ToList();
        _db.JobStandards.AddRange(standards.Select(StandardsStore.ToEntity));

        var haveDecisions = (await _db.StandardMatchDecisions.Select(d => new { d.StandardTitle, d.UcJobCode }).ToListAsync(ct))
            .Select(d => (d.StandardTitle, TitleCodeIndex.Pad(d.UcJobCode)))
            .ToHashSet();
        var haveMatch = (await _db.StandardMatchDecisions.Where(d => d.IsMatch).Select(d => d.StandardTitle).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var d in bundle.StandardMatches)
        {
            // One match per standard: a standard already matched here keeps its match.
            if (haveDecisions.Contains((d.StandardTitle, TitleCodeIndex.Pad(d.UcJobCode))) || (d.IsMatch && haveMatch.Contains(d.StandardTitle)))
            {
                continue;
            }

            _db.StandardMatchDecisions.Add(new StandardMatchDecision
            {
                StandardTitle = d.StandardTitle,
                UcJobCode = d.UcJobCode,
                IsMatch = d.IsMatch,
                DecidedAt = bundle.ExportedAt,
            });
            haveDecisions.Add((d.StandardTitle, TitleCodeIndex.Pad(d.UcJobCode)));
            if (d.IsMatch)
            {
                haveMatch.Add(d.StandardTitle);
            }

            added++;
        }

        // Codes marked active or not by hand, so an envelope for a code active only by an admin's
        // call isn't refused here as not on payroll. A code this environment has already decided keeps
        // its decision.
        var haveActivity = (await _db.CodeActiveOverrides.Select(o => o.UcJobCode).ToListAsync(ct))
            .Select(TitleCodeIndex.Pad)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var a in bundle.ActiveCodes.DistinctBy(a => TitleCodeIndex.Pad(a.UcJobCode)))
        {
            if (haveActivity.Add(TitleCodeIndex.Pad(a.UcJobCode)))
            {
                _db.CodeActiveOverrides.Add(new CodeActiveOverride { UcJobCode = a.UcJobCode, IsActive = a.IsActive, DecidedAt = bundle.ExportedAt });
            }
        }

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        _standards.Invalidate();
        _titleCodes.Invalidate();
        return (standards.Count, added);
    }

    private static bool WithinBounds(ClassStandardRecord s)
    {
        var lists = new[] { s.KeyResponsibilities, s.Ksa, s.Education, s.Licenses, s.SpecialConditions };
        return s.LongTitle.Length is > 0 and <= 300
               && lists.All(l => l.Count <= 200)
               && lists.SelectMany(l => l).Append(s.GenericScope).Append(s.CustomScope).All(t => (t ?? "").Length <= 8_000);
    }

    private static bool WithinBounds(BundledEnvelope e)
    {
        var w = e.Envelope;
        var lists = new[]
        {
            w.RequiredCertifications, w.Education, w.WorkExperience, w.MinQualifications, w.PrefQualifications,
            w.ConditionsOfEmployment, w.WorkEnvironment, w.PhysicalRequirements, w.OutOfEnvelope,
        };
        var texts = lists.SelectMany(l => l)
            .Concat(w.KeyResponsibilities.SelectMany(r => r.Duties.Append(r.FunctionName)))
            .Append(w.Summary).Append(w.ScopeStatement).Append(e.Title);
        return w.KeyResponsibilities.Count <= 40
               && w.KeyResponsibilities.All(r => r.Duties.Count <= 60)
               && lists.All(l => l.Count <= 200)
               && texts.All(t => (t ?? "").Length <= 4_000);
    }

    /// <summary>
    /// Create a class for each bundled envelope that this environment does not already have. Never
    /// overwrites: an existing class — learned from JDs, edited by hand, or bootstrapped here — is
    /// reported and left alone. Each class is its own save, so one refusal does not undo the rest.
    /// </summary>
    public async Task<EnvelopeImportResult> ImportAsync(EnvelopeBundle bundle, CancellationToken ct = default)
    {
        if (bundle.Format != EnvelopeBundle.FormatName || bundle.Version != 1)
        {
            throw new InvalidOperationException(
                $"Not a JDWriter envelope export (format \"{bundle.Format}\", version {bundle.Version}).");
        }

        // Admin-only, but still a file from elsewhere: bounded before any of it is stored.
        if (bundle.Envelopes.Count > 2_000 || bundle.Envelopes.Any(e => !WithinBounds(e))
            || bundle.Standards.Count > 5_000 || bundle.Standards.Any(s => !WithinBounds(s))
            || bundle.StandardMatches.Count > 20_000
            || bundle.StandardMatches.Any(d => d.StandardTitle.Length is 0 or > 300 || d.UcJobCode.Length is 0 or > 20)
            || bundle.ActiveCodes.Count > 10_000
            || bundle.ActiveCodes.Any(a => a.UcJobCode.Length is 0 or > 20))
        {
            throw new InvalidOperationException("This export is larger than any real set of envelopes; it was not imported.");
        }

        // Standards and match decisions first, so the envelopes find what they were built from.
        var (standardsAdded, matchesAdded) = await AddStandardsAndMatchesAsync(bundle, ct);

        var note = $"Standard-derived — envelope generated in another environment (exported "
                   + $"{bundle.ExportedAt:yyyy-MM-dd}) and imported. Ingest JDs for this class to learn the real envelope.";

        var result = new EnvelopeImportResult { StandardsAdded = standardsAdded, MatchesAdded = matchesAdded };
        foreach (var item in bundle.Envelopes)
        {
            try
            {
                var profile = await _bootstrapper.ImportAsync(item.Title, item.UcJobCode, item.Envelope.ToEntity(), note, ct);
                result.Created.Add(new ImportedClass { Title = profile.Title, Slug = profile.Slug, UcJobCode = profile.UcJobCode });
            }
            catch (BootstrapRefusedException ex)
            {
                result.Skipped.Add(new SkippedEnvelope { Title = item.Title, Reason = ex.Reason, Message = ex.Message });
            }
            finally
            {
                // A refused class can leave its half-built profile tracked; it must not ride along
                // on the next class's save.
                _db.ChangeTracker.Clear();
            }
        }

        _logger.LogInformation(
            "Envelope import: {Created} created, {Skipped} skipped of {Total} (exported {ExportedAt:o})",
            result.Created.Count, result.Skipped.Count, bundle.Envelopes.Count, bundle.ExportedAt);
        return result;
    }
}

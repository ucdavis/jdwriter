using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Titles;

namespace Server.Core.Profiles;

/// <summary>
/// A selectable class in the intake list. "Ready" means an ingested profile exists and an envelope
/// is available; "Seed" means a title in use at UC Davis that has not been ingested yet.
/// </summary>
public sealed class ClassListItem
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public bool Ready { get; set; }
    public int? CorpusSize { get; set; }
    public string? BargainingUnit { get; set; }
    public string? Family { get; set; }
    public string? Grade { get; set; }

    /// <summary>The site this class is only for ("Health Center", "Student Health Center"), or null.</summary>
    public string? Site { get; set; }
}

/// <summary>
/// A slim descriptor of an ingested class. Used to route an out-of-envelope request to a real
/// neighbouring class rather than merely naming one.
/// </summary>
public sealed class ClassDescriptor
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public string Summary { get; set; } = "";
    public string CtJobFamily { get; set; } = "";
    public string CtJobFunction { get; set; } = "";
}

/// <summary>
/// Curation statistics for one ingested class: the analyst index's answer to "what needs my
/// attention". Every field is a count or an already-stored number, so the whole list can be
/// projected in SQL without loading a single duty row.
/// </summary>
public sealed class ClassSummary
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public string CtJobFamily { get; set; } = "";
    public string CtJobFunction { get; set; } = "";
    public string PersonnelProgram { get; set; } = "";
    public int CorpusSize { get; set; }
    /// <summary>Salary grade consensus across the corpus, or null when none dominates.</summary>
    public string? Grade { get; set; }

    public EnvelopeSource? EnvelopeSource { get; set; }
    public bool HasEnvelope { get; set; }
    public int EnvelopeResponsibilities { get; set; }
    /// <summary>
    /// Sum of the envelope's % time. Unlike the corpus, an envelope is ours to get right, so a
    /// total that is not 100 is the fastest signal that a class needs attention. Reported, never
    /// corrected — correcting it here would hide exactly what the analyst is looking for.
    /// </summary>
    public int EnvelopePctTotal { get; set; }

    /// <summary>Consolidated categories. Zero means consolidation has not run for this class.</summary>
    public int ConsolidatedFunctions { get; set; }
    /// <summary>
    /// Responsibility categories as the POC's admin view counted them: consolidated when present,
    /// otherwise the raw corpus functions.
    /// </summary>
    public int Responsibilities { get; set; }
    /// <summary>Minimum qualifications, with the same consolidated-else-raw fallback.</summary>
    public int Ksas { get; set; }

    /// <summary>Null when no backwards-coverage report has been computed.</summary>
    public int? CoverageN { get; set; }
    public double? MeanCoverage { get; set; }
    /// <summary>Share of JDs at 90% coverage or better, 0..1.</summary>
    public double? WellCoveredPct { get; set; }

    /// <summary>
    /// Whether an official standard matches this class. Not a column: it is resolved against the
    /// standards index by title, so the repository leaves it false and the caller fills it in.
    /// </summary>
    public bool StandardLinked { get; set; }
}

public interface IClassProfileRepository
{
    /// <summary>One profile with everything the authoring and matching paths read.</summary>
    Task<ClassProfile?> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task<ClassProfile?> GetByCodeAsync(string ucJobCode, CancellationToken ct = default);

    /// <summary>
    /// Every ingested profile, fully loaded. The matcher needs all of them, so this is the one
    /// query worth being deliberate about — see the note on <see cref="GetAllAsync"/>.
    /// </summary>
    Task<List<ClassProfile>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Slim descriptors only — no envelope bodies, no duties.</summary>
    Task<List<ClassDescriptor>> GetDescriptorsAsync(string? excludeSlug = null, CancellationToken ct = default);

    /// <summary>Ingested classes first, then in-use titles that have no profile yet.</summary>
    Task<List<ClassListItem>> GetClassListAsync(CancellationToken ct = default);

    /// <summary>Curation statistics for every ingested class, projected in SQL.</summary>
    Task<List<ClassSummary>> GetSummariesAsync(CancellationToken ct = default);

    /// <summary>Parsed JD records for one class, for backwards-coverage comparison.</summary>
    Task<List<JobDescription>> GetJobDescriptionsAsync(string? ucJobCode = null, CancellationToken ct = default);
}

/// <summary>
/// Reads class profiles. Ported from the POC's src/lib/profile/load.ts, where it read JSON files
/// from disk; here it is EF, and the mtime-keyed caching that file access needed is gone.
/// </summary>
public sealed class ClassProfileRepository : IClassProfileRepository
{
    private readonly AppDbContext _db;
    private readonly ITitleCodeService _titleCodes;

    public ClassProfileRepository(AppDbContext db, ITitleCodeService titleCodes)
    {
        _db = db;
        _titleCodes = titleCodes;
    }

    /// <summary>
    /// The full object graph a profile needs to be useful.
    ///
    /// Written as explicit includes with `AsSplitQuery` rather than one joined query on purpose: a
    /// profile fans out into functions, duties, envelope responsibilities, consolidated members and
    /// coverage rows, and a single query multiplies those together into a cartesian result that is
    /// dramatically larger than the data.
    /// </summary>
    private static IQueryable<ClassProfile> FullGraph(IQueryable<ClassProfile> q) => q
        .Include(p => p.Envelope!).ThenInclude(e => e.KeyResponsibilities).ThenInclude(r => r.Duties)
        .Include(p => p.Envelope!).ThenInclude(e => e.Items)
        .Include(p => p.Distributions).ThenInclude(d => d.Values)
        .Include(p => p.Functions).ThenInclude(f => f.SampleDuties)
        .Include(p => p.Qualifications)
        .Include(p => p.ConsolidatedFunctions).ThenInclude(f => f.Members)
        .Include(p => p.ConsolidatedFunctions).ThenInclude(f => f.SampleDuties)
        .Include(p => p.ConsolidatedQuals).ThenInclude(q2 => q2.Members)
        .Include(p => p.DroppedItems)
        .Include(p => p.SourceFiles)
        .Include(p => p.Coverage!).ThenInclude(c => c.PerJd).ThenInclude(j => j.Uncovered)
        .AsSplitQuery();

    public async Task<ClassProfile?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        await FullGraph(_db.ClassProfiles.AsNoTracking())
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);

    public async Task<ClassProfile?> GetByCodeAsync(string ucJobCode, CancellationToken ct = default)
    {
        // Compare padded: forms state codes unpadded ("7399") and the reference stores them padded.
        var padded = TitleCodeIndex.Pad(ucJobCode);
        var candidates = await FullGraph(_db.ClassProfiles.AsNoTracking()).ToListAsync(ct);
        return candidates.FirstOrDefault(p => TitleCodeIndex.Pad(p.UcJobCode) == padded);
    }

    public async Task<List<ClassProfile>> GetAllAsync(CancellationToken ct = default) =>
        await FullGraph(_db.ClassProfiles.AsNoTracking())
            .OrderBy(p => p.Title)
            .ToListAsync(ct);

    public async Task<List<ClassDescriptor>> GetDescriptorsAsync(
        string? excludeSlug = null, CancellationToken ct = default)
    {
        // Projected in SQL rather than loading graphs and mapping: the caller wants a one-line
        // summary per class, and pulling every duty to build it would be absurd.
        var rows = await _db.ClassProfiles.AsNoTracking()
            .Where(p => excludeSlug == null || p.Slug != excludeSlug)
            .Select(p => new
            {
                p.Slug,
                p.Title,
                p.UcJobCode,
                p.CtJobFamily,
                p.CtJobFunction,
                EnvelopeSummary = p.Envelope != null ? p.Envelope.Summary : null,
                p.RepresentativeSummary,
            })
            .ToListAsync(ct);

        return rows.Select(r => new ClassDescriptor
        {
            Slug = r.Slug,
            Title = r.Title,
            UcJobCode = r.UcJobCode,
            CtJobFamily = r.CtJobFamily,
            CtJobFunction = r.CtJobFunction,
            // The envelope summary when synthesized, else a clipped representative summary —
            // matching the reference's fallback exactly.
            Summary = !string.IsNullOrEmpty(r.EnvelopeSummary)
                ? r.EnvelopeSummary
                : Clip(r.RepresentativeSummary, 240),
        }).ToList();
    }

    public async Task<List<ClassListItem>> GetClassListAsync(CancellationToken ct = default)
    {
        var profiles = await _db.ClassProfiles.AsNoTracking()
            .Select(p => new
            {
                p.Slug,
                p.Title,
                p.UcJobCode,
                p.CorpusSize,
                Union = p.Distributions
                    .Where(d => d.Field == DistributionField.UnionCode)
                    .Select(d => d.Consensus)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var ready = profiles.Select(p => new ClassListItem
        {
            Slug = p.Slug,
            Title = p.Title,
            UcJobCode = p.UcJobCode,
            Ready = true,
            Site = Sites.SiteOf(p.Title),
            CorpusSize = p.CorpusSize,
            BargainingUnit = p.Union,
        }).OrderBy(x => x.Title, StringComparer.Ordinal).ToList();

        var readyCodes = ready.Select(r => r.UcJobCode).ToHashSet(StringComparer.Ordinal);
        // Site-aware: a Health Center or Student Health class doesn't hide the regular one, nor the reverse.
        var readyTitleKeys = profiles.Select(p => Sites.ClassKey(p.Title))
            .ToHashSet(StringComparer.Ordinal);

        // Seeds are titles IN USE at UC Davis minus what is already ingested, matched by code OR by
        // abbreviation-aware title. Superseded codes are already excluded by InUseTitleCodes.
        var index = await _titleCodes.GetAsync(ct);
        var seeds = index.InUseTitleCodes()
            .Where(t => !readyCodes.Contains(t.Code)
                        && !readyTitleKeys.Contains(Sites.ClassKey(t.Title)))
            .Select(t => new ClassListItem
            {
                Slug = $"seed-{t.Code}",
                Title = Titleize(t.Title),
                UcJobCode = t.Code,
                Ready = false,
                Site = Sites.SiteOf(t.Title),
                Family = string.IsNullOrEmpty(t.Family) ? null : t.Family,
                Grade = string.IsNullOrEmpty(t.Grade) ? null : t.Grade,
            })
            .OrderBy(x => x.Title, StringComparer.Ordinal)
            .ToList();

        return [.. ready, .. seeds];
    }

    public async Task<List<ClassSummary>> GetSummariesAsync(CancellationToken ct = default)
    {
        // Counts and stored numbers only. The alternative — FullGraph over 65 profiles, then
        // counting in memory — pulls tens of thousands of duty and coverage rows to render a page
        // of summary cards.
        var rows = await _db.ClassProfiles.AsNoTracking()
            .Select(p => new
            {
                p.Slug,
                p.Title,
                p.UcJobCode,
                p.CtJobFamily,
                p.CtJobFunction,
                p.PersonnelProgram,
                p.CorpusSize,
                Grade = p.Distributions
                    .Where(d => d.Field == DistributionField.SalaryGrade)
                    .Select(d => d.Consensus)
                    .FirstOrDefault(),
                p.EnvelopeSource,
                HasEnvelope = p.Envelope != null,
                EnvelopeResponsibilities = p.Envelope != null ? p.Envelope.KeyResponsibilities.Count : 0,
                EnvelopePctTotal = p.Envelope != null ? p.Envelope.KeyResponsibilities.Sum(r => r.PctTime) : 0,
                ConsolidatedFunctions = p.ConsolidatedFunctions.Count,
                Functions = p.Functions.Count,
                ConsolidatedMinQuals = p.ConsolidatedQuals.Count(q => q.Kind == ConsolidatedQualKind.MinQualification),
                KsaMin = p.Qualifications.Count(q => q.Kind == ProfileQualKind.KsaMin),
                CoverageN = p.Coverage != null ? p.Coverage.N : (int?)null,
                MeanCoverage = p.Coverage != null ? p.Coverage.MeanCoverage : (double?)null,
                WellCoveredPct = p.Coverage != null ? p.Coverage.WellCoveredPct : (double?)null,
            })
            .ToListAsync(ct);

        return rows.Select(r => new ClassSummary
        {
            Slug = r.Slug,
            Title = r.Title,
            UcJobCode = r.UcJobCode,
            CtJobFamily = r.CtJobFamily,
            CtJobFunction = r.CtJobFunction,
            PersonnelProgram = r.PersonnelProgram,
            CorpusSize = r.CorpusSize,
            Grade = r.Grade,
            EnvelopeSource = r.EnvelopeSource,
            HasEnvelope = r.HasEnvelope,
            EnvelopeResponsibilities = r.EnvelopeResponsibilities,
            EnvelopePctTotal = r.EnvelopePctTotal,
            ConsolidatedFunctions = r.ConsolidatedFunctions,
            // The POC fell back when the consolidated block was ABSENT. Normalized rows cannot
            // tell absent from empty, and an empty consolidation is not a real outcome, so zero
            // stands in for absent.
            Responsibilities = r.ConsolidatedFunctions > 0 ? r.ConsolidatedFunctions : r.Functions,
            Ksas = r.ConsolidatedMinQuals > 0 ? r.ConsolidatedMinQuals : r.KsaMin,
            CoverageN = r.CoverageN,
            MeanCoverage = r.MeanCoverage,
            WellCoveredPct = r.WellCoveredPct,
        }).OrderBy(x => x.Title, StringComparer.Ordinal).ToList();
    }

    public async Task<List<JobDescription>> GetJobDescriptionsAsync(
        string? ucJobCode = null, CancellationToken ct = default) =>
        await _db.JobDescriptions.AsNoTracking()
            .Where(j => ucJobCode == null || j.UcJobCode == ucJobCode)
            .Include(j => j.Responsibilities).ThenInclude(r => r.Duties)
            .Include(j => j.Qualifications)
            .AsSplitQuery()
            .OrderBy(j => j.SourceFile)
            .ToListAsync(ct);

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>The reference lowercases then capitalizes each word; seed titles arrive SHOUTING.</summary>
    private static string Titleize(string s)
    {
        var chars = s.ToLowerInvariant().ToCharArray();
        var atWordStart = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsLetterOrDigit(chars[i]))
            {
                if (atWordStart)
                {
                    chars[i] = char.ToUpperInvariant(chars[i]);
                }

                atWordStart = false;
            }
            else
            {
                atWordStart = true;
            }
        }

        return new string(chars);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Profiles;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Controllers;

/// <summary>
/// Reads over the class catalogue. The POC got these from React Server Components; they are real
/// endpoints now.
/// </summary>
[ApiController]
[Route("api/classes")]
[Authorize(Roles = AppRoles.Author)]
public class ClassesController : ApiControllerBase
{
    private readonly IClassProfileRepository _profiles;
    private readonly IStandardsStore _standards;
    private readonly ITitleCodeService _titleCodes;

    public ClassesController(IClassProfileRepository profiles, IStandardsStore standards, ITitleCodeService titleCodes)
    {
        _titleCodes = titleCodes;
        _profiles = profiles;
        _standards = standards;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(new { classes = await _profiles.GetClassListAsync(ct) });

    /// <summary>
    /// The analyst index: per-class curation statistics for every ingested class.
    ///
    /// Deliberately separate from <see cref="List"/>. That list answers "what can I author
    /// against" and every author hits it on every visit; this answers "what needs my attention"
    /// for a handful of analysts. Merging them would put coverage statistics on the hot path of
    /// the most-used screen.
    ///
    /// The literal segment outranks the <c>{slug}</c> template, and no slug can collide with it —
    /// slugs are always "&lt;code&gt;-&lt;title&gt;".
    /// </summary>
    [HttpGet("summary")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var summaries = await _profiles.GetSummariesAsync(ct);
        var index = await _standards.GetIndexAsync(ct);
        foreach (var s in summaries)
        {
            s.StandardLinked = index.ForTitle(s.Title) != null;
        }

        return Ok(new { classes = summaries });
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Get(string slug, CancellationToken ct)
    {
        var profile = await _profiles.GetBySlugAsync(slug, ct);
        if (profile is null)
        {
            return NotFound(new { message = $"No class found for “{slug}”." });
        }

        // The standard travels with the profile because the two are read together everywhere.
        // Roughly 19 of 65 classes have one — the gap is standards COVERAGE, not a matching
        // failure, so null here is a normal state the UI renders calmly.
        var index = await _standards.GetIndexAsync(ct);
        var titles = await _titleCodes.GetAsync(ct);
        var classes = Sites.ClassesByCode((await _profiles.GetClassListAsync(ct))
            .Where(c => c.Ready)
            .Select(c => (c.UcJobCode, c.Slug, c.Title)));
        var twins = Sites.TwinsOf(profile.Title, profile.UcJobCode, titles, classes);
        return Ok(ClassProfileView.From(profile, index.ForTitle(profile.Title), twins));
    }

    [HttpGet("{slug}/coverage")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Coverage(string slug, CancellationToken ct)
    {
        var profile = await _profiles.GetBySlugAsync(slug, ct);
        if (profile is null)
        {
            return NotFound(new { message = $"No class found for “{slug}”." });
        }

        return Ok(profile.Coverage != null ? CoverageView.From(profile.Coverage) : null);
    }

    /// <summary>
    /// One JD side by side with its class envelope, each responsibility marked as covered by the
    /// envelope or idiosyncratic to this position.
    ///
    /// The in-envelope decision is made HERE rather than in the browser, and it is made against the
    /// CONSOLIDATED members rather than the envelope's wording. That distinction is the whole
    /// point: consolidation is where "CULTURAL OPERATIONS" and "Cultural Ops" were judged to be the
    /// same function, so matching on envelope text would mark real coverage as idiosyncratic purely
    /// because an analyst reworded a heading.
    ///
    /// This is the screen the review-and-nudge workflow is built on — an analyst looks at what a
    /// position does that its class does not describe, and decides whether to nudge the JD toward
    /// the standard or the class toward reality.
    /// </summary>
    [HttpGet("{slug}/jds/{**sourceFile}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Jd(string slug, string sourceFile, CancellationToken ct)
    {
        var profile = await _profiles.GetBySlugAsync(slug, ct);
        if (profile is null)
        {
            return NotFound(new { message = $"No class found for \u201C{slug}\u201D." });
        }

        var jds = await _profiles.GetJobDescriptionsAsync(profile.UcJobCode, ct);
        var jd = jds.FirstOrDefault(j => j.SourceFile == sourceFile);
        if (jd is null)
        {
            return NotFound(new { message = "That job description is not in this class." });
        }

        // Every raw function name the consolidation folded into a category. Compared
        // case-insensitively and whitespace-collapsed, because the corpus spells the same function
        // several ways and that variance is exactly what consolidation exists to absorb.
        var covered = profile.ConsolidatedFunctions
            .SelectMany(f => f.Members.Select(m => Normalize(m.Text)))
            .ToHashSet(StringComparer.Ordinal);

        var coveredPct = profile.Coverage?.PerJd
            .FirstOrDefault(c => c.SourceFile == sourceFile)?.CoveredPct;

        // The record goes through the parser's shape so qualifications arrive grouped the way the
        // POC's review screen reads them, rather than as one kind-discriminated list.
        var record = jd.ToHrtmsRecord();

        return Ok(new
        {
            profile = new { profile.Slug, profile.Title, profile.UcJobCode },
            record = new
            {
                record.SourceFile,
                record.WorkingTitle,
                record.UcJobTitle,
                record.UcJobCode,
                record.DepartmentName,
                record.JobSummary,
                record.SalaryGrade,
                record.FlsaStatus,
                record.UnionCode,
                record.Supervises,
                record.Leads,
                Qualifications = new
                {
                    record.Qualifications.Licenses,
                    record.Qualifications.Education,
                    record.Qualifications.MinExperience,
                    record.Qualifications.KsaMin,
                    record.Qualifications.KsaPref,
                },
                Responsibilities = record.Responsibilities.Select(r => new
                {
                    r.Pct,
                    r.FunctionName,
                    r.Duties,
                    InEnvelope = covered.Contains(Normalize(r.FunctionName)),
                }),
            },
            envelope = profile.Envelope != null ? EnvelopeWire.From(profile.Envelope) : null,
            coveredPct,
        });
    }

    private static string Normalize(string s) =>
        string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

    /// <summary>
    /// The real JDs behind a class, with how well each is covered by the envelope. This is the
    /// entry point to the review-and-nudge workflow.
    /// </summary>
    [HttpGet("{slug}/jds")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Jds(string slug, CancellationToken ct)
    {
        var profile = await _profiles.GetBySlugAsync(slug, ct);
        if (profile is null)
        {
            return NotFound(new { message = $"No class found for “{slug}”." });
        }

        var jds = await _profiles.GetJobDescriptionsAsync(profile.UcJobCode, ct);
        var coverage = profile.Coverage?.PerJd.ToDictionary(c => c.SourceFile, c => c.CoveredPct)
                       ?? [];

        return Ok(new
        {
            jds = jds.Select(j => new
            {
                j.SourceFile,
                j.WorkingTitle,
                j.DepartmentName,
                CoveredPct = coverage.TryGetValue(j.SourceFile, out var pct) ? pct : (double?)null,
            }),
        });
    }
}

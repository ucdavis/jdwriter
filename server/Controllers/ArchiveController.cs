using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Ai;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Jd;
using Server.Core.Profiles;
using Server.Helpers;

namespace Server.Controllers;

/// <summary>
/// A class's archived job descriptions — every JD filed under it, whatever its origin — for an
/// author to start from instead of the plain envelope. Starting from one is the goodness-of-fit
/// rewrite in its keep-specifics mode, the same path Classify uses for a unit's own document: the
/// model only places the JD's work in the class's functions, its specific duties come over word for
/// word, and the draft then goes through the normal check.
///
/// A JD is named by its working title, never by a position number; it is addressed by the corpus's
/// own key, as everywhere else.
/// </summary>
[ApiController]
[Route("api/classes/{slug}/archive")]
[Authorize(Roles = AppRoles.Author)]
public class ArchiveController : ApiControllerBase
{
    private readonly IClassProfileRepository _profiles;
    private readonly IStructuredLlm _llm;
    private readonly IFitRewriter _rewriter;
    private readonly AuthoredJdStore _saved;
    private readonly AppDbContext _db;
    private readonly ILogger<ArchiveController> _logger;

    public ArchiveController(
        IClassProfileRepository profiles,
        IStructuredLlm llm,
        IFitRewriter rewriter,
        AuthoredJdStore saved,
        AppDbContext db,
        ILogger<ArchiveController> logger)
    {
        _profiles = profiles;
        _llm = llm;
        _rewriter = rewriter;
        _saved = saved;
        _db = db;
        _logger = logger;
    }

    public sealed class StartRequest
    {
        public string SourceFile { get; set; } = "";
    }

    /// <summary>
    /// Every JD in the class, whole: a class has a few dozen at most, so the author can search their
    /// wording and preview one without another request. No position numbers or reporting lines.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(string slug, CancellationToken ct)
    {
        var profile = await _profiles.GetBySlugAsync(slug, ct);
        if (profile is null)
        {
            return NotFound(new { message = $"No class found for “{slug}”." });
        }

        var jds = await _profiles.GetJobDescriptionsAsync(profile.UcJobCode, ct);
        return Ok(new
        {
            jds = jds
                .OrderBy(j => j.WorkingTitle.Length > 0 ? j.WorkingTitle : j.UcJobTitle, StringComparer.OrdinalIgnoreCase)
                .Select(j => new
                {
                    j.SourceFile,
                    WorkingTitle = j.WorkingTitle.Length > 0 ? j.WorkingTitle : ProfileAggregator.Titleize(j.UcJobTitle),
                    Department = j.DepartmentName,
                    Origin = j.Origin.ToString().ToLowerInvariant(),
                    Summary = j.JobSummary,
                    Responsibilities = j.Responsibilities.OrderBy(r => r.Ordinal).Select(r => new
                    {
                        r.FunctionName,
                        r.Pct,
                        Duties = r.Duties.OrderBy(d => d.Ordinal).Select(d => d.Text),
                    }),
                }),
        });
    }

    /// <summary>
    /// Start a JD from one of the class's archived JDs: rewritten to fit, keeping its specifics, and
    /// saved as a draft to open at /class/{slug}?draft={authoredJdId}.
    /// </summary>
    [HttpPost("start")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(WebHardening.ModelPolicy)]
    public async Task<IActionResult> Start(string slug, StartRequest body, CancellationToken ct)
    {
        var profile = await _profiles.GetBySlugAsync(slug, ct);
        if (profile is null)
        {
            return NotFound(new { message = $"No class found for “{slug}”." });
        }

        var jds = await _profiles.GetJobDescriptionsAsync(profile.UcJobCode, ct);
        var source = jds.FirstOrDefault(j => j.SourceFile == body.SourceFile);
        if (source is null)
        {
            return NotFound(new { message = "That job description is not in this class." });
        }

        if (!_llm.HasApiKey)
        {
            return StatusCode(503, new { message = "Starting from an archived JD is unavailable — no AI provider is configured." });
        }

        FitRewrite rewrite;
        try
        {
            rewrite = await _rewriter.RewriteAsync(profile, source, RewriteMode.KeepSpecifics, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (StructuredLlmException ex)
        {
            // The provider's own error text can name deployments or configuration; it is logged, and
            // the author gets a message that is useful without it.
            _logger.LogWarning(ex, "Model call failed while starting from an archived JD");
            return StatusCode(502, new { message = "Starting from this JD could not be completed — the AI service did not return a usable answer. Try again in a moment." });
        }

        var id = await _saved.SaveDraftAsync(rewrite.Inputs, profile, await User.IdAsync(_db, ct), null, rewrite.DraftState, ct);
        return Ok(new
        {
            authoredJdId = id,
            slug = profile.Slug,
            title = profile.Title,
            rewrite.KeptFunctions,
            rewrite.DroppedFunctions,
            rewrite.CarriedDuties,
            rewrite.MatchedDuties,
            rewrite.Outside,
        });
    }
}

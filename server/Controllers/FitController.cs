using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Ai;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Intake;
using Server.Core.Jd;
using Server.Core.Profiles;
using Server.Helpers;

namespace Server.Controllers;

public sealed record FitSuggestRequest(string Slug, string SourceFile);

/// <param name="Slug">The class the JD is filed under.</param>
/// <param name="TargetSlug">The class to rewrite it into; its own class when omitted.</param>
public sealed record FitRewriteRequest(string Slug, string SourceFile, string? TargetSlug);

/// <summary>
/// The review-and-nudge surface: which real JDs sit badly in their assigned class, and where each
/// might belong instead.
/// </summary>
[ApiController]
[Route("api/fit")]
[Authorize(Roles = AppRoles.Admin)]
public class FitController : ApiControllerBase
{
    private readonly IFitService _fit;
    private readonly IIntakeMatcher _matcher;
    private readonly IClassProfileRepository _profiles;
    private readonly IStructuredLlm _llm;
    private readonly IFitRewriter _rewriter;
    private readonly AuthoredJdStore _saved;
    private readonly AppDbContext _db;

    public FitController(
        IFitService fit,
        IIntakeMatcher matcher,
        IClassProfileRepository profiles,
        IStructuredLlm llm,
        IFitRewriter rewriter,
        AuthoredJdStore saved,
        AppDbContext db)
    {
        _fit = fit;
        _matcher = matcher;
        _profiles = profiles;
        _llm = llm;
        _rewriter = rewriter;
        _saved = saved;
        _db = db;
    }

    /// <summary>
    /// JDs whose coverage of their own class envelope falls below the threshold. Reads the
    /// coverage already stored on each profile — nothing is recomputed and no model is called, so
    /// this is cheap enough to be the page's default view.
    /// </summary>
    [HttpGet("misfits")]
    public async Task<IActionResult> Misfits([FromQuery] int threshold = 90, CancellationToken ct = default) =>
        Ok(await _fit.GetMisfitsAsync(threshold, ct));

    /// <summary>
    /// Where does ONE specific JD actually belong? A suggestion is about a job description rather
    /// than a class, so it matches that JD's real responsibilities against every class envelope.
    /// </summary>
    [HttpPost("suggest")]
    public async Task<IActionResult> Suggest(FitSuggestRequest body, CancellationToken ct)
    {
        var profile = await _profiles.GetBySlugAsync(body.Slug, ct);
        if (profile is null || string.IsNullOrWhiteSpace(body.SourceFile))
        {
            return BadRequest(new { message = "A class and a job description are both required." });
        }

        if (!_llm.HasApiKey)
        {
            return StatusCode(503, new { message = "Suggestions are unavailable — no API key is configured." });
        }

        // Read from the database, not from the filesystem corpus. The 150MB of HRTMS exports is a
        // local development artifact; a deployed app has the corpus in SQL and no such directory.
        var jds = await _profiles.GetJobDescriptionsAsync(profile.UcJobCode, ct);
        var record = jds.FirstOrDefault(j => j.SourceFile == body.SourceFile);
        if (record is null)
        {
            return NotFound(new { message = "That job description is not in the corpus." });
        }

        // Match on what the JD actually says, not on its assigned class — the whole question is
        // whether that assignment is right.
        var request = $"{record.JobSummary}\n\nActual responsibilities (% time · function · duties):\n"
                      + string.Join('\n', record.Responsibilities.Select(r =>
                          $"{r.Pct}% {r.FunctionName}: {string.Join("; ", r.Duties)}"));

        var matches = await _matcher.MatchAsync(request, await _profiles.GetAllAsync(ct), ct);
        return Ok(new { matches, currentSlug = body.Slug });
    }

    /// <summary>
    /// Rewrite a misfitting JD to fit a class — its own, or a better one "Suggest class" found — and
    /// save it as a draft for the analyst. The model only maps the incumbent's work onto the class's
    /// functions; the text is the envelope's and the incumbent's, the % time is computed here, and
    /// the draft goes through the normal envelope check and assembly before it is a JD.
    /// </summary>
    [HttpPost("rewrite")]
    public async Task<IActionResult> Rewrite(FitRewriteRequest body, CancellationToken ct)
    {
        var source = await _profiles.GetBySlugAsync(body.Slug, ct);
        if (source is null || string.IsNullOrWhiteSpace(body.SourceFile))
        {
            return BadRequest(new { message = "A class and a job description are both required." });
        }

        var target = string.IsNullOrWhiteSpace(body.TargetSlug) || body.TargetSlug == body.Slug
            ? source
            : await _profiles.GetBySlugAsync(body.TargetSlug, ct);
        if (target is null)
        {
            return NotFound(new { message = $"No class found for “{body.TargetSlug}”." });
        }

        if (!_llm.HasApiKey)
        {
            return StatusCode(503, new { message = "Rewriting is unavailable — no AI provider is configured." });
        }

        var jds = await _profiles.GetJobDescriptionsAsync(source.UcJobCode, ct);
        var incumbent = jds.FirstOrDefault(j => j.SourceFile == body.SourceFile);
        if (incumbent is null)
        {
            return NotFound(new { message = "That job description is not in the corpus." });
        }

        FitRewrite rewrite;
        try
        {
            rewrite = await _rewriter.RewriteAsync(target, incumbent, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (StructuredLlmException ex)
        {
            return StatusCode(502, new { message = ex.Message });
        }

        var id = await _saved.SaveDraftAsync(
            rewrite.Inputs, target, await User.IdAsync(_db, ct), null, rewrite.DraftState, ct);

        return Ok(new
        {
            authoredJdId = id,
            slug = target.Slug,
            title = target.Title,
            rewrite.KeptFunctions,
            rewrite.DroppedFunctions,
            rewrite.CarriedDuties,
            rewrite.Outside,
        });
    }
}

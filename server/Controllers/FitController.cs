using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Ai;
using Server.Core.Domain;
using Server.Core.Intake;
using Server.Core.Profiles;

namespace Server.Controllers;

public sealed record FitSuggestRequest(string Slug, string SourceFile);

/// <summary>
/// The review-and-nudge surface: which real JDs sit badly in their assigned class, and where each
/// might belong instead.
/// </summary>
[ApiController]
[Route("api/fit")]
[Authorize(Roles = AppRoles.Analyst)]
public class FitController : ApiControllerBase
{
    private readonly IFitService _fit;
    private readonly IIntakeMatcher _matcher;
    private readonly IClassProfileRepository _profiles;
    private readonly IStructuredLlm _llm;

    public FitController(
        IFitService fit,
        IIntakeMatcher matcher,
        IClassProfileRepository profiles,
        IStructuredLlm llm)
    {
        _fit = fit;
        _matcher = matcher;
        _profiles = profiles;
        _llm = llm;
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
}

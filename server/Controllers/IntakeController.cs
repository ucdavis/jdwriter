using Server.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Ai;
using Server.Core.Domain;
using Server.Core.Intake;
using Server.Core.Profiles;
using Server.Core.Titles;

namespace Server.Controllers;

public sealed record IntakeMatchRequest(string Request);

/// <summary>
/// Natural-language intake: a manager describes the role they need, and we name the classes it
/// could be.
/// </summary>
[ApiController]
[Route("api/intake")]
[Authorize(Roles = AppRoles.Author)]
public class IntakeController : ApiControllerBase
{
    private readonly IIntakeMatcher _matcher;
    private readonly IClassProfileRepository _profiles;
    private readonly IStructuredLlm _llm;
    private readonly ITitleCodeService _titleCodes;

    public IntakeController(
        IIntakeMatcher matcher, IClassProfileRepository profiles, IStructuredLlm llm, ITitleCodeService titleCodes)
    {
        _titleCodes = titleCodes;
        _matcher = matcher;
        _profiles = profiles;
        _llm = llm;
    }

    [HttpPost("match")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(WebHardening.ModelPolicy)]
    public async Task<IActionResult> Match(IntakeMatchRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Request))
        {
            return BadRequest(new { message = "Describe the role you need before searching." });
        }

        var tooLong = RequestLimits.IntakeRequest(body.Request);
        if (tooLong is not null)
        {
            return BadRequest(new { message = tooLong });
        }

        if (!_llm.HasApiKey)
        {
            // 503 rather than 500: the deployment is misconfigured, the request was fine.
            return StatusCode(503, new { message = "Classification is unavailable — no API key is configured." });
        }

        var profiles = await _profiles.GetAllAsync(ct);
        var matches = await _matcher.MatchAsync(body.Request, profiles, ct);

        // Health Center (HC) classes are labelled, and a match with an HC twin names it.
        var titles = await _titleCodes.GetAsync(ct);
        var classes = HealthCenter.ClassesByCode(profiles.Select(p => (p.UcJobCode, p.Slug, p.Title)));
        foreach (var m in matches)
        {
            m.HealthCenterOnly = HealthCenter.IsHealthCenter(m.Title);
            m.HealthCenterTwin = HealthCenter.TwinOf(m.Title, m.UcJobCode, titles, classes);
        }

        return Ok(new { matches });
    }
}

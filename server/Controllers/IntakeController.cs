using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Ai;
using Server.Core.Domain;
using Server.Core.Intake;
using Server.Core.Profiles;

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

    public IntakeController(
        IIntakeMatcher matcher, IClassProfileRepository profiles, IStructuredLlm llm)
    {
        _matcher = matcher;
        _profiles = profiles;
        _llm = llm;
    }

    [HttpPost("match")]
    public async Task<IActionResult> Match(IntakeMatchRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Request))
        {
            return BadRequest(new { message = "Describe the role you need before searching." });
        }

        if (!_llm.HasApiKey)
        {
            // 503 rather than 500: the deployment is misconfigured, the request was fine.
            return StatusCode(503, new { message = "Classification is unavailable — no API key is configured." });
        }

        var profiles = await _profiles.GetAllAsync(ct);
        var matches = await _matcher.MatchAsync(body.Request, profiles, ct);
        return Ok(new { matches });
    }
}

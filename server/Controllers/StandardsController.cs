using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Domain;
using Server.Core.Profiles;
using Server.Core.Standards;

namespace Server.Controllers;

/// <summary>The official UC job standard behind a class, when one exists.</summary>
[ApiController]
[Route("api/standards")]
[Authorize(Roles = AppRoles.Analyst)]
public class StandardsController : ApiControllerBase
{
    private readonly IStandardsStore _standards;
    private readonly IClassProfileRepository _profiles;

    public StandardsController(IStandardsStore standards, IClassProfileRepository profiles)
    {
        _standards = standards;
        _profiles = profiles;
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Get(string slug, CancellationToken ct)
    {
        var profile = await _profiles.GetBySlugAsync(slug, ct);
        if (profile is null)
        {
            return NotFound(new { message = $"No class found for “{slug}”." });
        }

        // Null is a NORMAL answer here, not an error: the workbooks cover 19 families, so most
        // classes legitimately have no published standard.
        var index = await _standards.GetIndexAsync(ct);
        return Ok(index.ForTitle(profile.Title));
    }
}

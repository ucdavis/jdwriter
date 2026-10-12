using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Domain;
using Server.Core.Profiles;
using Server.Core.Standards;

namespace Server.Controllers;

/// <summary>The official UC job standard behind a class, when one exists.</summary>
[ApiController]
[Route("api/standards")]
[Authorize(Roles = AppRoles.Admin)]
public class StandardsController : ApiControllerBase
{
    private readonly IStandardsStore _standards;
    private readonly IClassProfileRepository _profiles;
    private readonly StandardsDirectory _directory;

    public StandardsController(IStandardsStore standards, IClassProfileRepository profiles, StandardsDirectory directory)
    {
        _standards = standards;
        _profiles = profiles;
        _directory = directory;
    }

    /// <summary>Every job standard, with the job code and class each is attached to.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(new { standards = await _directory.ListAsync(ct) });

    /// <summary>One standard, whole, by its title.</summary>
    [HttpGet("by-title")]
    public async Task<IActionResult> ByTitle([FromQuery] string title, CancellationToken ct)
    {
        var standard = await _directory.GetAsync(title, ct);
        return standard is null ? NotFound(new { message = $"No standard titled “{title}”." }) : Ok(standard);
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
        return Ok(index.For(profile.Title, profile.UcJobCode));
    }
}

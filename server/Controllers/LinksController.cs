using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Domain;

namespace Server.Controllers;

/// <summary>
/// Where JDWriter hands off to other CAES People tools. Configuration, not code: the workforce
/// management tool (CTHULHU) has no production address yet, and a link appears only once
/// <c>Wfm:Url</c> (WFM_URL in deployment) is set.
/// </summary>
[ApiController]
[Route("api/links")]
[Authorize(Roles = AppRoles.Author)]
public class LinksController(IConfiguration configuration) : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var wfm = configuration["Wfm:Url"];
        return Ok(new { wfmUrl = Uri.TryCreate(wfm, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? wfm : null });
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Domain;
using Server.Core.Ingest;

namespace Server.Controllers;

/// <summary>
/// The job descriptions in the corpus: every one, whatever brought it in, and any one whole. JDs
/// carry departments, position numbers and reporting lines, so admins only.
/// </summary>
[ApiController]
[Route("api/admin/corpus")]
[Authorize(Roles = AppRoles.Admin)]
public class CorpusController : ApiControllerBase
{
    private readonly CorpusDirectory _directory;

    public CorpusController(CorpusDirectory directory)
    {
        _directory = directory;
    }

    [HttpGet("jds")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(new { jds = await _directory.ListAsync(ct) });

    [HttpGet("jds/{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var jd = await _directory.GetAsync(id, ct);
        return jd is null ? NotFound(new { message = "No such job description in the corpus." }) : Ok(jd);
    }
}

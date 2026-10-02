using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Jd;
using Server.Helpers;

namespace Server.Controllers;

/// <summary>
/// Saved JDs. An author sees their own; an admin can see everyone's. Every assembly is saved by
/// <see cref="BuildController.Assemble"/>, so there is no create endpoint here.
/// </summary>
[ApiController]
[Route("api/jds")]
[Authorize(Roles = AppRoles.Author)]
public class JdsController : ApiControllerBase
{
    private readonly AuthoredJdStore _store;
    private readonly AppDbContext _db;

    public JdsController(AuthoredJdStore store, AppDbContext db)
    {
        _store = store;
        _db = db;
    }

    /// <summary>
    /// The caller's saved JDs, newest first. <c>?scope=all</c> lists everyone's and is admin-only.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? scope, CancellationToken ct)
    {
        if (scope == "all")
        {
            if (!User.IsInRole(AppRoles.Admin))
            {
                return Forbid();
            }

            return Ok(new { jds = await _store.ListAsync(null, ct) });
        }

        var me = await User.IdAsync(_db, ct);
        return Ok(new { jds = me == null ? [] : await _store.ListAsync(me, ct) });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var found = await _store.GetAsync(id, ct);
        // Someone else's JD reads as not found rather than forbidden, so ids cannot be probed.
        if (found == null
            || (found.Value.OwnerId != await User.IdAsync(_db, ct) && !User.IsInRole(AppRoles.Admin)))
        {
            return NotFound(new { message = "That job description was not found." });
        }

        return Ok(found.Value.Jd);
    }

    /// <summary>Delete one of your saved JDs (an admin may delete any). 404 for anyone else's.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await _store.DeleteAsync(id, await User.IdAsync(_db, ct), User.IsInRole(AppRoles.Admin), ct);
        return deleted
            ? Ok(new { ok = true })
            : NotFound(new { message = "That job description was not found." });
    }
}

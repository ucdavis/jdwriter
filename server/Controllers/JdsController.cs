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

    /// <summary>
    /// The JD as a Word document — the same sections as the PDF. Same access as reading it, and
    /// refused while time is unallocated, as the PDF is: a .docx that looks final but is not
    /// publishable would travel further than the screen that says so.
    /// </summary>
    [HttpGet("{id:int}/docx")]
    public async Task<IActionResult> Docx(int id, CancellationToken ct)
    {
        var (jd, refusal) = await PublishableAsync(id, ct);
        return refusal ?? File(
            JdDocx.Build(jd!),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            JdDocx.FileName(jd!));
    }

    /// <summary>
    /// The JD as JDWriter hands it to the workforce management tool (format jdwriter.jd, version 1;
    /// docs/WFM-HANDOFF.md). WFM fetches this from the user's browser on the same host, so the
    /// user's own JDWriter session authorizes it and the usual access rule applies.
    /// </summary>
    [HttpGet("{id:int}/handoff")]
    public async Task<IActionResult> Handoff(int id, CancellationToken ct)
    {
        var (jd, refusal) = await PublishableAsync(id, ct);
        return refusal ?? Ok(JdHandoffBuilder.Build(jd!, AppBaseUrl(), DateTimeOffset.UtcNow));
    }

    /// <summary>The JD as Markdown, for the HR package. Same sections and order as the PDF and Word file.</summary>
    [HttpGet("{id:int}/markdown")]
    public async Task<IActionResult> Markdown(int id, CancellationToken ct)
    {
        var (jd, refusal) = await PublishableAsync(id, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        var name = Path.ChangeExtension(JdDocx.FileName(jd!), ".md");
        return File(System.Text.Encoding.UTF8.GetBytes(JdHandoffBuilder.Markdown(jd!)), "text/markdown; charset=utf-8", name);
    }

    /// <summary>
    /// A JD the caller may read and that is finished. Someone else's reads as not found rather than
    /// forbidden, so ids cannot be probed; one with unallocated time is refused, because every copy
    /// of a JD that leaves the app should be one that could be published.
    /// </summary>
    private async Task<(SavedJd? Jd, IActionResult? Refusal)> PublishableAsync(int id, CancellationToken ct)
    {
        var found = await _store.GetAsync(id, ct);
        if (found == null
            || (found.Value.OwnerId != await User.IdAsync(_db, ct) && !User.IsInRole(AppRoles.Admin)))
        {
            return (null, NotFound(new { message = "That job description was not found." }));
        }

        var jd = found.Value.Jd;
        if (!jd.CanPublish)
        {
            return (null, BadRequest(new { message = $"{jd.UnallocatedPct}% of time is unallocated, so this JD is not publishable yet." }));
        }

        return (jd, null);
    }

    /// <summary>JDWriter's absolute root as this request reached it, mount point included.</summary>
    private string AppBaseUrl() => $"{Request.Scheme}://{Request.Host}{Request.PathBase}";

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

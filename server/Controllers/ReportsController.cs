using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;

namespace Server.Controllers;

/// <summary>
/// JDWriter's own support queue: anyone signed in sends a bug report, request or question from the
/// footer, and the app's admins work them under Back end → Reports. CRU hosts the app but doesn't
/// take its support (decided 2026-10-28), so nothing here goes to their ticket system. Reports live
/// in the database only and are never logged — a message may quote a job description.
/// </summary>
// Under the base's api/reports; the admins' inbox is under api/admin/reports, with the rest of the back end.
[ApiController]
[Authorize(Roles = AppRoles.Author)]
public class ReportsController : ApiControllerBase
{
    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db) => _db = db;

    public sealed record NewReport(string Kind, string Message, string Page);

    public sealed record ReportStatus(bool Resolved, string? Note);

    [HttpPost]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(WebHardening.ModelPolicy)]
    public async Task<IActionResult> Send(NewReport body, CancellationToken ct)
    {
        var message = (body.Message ?? "").Trim();
        if (message.Length == 0)
        {
            return BadRequest(new { message = "Say what happened, or what you need." });
        }

        if (message.Length > ProblemReport.MaxMessage)
        {
            return BadRequest(new { message = $"Keep it under {ProblemReport.MaxMessage:N0} characters." });
        }

        var kind = ProblemReport.Kinds.FirstOrDefault(k => k.Equals(body.Kind?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Bug";
        // The path alone: a query string could carry search text, and isn't needed to find the page.
        var page = (body.Page ?? "").Split('?', '#')[0].Trim();
        _db.ProblemReports.Add(new ProblemReport
        {
            Kind = kind,
            Message = message,
            Page = page.Length > ProblemReport.MaxPage ? page[..ProblemReport.MaxPage] : page,
            Browser = Clip(Request.Headers.UserAgent.ToString(), ProblemReport.MaxBrowser),
            CreatedByUserId = await User.IdAsync(_db, ct),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        return Ok(new { sent = true });
    }

    /// <summary>Reports, newest first: open ones, resolved ones, or all.</summary>
    [HttpGet("/api/admin/reports")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        var q = _db.ProblemReports.AsNoTracking();
        if (status == "open")
        {
            q = q.Where(r => !r.Resolved);
        }
        else if (status == "resolved")
        {
            q = q.Where(r => r.Resolved);
        }

        var reports = await q.OrderByDescending(r => r.CreatedAt).Take(500)
            .Select(r => new
            {
                r.Id,
                r.Kind,
                r.Message,
                r.Page,
                r.Browser,
                r.CreatedAt,
                From = r.CreatedBy == null ? null : (r.CreatedBy.DisplayName ?? r.CreatedBy.Email ?? r.CreatedBy.LoginId),
                FromEmail = r.CreatedBy == null ? null : r.CreatedBy.Email,
                r.Resolved,
                r.ResolvedAt,
                r.Note,
            })
            .ToListAsync(ct);
        var open = await _db.ProblemReports.CountAsync(r => !r.Resolved, ct);
        return Ok(new { reports, open });
    }

    /// <summary>How many reports are open: the back end's badge.</summary>
    [HttpGet("/api/admin/reports/open-count")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> OpenCount(CancellationToken ct) =>
        Ok(new { open = await _db.ProblemReports.CountAsync(r => !r.Resolved, ct) });

    /// <summary>Approve an access request: its sender joins the allow-list, and it's resolved.</summary>
    [HttpPost("/api/admin/reports/{id:int}/approve-access")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> ApproveAccess(int id, [FromServices] Server.Core.Access.AppAccess access, CancellationToken ct)
    {
        var report = await _db.ProblemReports.Include(r => r.CreatedBy).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (report is null || report.Kind != ProblemReport.Access)
        {
            return NotFound(new { message = "No such access request." });
        }

        var login = report.CreatedBy?.LoginId;
        if (string.IsNullOrEmpty(login))
        {
            return BadRequest(new { message = "This request has no campus login to approve; add them by login under Settings → Who can use JDWriter." });
        }

        await access.GrantAsync(login, await User.IdAsync(_db, ct), ct);
        return Ok(new { loginId = login });
    }

    /// <summary>Resolve or reopen a report, with the admin's note.</summary>
    [HttpPost("/api/admin/reports/{id:int}/status")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> SetStatus(int id, ReportStatus body, CancellationToken ct)
    {
        var report = await _db.ProblemReports.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (report is null)
        {
            return NotFound(new { message = "No such report." });
        }

        var note = (body.Note ?? report.Note).Trim();
        if (note.Length > ProblemReport.MaxNote)
        {
            return BadRequest(new { message = $"Keep the note under {ProblemReport.MaxNote:N0} characters." });
        }

        report.Resolved = body.Resolved;
        report.ResolvedAt = body.Resolved ? DateTimeOffset.UtcNow : null;
        report.Note = note;
        await _db.SaveChangesAsync(ct);
        return Ok(new { report.Id, report.Resolved, report.ResolvedAt, report.Note });
    }

    private static string Clip(string s, int max) => s.Length > max ? s[..max] : s;
}

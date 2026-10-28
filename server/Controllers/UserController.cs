using Server.Helpers;
using Server.Core.Domain;
using Server.Core.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Server.Controllers;

/// <summary>
/// The signed-in person's own account. Open to anyone signed in — including someone not on the
/// allow-list, who uses it to see that they lack access and to ask for it.
/// </summary>
public class UserController : ApiControllerBase
{
    private const int MaxRequest = 1000;

    private readonly AppDbContext _db;

    public UserController(AppDbContext db) => _db = db;

    public sealed record AccessRequestBody(string? Message);

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userName = User.FindFirst("name")?.Value;
        var userEmail = User.FindFirst("preferred_username")?.Value;
        var iamId = User.FindFirst("ucdPersonIAMID")?.Value;

        var userRoles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        if (userId == null)
        {
            return Unauthorized();
        }

        // Not on the allow-list: say whether they've already asked, so the page can say so.
        DateTimeOffset? accessRequestedAt = null;
        if (!userRoles.Contains(AppRoles.Author))
        {
            accessRequestedAt = await OpenRequest(userId)
                .Select(r => (DateTimeOffset?)r.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        var userInfo = new
        {
            Id = userId,
            Name = userName,
            Email = userEmail,
            IamId = iamId,
            Roles = userRoles,
            AccessRequestedAt = accessRequestedAt,
        };

        return Ok(userInfo);
    }

    /// <summary>
    /// Ask to use JDWriter. Lands in the admins' Reports inbox as an access request, where one click
    /// approves it. Asking again while a request is open updates it rather than adding another.
    /// </summary>
    [HttpPost("access-request")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(WebHardening.ModelPolicy)]
    public async Task<IActionResult> RequestAccess(AccessRequestBody body, CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            return Unauthorized();
        }

        if (User.IsInRole(AppRoles.Author))
        {
            return Ok(new { requestedAt = (DateTimeOffset?)null, hasAccess = true });
        }

        var message = (body.Message ?? "").Trim();
        if (message.Length > MaxRequest)
        {
            return BadRequest(new { message = $"Keep it under {MaxRequest:N0} characters." });
        }

        var request = await OpenRequest(userId).FirstOrDefaultAsync(ct);
        if (request is null)
        {
            request = new ProblemReport
            {
                Kind = ProblemReport.Access,
                Page = "/",
                CreatedByUserId = await User.IdAsync(_db, ct),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _db.ProblemReports.Add(request);
        }

        request.Message = message.Length > 0 ? message : "Requesting access to JDWriter.";
        var browser = Request.Headers.UserAgent.ToString();
        request.Browser = browser.Length > ProblemReport.MaxBrowser ? browser[..ProblemReport.MaxBrowser] : browser;
        await _db.SaveChangesAsync(ct);
        return Ok(new { requestedAt = request.CreatedAt, hasAccess = false });
    }

    private IQueryable<ProblemReport> OpenRequest(string nameIdentifier) =>
        _db.ProblemReports.Where(r => r.Kind == ProblemReport.Access && !r.Resolved
                                      && r.CreatedBy != null && r.CreatedBy.NameIdentifier == nameIdentifier);
}

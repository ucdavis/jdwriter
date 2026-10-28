using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Core.Access;

/// <summary>One approved login, as the settings screen shows it.</summary>
public sealed class AccessEntry
{
    public string LoginId { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public string? GrantedBy { get; set; }
    public DateTimeOffset? GrantedAt { get; set; }
}

/// <summary>Someone who has signed in but isn't approved: who they are, and whether they've asked.</summary>
public sealed class WaitingEntry
{
    public string LoginId { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
}

/// <summary>
/// Who may use JDWriter (decided 2026-10-28: gated in the app, not in Azure). Access is the admin
/// list plus the approved logins; anyone else who signs in gets no role, sees only a page to ask
/// for access, and every endpoint refuses them — they all require Author or Admin. Like the admin
/// list, it is keyed by campus login and read on every request, so approving or removing someone
/// takes effect on their next page.
/// </summary>
public sealed class AppAccess
{
    private readonly AppDbContext _db;
    private readonly AdminAccess _admins;

    public AppAccess(AppDbContext db, AdminAccess admins)
    {
        _db = db;
        _admins = admins;
    }

    public async Task<bool> HasAccessAsync(string? loginId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(loginId))
        {
            return false;
        }

        return await _admins.IsAdminAsync(loginId, ct)
               || await _db.AccessGrants.AnyAsync(g => g.LoginId == loginId, ct);
    }

    /// <summary>The approved logins, and everyone who has signed in without access.</summary>
    public async Task<(List<AccessEntry> Approved, List<WaitingEntry> Waiting)> ListAsync(CancellationToken ct = default)
    {
        var approved = await _db.AccessGrants.AsNoTracking()
            .OrderBy(g => g.LoginId)
            .Select(g => new AccessEntry
            {
                LoginId = g.LoginId,
                GrantedAt = g.GrantedAt,
                GrantedBy = g.GrantedBy != null ? (g.GrantedBy.DisplayName ?? g.GrantedBy.LoginId) : null,
            })
            .ToListAsync(ct);
        var users = (await _db.AppUsers.AsNoTracking().Where(u => u.LoginId != null).ToListAsync(ct))
            .GroupBy(u => u.LoginId!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(u => u.LastSeenAt).First(), StringComparer.Ordinal);
        foreach (var a in approved)
        {
            if (users.TryGetValue(a.LoginId, out var u))
            {
                a.DisplayName = u.DisplayName;
                a.Email = u.Email;
                a.LastSeenAt = u.LastSeenAt;
            }
        }

        var granted = approved.Select(a => a.LoginId).ToHashSet(StringComparer.Ordinal);
        var admins = (await _admins.ListAsync(ct)).Select(a => a.LoginId).ToHashSet(StringComparer.Ordinal);
        var requests = await _db.ProblemReports.AsNoTracking()
            .Where(r => r.Kind == ProblemReport.Access && !r.Resolved && r.CreatedBy != null && r.CreatedBy.LoginId != null)
            .Select(r => new { r.CreatedBy!.LoginId, r.CreatedAt })
            .ToListAsync(ct);
        var asked = requests.GroupBy(r => r.LoginId!).ToDictionary(g => g.Key, g => g.Max(r => r.CreatedAt), StringComparer.Ordinal);

        var waiting = users.Values
            .Where(u => !granted.Contains(u.LoginId!) && !admins.Contains(u.LoginId!))
            .Select(u => new WaitingEntry
            {
                LoginId = u.LoginId!,
                DisplayName = u.DisplayName,
                Email = u.Email,
                LastSeenAt = u.LastSeenAt,
                RequestedAt = asked.TryGetValue(u.LoginId!, out var at) ? at : null,
            })
            // Those who asked first, newest asks first; then everyone else, most recently seen first.
            .OrderByDescending(w => w.RequestedAt.HasValue)
            .ThenByDescending(w => w.RequestedAt)
            .ThenByDescending(w => w.LastSeenAt)
            .ToList();
        return (approved, waiting);
    }

    /// <summary>
    /// Approve a campus login. They need not have signed in yet. Any open access request of theirs
    /// is resolved with it.
    /// </summary>
    public async Task<string> GrantAsync(string input, int? grantedByUserId, CancellationToken ct = default)
    {
        var login = CampusLogin.Normalize(input)
                    ?? throw new ArgumentException($"“{input}” is not a UC Davis login ID. Use the campus login, e.g. “rsmith”.");

        if (!await _db.AccessGrants.AnyAsync(g => g.LoginId == login, ct))
        {
            _db.AccessGrants.Add(new AccessGrant { LoginId = login, GrantedByUserId = grantedByUserId, GrantedAt = DateTimeOffset.UtcNow });
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var r in await _db.ProblemReports
                     .Where(r => r.Kind == ProblemReport.Access && !r.Resolved && r.CreatedBy != null && r.CreatedBy.LoginId == login)
                     .ToListAsync(ct))
        {
            r.Resolved = true;
            r.ResolvedAt = now;
            r.Note = r.Note.Length > 0 ? r.Note : "Access granted.";
        }

        await _db.SaveChangesAsync(ct);
        return login;
    }

    /// <summary>Remove an approval. An admin keeps access regardless; that's the admin list's to change.</summary>
    public async Task RevokeAsync(string loginId, CancellationToken ct = default)
    {
        var login = CampusLogin.Normalize(loginId) ?? loginId;
        var grant = await _db.AccessGrants.FirstOrDefaultAsync(g => g.LoginId == login, ct);
        if (grant is not null)
        {
            _db.AccessGrants.Remove(grant);
            await _db.SaveChangesAsync(ct);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Core.Access;

/// <summary>One row of the admin list as the settings screen shows it.</summary>
public sealed class AdminEntry
{
    public string LoginId { get; set; } = "";

    /// <summary>
    /// True for admins named in configuration (<c>Admin:BootstrapLoginIds</c>). They cannot be
    /// removed from the app, which is what makes locking every admin out impossible.
    /// </summary>
    public bool FromConfiguration { get; set; }

    public string? GrantedBy { get; set; }
    public DateTimeOffset? GrantedAt { get; set; }

    /// <summary>From the user record, once this person has signed in at least once.</summary>
    public string? DisplayName { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
}

/// <summary>
/// The admin whitelist. Admin is the union of the configured bootstrap logins and the grants made
/// in the app; everyone else who signs in is an Author.
/// </summary>
public sealed class AdminAccess
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AdminAccess(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    /// <summary>Admins named in configuration, normalized. Invalid entries are ignored.</summary>
    public IReadOnlyList<string> ConfiguredAdmins =>
        (_config["Admin:BootstrapLoginIds"] ?? "")
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(CampusLogin.Normalize)
            .Where(x => x != null)
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public async Task<bool> IsAdminAsync(string? loginId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(loginId))
        {
            return false;
        }

        return ConfiguredAdmins.Contains(loginId)
               || await _db.AdminGrants.AnyAsync(g => g.LoginId == loginId, ct);
    }

    public async Task<List<AdminEntry>> ListAsync(CancellationToken ct = default)
    {
        var grants = await _db.AdminGrants.AsNoTracking()
            .Select(g => new
            {
                g.LoginId,
                g.GrantedAt,
                GrantedBy = g.GrantedBy != null ? (g.GrantedBy.DisplayName ?? g.GrantedBy.LoginId) : null,
            })
            .ToListAsync(ct);

        var configured = ConfiguredAdmins;
        var logins = configured.Concat(grants.Select(g => g.LoginId)).Distinct(StringComparer.Ordinal).ToList();
        var users = await _db.AppUsers.AsNoTracking()
            .Where(u => u.LoginId != null && logins.Contains(u.LoginId))
            .Select(u => new { u.LoginId, u.DisplayName, u.LastSeenAt })
            .ToListAsync(ct);

        return logins
            .Select(login =>
            {
                var grant = grants.FirstOrDefault(g => g.LoginId == login);
                var user = users.FirstOrDefault(u => u.LoginId == login);
                return new AdminEntry
                {
                    LoginId = login,
                    FromConfiguration = configured.Contains(login),
                    GrantedBy = grant?.GrantedBy,
                    GrantedAt = grant?.GrantedAt,
                    DisplayName = user?.DisplayName,
                    LastSeenAt = user?.LastSeenAt,
                };
            })
            .OrderBy(e => e.LoginId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Whitelist a campus login. Idempotent. Throws <see cref="ArgumentException"/> with a
    /// user-facing message when the input is not a campus login id.
    /// </summary>
    public async Task<string> GrantAsync(string input, int? grantedByUserId, CancellationToken ct = default)
    {
        var login = CampusLogin.Normalize(input)
                    ?? throw new ArgumentException(
                        $"“{input}” is not a UC Davis login ID. Use the campus login, e.g. “ndlewis”.");

        if (!await _db.AdminGrants.AnyAsync(g => g.LoginId == login, ct))
        {
            _db.AdminGrants.Add(new AdminGrant
            {
                LoginId = login,
                GrantedByUserId = grantedByUserId,
                GrantedAt = DateTimeOffset.UtcNow,
            });
            await _db.SaveChangesAsync(ct);
        }

        return login;
    }

    /// <summary>
    /// Remove a grant. Throws <see cref="InvalidOperationException"/> with a user-facing message
    /// for a configured admin (removable only in configuration) and for the acting admin removing
    /// themselves (which would end their own access mid-session; another admin has to do it).
    /// </summary>
    public async Task RevokeAsync(string loginId, string? actingLoginId = null, CancellationToken ct = default)
    {
        var login = CampusLogin.Normalize(loginId) ?? loginId;
        if (actingLoginId != null && login == actingLoginId)
        {
            throw new InvalidOperationException("You can’t remove yourself — ask another admin.");
        }

        if (ConfiguredAdmins.Contains(login))
        {
            throw new InvalidOperationException(
                $"{login} is an admin by configuration (Admin:BootstrapLoginIds) and can only be "
                + "removed there.");
        }

        var grant = await _db.AdminGrants.FirstOrDefaultAsync(g => g.LoginId == login, ct);
        if (grant != null)
        {
            _db.AdminGrants.Remove(grant);
            await _db.SaveChangesAsync(ct);
        }
    }
}

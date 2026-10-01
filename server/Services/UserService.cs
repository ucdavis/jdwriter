using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Server.Core.Domain;
using Server.Core.Access;
using Server.Core.Data;
using Server.Helpers;

namespace Server.Services;

public interface IUserService
{
    Task<ClaimsPrincipal?> UpdateUserPrincipalIfNeeded(ClaimsPrincipal principal);
}

/// <summary>
/// Resolves an application user's roles: Author for everyone who signs in, Admin for a login on
/// the admin whitelist. Derived on every request rather than stored, so revoking an admin takes
/// effect immediately.
/// </summary>
public class UserService : IUserService
{
    /// <summary>
    /// The campus login id of a signed-in user. The sandbox personas' domain counts only for
    /// identities the local sandbox scheme created, which cannot exist outside Development.
    /// </summary>
    public static string? LoginIdOf(ClaimsPrincipal principal)
    {
        string[] domains = principal.Identity?.AuthenticationType == LocalAuthentication.Scheme
            ? [CampusLogin.Domain, LocalAuthentication.PersonaDomain]
            : [CampusLogin.Domain];
        return CampusLogin.FromPrincipal(principal, domains);
    }

    private readonly ILogger<UserService> _logger;
    private readonly AppDbContext _dbContext;
    private readonly AdminAccess _admins;

    public UserService(ILogger<UserService> logger, AppDbContext dbContext, AdminAccess admins)
    {
        _logger = logger;
        _dbContext = dbContext;
        _admins = admins;
    }

    /// <summary>
    /// Record the user (creating the row on first sign-in) and work out their roles.
    ///
    /// Authoring is self-service, so everyone gets Author. Admin is granted only by the whitelist,
    /// because an admin edits the envelopes every author writes against.
    /// </summary>
    private async Task<List<string>> GetRolesForUser(ClaimsPrincipal principal, string userId)
    {
        var loginId = LoginIdOf(principal);

        var user = await _dbContext.AppUsers.FirstOrDefaultAsync(u => u.NameIdentifier == userId);
        if (user is null)
        {
            user = new AppUser
            {
                NameIdentifier = userId,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _dbContext.AppUsers.Add(user);
            _logger.LogInformation("Recorded new user {UserId} ({LoginId}).", userId, loginId);
        }

        // Refreshed every time: cheap, and on an HR system it is worth knowing who has actually
        // used this, and when.
        user.LoginId = loginId;
        user.Email = principal.FindFirst(ClaimTypes.Email)?.Value
                     ?? principal.FindFirst("preferred_username")?.Value;
        user.DisplayName = principal.Identity?.Name ?? user.DisplayName;
        // The UC Davis IAM id arrives only when the app registration is configured to release
        // that claim. Absent is normal, not an error.
        user.IamId = principal.FindFirst("ucdPersonIAMID")?.Value ?? user.IamId;
        user.LastSeenAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync();

        return await _admins.IsAdminAsync(loginId)
            ? [AppRoles.Author, AppRoles.Admin]
            : [AppRoles.Author];
    }

    public async Task<ClaimsPrincipal?> UpdateUserPrincipalIfNeeded(ClaimsPrincipal principal)
    {
        // Application roles are authoritative for both sign-in and cookie validation.
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return null; // can't update without user ID
        }

        // get user's roles
        // might want to cache w/ IMemoryCache to avoid DB hits on every request, but we'll skip that for simplicity
        var currentRoles = await GetRolesForUser(principal, userId);

        // compare roles to existing claims, only update if different
        var existingRoles = principal.Identities
            .SelectMany(identity => identity.FindAll(identity.RoleClaimType))
            .Select(claim => claim.Value)
            .ToList();
        var changed = currentRoles.Count != existingRoles.Count ||
                      currentRoles.Except(existingRoles).Any();

        if (!changed) { return null; } // no change

        // Clone each identity to preserve claim mappings and metadata without changing the input.
        var updatedPrincipal = new ClaimsPrincipal(principal.Identities.Select(identity => identity.Clone()));

        foreach (var identity in updatedPrincipal.Identities)
        {
            foreach (var roleClaim in identity.FindAll(identity.RoleClaimType).ToList())
            {
                identity.RemoveClaim(roleClaim);
            }
        }

        var primaryIdentity = (ClaimsIdentity)updatedPrincipal.Identity!;
        foreach (var role in currentRoles)
        {
            primaryIdentity.AddClaim(new Claim(primaryIdentity.RoleClaimType, role));
        }

        return updatedPrincipal;
    }
}

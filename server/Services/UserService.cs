using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Server.Core.Domain;
using Server.Core.Data;

namespace Server.Services;

public interface IUserService
{
    Task<ClaimsPrincipal?> UpdateUserPrincipalIfNeeded(ClaimsPrincipal principal);
}

/// <summary>
/// Resolves an application user's roles from the database, replacing the template's stub, which
/// handed every signed-in account the same two fake roles.
/// </summary>
public class UserService : IUserService
{
    private readonly ILogger<UserService> _logger;
    private readonly AppDbContext _dbContext;

    public UserService(ILogger<UserService> logger, AppDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Look up a user's granted roles, creating the user row on first sign-in.
    ///
    /// A first-time user gets <see cref="AppRoles.Author"/> and nothing else. Authoring is the
    /// self-service half of the product, and gating it behind a provisioning request would defeat
    /// the point. Analyst and Admin are granted deliberately, because they change what everyone
    /// else sees: an analyst curates the envelopes every author writes against, so letting authors
    /// hold that role would let one department quietly redefine a classification campus-wide.
    /// </summary>
    private async Task<List<string>> GetRolesForUser(ClaimsPrincipal principal, string userId)
    {
        var user = await _dbContext.AppUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.NameIdentifier == userId);

        if (user is null)
        {
            user = new AppUser
            {
                NameIdentifier = userId,
                Email = principal.FindFirst(ClaimTypes.Email)?.Value
                        ?? principal.FindFirst("preferred_username")?.Value,
                DisplayName = principal.Identity?.Name,
                // The UC Davis IAM id arrives only when the app registration is configured to
                // release that claim. Absent is normal, not an error.
                IamId = principal.FindFirst("ucdPersonIAMID")?.Value,
                CreatedAt = DateTimeOffset.UtcNow,
                Roles = [new AppUserRole { Role = AppRoles.Author, GrantedAt = DateTimeOffset.UtcNow }],
            };

            _dbContext.AppUsers.Add(user);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Provisioned new user {UserId} with the {Role} role.", userId, AppRoles.Author);
        }
        else
        {
            // Cheap, and worth having on an HR system: who has actually used this, and when.
            user.LastSeenAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        return user.Roles.Select(r => r.Role).ToList();
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

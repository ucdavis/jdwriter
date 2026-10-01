using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;

namespace Server.Helpers;

public static class CurrentUser
{
    /// <summary>The signed-in user's AppUser id, or null if they have no row yet.</summary>
    public static Task<int?> IdAsync(this ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var nameIdentifier = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return db.AppUsers.Where(u => u.NameIdentifier == nameIdentifier)
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync(ct);
    }
}

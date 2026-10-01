using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Server.Core.Data;
using Server.Core.Domain;

public interface IDbInitializer
{
    Task InitializeAsync(bool includeSampleData, CancellationToken cancellationToken = default);
}

public class DbInitializer : IDbInitializer
{
    private readonly AppDbContext _db;
    private readonly ILogger<DbInitializer> _logger;

    public DbInitializer(AppDbContext db, ILogger<DbInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task InitializeAsync(bool includeSampleData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Applying database migrations...");
        await _db.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Migrations applied.");

        if (includeSampleData)
        {
            await SeedDevelopmentAsync(cancellationToken);
        }
        else
        {
            await SeedProductionSafeAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Development seeding for JDWriter is a corpus load, not a handful of rows, so that belongs in
    /// the migration CLI rather than in startup. What DOES belong here is giving the fictional
    /// sandbox personas the roles needed to explore the app.
    ///
    /// Without it, local sign-in succeeds and then every surface returns 403 — which reads as a
    /// broken authorization rule rather than a user who was never granted anything. The personas
    /// only exist when local authentication is enabled, which startup already refuses outside
    /// Development, so these grants cannot reach a real environment.
    ///
    /// "sample" gets everything, so one sign-in can reach every surface. "basic" deliberately gets
    /// only Author, which is what makes it useful: it is how you check that the analyst and admin
    /// gates actually hold.
    /// </summary>
    private async Task SeedDevelopmentAsync(CancellationToken ct)
    {
        await GrantAsync("sandbox-sample", "Sample User", "sample@example.test",
            [AppRoles.Author, AppRoles.Analyst, AppRoles.Admin], ct);
        await GrantAsync("sandbox-basic", "Basic User", "basic@example.test",
            [AppRoles.Author], ct);
    }

    private async Task GrantAsync(
        string nameIdentifier, string displayName, string email, string[] roles, CancellationToken ct)
    {
        var user = await _db.AppUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.NameIdentifier == nameIdentifier, ct);

        if (user is null)
        {
            user = new AppUser
            {
                NameIdentifier = nameIdentifier,
                DisplayName = displayName,
                Email = email,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _db.AppUsers.Add(user);
        }

        // Additive: a role granted by hand during a session is not revoked on the next restart.
        foreach (var role in roles.Where(r => !user.Roles.Any(x => x.Role == r)))
        {
            user.Roles.Add(new AppUserRole { Role = role, GrantedAt = DateTimeOffset.UtcNow });
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Sandbox persona {Name} has roles {Roles}.",
            nameIdentifier, string.Join(", ", user.Roles.Select(r => r.Role)));
    }

    // Reference data that every environment needs (title codes, supersessions) will be
    // seeded here once those entities exist.
    private Task SeedProductionSafeAsync(CancellationToken ct) => Task.CompletedTask;
}

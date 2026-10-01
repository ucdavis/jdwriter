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
    /// the migration CLI rather than in startup. What DOES belong here is making the fictional
    /// "sample" persona an admin, so one local sign-in can reach every surface. "basic" is left an
    /// Author, which is what makes it useful: it is how you check the admin gates actually hold.
    ///
    /// The personas only exist when local authentication is enabled, which startup refuses outside
    /// Development, and their sign-in domain is accepted only for that scheme — so this grant
    /// cannot make anyone an admin in a real environment.
    /// </summary>
    private async Task SeedDevelopmentAsync(CancellationToken ct)
    {
        const string persona = "sample";
        if (!await _db.AdminGrants.AnyAsync(g => g.LoginId == persona, ct))
        {
            _db.AdminGrants.Add(new AdminGrant { LoginId = persona, GrantedAt = DateTimeOffset.UtcNow });
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Sandbox persona {Persona} is whitelisted as an admin.", persona);
    }

    // Reference data that every environment needs (title codes, supersessions) will be
    // seeded here once those entities exist.
    private Task SeedProductionSafeAsync(CancellationToken ct) => Task.CompletedTask;
}

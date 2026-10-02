using Microsoft.EntityFrameworkCore;

namespace Server.Core.Data;

/// <summary>
/// Reports whether the database is encrypted at rest — asked of SQL Server rather than assumed.
///
/// Uploaded HRTMS exports and the JD corpus carry UCPath position numbers and reporting lines, and
/// their protection at rest is Azure SQL's Transparent Data Encryption, which is on by default for
/// new Azure SQL databases and which nothing in this app's Bicep turns off. This makes that
/// verifiable from the Settings page instead of a line in a document.
/// </summary>
public sealed class DatabaseSecurity
{
    private readonly AppDbContext _db;

    public DatabaseSecurity(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// True or false from <c>sys.databases.is_encrypted</c>; null when the provider is not SQL
    /// Server (tests) or the question cannot be answered.
    /// </summary>
    public async Task<bool?> EncryptedAtRestAsync(CancellationToken ct = default)
    {
        if (!_db.Database.IsRelational())
        {
            return null;
        }

        try
        {
            return await _db.Database
                .SqlQueryRaw<bool>("SELECT CAST(is_encrypted AS bit) AS [Value] FROM sys.databases WHERE name = DB_NAME()")
                .SingleOrDefaultAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}

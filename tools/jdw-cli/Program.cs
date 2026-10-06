using Jdw.Cli.Migration;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Ingest;

// JDWriter maintenance CLI.
//
// Dry-run by default, matching the POC's script convention (scripts/backfill-codes.ts): the tool
// reports what it would load and writes nothing until --write is passed. A migration that commits
// on a typo is a migration nobody runs twice.

var command = args.FirstOrDefault() ?? "help";
var write = args.Contains("--write");
var scrub = args.Contains("--scrub");

if (command is "help" or "--help" or "-h")
{
    PrintUsage();
    return 0;
}

if (command != "migrate-poc")
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    Console.Error.WriteLine();
    PrintUsage();
    return 2;
}

var pocRoot = ArgValue("--poc") ?? DefaultPocRoot();
if (pocRoot is null || !Directory.Exists(pocRoot))
{
    Console.Error.WriteLine(
        $"POC repository not found{(pocRoot is null ? "" : $" at '{pocRoot}'")}. " +
        "Pass --poc <path to the JDWriter POC checkout>.");
    return 2;
}

var connection = ArgValue("--connection")
                 ?? Environment.GetEnvironmentVariable("DB_CONNECTION")
                 ?? "Server=localhost,14333;Database=AppDb;User ID=sa;Password=LocalDev123!;Encrypt=False;TrustServerCertificate=True;";

Console.WriteLine($"POC root:   {pocRoot}");
Console.WriteLine($"Mode:       {(write ? "WRITE" : "dry run")}");
Console.WriteLine($"Scrub:      {(scrub ? "yes — identifying fields replaced (test environment)" : "no — real corpus (prod only)")}");
Console.WriteLine();

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(connection, o =>
    {
        o.MigrationsAssembly("server.core");
        // The corpus load is long; the default 30s is not enough for the larger batches.
        o.CommandTimeout(300);
    })
    .Options;

await using var db = new AppDbContext(options);

try
{
    // Fail early and clearly when the schema is not there, rather than partway through a load.
    var pending = await db.Database.GetPendingMigrationsAsync();
    if (pending.Any())
    {
        Console.Error.WriteLine(
            $"The database has {pending.Count()} pending migration(s). Run the server once, or " +
            "`dotnet ef database update -p server.core -s server`, before migrating the corpus.");
        return 3;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not reach the database: {ex.Message}");
    Console.Error.WriteLine(
        "Start it with `npm run db:up` (on Apple Silicon, with Docker's Rosetta emulation on; see " +
        "docs/RUNNING-LOCALLY.md) and wait for it to report healthy.");
    return 3;
}

var migrator = new CorpusMigrator(db, pocRoot, write, Console.WriteLine, scrub ? new TestDataScrubber() : null);
var started = DateTimeOffset.UtcNow;
var report = await migrator.RunAsync();
var elapsed = DateTimeOffset.UtcNow - started;

Console.WriteLine();
Console.WriteLine(report.ToString());
Console.WriteLine($"  elapsed: {elapsed.TotalSeconds:F1}s");

return report.Problems.Count > 0 ? 1 : 0;

string? ArgValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// The POC normally sits beside this checkout. Probing for it keeps the common case argument-free
// while --poc stays available for anything else.
static string? DefaultPocRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "..", "JDWriter");
        if (Directory.Exists(Path.Combine(candidate, "data", "profiles")))
        {
            return Path.GetFullPath(candidate);
        }

        dir = dir.Parent;
    }

    return null;
}

static void PrintUsage()
{
    Console.WriteLine("""
        jdw-cli — JDWriter maintenance commands

        Usage:
          jdw-cli migrate-poc [--write] [--scrub] [--poc <path>] [--connection <conn>]

        migrate-poc
          Loads the POC's title reference, job standards, JD corpus and class profiles into SQL.
          Dry-run by default: reports what it would load and writes nothing.

          --write        Commit the load. Without it, nothing is written.
          --scrub        Replace position numbers, reports-to, JD numbers, departments and export
                         file names with synthetic values. For the Azure TEST environment; the
                         real corpus goes to prod only.
          --poc <path>   The JDWriter POC checkout. Defaults to a sibling 'JDWriter' directory.
          --connection   Override the connection string. Defaults to DB_CONNECTION, then to the
                         local SQL container on port 14333.

          The JD corpus is re-parsed from the HRTMS exports rather than imported from
          data/parsed/records.json, which is stale. Class profiles are migrated as they stand:
          their envelope, consolidation and coverage blocks came from model calls and are not
          recomputed.
        """);
}

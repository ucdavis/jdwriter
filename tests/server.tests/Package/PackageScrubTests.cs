using System.IO.Compression;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Package;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Tests.Package;

/// <summary>
/// A package scrubbed for the test environment carries nothing that identifies a position, stays
/// stable from one export to the next so test can merge it, and only lands where it belongs. The
/// "real" values here are synthetic stand-ins shaped like real ones.
/// </summary>
public class PackageScrubTests
{
    private const string Position = "40012345";
    private const string Boss = "40054321";
    private const string ExportName = "LAB AST 1/LAB AST 1 (40012345)_[0005]_Job Description.html";

    private static readonly PackageOptions Dev = PackageOptions.For(isDevelopment: true, acceptsRealData: null);
    private static readonly PackageOptions Test = PackageOptions.For(isDevelopment: false, acceptsRealData: null);
    private static readonly PackageOptions Prod = PackageOptions.For(isDevelopment: false, acceptsRealData: true);

    private static (AppDbContext Db, EnvironmentPackages Packages, PackageImporter Importer) Env(PackageOptions options)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"scrub_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var packages = new EnvironmentPackages(db);
        return (db, packages, new PackageImporter(db, packages, new TitleCodeService(db, cache), new StandardsStore(db, cache), options));
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        db.CorpusUploads.Add(new CorpusUpload { FileName = ExportName, Sha256 = new string('a', 64), Content = [1], Status = CorpusUploadStatus.Ingested });
        db.JobDescriptions.AddRange(
            new JobDescription
            {
                SourceFile = ExportName, UcJobCode = "009605", UcJobTitle = "LAB AST 1", Origin = CorpusOrigin.Export,
                UcPathPositionNumber = Position, ReportsToPositionNumber = Boss, JdNumber = "100001",
                DepartmentName = "Plant Sciences", DepartmentCode = "030000", Division = "College",
                WorkingTitle = $"Greenhouse Technician ({Position})",
                Responsibilities = [new JdResponsibility { FunctionName = "PLANT CARE", Pct = 100, Duties = [new JdDuty { Text = $"Reports to position {Boss}." }] }],
            },
            // An app-given name: a hash that happens to hold eight digits in a row.
            new JobDescription { SourceFile = "import/b12345678bbbbbbb", UcJobCode = "009605", Origin = CorpusOrigin.Imported, UcPathPositionNumber = Boss });
        db.ClassProfiles.Add(new ClassProfile
        {
            Slug = "009605-lab-ast-1", UcJobCode = "009605", Title = "Lab Ast 1",
            SourceFiles = [new ProfileSourceFile { SourceFile = ExportName }, new ProfileSourceFile { SourceFile = "import/b12345678bbbbbbb" }],
            Coverage = new CoverageReport { PerJd = [new JdCoverage { SourceFile = ExportName }] },
            Functions = [new ProfileFunction { Name = "PLANT CARE", SampleDuties = [new ProfileFunctionSampleDuty { Text = $"Reports to position {Boss}." }] }],
        });
        db.JdFitReviews.Add(new JdFitReview { SourceFile = ExportName, UcJobCode = "009605", ReviewedAt = DateTimeOffset.UtcNow, Note = $"Reviewed {Position}." });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<string> JsonAsync(byte[] bytes)
    {
        await using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        var json = (JsonObject)(await JsonNode.ParseAsync(gzip))!;
        json.Remove("exportedAt");
        return json.ToJsonString();
    }

    [Fact]
    public async Task A_scrubbed_package_carries_nothing_that_identifies_a_position()
    {
        var (db, packages, _) = Env(Dev);
        await SeedAsync(db);

        var json = await JsonAsync(await packages.ExportAsync(scrubbed: true));

        json.Should().NotContain(Position).And.NotContain(Boss).And.NotContain("100001")
            .And.NotContain("Plant Sciences").And.NotContain("030000").And.NotContain("College");
        var doc = JsonNode.Parse(json)!;
        doc["scrubbed"]!.GetValue<bool>().Should().BeTrue();
        doc["originals"]!.AsArray().Should().BeEmpty("the originals are the real documents");

        // The export's name is replaced the same way wherever it's recorded, so the class still finds its JD.
        var jdName = doc["jobDescriptions"]!.AsArray().Select(j => j!["sourceFile"]!.GetValue<string>()).Single(n => n.StartsWith("LAB AST 1/"));
        jdName.Should().MatchRegex(@"^LAB AST 1/JD-[a-p]{12}\.HTML$");
        var importName = doc["jobDescriptions"]!.AsArray().Select(j => j!["sourceFile"]!.GetValue<string>()).Single(n => n.StartsWith("import/"));
        importName.Should().MatchRegex("^import/[a-p]{16}$");
        var cls = doc["classes"]![0]!;
        cls["sourceFiles"]!.AsArray().Select(f => f!["sourceFile"]!.GetValue<string>()).Should().Equal(jdName, importName);
        cls["coverage"]!["perJd"]![0]!["sourceFile"]!.GetValue<string>().Should().Be(jdName);
        doc["fitReviews"]![0]!["sourceFile"]!.GetValue<string>().Should().Be(jdName, "a review follows its JD's stand-in");

        // A position maps to the same stand-in wherever it appears, so "reports to" still points at its JD.
        var jds = doc["jobDescriptions"]!.AsArray();
        var reportsTo = jds.Single(j => j!["origin"]!.GetValue<string>() == "Export")!["reportsToPositionNumber"]!.GetValue<string>();
        jds.Single(j => j!["origin"]!.GetValue<string>() == "Imported")!["ucPathPositionNumber"]!.GetValue<string>().Should().Be(reportsTo);
        json.Should().Contain($"Reports to position {reportsTo}.");
        System.Text.RegularExpressions.Regex.IsMatch(json, @"(?<!\d)\d{8}(?!\d)").Should().BeFalse("no stand-in looks like a position number");
    }

    [Fact]
    public async Task Scrubbing_changes_nothing_stored_and_is_the_same_in_every_export()
    {
        var (db, packages, _) = Env(Dev);
        await SeedAsync(db);

        var first = await JsonAsync(await packages.ExportAsync(scrubbed: true));
        var second = await JsonAsync(await packages.ExportAsync(scrubbed: true));

        second.Should().Be(first, "the test environment merges each export by these names");
        var jd = await db.JobDescriptions.SingleAsync(j => j.Origin == CorpusOrigin.Export);
        jd.UcPathPositionNumber.Should().Be(Position);
        jd.SourceFile.Should().Be(ExportName);
        (await db.CorpusUploads.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task The_test_environment_takes_scrubbed_packages_only_and_prod_real_ones_only()
    {
        var (source, packages, _) = Env(Dev);
        await SeedAsync(source);
        var real = await packages.ExportAsync(scrubbed: false);
        var scrubbed = await packages.ExportAsync(scrubbed: true);

        var test = Env(Test).Importer;
        await test.Invoking(i => i.ReadAsync(new MemoryStream(real))).Should().ThrowAsync<PackageRefusedException>()
            .WithMessage("*only scrubbed packages*");
        (await test.ReadAsync(new MemoryStream(scrubbed))).Scrubbed.Should().BeTrue();

        var prod = Env(Prod).Importer;
        await prod.Invoking(i => i.ReadAsync(new MemoryStream(scrubbed))).Should().ThrowAsync<PackageRefusedException>()
            .WithMessage("*scrubbed for testing*");
        (await prod.ReadAsync(new MemoryStream(real))).Scrubbed.Should().BeFalse();
    }

    [Fact]
    public async Task A_second_scrubbed_export_merges_into_test_without_changes()
    {
        var (source, packages, _) = Env(Dev);
        await SeedAsync(source);
        var (_, _, test) = Env(Test);
        await test.ApplyAsync(await test.ReadAsync(new MemoryStream(await packages.ExportAsync(scrubbed: true))));

        var again = await test.PreviewAsync(await test.ReadAsync(new MemoryStream(await packages.ExportAsync(scrubbed: true))));

        again.Sections.Should().OnlyContain(s => s.Added == 0 && s.Updated == 0 && s.OnlyHere == 0);
    }

    [Theory]
    [InlineData(true, null, true, true)]
    [InlineData(false, null, false, true)]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, true, false)]
    public void Which_packages_an_environment_takes(bool development, bool? acceptsRealData, bool real, bool scrubbed)
    {
        var options = PackageOptions.For(development, acceptsRealData);
        options.AcceptsRealData.Should().Be(real);
        options.AcceptsScrubbed.Should().Be(scrubbed);
    }
}

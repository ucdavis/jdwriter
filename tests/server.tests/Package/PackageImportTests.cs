using System.IO.Compression;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Package;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Tests.Package;

/// <summary>
/// Importing an environment package: a round trip reproduces an environment exactly; on a second
/// environment, the newer change wins, nothing is removed for being absent, and a replaced class keeps
/// its row. Synthetic records only.
/// </summary>
public class PackageImportTests
{
    private static readonly DateTimeOffset Then = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Then.AddDays(10);

    private sealed record Env(AppDbContext Db, EnvironmentPackages Packages, PackageImporter Importer, string Name);

    private static Env NewEnv()
    {
        var name = $"pkgimport_{Guid.NewGuid():N}";
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var packages = new EnvironmentPackages(db);
        return new Env(db, packages, new PackageImporter(db, packages, new TitleCodeService(db, cache), new StandardsStore(db, cache), new PackageOptions { AcceptsRealData = true, AcceptsScrubbed = true }), name);
    }

    private static TitleCode Tc(string code, string title) => new()
    {
        Code = code, Title = title, Source = "both",
        TitleKey = TitleNormalizer.TitleKey(title), TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
    };

    private static ClassProfile Class(string slug, string code, string title, string summary, DateTimeOffset? at) => new()
    {
        Slug = slug, UcJobCode = code, Title = title, UpdatedAt = at, CorpusSize = 1,
        Envelope = new JobEnvelope
        {
            Summary = summary,
            KeyResponsibilities = [new EnvelopeResponsibility { FunctionName = "PLANT CARE", PctTime = 100, Duties = [new EnvelopeDuty { Ordinal = 0, Text = "First." }, new EnvelopeDuty { Ordinal = 1, Text = "Second." }] }],
            Items = [new EnvelopeListItem { Kind = EnvelopeListKind.Education, Text = "High school." }],
        },
        Coverage = new CoverageReport { PerJd = [new JdCoverage { SourceFile = "import/aaaaaaaaaaaaaaaa", Uncovered = [new JdCoverageUncovered { Name = "Odd duty.", Pct = 10 }] }] },
        ConsolidatedFunctions = [new ConsolidatedFunction { Name = "Plant care", Members = [new ConsolidatedFunctionMember { Text = "PLANT CARE" }] }],
        SourceFiles = [new ProfileSourceFile { SourceFile = "import/aaaaaaaaaaaaaaaa" }],
    };

    private static JobDescription Jd(string sourceFile, string position, DateTimeOffset? addedAt, string duty = "Waters plants.") => new()
    {
        SourceFile = sourceFile, UcJobCode = "009605", UcJobTitle = "LAB AST 1", UcPathPositionNumber = position,
        Origin = CorpusOrigin.Imported, AddedAt = addedAt,
        Responsibilities = [new JdResponsibility { FunctionName = "PLANT CARE", Pct = 100, Duties = [new JdDuty { Text = duty }] }],
    };

    private static async Task SeedAsync(AppDbContext db)
    {
        db.TitleCodes.AddRange(Tc("009605", "LAB AST 1"), Tc("004724", "FARM LABORER"));
        db.CodeActiveOverrides.Add(new CodeActiveOverride { UcJobCode = "004724", IsActive = false, DecidedAt = Then });
        db.JobStandards.Add(new JobStandard { LongTitle = "Laboratory Assistant 1", Code = "009605", Items = [new JobStandardItem { Kind = StandardItemKind.KeyResponsibility, Text = "Duty." }] });
        db.StandardMatchDecisions.Add(new StandardMatchDecision { StandardTitle = "Laboratory Assistant 1", UcJobCode = "009605", IsMatch = true, DecidedAt = Then });
        db.ComplianceRules.Add(new ComplianceRule { Key = "or-equivalent", Pattern = "degree", Replacement = "degree or equivalent", Reason = "House rule." });
        db.CorpusUploads.Add(new CorpusUpload { FileName = "a.docx", Sha256 = new string('a', 64), Content = [1, 2, 3], Status = CorpusUploadStatus.Filed, Source = CorpusUploadSource.Document });
        db.JobDescriptions.Add(Jd("import/aaaaaaaaaaaaaaaa", "40000001", Then));
        db.ClassProfiles.Add(Class("009605-lab-ast-1", "009605", "Lab Ast 1", "Supports research.", Then));
        db.JdFitReviews.Add(new JdFitReview { SourceFile = "import/aaaaaaaaaaaaaaaa", UcJobCode = "009605", ReviewedByName = "An Admin", Note = "Belongs.", ReviewedAt = Then });
        db.CtTitles.Add(new CtTitle { UcJobCode = "009605", Title = "Laboratory Assistant 1", LevelCode = "P1", Grade = "17", ImportedAt = Then });
        using (db.KeepingChangeStamps())
        {
            await db.SaveChangesAsync();
        }

        db.ChangeTracker.Clear();
    }

    private static async Task<EnvironmentPackage> ExportAsync(Env env)
    {
        var bytes = await env.Packages.ExportAsync();
        return await env.Importer.ReadAsync(new MemoryStream(bytes));
    }

    private static async Task<string> ExportJsonAsync(Env env)
    {
        var bytes = await env.Packages.ExportAsync();
        await using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        var json = (JsonObject)(await JsonNode.ParseAsync(gzip))!;
        json.Remove("exportedAt");
        return json.ToJsonString();
    }

    [Fact]
    public async Task A_round_trip_into_an_empty_environment_reproduces_it_exactly()
    {
        var source = NewEnv();
        await SeedAsync(source.Db);
        var package = await ExportAsync(source);

        var target = NewEnv();
        var plan = await target.Importer.ApplyAsync(package);

        plan.Applied.Should().BeTrue();
        plan.Sections.Should().OnlyContain(s => s.Updated == 0 && s.KeptHere == 0 && s.Skipped == 0 && s.OnlyHere == 0);
        (await ExportJsonAsync(target)).Should().Be(await ExportJsonAsync(source));
    }

    [Fact]
    public async Task Importing_the_same_package_twice_changes_nothing_the_second_time()
    {
        var source = NewEnv();
        await SeedAsync(source.Db);
        var package = await ExportAsync(source);
        var target = NewEnv();
        await target.Importer.ApplyAsync(package);

        var again = await target.Importer.PreviewAsync(await ExportAsync(source));

        again.Sections.Should().OnlyContain(s => s.Added == 0 && s.Updated == 0);
    }

    [Fact]
    public async Task The_newer_class_wins_either_way_and_a_replaced_class_keeps_its_row()
    {
        var source = NewEnv();
        await SeedAsync(source.Db);

        // Here: the same class, changed earlier (older), and another changed later (newer than the package's).
        var target = NewEnv();
        target.Db.ClassProfiles.Add(Class("009605-lab-ast-1", "009605", "Lab Ast 1", "Older wording.", Then.AddDays(-5)));
        using (target.Db.KeepingChangeStamps())
        {
            await target.Db.SaveChangesAsync();
        }

        var rowId = (await target.Db.ClassProfiles.SingleAsync()).Id;
        target.Db.ChangeTracker.Clear();

        var plan = await target.Importer.ApplyAsync(await ExportAsync(source));

        plan.Sections.Single(s => s.Name == "Classes").Updated.Should().Be(1);
        var replaced = await target.Db.ClassProfiles.Include(p => p.Envelope).SingleAsync();
        replaced.Id.Should().Be(rowId, "authors' saved JDs reference the row");
        replaced.Envelope!.Summary.Should().Be("Supports research.");
        replaced.UpdatedAt.Should().Be(Then, "the package's stamp travels with its version");
        (await target.Db.Set<JobEnvelope>().CountAsync()).Should().Be(1);
        (await target.Db.Set<EnvelopeDuty>().CountAsync()).Should().Be(2);

        // Now change it here, later than the package: a second import keeps this one.
        // Same options, so the same in-memory store: a request's own context.
        await using (var edit = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                         .UseInMemoryDatabase(target.Name)
                         .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                         .Options))
        {
            (await edit.Set<JobEnvelope>().SingleAsync()).Summary = "Edited here, later.";
            await edit.SaveChangesAsync();
        }

        var second = await target.Importer.PreviewAsync(await ExportAsync(source));
        second.Sections.Single(s => s.Name == "Classes").KeptHere.Should().Be(1);
    }

    [Fact]
    public async Task Nothing_here_is_removed_for_being_absent_from_the_package()
    {
        var source = NewEnv();
        await SeedAsync(source.Db);
        var target = NewEnv();
        target.Db.ClassProfiles.Add(Class("004724-farm-laborer", "004724", "Farm Laborer", "Farm work.", Then));
        target.Db.JobDescriptions.Add(Jd("classify/bbbbbbbbbbbbbbbb", "", Then));
        await target.Db.SaveChangesAsync();
        target.Db.ChangeTracker.Clear();

        var plan = await target.Importer.ApplyAsync(await ExportAsync(source));

        plan.Sections.Single(s => s.Name == "Classes").OnlyHereItems.Should().ContainSingle().Which.Should().Contain("Farm Laborer");
        plan.Sections.Single(s => s.Name == "Job descriptions").OnlyHere.Should().Be(1);
        (await target.Db.ClassProfiles.CountAsync()).Should().Be(2);
        (await target.Db.JobDescriptions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_newer_JD_for_the_same_position_replaces_the_older_one_as_in_the_app()
    {
        var source = NewEnv();
        await SeedAsync(source.Db);
        var target = NewEnv();
        target.Db.JobDescriptions.Add(Jd("import/cccccccccccccccc", "40000001", Then.AddDays(-30), "Old wording."));
        await target.Db.SaveChangesAsync();
        target.Db.ChangeTracker.Clear();

        await target.Importer.ApplyAsync(await ExportAsync(source));

        (await target.Db.JobDescriptions.Select(j => j.SourceFile).ToListAsync()).Should().Equal("import/aaaaaaaaaaaaaaaa");
    }

    [Fact]
    public async Task A_class_whose_code_is_superseded_here_is_not_created()
    {
        var source = NewEnv();
        await SeedAsync(source.Db);
        var target = NewEnv();
        // Here, LAB AST 1 has a union successor that is active, so 009605 is superseded.
        target.Db.TitleCodes.AddRange(Tc("009605", "LAB AST 1"), Tc("009606", "LAB AST 1 TX"));
        await target.Db.SaveChangesAsync();
        target.Db.ChangeTracker.Clear();

        var plan = await target.Importer.ApplyAsync(await ExportAsync(source));

        plan.Sections.Single(s => s.Name == "Classes").SkippedItems.Should().ContainSingle().Which.Should().Contain("superseded here");
        (await target.Db.ClassProfiles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_preview_writes_nothing()
    {
        var source = NewEnv();
        await SeedAsync(source.Db);
        var target = NewEnv();

        var plan = await target.Importer.PreviewAsync(await ExportAsync(source));

        plan.Applied.Should().BeFalse();
        plan.Sections.Single(s => s.Name == "Classes").Added.Should().Be(1);
        (await target.Db.ClassProfiles.CountAsync()).Should().Be(0);
        (await target.Db.TitleCodes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_file_that_is_not_a_package_is_refused()
    {
        var env = NewEnv();
        await env.Importer.Invoking(i => i.ReadAsync(new MemoryStream("not gzip"u8.ToArray())))
            .Should().ThrowAsync<PackageRefusedException>();
    }

    /// <summary>
    /// Every record type reachable from what a package holds is in it — or is deliberately left out.
    /// A table added under a class or a JD later fails this until it is placed, rather than being
    /// silently left behind on the way to the server.
    /// </summary>
    [Fact]
    public void Every_table_under_a_packaged_record_travels_or_is_deliberately_left_out()
    {
        var env = NewEnv();
        var leftOut = new HashSet<Type> { typeof(AppUser), typeof(AuthoredJd) };
        var missing = new List<string>();
        foreach (var type in EnvironmentPackage.Types)
        {
            foreach (var nav in env.Db.Model.FindEntityType(type)!.GetNavigations().Where(n => !n.IsOnDependent))
            {
                var target = nav.TargetEntityType.ClrType;
                if (!EnvironmentPackage.Types.Contains(target) && !leftOut.Contains(target))
                {
                    missing.Add($"{type.Name}.{nav.Name} → {target.Name}");
                }
            }
        }

        missing.Should().BeEmpty();
    }
}

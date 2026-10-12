using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Package;

namespace Server.Tests.Package;

/// <summary>
/// The environment package export: everything built, shaped by the model, with no database identity
/// and nothing belonging to users. Synthetic records only.
/// </summary>
public class EnvironmentPackageExportTests
{
    private static async Task<AppDbContext> SeededAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"pkg_{Guid.NewGuid():N}").Options);
        var user = new AppUser { Email = "admin@example.edu", DisplayName = "Admin" };
        db.AppUsers.Add(user);
        db.TitleCodes.Add(new TitleCode { Code = "009605", Title = "LAB AST 1", Source = "both" });
        db.CodeActiveOverrides.Add(new CodeActiveOverride { UcJobCode = "009605", IsActive = false, DecidedBy = user, DecidedAt = DateTimeOffset.UtcNow });
        db.JobStandards.Add(new JobStandard { LongTitle = "Laboratory Assistant 1", Items = [new JobStandardItem { Text = "Duty one." }] });
        db.StandardMatchDecisions.Add(new StandardMatchDecision { StandardTitle = "Laboratory Assistant 1", UcJobCode = "009605", IsMatch = true, DecidedBy = user });
        db.CorpusUploads.AddRange(
            new CorpusUpload { FileName = "a.docx", Sha256 = new string('a', 64), Content = [1, 2, 3], Status = CorpusUploadStatus.Filed, UploadedBy = user },
            new CorpusUpload { FileName = "bad.docx", Sha256 = new string('b', 64), Content = [9], Status = CorpusUploadStatus.Failed });
        db.JobDescriptions.Add(new JobDescription
        {
            SourceFile = "import/aaaaaaaaaaaaaaaa", UcJobCode = "009605", Origin = CorpusOrigin.Imported,
            Responsibilities = [new JdResponsibility { FunctionName = "PLANT CARE", Pct = 100, Duties = [new JdDuty { Ordinal = 0, Text = "Waters plants." }, new JdDuty { Ordinal = 1, Text = "Feeds plants." }] }],
        });
        db.ClassProfiles.Add(new ClassProfile
        {
            Slug = "009605-lab-ast-1", UcJobCode = "009605", Title = "Lab Ast 1",
            Envelope = new JobEnvelope
            {
                Summary = "Supports research.",
                KeyResponsibilities = [new EnvelopeResponsibility { FunctionName = "PLANT CARE", PctTime = 100, Duties = [new EnvelopeDuty { Text = "First." }, new EnvelopeDuty { Text = "Second." }] }],
            },
            SourceFiles = [new ProfileSourceFile { SourceFile = "import/aaaaaaaaaaaaaaaa" }],
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static async Task<JsonObject> ExportedAsync(AppDbContext db)
    {
        var bytes = await new EnvironmentPackages(db).ExportAsync();
        await using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        return (JsonObject)(await JsonNode.ParseAsync(gzip))!;
    }

    private static IEnumerable<string> Keys(JsonNode? node) => node switch
    {
        JsonObject o => o.SelectMany(kv => Keys(kv.Value).Prepend(kv.Key)),
        JsonArray a => a.SelectMany(Keys),
        _ => [],
    };

    [Fact]
    public async Task Carries_every_class_and_JD_whole_in_order()
    {
        await using var db = await SeededAsync();
        var json = await ExportedAsync(db);

        json["format"]!.GetValue<string>().Should().Be("jdwriter.environment");
        var cls = json["classes"]!.AsArray().Single()!;
        cls["slug"]!.GetValue<string>().Should().Be("009605-lab-ast-1");
        cls["envelope"]!["keyResponsibilities"]![0]!["duties"]!.AsArray().Select(d => d!["text"]!.GetValue<string>())
            .Should().Equal("First.", "Second.");
        cls["sourceFiles"]![0]!["sourceFile"]!.GetValue<string>().Should().Be("import/aaaaaaaaaaaaaaaa");
        json["jobDescriptions"]![0]!["responsibilities"]![0]!["duties"]!.AsArray().Should().HaveCount(2);
        json["standards"]![0]!["items"]!.AsArray().Should().HaveCount(1);
        json["standardMatches"]!.AsArray().Should().HaveCount(1);
        json["activeCodes"]![0]!["isActive"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Carries_no_database_identity_and_nothing_belonging_to_users()
    {
        await using var db = await SeededAsync();
        var keys = Keys(await ExportedAsync(db)).ToHashSet();

        keys.Should().NotContain(k => k == "id" || k.EndsWith("Id", StringComparison.Ordinal), "keys are assigned where it lands");
        keys.Should().NotContain(["decidedBy", "uploadedBy", "authoredJd", "classProfile", "jobDescription", "email"]);
    }

    [Fact]
    public async Task Carries_the_originals_behind_JDs_but_not_failed_uploads()
    {
        await using var db = await SeededAsync();
        var originals = (await ExportedAsync(db))["originals"]!.AsArray();

        originals.Select(o => o!["fileName"]!.GetValue<string>()).Should().Equal("a.docx");
        Convert.FromBase64String(originals[0]!["content"]!.GetValue<string>()).Should().Equal(1, 2, 3);
    }
}

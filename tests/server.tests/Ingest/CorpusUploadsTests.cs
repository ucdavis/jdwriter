using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Profiles;
using Server.Core.Standards;
using Server.Core.Titles;
using Server.Tests.Profiles;

namespace Server.Tests.Ingest;

/// <summary>
/// Admin upload of HRTMS exports — the only way new JDs reach a deployed environment. These pin
/// that every file gets a verdict, duplicates are stored once, a re-export replaces its position
/// rather than doubling it, and refreshing a class keeps the profile that saved JDs point at.
/// </summary>
public class CorpusUploadsTests
{
    /// <summary>
    /// A synthetic HRTMS export. Every value is invented; the LABELS are copied verbatim from the
    /// parser (and the real exports), because a fixture that misspells a label silently tests
    /// nothing.
    /// </summary>
    private static byte[] Export(string code, string title, string position, string working, params (int Pct, string Fn)[] fns)
    {
        var rows = new StringBuilder();
        void Row(string label, string value) => rows.Append($"<tr><td>{label}</td><td>{value}</td></tr>");
        Row("Business Unit (Location):", "DVCMP");
        Row("Department Name:", "Synthetic Dept");
        Row("Job Description Number", $"JD-{position}");
        Row("UCPath Position Number", position);
        Row("UC Job Title:", title);
        Row("UC Job Code:", code);
        Row("Working Title:", working);
        Row("Salary Grade:", "Grade 19");
        Row("Job FLSA Status:", "Non-Exempt");
        Row("Union Code (Collective Bargaining Unit):", "TX");
        rows.Append("<tr><td>% Time</td><td>Function</td><td>Duties</td></tr>");
        foreach (var (pct, fn) in fns)
        {
            rows.Append($"<tr><td>{pct}%</td><td>{fn}</td><td>• Does {fn.ToLowerInvariant()} work • Records it</td></tr>");
        }

        var html = $"<html><body><table>{rows}</table><p>Job Summary Synthetic summary for {working}. Key Responsibilities</p></body></html>";
        return Encoding.UTF8.GetBytes(html);
    }

    private sealed class Titles(params TitleCode[] rows) : ITitleCodeService
    {
        private readonly TitleCodeIndex _index = new(rows);
        public Task<TitleCodeIndex> GetAsync(CancellationToken ct = default) => Task.FromResult(_index);
        public void Invalidate() { }
    }

    private sealed class NoStandards : IStandardLookup
    {
        public Task<ClassStandardRecord?> ForTitleAsync(string title, CancellationToken ct = default) =>
            Task.FromResult<ClassStandardRecord?>(null);
    }

    private static TitleCode Tc(string code, string title) => new()
    {
        Code = code,
        Title = title,
        Source = "both",
        TitleKey = TitleNormalizer.TitleKey(title),
        TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
    };

    private static (AppDbContext Db, CorpusUploads Uploads) Harness()
    {
        var (db, uploads, _) = HarnessWithPipeline();
        return (db, uploads);
    }

    private static (AppDbContext Db, CorpusUploads Uploads, IngestPipeline Pipeline) HarnessWithPipeline()
    {
        // Re-ingest runs inside a transaction; the in-memory provider has none, so it is told to
        // proceed rather than fail.
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"uploads_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var titles = new Titles(Tc("009605", "LAB AST 1"), Tc("004724", "FARM LABORER"));
        // No API key: the deterministic envelope path, so nothing here calls a model.
        var llm = new FakeStructuredLlm { HasApiKey = false };
        var pipeline = new IngestPipeline(db, titles, llm, new Consolidator(llm),
            new EnvelopeSynthesizer(llm, new NoStandards()), NullLogger<IngestPipeline>.Instance);
        return (db, new CorpusUploads(db, titles, pipeline), pipeline);
    }

    [Fact]
    public async Task Every_file_gets_a_verdict_and_one_bad_file_does_not_sink_the_batch()
    {
        var (db, uploads) = Harness();
        var good = Export("009605", "LAB AST 1", "40000001", "Greenhouse Tech", (60, "Plant care"), (40, "Records"));

        var outcomes = await uploads.UploadAsync(
        [
            ("LAB AST 1/a.HTML", good),
            ("LAB AST 1/a-copy.HTML", good),
            ("notes.docx", Encoding.UTF8.GetBytes("not html")),
            ("empty.html", Encoding.UTF8.GetBytes("<html><body>nothing here</body></html>")),
        ], userId: null);

        outcomes.Select(o => o.Result).Should().Equal("added", "duplicate", "failed", "failed");
        outcomes[0].UcJobCode.Should().Be("009605");
        outcomes[2].Error.Should().Contain(".html");
        outcomes[3].Error.Should().Contain("UC job code");
        (await db.CorpusUploads.CountAsync()).Should().Be(3, "identical bytes are stored once");
    }

    [Fact]
    public async Task Pending_groups_by_class_and_knows_new_from_refresh()
    {
        var (db, uploads) = Harness();
        db.ClassProfiles.Add(new ClassProfile { Slug = "009605-lab-ast-1", UcJobCode = "009605", Title = "Lab Ast 1" });
        await db.SaveChangesAsync();

        await uploads.UploadAsync(
        [
            ("a.html", Export("009605", "LAB AST 1", "40000001", "Tech A", (100, "Plant care"))),
            ("b.html", Export("009605", "LAB AST 1", "40000002", "Tech B", (100, "Plant care"))),
            ("c.html", Export("004724", "FARM LABORER", "40000003", "Field Hand", (100, "Harvest"))),
        ], null);

        var pending = await uploads.PendingAsync();

        pending.Select(p => (p.Code, p.NewFiles, p.ExistingSlug)).Should().BeEquivalentTo(new[]
        {
            ("004724", 1, (string?)null),
            ("009605", 2, (string?)"009605-lab-ast-1"),
        });
    }

    [Fact]
    public async Task Ingesting_a_new_class_creates_it_and_adds_its_jds_to_the_corpus()
    {
        var (db, uploads) = Harness();
        await uploads.UploadAsync(
        [
            ("a.html", Export("004724", "FARM LABORER", "40000003", "Field Hand", (70, "Harvest"), (30, "Equipment"))),
        ], null);

        var profile = await uploads.IngestAsync("004724");

        profile.UcJobCode.Should().Be("004724");
        profile.CorpusSize.Should().Be(1);
        profile.Envelope.Should().NotBeNull();
        (await db.JobDescriptions.SingleAsync()).UcPathPositionNumber.Should().Be("40000003");
        (await db.CorpusUploads.SingleAsync()).Status.Should().Be(CorpusUploadStatus.Ingested);
        (await uploads.PendingAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_re_export_of_a_position_replaces_it_instead_of_counting_it_twice()
    {
        var (db, uploads) = Harness();
        await uploads.UploadAsync([("v1.html", Export("004724", "FARM LABORER", "40000003", "Field Hand", (100, "Harvest")))], null);
        await uploads.IngestAsync("004724");

        await uploads.UploadAsync([("v2.html", Export("004724", "FARM LABORER", "40000003", "Senior Field Hand", (100, "Harvest")))], null);
        var profile = await uploads.IngestAsync("004724");

        profile.CorpusSize.Should().Be(1);
        (await db.JobDescriptions.SingleAsync()).WorkingTitle.Should().Be("Senior Field Hand");
    }

    [Fact]
    public async Task Refreshing_a_class_keeps_the_profile_that_saved_jds_point_at()
    {
        // Saved JDs restrict deletion of their profile. Re-ingest must therefore update the
        // profile in place: deleting and re-adding it would fail (or orphan the JD) the first time
        // anyone had saved a JD against the class.
        var (db, uploads) = Harness();
        await uploads.UploadAsync([("a.html", Export("004724", "FARM LABORER", "40000003", "Field Hand", (100, "Harvest")))], null);
        var first = await uploads.IngestAsync("004724");
        db.AuthoredJds.Add(new AuthoredJd { ClassProfileId = first.Id, Title = "Farm Laborer", WorkingTitle = "Saved" });
        await db.SaveChangesAsync();

        await uploads.UploadAsync([("b.html", Export("004724", "FARM LABORER", "40000004", "Second Hand", (100, "Harvest")))], null);
        var refreshed = await uploads.IngestAsync("004724");

        refreshed.Id.Should().Be(first.Id);
        refreshed.CorpusSize.Should().Be(2);
        (await db.ClassProfiles.CountAsync()).Should().Be(1);
        (await db.AuthoredJds.SingleAsync()).ClassProfileId.Should().Be(first.Id);
    }

    [Fact]
    public async Task Ingesting_a_class_with_nothing_pending_is_refused_with_a_message()
    {
        var (_, uploads) = Harness();

        var ingest = () => uploads.IngestAsync("004724");

        await ingest.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Nothing in the corpus*");
    }

    // ------------------------------------------------------------------ bootstrapped, then real JDs

    /// <summary>A class bootstrapped from its standard: its slug and title come from the standard.</summary>
    private static async Task<ClassProfile> Starter(AppDbContext db)
    {
        var starter = new ClassProfile
        {
            Slug = "009605-laboratory-assistant-1",
            UcJobCode = "009605",
            Title = "Laboratory Assistant 1",
            EnvelopeSource = EnvelopeSource.Standard,
            CorpusSize = 0,
            GeneratedNote = "Standard-derived — no JD corpus yet.",
            Envelope = new JobEnvelope { Summary = "From the standard." },
        };
        db.ClassProfiles.Add(starter);
        await db.SaveChangesAsync();
        db.AuthoredJds.Add(new AuthoredJd { ClassProfileId = starter.Id, Title = starter.Title, UcJobCode = "009605" });
        await db.SaveChangesAsync();
        return starter;
    }

    [Fact]
    public async Task Real_jds_turn_a_bootstrapped_class_into_a_learned_one_in_place()
    {
        var (db, uploads) = Harness();
        var starter = await Starter(db);
        await uploads.UploadAsync(
        [
            ("a.html", Export("009605", "LAB AST 1", "40000001", "Greenhouse Tech", (60, "Plant care"), (40, "Records"))),
        ], userId: null);

        var queued = (await uploads.PendingAsync()).Single();
        queued.ExistingSlug.Should().Be(starter.Slug);
        queued.ReplacesStarter.Should().BeTrue();

        await uploads.IngestAsync("009605");
        db.ChangeTracker.Clear();

        var profile = await db.ClassProfiles.SingleAsync();
        profile.Id.Should().Be(starter.Id, "one class per code: the starter is rebuilt, not duplicated");
        profile.Slug.Should().Be(starter.Slug);
        profile.Title.Should().Be("Laboratory Assistant 1", "the standard's title survives; it is not renamed to the payroll spelling");
        profile.CorpusSize.Should().Be(1);
        profile.EnvelopeSource.Should().NotBe(EnvelopeSource.Standard);
        (await db.AuthoredJds.SingleAsync()).ClassProfileId.Should().Be(starter.Id, "saved JDs stay with their class");
    }

    [Fact]
    public async Task Ingesting_without_a_slug_finds_the_class_by_job_code()
    {
        // The corpus-folder path passes no slug. Deriving one from the payroll title missed the
        // starter's standard-titled slug and created a second class for the same code.
        var (db, _, pipeline) = HarnessWithPipeline();
        var starter = await Starter(db);
        var record = HrtmsParser.Parse(
            Encoding.UTF8.GetString(Export("009605", "LAB AST 1", "40000001", "Greenhouse Tech", (100, "Plant care"))),
            "LAB AST 1/a.html");

        await pipeline.IngestRecordsAsync([record]);
        db.ChangeTracker.Clear();

        (await db.ClassProfiles.Select(p => p.Id).ToListAsync()).Should().Equal(starter.Id);
    }

    [Fact]
    public async Task The_corpus_folder_lists_new_exports_for_a_bootstrapped_class()
    {
        var (db, _, pipeline) = HarnessWithPipeline();
        var starter = await Starter(db);
        db.ClassProfiles.Add(new ClassProfile { Slug = "004724-farm-laborer", UcJobCode = "004724", Title = "Farm Laborer", CorpusSize = 3, EnvelopeSource = EnvelopeSource.Claude });
        await db.SaveChangesAsync();

        var dir = Directory.CreateTempSubdirectory("jdw-corpus-");
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "lab.html"), Export("009605", "LAB AST 1", "40000001", "Tech", (100, "Plant care")));
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "farm.html"), Export("004724", "FARM LABORER", "40000002", "Hand", (100, "Harvest")));

            var pending = await pipeline.PendingClassesAsync(dir.FullName);

            // The learned class is up to date and stays hidden; the starter is offered, under its own name.
            var only = pending.Should().ContainSingle().Subject;
            only.Code.Should().Be("009605");
            only.Slug.Should().Be(starter.Slug);
            only.Title.Should().Be("Laboratory Assistant 1");
            only.ReplacesStarter.Should().BeTrue();
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}

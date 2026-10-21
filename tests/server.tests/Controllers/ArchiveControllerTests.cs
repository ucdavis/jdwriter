using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Controllers;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Jd;
using Server.Core.Profiles;
using Server.Core.Titles;
using Server.Tests.Profiles;

namespace Server.Tests.Controllers;

public class ArchiveControllerTests
{
    private const string Slug = "005183-financial-anl-3-cx";
    private const string SourceFile = "Financial Anl 3 (40917603)_[0006]_Job Description.html";

    private sealed class Titles : ITitleCodeService
    {
        private readonly TitleCodeIndex _index = new([]);
        public Task<TitleCodeIndex> GetAsync(CancellationToken ct = default) => Task.FromResult(_index);
        public void Invalidate() { }
    }

    private static async Task<AppDbContext> Seed()
    {
        var db = TestDbContextFactory.CreateInMemory();
        var env = new JobEnvelope { Summary = "Analyzes finances." };
        var fn = new EnvelopeResponsibility { Ordinal = 0, FunctionName = "Financial Analysis", PctTime = 100 };
        fn.Duties.Add(new EnvelopeDuty { Ordinal = 0, Text = "Prepares budget forecasts." });
        env.KeyResponsibilities.Add(fn);
        db.ClassProfiles.Add(new ClassProfile { Slug = Slug, Title = "Financial Anl 3 Cx", UcJobCode = "005183", Envelope = env });
        db.ClassProfiles.Add(new ClassProfile { Slug = "111111-other", Title = "Other", UcJobCode = "111111", Envelope = new JobEnvelope() });

        db.JobDescriptions.Add(Jd(SourceFile, "005183", "Budget Lead"));
        db.JobDescriptions.Add(Jd("Other (40000002)_[0001]_Job Description.html", "111111", "Someone Else"));
        await db.SaveChangesAsync();
        return db;
    }

    private static JobDescription Jd(string sourceFile, string code, string workingTitle)
    {
        var jd = new JobDescription
        {
            SourceFile = sourceFile, UcJobCode = code, UcJobTitle = "FINANCIAL ANL 3", WorkingTitle = workingTitle,
            DepartmentName = "Plant Sciences", UcPathPositionNumber = "40917603", ReportsToPositionNumber = "40000001",
            JobSummary = "Runs the department budget.", Origin = CorpusOrigin.Export,
        };
        var r = new JdResponsibility { Ordinal = 0, FunctionName = "Budget work", Pct = 100 };
        r.Duties.Add(new JdDuty { Ordinal = 0, Text = "Prepares budget forecasts for the tomato trials." });
        jd.Responsibilities.Add(r);
        return jd;
    }

    private static ArchiveController Controller(AppDbContext db, FakeStructuredLlm llm) =>
        new(new ClassProfileRepository(db, new Titles()), llm, new FitRewriter(llm), new AuthoredJdStore(db), db,
            NullLogger<ArchiveController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) } },
        };

    [Fact]
    public async Task Lists_the_class_s_jds_by_working_title_without_position_numbers_or_reporting_lines()
    {
        using var db = await Seed();

        var ok = (await Controller(db, new FakeStructuredLlm()).List(Slug, default)).Should().BeOfType<OkObjectResult>().Subject;

        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var jds = doc.RootElement.GetProperty("jds");
        jds.GetArrayLength().Should().Be(1, "only this class's JDs");
        jds[0].GetProperty("WorkingTitle").GetString().Should().Be("Budget Lead");
        jds[0].GetProperty("Responsibilities")[0].GetProperty("Duties")[0].GetString()
            .Should().Be("Prepares budget forecasts for the tomato trials.");
        json.Should().NotContain("40000001").And.NotContain("PositionNumber").And.NotContain("ReportsTo");
    }

    [Fact]
    public async Task Will_not_start_from_a_jd_in_another_class_or_without_an_ai_provider()
    {
        using var db = await Seed();

        (await Controller(db, new FakeStructuredLlm()).Start(Slug, new() { SourceFile = "Other (40000002)_[0001]_Job Description.html" }, default))
            .Should().BeOfType<NotFoundObjectResult>();
        (await Controller(db, new FakeStructuredLlm { HasApiKey = false }).Start(Slug, new() { SourceFile = SourceFile }, default))
            .Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task Starts_a_draft_that_keeps_the_jd_s_specifics_and_summary()
    {
        using var db = await Seed();
        var llm = new FakeStructuredLlm().Returns(new
        {
            placements = new[] { new { responsibility = 0, function = 0 } },
            functions = new[] { new { function = 0, keepDuties = new[] { 0 } } },
            carryOver = new[] { new { responsibility = 0, duty = 0, function = 0 } },
            outside = Array.Empty<object>(),
            matches = Array.Empty<object>(),
        });

        var ok = (await Controller(db, llm).Start(Slug, new() { SourceFile = SourceFile }, default)).Should().BeOfType<OkObjectResult>().Subject;

        llm.Requests.Single().System.Should().Contain("the unit's specifics must survive");
        var draft = db.AuthoredJds.Single();
        draft.AssembledAt.Should().BeNull("it opens in the builder, to be checked");
        using var state = JsonDocument.Parse(draft.DraftState!);
        state.RootElement.GetProperty("summary").GetString().Should().Be("Runs the department budget.");
        JsonSerializer.Serialize(ok.Value).Should().Contain($"\"authoredJdId\":{draft.Id}");
    }
}

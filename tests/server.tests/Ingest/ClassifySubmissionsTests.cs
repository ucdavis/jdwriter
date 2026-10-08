using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Intake;
using Server.Core.Titles;

namespace Server.Tests.Ingest;

/// <summary>
/// Every Classify submission is filed into a class: an HRTMS export under the code it states, and
/// anything else under the classifier's top match — once per distinct submission.
/// </summary>
public class ClassifySubmissionsTests
{
    private sealed class Titles : ITitleCodeService
    {
        private readonly TitleCodeIndex _index = new([]);
        public Task<TitleCodeIndex> GetAsync(CancellationToken ct = default) => Task.FromResult(_index);
        public void Invalidate() { }
    }

    /// <summary>Synthetic HRTMS export; labels copied verbatim from the parser.</summary>
    private static string Export(string code, string position, string working) =>
        "<html><body><table>"
        + $"<tr><td>UCPath Position Number</td><td>{position}</td></tr>"
        + "<tr><td>UC Job Title:</td><td>FARM LABORER</td></tr>"
        + $"<tr><td>UC Job Code:</td><td>{code}</td></tr>"
        + $"<tr><td>Working Title:</td><td>{working}</td></tr>"
        + "<tr><td>% Time</td><td>Function</td><td>Duties</td></tr>"
        + "<tr><td>100%</td><td>Harvest</td><td>• Picks crops</td></tr>"
        + "</table></body></html>";

    private static Classification Ranked(string code, string title) => new()
    {
        Distilled = new DistilledJd
        {
            WorkingTitle = "Evaluation Analyst",
            Summary = "Designs surveys and analyses study data.",
            Supervises = "no",
            Functions = [new DistilledFunction { Name = "Evaluation", PctTime = 62.5, Duties = ["Designs surveys"] }],
            Education = ["Bachelor's degree"],
            Ksas = ["Statistics"],
        },
        Matches = [new ClassificationMatch { Slug = $"{code}-x", Title = title, UcJobCode = code }],
    };

    [Fact]
    public async Task An_export_is_filed_under_the_code_it_states_not_the_classifier_guess()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var filer = new ClassifySubmissions(db, new Titles());

        var code = await filer.FileAsync(Export("004724", "40000003", "Field Hand"), Ranked("009605", "Lab Ast 1"), userId: null);

        code.Should().Be("004724");
        var jd = await db.JobDescriptions.SingleAsync();
        jd.Origin.Should().Be(CorpusOrigin.Classify);
        jd.AddedAt.Should().NotBeNull();
        jd.UcPathPositionNumber.Should().Be("40000003");
        var original = await db.CorpusUploads.SingleAsync();
        original.Status.Should().Be(CorpusUploadStatus.Filed);
        original.Source.Should().Be(CorpusUploadSource.Classify);
        Encoding.UTF8.GetString(original.Content).Should().Contain("Field Hand", "the original is kept for reparsing");
    }

    [Fact]
    public async Task Other_text_is_filed_under_the_top_match_as_the_classifier_read_it()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var filer = new ClassifySubmissions(db, new Titles());

        var code = await filer.FileAsync("We need an evaluation analyst to design surveys.", Ranked("007399", "Project Policy Anl 4"), null);

        code.Should().Be("007399");
        var jd = await db.JobDescriptions.Include(j => j.Responsibilities).Include(j => j.Qualifications).SingleAsync();
        jd.WorkingTitle.Should().Be("Evaluation Analyst");
        jd.Responsibilities.Single().Pct.Should().Be(63, "fractional distilled percentages are rounded");
        jd.Supervises.Should().BeFalse();
        jd.Qualifications.Select(q => q.Kind).Should().Contain(JdQualificationKind.Education);
        jd.SourceFile.Should().StartWith("classify/");
    }

    [Fact]
    public async Task The_same_submission_is_filed_once()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var filer = new ClassifySubmissions(db, new Titles());

        await filer.FileAsync("Same text.", Ranked("007399", "P"), null);
        var second = await filer.FileAsync("Same text.", Ranked("007399", "P"), null);

        second.Should().BeNull();
        (await db.JobDescriptions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task With_no_match_the_original_is_kept_but_nothing_is_filed()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var filer = new ClassifySubmissions(db, new Titles());

        var code = await filer.FileAsync("Unclassifiable.", new Classification(), null);

        code.Should().BeNull();
        (await db.JobDescriptions.CountAsync()).Should().Be(0);
        (await db.CorpusUploads.SingleAsync()).Status.Should().Be(CorpusUploadStatus.Failed);
    }

    [Fact]
    public async Task A_resubmitted_export_of_a_position_replaces_the_earlier_copy()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var filer = new ClassifySubmissions(db, new Titles());

        await filer.FileAsync(Export("004724", "40000003", "Field Hand"), Ranked("004724", "F"), null);
        await filer.FileAsync(Export("004724", "40000003", "Senior Field Hand"), Ranked("004724", "F"), null);

        (await db.JobDescriptions.SingleAsync()).WorkingTitle.Should().Be("Senior Field Hand");
    }

    [Fact]
    public void A_distilled_description_becomes_a_jd_record_its_own_work_in_order()
    {
        // The shape "Start a JD from this class" maps onto a class standard, and the corpus files.
        var jd = ClassifySubmissions.ToJobDescription(new DistilledJd
        {
            WorkingTitle = "Evaluation Analyst",
            Summary = "Evaluates program outcomes.",
            Supervises = "no",
            Functions =
            [
                new DistilledFunction { Name = "ANALYSIS", PctTime = 62.5, Duties = ["Analyzes data.", "Writes reports."] },
                new DistilledFunction { Name = "OTHER", PctTime = 37.5, Duties = ["Other duties."] },
            ],
            Education = ["Bachelor's degree", "or equivalent experience"],
            Ksas = ["Statistics."],
        });

        jd.WorkingTitle.Should().Be("Evaluation Analyst");
        jd.JobSummary.Should().Be("Evaluates program outcomes.");
        jd.Supervises.Should().BeFalse();
        jd.UcJobCode.Should().BeEmpty("it is not filed under any class until one is chosen");
        jd.Responsibilities.Select(r => (r.Ordinal, r.FunctionName, r.Pct)).Should().Equal((0, "ANALYSIS", 63), (1, "OTHER", 38));
        jd.Responsibilities[0].Duties.OrderBy(d => d.Ordinal).Select(d => d.Text).Should().Equal("Analyzes data.", "Writes reports.");
        jd.Qualifications.Should().Contain(q => q.Kind == JdQualificationKind.Education && q.Text == "Bachelor's degree; or equivalent experience");
        jd.Qualifications.Should().Contain(q => q.Kind == JdQualificationKind.KsaMin && q.Text == "Statistics.");
    }
}

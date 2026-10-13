using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Standards;
using Server.Core.Titles;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Server.Tests.Titles;

/// <summary>
/// The Career Tracks title code listing: read from its PDF, laid over the title reference, and
/// trusted over it. The PDF here is synthetic, laid out as the real report is — the same columns at
/// the same positions, two headers centred over their columns, a title wrapped above and below its
/// row — with invented content.
/// </summary>
public class CtListingTests
{
    // ------------------------------------------------------------------ the report

    private static byte[] Listing(params (double Y, (double X, string Text)[] Words)[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(792, 612);
        void Put(double x, double y, string text) => page.AddText(text, 7, new PdfPoint(x, y), font);

        // The header, as the report sets it: most headers at their column's edge; JOB_FUNCTION and
        // Job_Title centred; "Last updated on" and "OP_Only" over two lines.
        foreach (var (x, t) in new[] { (396.0, "Last"), (413, "updated"), (450, "OP_Onl") }) Put(x, 548, t);
        foreach (var (x, t) in new[]
                 {
                     (67.0, "Job"), (82, "Family"), (198, "JOB_FUNCTION"), (302, "Origin"), (332, "LvlCd"), (359, "JobCode"),
                     (543, "Job_Title"), (642, "FLSA"), (666, "PerProg"), (703, "JobStat"), (737, "GRADE"),
                 })
        {
            Put(x, 543, t);
        }

        foreach (var (x, t) in new[] { (416.0, "on"), (462, "y") }) Put(x, 537, t);
        foreach (var (y, words) in lines)
        {
            foreach (var (x, t) in words)
            {
                Put(x, y, t);
            }
        }

        return builder.Build();
    }

    private static (double, string)[] Row(string family, string function, string origin, string level, string code, string date, string op, string title, string flsa, string pp, string stat, string grade)
    {
        var words = new List<(double, string)>();
        void Cell(double x, string text)
        {
            foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                words.Add((x, w));
                x += w.Length * 4.2 + 3;
            }
        }

        Cell(20, family);
        Cell(156, function);
        Cell(300, origin);
        Cell(330, level);
        Cell(357, code);
        Cell(408, date);
        Cell(459, op);
        Cell(483, title);
        Cell(640, flsa);
        Cell(664, pp);
        Cell(701, stat);
        Cell(735, grade);
        return [.. words];
    }

    private static byte[] SampleListing() => Listing(
        (525, Row("Research Administration", "Intellectual Property", "C", "M2", "000372", "01-Apr-09", "FALSE", "Intellectual Property Manager 2", "E", "MSP", "A", "29")),
        (513, Row("Research Administration", "Intellectual Property", "OP", "P4", "000339", "01-Apr-09", "TRUE", "Intellectual Property Officer 4", "E", "MSP", "A", "26")),
        // A long title wraps: half above its row, half below, and none on the row itself.
        (489, Row("", "", "", "", "", "", "", "Clinical Nurse or Advanced Practice", "", "", "", "")),
        (483, Row("Advanced Practice", "Clinical or Adv Nurse Spec", "MC", "P4", "006481", "17-Mar-16", "FALSE", "", "E", "MSP", "A", "27")),
        (477, Row("", "", "", "", "", "", "", "Specialist 4", "", "", "", "")),
        (465, Row("General Administration", "Health Sciences Admin Ops", "MC", "M4", "005637", "09-Aug-22", "FALSE", "Health Sciences Administrator 4", "E", "MSP", "A", "31")));

    [Fact]
    public void Reads_each_column_by_position_including_a_title_that_wraps()
    {
        var pdf = SampleListing();
        CtListing.IsListing(pdf).Should().BeTrue();

        var rows = CtListing.Parse(pdf).ToDictionary(r => r.UcJobCode);

        rows.Keys.Should().BeEquivalentTo(["000372", "000339", "006481", "005637"]);
        rows["000372"].Should().BeEquivalentTo(new
        {
            JobFamily = "Research Administration", JobFunction = "Intellectual Property", Origin = "C", LevelCode = "M2",
            Title = "Intellectual Property Manager 2", Flsa = "E", PersonnelProgram = "MSP", JobStatus = "A", Grade = "29",
            OpOnly = false, LastUpdated = (DateOnly?)new DateOnly(2009, 4, 1),
        });
        rows["006481"].Title.Should().Be("Clinical Nurse or Advanced Practice Specialist 4");
        rows["006481"].JobFunction.Should().Be("Clinical or Adv Nurse Spec");
        rows["000339"].OpOnly.Should().BeTrue();
    }

    [Fact]
    public void Another_pdf_is_refused()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(595, 842);
        page.AddText("A memo", 12, new PdfPoint(50, 800), builder.AddStandard14Font(Standard14Font.Helvetica));

        var memo = builder.Build();
        CtListing.IsListing(memo).Should().BeFalse();
        FluentActions.Invoking(() => CtListingImport.Read(memo)).Should().Throw<CtListingRefusedException>();
    }

    // ------------------------------------------------------------------ over the reference

    private static TitleCode Tc(string code, string title, string source = "both", string grade = "", string family = "") => new()
    {
        Code = code, Title = title, Source = source, Grade = grade, Family = family,
        TitleKey = TitleNormalizer.TitleKey(title), TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
    };

    private static CtTitle Ct(string code, string title, string level = "P3", string grade = "", string family = "", bool opOnly = false) => new()
    {
        UcJobCode = code, Title = title, LevelCode = level, Grade = grade, JobFamily = family, OpOnly = opOnly, ImportedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Adds_the_codes_the_reference_lacks_takes_its_newer_grades_and_keeps_OP_titles_inactive()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"ct_{Guid.NewGuid():N}").Options);
        db.TitleCodes.AddRange(Tc("000680", "AUDIT MGR 2", grade: "Grade 27", family: "Audit"), Tc("000339", "IP OFCR 4"));
        db.CtTitles.AddRange(
            Ct("000680", "Audit Manager 2", "M2", grade: "26", family: "Audit Compliance and Ethics"),
            Ct("000339", "Intellectual Property Officer 4", opOnly: true),
            Ct("004638", "Business Development Professional 3", grade: "23"));
        await db.SaveChangesAsync();

        var index = await new TitleCodeService(db, new MemoryCache(new MemoryCacheOptions())).GetAsync();

        var audit = index.FindByCode("000680")!;
        audit.Grade.Should().Be("Grade 26", "the listing is newer than the reference");
        audit.Family.Should().Be("Audit Compliance and Ethics");
        audit.Ct!.Title.Should().Be("Audit Manager 2");
        index.FindByCode("000339")!.IsInUse.Should().BeFalse("the Office of the President's own title");
        var added = index.FindByCode("004638")!;
        added.Source.Should().Be(CtOverlay.Source);
        added.IsInUse.Should().BeFalse("it isn't on payroll");
        added.Grade.Should().Be("Grade 23");
    }

    [Theory]
    [InlineData("HEALTH SCIENCES ADM 4", "M4", true)]
    [InlineData("CHF HR EXEC ME", "ME", true)]
    [InlineData("LAB SUPV 1", null, true)]
    [InlineData("LAB SUPV 1", "P3", false)]
    [InlineData("ANALYST 3", null, false)]
    public void The_listing_level_decides_who_may_supervise_and_the_title_does_otherwise(string title, string? level, bool supervisory) =>
        Supervision.IsSupervisory(title, level is null ? null : new CtTitle { LevelCode = level }).Should().Be(supervisory);

    // ------------------------------------------------------------------ the import

    private sealed record Env(AppDbContext Db, CtListingImport Import, TitleCodeService Titles, StandardsStore Standards);

    private static async Task<Env> SeededAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"ctimp_{Guid.NewGuid():N}").Options);
        db.TitleCodes.AddRange(
            Tc("000621", "EMPLOYMENT MGR 1"), Tc("000618", "EQUAL EMPLOYMENT OPPORTUNITY MGR 1"),
            Tc("000372", "IP MGR 2"),
            Tc("007709", "FINANCIAL ANL 3"), Tc("005183", "FINANCIAL ANL 3 CX"),
            Tc("001111", "DUPLICATE TITLE A"), Tc("001112", "DUPLICATE TITLE B"));
        foreach (var (title, code) in new[]
                 {
                     ("Equal Employment Opportunity Manager 1", "000621"), // matched by hand, wrongly
                     ("Financial Analyst 3 CX", "005183"),                  // on the union successor
                     ("Intellectual Property Manager 2", (string?)null),    // on no code
                     ("Shared Standard", "001111"),
                 })
        {
            db.JobStandards.Add(StandardsStore.ToEntity(new ClassStandardRecord { LongTitle = title, KeyResponsibilities = ["Duty."] }));
            if (code is not null)
            {
                db.StandardMatchDecisions.Add(new StandardMatchDecision { StandardTitle = title, UcJobCode = code, IsMatch = true, DecidedAt = DateTimeOffset.UtcNow });
            }
        }

        db.ClassProfiles.AddRange(
            new ClassProfile { Slug = "000621-employment-manager-1", UcJobCode = "000621", Title = "Employment Manager 1" },
            new ClassProfile { Slug = "000618-eeo-manager-1", UcJobCode = "000618", Title = "EEO Manager 1" });
        await db.SaveChangesAsync();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var titles = new TitleCodeService(db, cache);
        var standards = new StandardsStore(db, cache);
        return new Env(db, new CtListingImport(db, titles, standards, new StandardMatching(db, standards, titles)), titles, standards);
    }

    private static CtListingRow Listed(string code, string title, string level = "P3") =>
        new() { UcJobCode = code, Title = title, LevelCode = level, Grade = "25" };

    private static List<CtListingRow> Rows() =>
    [
        Listed("000618", "Equal Employment Opportunity Manager 1", "M1"),
        Listed("007709", "Financial Analyst 3 CX"), // the listing knows the base code only
        Listed("000372", "Intellectual Property Manager 2", "M2"),
        Listed("001111", "Shared Standard"), Listed("001112", "Shared Standard"),
    ];

    [Fact]
    public async Task Moves_a_standard_to_the_code_the_listing_gives_its_title_and_lists_the_classes_to_rebuild()
    {
        var env = await SeededAsync();

        var plan = await env.Import.ApplyAsync(Rows(), userId: null);

        plan.StandardMoves.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Standard = "Equal Employment Opportunity Manager 1", FromCode = "000621", ToCode = "000618",
        });
        plan.AffectedClasses.Select(c => c.Slug).Should().BeEquivalentTo(["000621-employment-manager-1", "000618-eeo-manager-1"]);
        env.Standards.Invalidate();
        env.Titles.Invalidate();
        var index = await env.Titles.GetAsync();
        var moved = (await env.Standards.GetIndexAsync()).All.Single(s => s.LongTitle == "Equal Employment Opportunity Manager 1");
        StandardCodes.For(moved, index)!.Code.Should().Be("000618");
    }

    [Fact]
    public async Task Leaves_a_standard_on_a_union_successor_and_a_title_the_listing_gives_two_codes()
    {
        var env = await SeededAsync();

        var plan = await env.Import.PreviewAsync(Rows());

        plan.StandardMoves.Select(m => m.Standard).Should().NotContain(["Financial Analyst 3 CX", "Shared Standard"]);
    }

    [Fact]
    public async Task Suggests_but_never_matches_a_standard_on_no_code()
    {
        var env = await SeededAsync();
        await env.Import.ApplyAsync(Rows(), userId: null);

        var review = await new StandardMatching(env.Db, env.Standards, env.Titles).ReviewAsync(includeNotActive: true);

        var ip = review.Unmatched.Single(u => u.StandardTitle == "Intellectual Property Manager 2");
        ip.Suggestions[0].Should().BeEquivalentTo(new { UcJobCode = "000372", FromCtListing = true, Score = 1.0 });
        (await env.Db.StandardMatchDecisions.AnyAsync(d => d.StandardTitle == "Intellectual Property Manager 2")).Should().BeFalse();
    }

    [Fact]
    public async Task Importing_the_same_listing_again_changes_nothing()
    {
        var env = await SeededAsync();
        await env.Import.ApplyAsync(Rows(), userId: null);
        env.Titles.Invalidate();
        env.Standards.Invalidate();

        var again = await env.Import.PreviewAsync(Rows());

        again.Should().BeEquivalentTo(new { NewToListing = 0, Changed = 0, Unchanged = 5 });
        again.StandardMoves.Should().BeEmpty();
    }
}

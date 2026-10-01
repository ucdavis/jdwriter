using System.Globalization;
using FluentAssertions;
using Server.Core.Ingest;

namespace Server.Tests.Ingest;

/// <summary>
/// Parity of the C# PD workbook parser against the POC's TypeScript.
///
/// Two input sets, for two different reasons. The 14 SYNTHETIC workbooks live in
/// tests/server.tests/Fixtures/inputs/pd/ — invented content, committable, one per documented
/// trap, and they always run. The one REAL workbook is the acceptance-test input named in the
/// plan; it carries no personal identifiers but lives in the gitignored corpus tree, so that test
/// alone is tagged CorpusParity.
///
/// The synthetic set exists because one real workbook is far too thin to port against: the
/// interesting behavior is in branches a single sample does not take. Worth recording that these
/// fixtures were themselves WRONG on first generation — they spelled the percent label
/// "Percent (%) of Time" where the real form writes "*Percent(%) of Time", so the parser ignored
/// every percent row and the cases silently tested nothing while claiming to cover the rescale
/// traps. Synthetic inputs have to copy real label spellings verbatim.
/// </summary>
public class PdWorkbookParserParityTests
{
    private sealed class PdFixture
    {
        public Manifest Manifest { get; set; } = new();
        public RealCase Real { get; set; } = new();
        public List<SyntheticCase> Synthetic { get; set; } = [];
    }

    private sealed class Manifest
    {
        public int SyntheticCases { get; set; }
        public bool RealSample { get; set; }
        public int ParsedNull { get; set; }
        public int ProposedCodeLeaks { get; set; }
    }

    private sealed class RealCase
    {
        public string File { get; set; } = "";
        public string InputSha256 { get; set; } = "";
        public Parsed? Parsed { get; set; }
        public string? Rendered { get; set; }
        public bool ProposedCodeLeaksIntoPrompt { get; set; }
    }

    private sealed class SyntheticCase
    {
        public string File { get; set; } = "";
        public string Why { get; set; } = "";
        public string InputSha256 { get; set; } = "";
        public Parsed? Parsed { get; set; }
        public string? Rendered { get; set; }
        public bool ProposedCodeLeaksIntoPrompt { get; set; }
    }

    private sealed class Parsed
    {
        public string SheetName { get; set; } = "";
        public List<string> OtherFilledSheets { get; set; } = [];
        public string WorkingTitle { get; set; } = "";
        public string Summary { get; set; } = "";
        public List<Fn> Functions { get; set; } = [];
        public double PctSum { get; set; }
        public string Supervises { get; set; } = "";
        public List<string> Education { get; set; } = [];
        public List<string> Experience { get; set; } = [];
        public List<string> Ksas { get; set; } = [];
        public string ProposedTitleCode { get; set; } = "";
    }

    private sealed class Fn
    {
        public string Name { get; set; } = "";
        public double PctTime { get; set; }
        public List<string> Duties { get; set; } = [];
    }

    private static readonly PdFixture Fixture = Fixtures.Load<PdFixture>("pd.json");

    private static string SyntheticPath(string fixtureFile) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "inputs", "pd",
            Path.GetFileName(fixtureFile));

    private static void AssertMatches(Parsed? want, PdWorkbook? got, string label)
    {
        if (want is null)
        {
            got.Should().BeNull("{0}: the reference returns null", label);
            return;
        }

        got.Should().NotBeNull("{0}: the reference parsed this workbook", label);

        got!.SheetName.Should().Be(want.SheetName, "{0}: chosen sheet", label);
        got.OtherFilledSheets.Should().Equal(want.OtherFilledSheets, "{0}: other filled sheets", label);
        got.WorkingTitle.Should().Be(want.WorkingTitle, "{0}: working title", label);
        got.Summary.Should().Be(want.Summary, "{0}: summary", label);
        got.Supervises.Should().Be(want.Supervises, "{0}: supervises", label);
        got.Education.Should().Equal(want.Education, "{0}: education", label);
        got.Experience.Should().Equal(want.Experience, "{0}: experience", label);
        got.Ksas.Should().Equal(want.Ksas, "{0}: KSAs", label);
        got.ProposedTitleCode.Should().Be(want.ProposedTitleCode, "{0}: proposed code", label);

        got.Functions.Should().HaveCount(want.Functions.Count, "{0}: function count", label);
        for (var i = 0; i < want.Functions.Count; i++)
        {
            got.Functions[i].Name.Should().Be(want.Functions[i].Name, "{0}: function[{1}] name", label, i);
            got.Functions[i].PctTime.Should().Be(want.Functions[i].PctTime, "{0}: function[{1}] pct", label, i);
            got.Functions[i].Duties.Should().Equal(want.Functions[i].Duties, "{0}: function[{1}] duties", label, i);
        }

        got.Functions.Sum(f => f.PctTime).Should().Be(want.PctSum, "{0}: pct sum", label);
    }

    [Fact]
    public void The_synthetic_inputs_are_all_present_and_unmodified()
    {
        // Guards every case below: a missing or edited workbook would otherwise make the
        // comparison pass against whatever happens to be on disk.
        Fixture.Synthetic.Should().HaveCount(Fixture.Manifest.SyntheticCases);
        Fixture.Synthetic.Should().HaveCountGreaterThan(10);

        foreach (var c in Fixture.Synthetic)
        {
            var path = SyntheticPath(c.File);
            File.Exists(path).Should().BeTrue("missing synthetic input {0}", c.File);
            HrtmsCanonical.Sha256(Convert.ToHexString(File.ReadAllBytes(path)))
                .Should().NotBeNull();

            using var sha = System.Security.Cryptography.SHA256.Create();
            Convert.ToHexStringLower(sha.ComputeHash(File.ReadAllBytes(path)))
                .Should().Be(c.InputSha256, "{0} has been modified since the fixture was generated", c.File);
        }
    }

    [Fact]
    public void Every_synthetic_workbook_parses_identically()
    {
        foreach (var c in Fixture.Synthetic)
        {
            using var stream = File.OpenRead(SyntheticPath(c.File));
            var got = PdWorkbookParser.Parse(stream);
            // `why` names the trap, so a failure message says what broke rather than which file.
            AssertMatches(c.Parsed, got, $"{Path.GetFileName(c.File)} ({c.Why})");
        }
    }

    [Fact]
    public void The_rendered_classifier_prompt_matches_exactly()
    {
        // Render output is prompt-assembly: this string IS what a classification runs against, so
        // a drift here changes model behavior while every parsed field still matches.
        foreach (var c in Fixture.Synthetic.Where(x => x.Parsed is not null))
        {
            using var stream = File.OpenRead(SyntheticPath(c.File));
            var pd = PdWorkbookParser.Parse(stream)!;
            PdWorkbookParser.Render(pd).Should().Be(c.Rendered, "rendered prompt for {0}", c.File);
        }
    }

    [Fact]
    public void The_proposed_job_code_never_reaches_the_prompt()
    {
        // The anti-anchoring guarantee, asserted rather than trusted. A model shown the
        // classification the unit wants tends to ratify it, which makes the confidence number
        // meaningless — so the code must travel beside the result, never inside the text.
        Fixture.Manifest.ProposedCodeLeaks.Should().Be(0, "the fixture itself must record no leak");

        var checkedAny = false;

        foreach (var c in Fixture.Synthetic.Where(x => x.Parsed is not null))
        {
            using var stream = File.OpenRead(SyntheticPath(c.File));
            var pd = PdWorkbookParser.Parse(stream)!;
            if (pd.ProposedTitleCode.Length == 0)
            {
                continue;
            }

            checkedAny = true;
            PdWorkbookParser.Render(pd).Should().NotContain(pd.ProposedTitleCode,
                "the proposed code must not appear in the classifier prompt ({0})", c.File);
        }

        // Without this the test could pass by never finding a workbook that states a code.
        checkedAny.Should().BeTrue("at least one synthetic workbook must state a proposed code");
    }

    [Fact]
    public void Two_workbooks_legitimately_parse_to_null()
    {
        // An all-blank workbook and a lookup-only workbook are different failures with the same
        // answer: nothing was filled in, so there is nothing to classify.
        var nulls = Fixture.Synthetic.Where(c => c.Parsed is null).ToList();
        nulls.Should().HaveCount(Fixture.Manifest.ParsedNull);

        foreach (var c in nulls)
        {
            using var stream = File.OpenRead(SyntheticPath(c.File));
            PdWorkbookParser.Parse(stream).Should().BeNull("{0} ({1})", c.File, c.Why);
        }
    }

    [Fact]
    public void The_percent_rescale_traps_behave_as_documented()
    {
        // Named explicitly because this is the trap most likely to be "fixed" into a bug by a
        // future reader: the obvious improvement is to read the cell's number format, which the
        // reference cannot see and therefore does not do.
        Parsed Case(string name) =>
            Fixture.Synthetic.Single(c => c.File.EndsWith(name, StringComparison.Ordinal)).Parsed!;

        // Excel-percent cells hold 0.85; the whole set is fractional, so it scales by 100.
        Case("fractional-percents.xlsx").Functions.Select(f => f.PctTime).Should().Equal(85d, 15d);

        // A form filled in with whole numbers must pass through untouched.
        Case("whole-percents.xlsx").Functions.Select(f => f.PctTime).Should().Equal(85d, 15d);

        // Mixed means NOT every value is <= 1, so nothing is rescaled and 0.5 survives as 0.5.
        // This is why PctTime is a double: an int here would silently destroy it.
        Case("mixed-percents.xlsx").Functions.Select(f => f.PctTime).Should().Equal(0.5d, 50d);
    }

    [Fact]
    public void The_filled_variant_is_chosen_by_content_weight()
    {
        Parsed Case(string name) =>
            Fixture.Synthetic.Single(c => c.File.EndsWith(name, StringComparison.Ordinal)).Parsed!;

        // Four near-identical variants, only Replacement filled. The three blanks parse fine and
        // must lose on weight rather than being excluded by name.
        Case("four-variants-one-filled.xlsx").SheetName.Should().Be("Replacement");
        Case("four-variants-one-filled.xlsx").OtherFilledSheets.Should().BeEmpty();

        // Two filled: the heavier wins and the other is reported.
        Case("two-variants-filled.xlsx").SheetName.Should().Be("Equity");
        Case("two-variants-filled.xlsx").OtherFilledSheets.Should().Equal("New Recruitment");

        // Lookup sheets are the largest in the book and carry no signature label, so they are
        // rejected outright rather than out-weighed.
        Case("lookup-sheets-dominate.xlsx").SheetName.Should().Be("New Recruitment");

        // Identical weights: the first sheet in workbook order wins, which requires a STABLE sort.
        // JS Array.sort has been stable since ES2019; .NET's List.Sort is not, so the port must use
        // LINQ ordering. Without this case that difference is invisible.
        Case("equal-weight-tie.xlsx").SheetName.Should().Be("New Recruitment");
        Case("equal-weight-tie.xlsx").OtherFilledSheets.Should().Equal("Equity");
    }

    [Trait("Category", "CorpusParity")]
    [Fact]
    public void The_real_acceptance_workbook_parses_identically()
    {
        // The workbook named in the plan's acceptance test. It carries no personal identifiers,
        // but it lives in the gitignored corpus tree, so this case is corpus-tagged.
        var path = ResolveRealWorkbook();
        using var stream = File.OpenRead(path);
        var got = PdWorkbookParser.Parse(stream);

        AssertMatches(Fixture.Real.Parsed, got, "PD_Evaluation_Analyst_final.xlsx");
        PdWorkbookParser.Render(got!).Should().Be(Fixture.Real.Rendered);

        // The documented behavior of the real sample: 85/15 rescaled from 0.85/0.15, summing to
        // 100, and proposing 7399 unpadded — which the classifier must never see.
        got!.Functions.Select(f => f.PctTime).Should().Equal(85d, 15d);
        got.ProposedTitleCode.Should().Be("7399");
        PdWorkbookParser.Render(got).Should().NotContain("7399");
    }

    private static string ResolveRealWorkbook()
    {
        var fromEnv = Environment.GetEnvironmentVariable("JDW_CORPUS_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            var p = Path.Combine(fromEnv, "PD_Evaluation_Analyst_final.xlsx");
            File.Exists(p).Should().BeTrue("expected the PD workbook at {0}", p);
            return p;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "..", "JDWriter", "Sample JDs",
                "PD_Evaluation_Analyst_final.xlsx");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "PD_Evaluation_Analyst_final.xlsx not found. Set JDW_CORPUS_DIR, or exclude corpus " +
            "tests: dotnet test --filter \"Category!=CorpusParity\".");
    }
}

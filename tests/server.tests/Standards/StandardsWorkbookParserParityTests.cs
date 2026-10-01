using System.Security.Cryptography;
using FluentAssertions;
using Server.Core.Standards;

namespace Server.Tests.Standards;

/// <summary>
/// Parity of the C# standards workbook parser against the POC's TypeScript.
///
/// The two layouts have very different evidence behind them, and that asymmetry is the point of
/// this file:
///
///   LAYOUT A is verified against the 18 real Job Builder sheets that produce all 216 standards in
///   the store. Those workbooks are public UC classification documents with no employee data, so
///   they are committed as test inputs.
///
///   LAYOUT B is a BLIND PORT. No real layout-B workbook exists locally — every usable sheet in the
///   corpus is layout A — so it is checked only against synthetic inputs built from the format as
///   documented. That pins the C# to the reference's behavior on inputs shaped the way the format
///   is DESCRIBED; it proves nothing about either implementation against a real UC website
///   PDF-to-Excel export. When such a file appears, drop it in the POC's `Job Standards/`,
///   regenerate, and this asymmetry goes away.
/// </summary>
public class StandardsWorkbookParserParityTests
{
    private sealed class SyntheticFile
    {
        public SyntheticManifest Manifest { get; set; } = new();
        public List<SyntheticCase> Synthetic { get; set; } = [];
    }

    private sealed class SyntheticManifest
    {
        public int Cases { get; set; }
        public List<string> LayoutsSeen { get; set; } = [];
        public string Caveat { get; set; } = "";
    }

    private sealed class SyntheticCase
    {
        public string File { get; set; } = "";
        public string Why { get; set; } = "";
        public string InputSha256 { get; set; } = "";
        public List<LayoutEntry> Layouts { get; set; } = [];
        public List<StandardExpectation> Standards { get; set; } = [];
    }

    private sealed class LayoutEntry
    {
        public string Sheet { get; set; } = "";
        public string Layout { get; set; } = "";
    }

    private sealed class PerWorkbookFile
    {
        public List<WorkbookEntry> Files { get; set; } = [];
    }

    private sealed class WorkbookEntry
    {
        public string File { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public List<LayoutEntry> Sheets { get; set; } = [];
        public List<StandardExpectation> Standards { get; set; } = [];
    }

    private sealed class StandardExpectation
    {
        public string LongTitle { get; set; } = "";
        public string? Code { get; set; }
        public string PersProg { get; set; } = "";
        public string Grade { get; set; } = "";
        public string Flsa { get; set; } = "";
        public string Union { get; set; } = "";
        public string GenericScope { get; set; } = "";
        public string CustomScope { get; set; } = "";
        public List<string> KeyResponsibilities { get; set; } = [];
        public List<string> Ksa { get; set; } = [];
        public List<string> Education { get; set; } = [];
        public List<string> Licenses { get; set; } = [];
        public List<string> SpecialConditions { get; set; } = [];
        public string TitleCodeKey { get; set; } = "";
    }

    private static readonly SyntheticFile Synthetic =
        Fixtures.Load<SyntheticFile>("standards.synthetic.json");

    private static string InputPath(string sub, string fixtureFile) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "inputs", sub, Path.GetFileName(fixtureFile));

    private static string Sha256File(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void AssertSame(
        List<StandardExpectation> want, List<ClassStandardRecord> got, string label)
    {
        got.Should().HaveCount(want.Count, "{0}: standard count", label);

        // The fixture is sorted by strict key; sort the same way so comparison is positional.
        var ordered = got
            .OrderBy(s => Server.Core.Titles.TitleNormalizer.TitleCodeKey(s.LongTitle), StringComparer.Ordinal)
            .ToList();

        for (var i = 0; i < want.Count; i++)
        {
            var w = want[i];
            var g = ordered[i];
            g.LongTitle.Should().Be(w.LongTitle, "{0}[{1}]: long title", label, i);
            g.Code.Should().Be(w.Code, "{0}[{1}]: code", label, i);
            g.PersProg.Should().Be(w.PersProg, "{0}[{1}]: pers prog", label, i);
            g.Grade.Should().Be(w.Grade, "{0}[{1}]: grade", label, i);
            g.Flsa.Should().Be(w.Flsa, "{0}[{1}]: flsa", label, i);
            g.Union.Should().Be(w.Union, "{0}[{1}]: union", label, i);
            g.GenericScope.Should().Be(w.GenericScope, "{0}[{1}]: generic scope", label, i);
            g.CustomScope.Should().Be(w.CustomScope, "{0}[{1}]: custom scope", label, i);
            g.KeyResponsibilities.Should().Equal(w.KeyResponsibilities, "{0}[{1}]: key responsibilities", label, i);
            g.Ksa.Should().Equal(w.Ksa, "{0}[{1}]: KSA", label, i);
            g.Education.Should().Equal(w.Education, "{0}[{1}]: education", label, i);
            g.Licenses.Should().Equal(w.Licenses, "{0}[{1}]: licenses", label, i);
            g.SpecialConditions.Should().Equal(w.SpecialConditions, "{0}[{1}]: special conditions", label, i);
        }
    }

    // ------------------------------------------------------------------ layout B (synthetic)

    [Fact]
    public void The_synthetic_inputs_are_present_and_unmodified()
    {
        Synthetic.Synthetic.Should().HaveCount(Synthetic.Manifest.Cases);
        Synthetic.Manifest.LayoutsSeen.Should().Contain("B", "these exist to cover layout B");

        foreach (var c in Synthetic.Synthetic)
        {
            var path = InputPath("standards", c.File);
            File.Exists(path).Should().BeTrue("missing synthetic input {0}", c.File);
            Sha256File(path).Should().Be(c.InputSha256, "{0} was modified after generation", c.File);
        }
    }

    [Fact]
    public void Every_synthetic_workbook_parses_identically()
    {
        foreach (var c in Synthetic.Synthetic)
        {
            using var stream = File.OpenRead(InputPath("standards", c.File));
            var got = StandardsWorkbookParser.Parse(stream);
            AssertSame(c.Standards, got, $"{Path.GetFileName(c.File)} ({c.Why})");
        }
    }

    [Fact]
    public void Wrapped_cells_rejoin_their_item_even_across_a_page_break()
    {
        // The single hardest behavior in layout B, and the reason the continuation pointer is not
        // cleared by header rows: a wrapped cell can resume after a page break, so its remainder
        // arrives with a whole repeated header block in between.
        var c = Synthetic.Synthetic.Single(x =>
            x.File.EndsWith("layout-b-continuation-after-page-break.xlsx", StringComparison.Ordinal));

        using var stream = File.OpenRead(InputPath("standards", c.File));
        var got = StandardsWorkbookParser.Parse(stream);

        got.Should().ContainSingle();
        got[0].KeyResponsibilities.Should().Equal(
            "Leads the effort and carries it to completion.",
            "Reviews the outcome.");
    }

    [Fact]
    public void Page_furniture_is_not_mistaken_for_wrapped_text()
    {
        // A row carrying content in column A is page furniture. Appending it would silently inject
        // "Page 2 of 7" style text into a responsibility.
        var c = Synthetic.Synthetic.Single(x =>
            x.File.EndsWith("layout-b-column-a-content-is-not-continuation.xlsx", StringComparison.Ordinal));

        using var stream = File.OpenRead(InputPath("standards", c.File));
        var got = StandardsWorkbookParser.Parse(stream);

        got.Should().ContainSingle();
        got[0].KeyResponsibilities.Should().Equal("Directs the unit and sets its direction.");
        got[0].KeyResponsibilities[0].Should().NotContain("FOOTER");
    }

    [Fact]
    public void Layout_B_states_a_code_but_never_a_grade()
    {
        // The asymmetry that makes the two layouts complementary: B has the exact join key,
        // A has the grade. Ingest backfills whichever is missing.
        var c = Synthetic.Synthetic.Single(x =>
            x.File.EndsWith("layout-b-basic.xlsx", StringComparison.Ordinal));

        using var stream = File.OpenRead(InputPath("standards", c.File));
        var got = StandardsWorkbookParser.Parse(stream);

        got.Should().HaveCount(2);
        got.Should().OnlyContain(s => s.Code != null && s.Code.Length > 0);
        got.Should().OnlyContain(s => s.Grade == "");
    }

    [Fact]
    public void Each_sheet_is_dispatched_independently()
    {
        // One workbook can hold both layouts; A is tried first, then B.
        var c = Synthetic.Synthetic.Single(x =>
            x.File.EndsWith("layout-a-and-b-in-one-workbook.xlsx", StringComparison.Ordinal));

        using var stream = File.OpenRead(InputPath("standards", c.File));
        var got = StandardsWorkbookParser.Parse(stream);

        got.Should().HaveCount(2);
        // The layout-A sheet supplies a grade and no code; the layout-B sheet the reverse.
        got.Should().ContainSingle(s => s.Grade == "Grade 21" && s.Code == null);
        got.Should().ContainSingle(s => s.Grade == "" && s.Code == "999999");
    }

    // ------------------------------------------------------------------ layout A (real workbooks)

    [Trait("Category", "StandardsWorkbooks")]
    [Fact]
    public void Every_real_workbook_parses_identically()
    {
        // Public UC classification documents, committed as test inputs — tagged only because they
        // are bulky, not because they are sensitive.
        var expected = Fixtures.Load<PerWorkbookFile>("standards.perWorkbook.json");
        expected.Files.Should().HaveCountGreaterThan(15);

        var missing = new List<string>();
        var mismatches = new List<string>();
        var totalStandards = 0;

        foreach (var wb in expected.Files)
        {
            var path = InputPath("standards-real", wb.File);
            if (!File.Exists(path))
            {
                missing.Add(wb.File);
                continue;
            }

            Sha256File(path).Should().Be(wb.Sha256, "{0} was modified after generation", wb.File);

            using var stream = File.OpenRead(path);
            var got = StandardsWorkbookParser.Parse(stream);
            totalStandards += got.Count;

            try
            {
                AssertSame(wb.Standards, got, wb.File);
            }
            catch (Exception ex)
            {
                mismatches.Add($"  {wb.File}: {ex.Message.Split('\n')[0]}");
            }
        }

        missing.Should().BeEmpty("real standards workbooks are missing from the test inputs:\n{0}",
            string.Join('\n', missing));
        mismatches.Should().BeEmpty("{0} workbooks parsed differently:\n{1}",
            mismatches.Count, string.Join('\n', mismatches.Take(10)));

        // 216 before dedup across all workbooks — the number the store reduces to 214.
        totalStandards.Should().Be(216);
    }

    [Trait("Category", "StandardsWorkbooks")]
    [Fact]
    public void Layout_B_has_no_real_world_coverage_and_that_is_recorded()
    {
        // Not a behavioral test: an assertion about the state of the evidence, so the coverage gap
        // cannot quietly disappear. If a real layout-B workbook is ever added, this fails and the
        // COVERAGE WARNING on the parser should be removed along with it.
        var expected = Fixtures.Load<PerWorkbookFile>("standards.perWorkbook.json");

        var layoutsInRealWorkbooks = expected.Files
            .SelectMany(f => f.Sheets)
            .Select(s => s.Layout)
            .Distinct()
            .ToList();

        layoutsInRealWorkbooks.Should().NotContain("B",
            "if this fails, a real layout-B workbook now exists: regenerate the fixtures, verify " +
            "the blind port against it, and drop the COVERAGE WARNING from StandardsWorkbookParser");
        layoutsInRealWorkbooks.Should().Contain("A");
    }
}

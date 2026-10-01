using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Tests.Standards;

/// <summary>
/// The store ingest — parse every workbook, backfill code then grade from the title reference,
/// dedup last-wins on the strict key — held to <c>standards.store.json</c>, which the POC's
/// fixture generator produces by running exactly that sequence over the same 19 workbooks.
/// </summary>
public class StandardsImporterParityTests
{
    private sealed class StoreFile
    {
        public Manifest Manifest { get; set; } = new();
        public List<StoreRow> Store { get; set; } = [];
    }

    private sealed class Manifest
    {
        public int ParsedBeforeDedup { get; set; }
        public int StoreAfterDedup { get; set; }
        public Report Report { get; set; } = new();
    }

    private sealed class Report
    {
        public int Count { get; set; }
        public int Coded { get; set; }
        public List<string> Unknown { get; set; } = [];
        public List<string> Ambiguous { get; set; } = [];
    }

    private sealed class StoreRow
    {
        public string TitleCodeKey { get; set; } = "";
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
    }

    private sealed class ReferenceRow
    {
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string Grade { get; set; } = "";
        public string Function { get; set; } = "";
        public string Family { get; set; } = "";
        public string Source { get; set; } = "";
    }

    private static readonly StoreFile Expected = Fixtures.Load<StoreFile>("standards.store.json");

    private static readonly string WorkbookDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "inputs", "standards-real");

    private static TitleCodeIndex Titles() =>
        new(Fixtures.Load<List<ReferenceRow>>("title-codes.json").Select(r => new TitleCode
        {
            Code = r.Code,
            Title = r.Title,
            Grade = r.Grade,
            Function = r.Function,
            Family = r.Family,
            Source = r.Source,
            TitleKey = TitleNormalizer.TitleKey(r.Title),
            TitleCodeKey = TitleNormalizer.TitleCodeKey(r.Title),
        }));

    private static readonly Lazy<StandardsImportResult> Result =
        new(() => StandardsImporter.Build(WorkbookDir, Titles()));

    [Trait("Category", "StandardsWorkbooks")]
    [Fact]
    public void The_store_dedups_216_parsed_standards_to_214()
    {
        Expected.Manifest.ParsedBeforeDedup.Should().Be(216, "the fixture itself is the full set");
        Result.Value.Store.Should().HaveCount(Expected.Manifest.StoreAfterDedup);
    }

    [Trait("Category", "StandardsWorkbooks")]
    [Fact]
    public void Every_stored_standard_matches_the_reference_field_for_field()
    {
        var got = Result.Value.Store;
        got.Should().HaveCount(Expected.Store.Count);

        for (var i = 0; i < Expected.Store.Count; i++)
        {
            var w = Expected.Store[i];
            var g = got[i];
            TitleNormalizer.TitleCodeKey(g.LongTitle).Should().Be(w.TitleCodeKey, "[{0}] order and strict key", i);
            g.LongTitle.Should().Be(w.LongTitle, "[{0}] long title", i);
            g.Code.Should().Be(w.Code, "[{0}] {1}: code (backfilled from the title)", i, w.LongTitle);
            g.Grade.Should().Be(w.Grade, "[{0}] {1}: grade", i, w.LongTitle);
            g.PersProg.Should().Be(w.PersProg, "[{0}] pers prog", i);
            g.Flsa.Should().Be(w.Flsa, "[{0}] flsa", i);
            g.Union.Should().Be(w.Union, "[{0}] union", i);
            g.GenericScope.Should().Be(w.GenericScope, "[{0}] generic scope", i);
            g.CustomScope.Should().Be(w.CustomScope, "[{0}] custom scope", i);
            g.KeyResponsibilities.Should().Equal(w.KeyResponsibilities, "[{0}] key responsibilities", i);
            g.Ksa.Should().Equal(w.Ksa, "[{0}] KSA", i);
            g.Education.Should().Equal(w.Education, "[{0}] education", i);
            g.Licenses.Should().Equal(w.Licenses, "[{0}] licenses", i);
            g.SpecialConditions.Should().Equal(w.SpecialConditions, "[{0}] special conditions", i);
        }
    }

    [Trait("Category", "StandardsWorkbooks")]
    [Fact]
    public void The_report_separates_unknown_titles_from_ambiguous_ones()
    {
        // "Not a UC Davis title" and "needs a human to pick a code" need different follow-up,
        // which is why the report names them rather than counting them.
        var r = Result.Value.Report;
        var w = Expected.Manifest.Report;

        r.Count.Should().Be(w.Count);
        r.Coded.Should().Be(w.Coded);
        r.Unknown.Should().Equal(w.Unknown);
        r.Ambiguous.Should().Equal(w.Ambiguous);
    }

    // ------------------------------------------------------------------ constructed branches
    //
    // Neither of these fires on the real workbooks: every layout-A standard states a grade, and
    // the only duplicate strict keys there carry identical content. The fixture suite passes with
    // either branch deleted, so they are pinned here instead.

    private static TitleCodeIndex OneRow(string code, string title, string grade) =>
        new([new TitleCode
        {
            Code = code,
            Title = title,
            Grade = grade,
            TitleKey = TitleNormalizer.TitleKey(title),
            TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
        }]);

    [Fact]
    public void A_missing_grade_is_backfilled_from_the_resolved_code()
    {
        var titles = OneRow("001111", "WIDGET ANALYST 2", "Grade 19");

        var result = StandardsImporter.Build(
            [new ClassStandardRecord { LongTitle = "Widget Analyst 2", Grade = "" }], titles);

        var std = result.Store.Single();
        std.Code.Should().Be("001111");
        std.Grade.Should().Be("Grade 19");
    }

    [Fact]
    public void A_stated_grade_is_never_overwritten_by_the_reference()
    {
        var titles = OneRow("001111", "WIDGET ANALYST 2", "Grade 19");

        var result = StandardsImporter.Build(
            [new ClassStandardRecord { LongTitle = "Widget Analyst 2", Grade = "Grade 20" }], titles);

        result.Store.Single().Grade.Should().Be("Grade 20");
    }

    [Fact]
    public void On_a_duplicate_strict_key_the_later_workbook_wins()
    {
        var titles = OneRow("001111", "WIDGET ANALYST 2", "Grade 19");

        var result = StandardsImporter.Build(
        [
            new ClassStandardRecord { LongTitle = "Widget Analyst 2", Grade = "Grade 19", GenericScope = "older" },
            new ClassStandardRecord { LongTitle = "Widget Analyst 2", Grade = "Grade 19", GenericScope = "newer" },
        ], titles);

        result.Store.Single().GenericScope.Should().Be("newer");
    }

    [Trait("Category", "StandardsWorkbooks")]
    [Fact]
    public void Workbooks_are_read_in_ordinal_path_order_so_the_later_file_wins()
    {
        // Built from a REAL workbook rather than a hand-drawn one, so the format is right by
        // construction: copy it, change one standard's generic scope in the copy, and name the
        // copy to sort after the original.
        const string source = "Job Standard_SideBySide (3).xlsx";
        var dir = Directory.CreateTempSubdirectory("jdw-standards-order-");
        try
        {
            File.Copy(Path.Combine(WorkbookDir, source), Path.Combine(dir.FullName, "a.xlsx"));

            ClassStandardRecord target;
            using (var stream = File.OpenRead(Path.Combine(WorkbookDir, source)))
            {
                target = StandardsWorkbookParser.Parse(stream).First(x => x.GenericScope.Length > 0);
            }

            using (var wb = new ClosedXML.Excel.XLWorkbook(Path.Combine(WorkbookDir, source)))
            {
                var cell = wb.Worksheets
                    .SelectMany(ws => ws.CellsUsed())
                    .First(c => c.GetString().Trim() == target.GenericScope);
                cell.Value = "LATER FILE";
                wb.SaveAs(Path.Combine(dir.FullName, "b.xlsx"));
            }

            var result = StandardsImporter.Build(dir.FullName, Titles());

            result.Store.Single(x => x.LongTitle == target.LongTitle).GenericScope.Should().Be("LATER FILE");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Entity_round_trip_preserves_every_field_and_derives_both_keys()
    {
        var record = new ClassStandardRecord
        {
            LongTitle = "Analyst 3 RP GF",
            Code = "001234",
            PersProg = "PSS",
            Grade = "Grade 21",
            Flsa = "Exempt",
            Union = "99",
            GenericScope = "g",
            CustomScope = "c",
            KeyResponsibilities = ["k1", "k2"],
            Ksa = ["s"],
            Education = ["e"],
            Licenses = ["l"],
            SpecialConditions = ["x"],
        };

        var entity = StandardsStore.ToEntity(record);
        entity.TitleCodeKey.Should().Be(TitleNormalizer.TitleCodeKey(record.LongTitle));
        entity.TitleKey.Should().Be(TitleNormalizer.TitleKey(record.LongTitle));
        StandardsStore.ToRecord(entity).Should().BeEquivalentTo(record);
    }
}

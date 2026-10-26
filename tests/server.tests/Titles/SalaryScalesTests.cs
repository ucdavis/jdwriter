using ClosedXML.Excel;
using FluentAssertions;
using Server.Core.Titles;

namespace Server.Tests.Titles;

public class SalaryScalesTests
{
    /// <summary>A workbook laid out as Compensation's is: title rows, a header, four pay types per grade.</summary>
    private static byte[] Workbook(params (int Grade, decimal Min, decimal Mid, decimal Max)[] grades)
    {
        using var book = new XLWorkbook();
        var ws = book.AddWorksheet("Career Tracks Salary Structure ");
        ws.Cell(1, 1).Value = "UC Davis Career Tracks Salary Ranges ";
        ws.Cell(2, 1).Value = "Geo Diff 112% for Non-Represented Staff";
        ws.Cell(3, 1).Value = "Effective July 1, 2026";
        string[] header = ["Grade", "Pay Type", "Minimum", "1st Quartile", "Midpoint", "3rd Quartile", "Maximum", "Spread"];
        for (var c = 0; c < header.Length; c++)
        {
            ws.Cell(4, c + 1).Value = header[c];
        }

        var r = 5;
        foreach (var g in grades)
        {
            foreach (var (type, per) in new[] { ("Annual", 1m), ("Monthly", 12m), ("Biweekly", 26.1m), ("Hourly", 2088m) })
            {
                ws.Cell(r, 1).Value = g.Grade;
                ws.Cell(r, 2).Value = type;
                ws.Cell(r, 3).Value = g.Min / per;
                ws.Cell(r, 4).Value = (g.Min + g.Mid) / 2 / per;
                ws.Cell(r, 5).Value = g.Mid / per;
                ws.Cell(r, 6).Value = (g.Mid + g.Max) / 2 / per;
                ws.Cell(r, 7).Value = g.Max / per;
                ws.Cell(r, 8).Value = 0.6;
                r++;
            }
        }

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public void Reads_every_grade_and_pay_type_with_the_effective_date_and_note()
    {
        var read = SalaryScales.Read(Workbook((20, 77_000m, 100_000m, 123_000m), (19, 66_000m, 85_000m, 104_000m)));

        read.EffectiveDate.Should().Be(new DateOnly(2026, 7, 1));
        read.Note.Should().Be("Geo Diff 112% for Non-Represented Staff");
        read.Rows.Should().HaveCount(8);
        read.Rows.Should().ContainEquivalentOf(new { Grade = 20, PayType = "Annual", Minimum = 77_000m, Midpoint = 100_000m, Maximum = 123_000m });
    }

    [Fact]
    public void Refuses_what_isnt_a_salary_workbook()
    {
        var notExcel = () => SalaryScales.Read("not a workbook"u8.ToArray());
        notExcel.Should().Throw<SalaryScaleRefusedException>().WithMessage("*Excel*");

        using var book = new XLWorkbook();
        book.AddWorksheet("Sheet1").Cell(1, 1).Value = "Something else";
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        var noTable = () => SalaryScales.Read(stream.ToArray());
        noTable.Should().Throw<SalaryScaleRefusedException>().WithMessage("*Grade, Pay Type, Minimum*");
    }

    [Fact]
    public async Task An_import_replaces_the_scale_and_a_grade_finds_its_annual_range()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var scales = new SalaryScales(db);
        await scales.ImportAsync(SalaryScales.Read(Workbook((20, 1m, 2m, 3m), (21, 1m, 2m, 3m))));
        var count = await scales.ImportAsync(SalaryScales.Read(Workbook((20, 77_000m, 100_000m, 123_000m))));

        count.Should().Be(4);
        (await scales.AnnualAsync()).Select(a => a.Grade).Should().Equal(20);
        (await scales.ForGradeAsync("Grade 20")).Should().BeEquivalentTo(new { Grade = 20, Minimum = 77_000m, Midpoint = 100_000m, Maximum = 123_000m, EffectiveDate = new DateOnly(2026, 7, 1) });
        (await scales.ForGradeAsync("20"))!.Grade.Should().Be(20);
    }

    [Fact]
    public async Task A_represented_title_or_unknown_grade_has_no_range()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var scales = new SalaryScales(db);
        await scales.ImportAsync(SalaryScales.Read(Workbook((20, 77_000m, 100_000m, 123_000m))));

        (await scales.ForGradeAsync("STEPS")).Should().BeNull();
        (await scales.ForGradeAsync("")).Should().BeNull();
        (await scales.ForGradeAsync("Grade 99")).Should().BeNull();
    }
}

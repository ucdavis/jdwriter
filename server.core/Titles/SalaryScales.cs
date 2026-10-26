using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Core.Titles;

public sealed class SalaryScaleRefusedException(string message) : Exception(message);

/// <summary>A grade's annual range, as the VRF and the class page show it.</summary>
public sealed record AnnualRange(int Grade, decimal Minimum, decimal Midpoint, decimal Maximum, DateOnly? EffectiveDate, string Scale, string Note);

/// <summary>
/// UC Davis's Career Tracks salary ranges, from Compensation's workbook ("Grade | Pay Type | Minimum |
/// 1st Quartile | Midpoint | 3rd Quartile | Maximum"). An upload replaces the scale whole: the
/// workbook is the structure as of its effective date. They are the non-represented ranges — a
/// represented title's grade is "STEPS", and finds none here.
/// </summary>
public sealed partial class SalaryScales
{
    public const string CareerTracks = "Career Tracks";

    [GeneratedRegex(@"(?i)effective\s+(.+)$")]
    private static partial Regex Effective();

    [GeneratedRegex(@"^\D*(\d{1,2})\D*$")]
    private static partial Regex GradeNumber();

    private readonly AppDbContext _db;

    public SalaryScales(AppDbContext db) => _db = db;

    public sealed record Row(int Grade, string PayType, decimal Minimum, decimal FirstQuartile, decimal Midpoint, decimal ThirdQuartile, decimal Maximum);

    public sealed record Workbook(DateOnly? EffectiveDate, string Note, List<Row> Rows);

    public static Workbook Read(byte[] xlsx)
    {
        XLWorkbook book;
        try
        {
            book = new XLWorkbook(new MemoryStream(xlsx));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SalaryScaleRefusedException("That isn't an Excel workbook (.xlsx).");
        }

        using (book)
        {
            var ws = book.Worksheets.First();
            var used = ws.RangeUsed() ?? throw new SalaryScaleRefusedException("The workbook is empty.");
            DateOnly? effective = null;
            var note = "";
            int? header = null;
            var lastRow = used.LastRow().RowNumber();
            for (var r = 1; r <= lastRow; r++)
            {
                var a = ws.Cell(r, 1).GetString().Trim();
                if (a.Equals("Grade", StringComparison.OrdinalIgnoreCase)
                    && ws.Cell(r, 2).GetString().Trim().Equals("Pay Type", StringComparison.OrdinalIgnoreCase)
                    && ws.Cell(r, 3).GetString().Trim().Equals("Minimum", StringComparison.OrdinalIgnoreCase))
                {
                    header = r;
                    break;
                }

                var e = Effective().Match(a);
                if (e.Success && DateTime.TryParse(e.Groups[1].Value, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var d))
                {
                    effective = DateOnly.FromDateTime(d);
                }
                else if (a.Contains("Represented", StringComparison.OrdinalIgnoreCase) || a.Contains("Geo Diff", StringComparison.OrdinalIgnoreCase))
                {
                    note = a;
                }
            }

            if (header is null)
            {
                throw new SalaryScaleRefusedException("No salary table was found: the sheet needs a header row starting Grade, Pay Type, Minimum.");
            }

            var rows = new List<Row>();
            for (var r = header.Value + 1; r <= lastRow; r++)
            {
                if (!int.TryParse(ws.Cell(r, 1).GetString().Trim(), out var grade))
                {
                    continue;
                }

                decimal N(int col) => ws.Cell(r, col).TryGetValue<decimal>(out var v) ? v : 0m;
                var payType = ws.Cell(r, 2).GetString().Trim();
                if (payType.Length == 0 || N(3) <= 0 || N(7) < N(3))
                {
                    continue;
                }

                rows.Add(new Row(grade, payType, N(3), N(4), N(5), N(6), N(7)));
            }

            return rows.Count == 0
                ? throw new SalaryScaleRefusedException("The salary table has no rows that could be read.")
                : new Workbook(effective, note, rows);
        }
    }

    /// <summary>Replace the Career Tracks scale with the workbook's, in one save: all of it or none.</summary>
    public async Task<int> ImportAsync(Workbook workbook, CancellationToken ct = default)
    {
        _db.SalaryRanges.RemoveRange(await _db.SalaryRanges.Where(s => s.Scale == CareerTracks).ToListAsync(ct));
        var now = DateTimeOffset.UtcNow;
        foreach (var r in workbook.Rows.DistinctBy(r => (r.Grade, r.PayType.ToLowerInvariant())))
        {
            _db.SalaryRanges.Add(new SalaryRange
            {
                Scale = CareerTracks,
                Grade = r.Grade,
                PayType = r.PayType,
                Minimum = r.Minimum,
                FirstQuartile = r.FirstQuartile,
                Midpoint = r.Midpoint,
                ThirdQuartile = r.ThirdQuartile,
                Maximum = r.Maximum,
                EffectiveDate = workbook.EffectiveDate,
                Note = workbook.Note,
                ImportedAt = now,
            });
        }

        await _db.SaveChangesAsync(ct);
        return await _db.SalaryRanges.CountAsync(s => s.Scale == CareerTracks, ct);
    }

    /// <summary>Every grade's annual range, highest grade first.</summary>
    public async Task<List<AnnualRange>> AnnualAsync(CancellationToken ct = default) =>
        [.. (await _db.SalaryRanges.AsNoTracking().Where(s => s.Scale == CareerTracks && s.PayType == "Annual").ToListAsync(ct))
            .OrderByDescending(s => s.Grade)
            .Select(s => new AnnualRange(s.Grade, s.Minimum, s.Midpoint, s.Maximum, s.EffectiveDate, s.Scale, s.Note))];

    /// <summary>
    /// The annual range for a grade as a JD states it ("Grade 20", "20"), or null: a represented
    /// title's "STEPS", or a grade the scale doesn't have.
    /// </summary>
    public async Task<AnnualRange?> ForGradeAsync(string grade, CancellationToken ct = default)
    {
        var m = GradeNumber().Match(grade ?? "");
        if (!m.Success)
        {
            return null;
        }

        var g = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var row = await _db.SalaryRanges.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Scale == CareerTracks && s.Grade == g && s.PayType == "Annual", ct);
        return row is null ? null : new AnnualRange(row.Grade, row.Minimum, row.Midpoint, row.Maximum, row.EffectiveDate, row.Scale, row.Note);
    }
}

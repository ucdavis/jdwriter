using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Server.Core.Titles;

/// <summary>One row of the UC Career Tracks title code listing, as the report states it.</summary>
public sealed class CtListingRow
{
    public string UcJobCode { get; set; } = "";

    /// <summary>The full title ("Ambulatory Care Administration Coordinator 3"), not payroll's abbreviation.</summary>
    public string Title { get; set; } = "";

    public string JobFamily { get; set; } = "";
    public string JobFunction { get; set; } = "";

    /// <summary>C (campus), MC (medical center), OP (Office of the President), AG, PS.</summary>
    public string Origin { get; set; } = "";

    /// <summary>P1–P5 professional, S1–S3 supervisor, M1–M4 manager, ME executive, L lead, NL.</summary>
    public string LevelCode { get; set; } = "";

    public DateOnly? LastUpdated { get; set; }

    /// <summary>A title the Office of the President alone uses.</summary>
    public bool OpOnly { get; set; }

    public string Flsa { get; set; } = "";
    public string PersonnelProgram { get; set; } = "";

    /// <summary>A (active), GF (grandfathered), F.</summary>
    public string JobStatus { get; set; } = "";

    public string Grade { get; set; } = "";
}

/// <summary>
/// Reads the Career Tracks "Title Code Listing Report" PDF. The report is a table that PDF text
/// extraction flattens, and two of its things defeat reading it line by line: job family and job
/// function run together with no separator ("Ambulatory Care Clinics or Ctr Ambulatory Care
/// Administration"), and a long title wraps onto the lines above and below its row. So each page's
/// header row fixes where every column starts, every word is placed in its column by position, and
/// a line without a job code is joined to the nearest row that has one.
/// </summary>
public static partial class CtListing
{
    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex JobCode();

    [GeneratedRegex(@"^\d{2}-[A-Za-z]{3}-\d{2}$")]
    private static partial Regex Date();

    private enum Column { Family, Function, Origin, Level, Code, Updated, OpOnly, Title, Flsa, Program, Status, Grade }

    /// <summary>The header word that marks where each column starts.</summary>
    private static readonly (Column Column, string Header)[] Headers =
    [
        (Column.Function, "JOB_FUNCTION"), (Column.Origin, "Origin"), (Column.Level, "LvlCd"),
        (Column.Code, "JobCode"), (Column.Updated, "Last"), (Column.OpOnly, "OP_Onl"), (Column.Title, "Job_Title"),
        (Column.Flsa, "FLSA"), (Column.Program, "PerProg"), (Column.Status, "JobStat"), (Column.Grade, "GRADE"),
    ];

    /// <summary>Whether a file is this report: its header row is unmistakable.</summary>
    public static bool IsListing(byte[] pdf)
    {
        try
        {
            using var doc = PdfDocument.Open(pdf);
            var words = doc.GetPage(1).GetWords().Select(w => w.Text).ToHashSet(StringComparer.Ordinal);
            return words.Contains("JobCode") && words.Contains("JOB_FUNCTION") && words.Contains("LvlCd");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    public static List<CtListingRow> Parse(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        var rows = new List<CtListingRow>();
        foreach (var page in doc.GetPages())
        {
            rows.AddRange(ParsePage(page));
        }

        // A code listed twice keeps its first row.
        return [.. rows.DistinctBy(r => r.UcJobCode)];
    }

    private static IEnumerable<CtListingRow> ParsePage(Page page)
    {
        var words = page.GetWords().ToList();
        var starts = ColumnStarts(words);
        if (starts is null)
        {
            yield break;
        }

        var headerBottom = words.Where(w => w.Text == "JobCode").Min(w => w.BoundingBox.Bottom);

        // Lines, top to bottom, of the table body. The header has a second line just under it ("on",
        // "y" — the ends of "Last updated on" and "OP_Only"), so the body starts clearly below.
        var lines = words.Where(w => w.BoundingBox.Bottom < headerBottom - 10)
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom))
            .OrderByDescending(g => g.Key)
            .Select(g => (Y: g.Key, Words: g.OrderBy(w => w.BoundingBox.Left).ToList()))
            .ToList();

        // A row is anchored by its job code; other lines are wrapped text of the nearest anchor.
        var anchors = lines.Where(l => l.Words.Any(w => JobCode().IsMatch(w.Text) && Place(w, starts) == Column.Code)).ToList();
        if (anchors.Count == 0)
        {
            yield break;
        }

        var cells = anchors.ToDictionary(a => a.Y, _ => new Dictionary<Column, List<(double Y, double X, string Text)>>());
        foreach (var line in lines)
        {
            var anchor = anchors.MinBy(a => Math.Abs(a.Y - line.Y)).Y;
            foreach (var w in line.Words)
            {
                var col = Place(w, starts);
                if (!cells[anchor].TryGetValue(col, out var list))
                {
                    cells[anchor][col] = list = [];
                }

                list.Add((line.Y, w.BoundingBox.Left, w.Text));
            }
        }

        foreach (var anchor in anchors)
        {
            var c = cells[anchor.Y];
            string Cell(Column col) => c.TryGetValue(col, out var parts)
                ? string.Join(" ", parts.OrderByDescending(p => p.Y).ThenBy(p => p.X).Select(p => p.Text)).Trim()
                : "";

            var code = Cell(Column.Code);
            if (!JobCode().IsMatch(code))
            {
                continue;
            }

            var updated = Cell(Column.Updated);
            yield return new CtListingRow
            {
                UcJobCode = code,
                Title = Cell(Column.Title),
                JobFamily = Cell(Column.Family),
                JobFunction = Cell(Column.Function),
                Origin = Cell(Column.Origin),
                LevelCode = Cell(Column.Level),
                LastUpdated = Date().IsMatch(updated)
                              && DateOnly.TryParseExact(updated, "dd-MMM-yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? d
                    : null,
                OpOnly = string.Equals(Cell(Column.OpOnly), "TRUE", StringComparison.OrdinalIgnoreCase),
                Flsa = Cell(Column.Flsa),
                PersonnelProgram = Cell(Column.Program),
                JobStatus = Cell(Column.Status),
                Grade = Cell(Column.Grade),
            };
        }
    }

    /// <summary>Each column's left edge, from this page's header row; null when the page has none.</summary>
    private static List<(Column Column, double Left)>? ColumnStarts(List<Word> words)
    {
        var starts = new List<(Column, double)>();
        var family = words.FirstOrDefault(w => w.Text == "Family");
        var job = family is null
            ? null
            : words.Where(w => w.Text == "Job" && Math.Abs(w.BoundingBox.Bottom - family.BoundingBox.Bottom) < 2 && w.BoundingBox.Left < family.BoundingBox.Left)
                .MaxBy(w => w.BoundingBox.Left);
        if (job is null)
        {
            return null;
        }

        starts.Add((Column.Family, 0));
        var headers = new Dictionary<Column, Word>();
        foreach (var (column, header) in Headers)
        {
            var w = words.FirstOrDefault(x => x.Text == header);
            if (w is null)
            {
                return null;
            }

            headers[column] = w;
            starts.Add((column, w.BoundingBox.Left));
        }

        // Most headers sit at their column's left edge, but JOB_FUNCTION and Job_Title are centred over
        // theirs. Their data starts where most words between the previous header and theirs start:
        // every cell's first word begins at the column's edge.
        double DataStart(double after, double before) =>
            words.Where(w => w.BoundingBox.Left > after && w.BoundingBox.Left <= before && w.BoundingBox.Top < job.BoundingBox.Bottom - 1)
                .GroupBy(w => Math.Round(w.BoundingBox.Left))
                .OrderByDescending(g => g.Count())
                .Select(g => (double?)g.Key)
                .FirstOrDefault() ?? before;

        Replace(Column.Function, DataStart(family!.BoundingBox.Right, headers[Column.Function].BoundingBox.Left));
        Replace(Column.Title, DataStart(headers[Column.OpOnly].BoundingBox.Right, headers[Column.Title].BoundingBox.Left));
        return [.. starts.OrderBy(s => s.Item2)];

        void Replace(Column column, double left)
        {
            starts.RemoveAll(s => s.Item1 == column);
            starts.Add((column, left));
        }
    }

    /// <summary>The column a word falls in: the last whose start is at or before it (a little slack for alignment).</summary>
    private static Column Place(Word w, List<(Column Column, double Left)> starts) =>
        starts.Last(s => s.Left <= w.BoundingBox.Left + 3).Column;
}

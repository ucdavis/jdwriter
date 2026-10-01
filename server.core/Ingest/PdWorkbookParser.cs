using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace Server.Core.Ingest;

/// <summary>One responsibility from a Position Description form.</summary>
public sealed class PdFunction
{
    public string Name { get; set; } = "";
    /// <summary>
    /// Percent of time as the form states it. A DOUBLE, not an int: when a form mixes fractional
    /// and whole values the set is judged non-fractional and passes through untouched, so 0.5 and
    /// 50 can legitimately coexist. Rounding here would silently destroy the 0.5.
    /// </summary>
    public double PctTime { get; set; }
    public List<string> Duties { get; set; } = [];
}

/// <summary>
/// A parsed UC Davis Position Description / requisition workbook — the Excel form units actually
/// fill in when they write a job description.
/// </summary>
public sealed class PdWorkbook
{
    public string SheetName { get; set; } = "";
    public string WorkingTitle { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<PdFunction> Functions { get; set; } = [];

    /// <summary>"yes", "no", or "unclear".</summary>
    public string Supervises { get; set; } = "unclear";

    public List<string> Education { get; set; } = [];
    public List<string> Experience { get; set; } = [];
    public List<string> Ksas { get; set; } = [];

    /// <summary>
    /// The classification the unit is PROPOSING. Its entire value is that it can disagree with
    /// ours, so it is carried through rather than trusted — and never shown to the model.
    /// </summary>
    public string ProposedTitleCode { get; set; } = "";

    /// <summary>Filled form variants that were not used, so the UI can say which one was read.</summary>
    public List<string> OtherFilledSheets { get; set; } = [];
}

/// <summary>
/// Parses the PD workbook. Ported from the POC's src/lib/intake/parsePd.ts and verified against
/// fixtures/pd.json — the one real workbook plus 14 synthetic ones, each built for one trap.
///
/// Deterministic on purpose, like the HRTMS parser: the form already carries every field a model
/// would be asked to extract, laid out as label/value pairs. Parsing it outright is free, exact,
/// and means a classification never depends on a model correctly reading a spreadsheet.
///
/// Three things about this format bite anyone who flattens it naively:
///
///   • The workbook holds several near-identical form variants (New Recruitment, Replacement,
///     Stipend, Equity, and in the real sample a Comp variant too) of which one is filled, PLUS
///     three lookup sheets backing dropdowns. Those lookups hold ~65K characters — more text than
///     the form — so any whole-workbook dump is mostly dropdown options.
///   • "% of Time" is Excel-percent-formatted: the cell holds 0.85, not 85. Read literally, an
///     85% function becomes a 1% one.
///   • Row indices are not stable. Collapsing blank rows shifts everything below, so anything
///     positional silently reads a neighbouring field. Labels are the only reliable key.
/// </summary>
public static partial class PdWorkbookParser
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex AnyWhitespace();

    [GeneratedRegex(@"^\*+")]
    private static partial Regex LeadingAsterisks();

    /// <summary>Trailing colon, ASCII or fullwidth — the form uses both.</summary>
    [GeneratedRegex("[:：]\\s*$")]
    private static partial Regex TrailingColon();

    [GeneratedRegex(@"^percent\(?%?\)? of time")]
    private static partial Regex PctLabel();

    [GeneratedRegex(@"^function \(capitalize\)")]
    private static partial Regex FnLabel();

    [GeneratedRegex("^duties")]
    private static partial Regex DutyLabel();

    [GeneratedRegex(@"\s*[;\n•]\s*")]
    private static partial Regex DutySplit();

    [GeneratedRegex(@"^[-–]\s*")]
    private static partial Regex LeadingDash();

    [GeneratedRegex(@"\d{4,6}")]
    private static partial Regex JobCodeDigits();

    [GeneratedRegex(@"^(n/a|none|0)$", RegexOptions.IgnoreCase)]
    private static partial Regex NoSupervision();

    /// <summary>Labels that appear only on a filled PD form, never on a lookup sheet.</summary>
    private static readonly string[] Signature =
    [
        "job summary", "function (capitalize)", "position (working title)",
    ];

    private static string Norm(string? s)
    {
        var t = AnyWhitespace().Replace(s ?? "", " ");
        t = LeadingAsterisks().Replace(t, "");
        t = TrailingColon().Replace(t, "");
        return t.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Everything right of the label column, joined. Values are often merged across B:F, which
    /// puts the text in B and leaves the rest blank.
    /// </summary>
    private static string ValueOf(IReadOnlyList<string> row)
    {
        var parts = new List<string>();
        for (var i = 1; i < row.Count; i++)
        {
            var v = AnyWhitespace().Replace(row[i], " ").Trim();
            if (v.Length > 0)
            {
                parts.Add(v);
            }
        }

        return string.Join(' ', parts).Trim();
    }

    /// <summary>
    /// Duty cells are a single blob with semicolon-, newline- or bullet-separated statements.
    /// Fragments of two characters or fewer are split artifacts, not duties.
    /// </summary>
    private static List<string> SplitDuties(string v)
    {
        var result = new List<string>();
        foreach (var part in DutySplit().Split(v))
        {
            var d = LeadingDash().Replace(part.Trim(), "");
            if (d.Length > 2)
            {
                result.Add(d);
            }
        }

        return result;
    }

    /// <summary>
    /// The form's percent cells are Excel-percent-formatted, so a full-time function reads as
    /// 0.85. Rescale only when the WHOLE set looks fractional — a form filled in with whole
    /// numbers (85) must pass through untouched.
    ///
    /// Note this infers from the values, and deliberately does NOT consult the cell's number
    /// format. The reference cannot see the format (SheetJS omits it unless asked) and therefore
    /// neither does this; reading it here would be "more correct" in the abstract while diverging
    /// from the implementation this is required to match.
    /// </summary>
    private static List<PdFunction> RescalePercents(List<PdFunction> fns)
    {
        var vals = fns.Select(f => f.PctTime).Where(p => p > 0).ToList();
        if (vals.Count == 0)
        {
            return fns;
        }

        // Every positive value at or below 1 means the set is a set of fractions. Integer
        // percentages cannot satisfy this unless they are all exactly 1, which is not a real form.
        if (!vals.All(p => p > 0 && p <= 1))
        {
            return fns;
        }

        foreach (var f in fns)
        {
            // JS Math.round is half-up; .NET Math.Round is banker's rounding by default.
            f.PctTime = Math.Floor((f.PctTime * 100.0) + 0.5);
        }

        return fns;
    }

    private static PdWorkbook? ParseSheet(List<List<string>> rows)
    {
        var labels = rows.Select(r => Norm(r.Count > 0 ? r[0] : "")).ToList();

        if (!Signature.Any(sig => labels.Any(l => l.Contains(sig, StringComparison.Ordinal))))
        {
            return null;
        }

        string First(params string[] needles)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (!needles.Any(n => labels[i] == n || labels[i].StartsWith(n, StringComparison.Ordinal)))
                {
                    continue;
                }

                var v = ValueOf(rows[i]);
                if (v.Length > 0)
                {
                    return v;
                }
            }

            return "";
        }

        // Responsibilities are repeating percent / function / duties triplets, several of which
        // are left blank. Walk in order and keep only the slots that were filled.
        var functions = new List<PdFunction>();
        PdFunction? cur = null;

        void Flush()
        {
            if (cur is not null && (cur.Name.Length > 0 || cur.Duties.Count > 0))
            {
                functions.Add(cur);
            }

            cur = null;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var l = labels[i];
            var v = ValueOf(rows[i]);

            if (PctLabel().IsMatch(l))
            {
                Flush();
                cur = new PdFunction { PctTime = ParseFloatPrefix(v), Duties = [] };
            }
            else if (FnLabel().IsMatch(l))
            {
                cur ??= new PdFunction();
                cur.Name = v;
            }
            else if (DutyLabel().IsMatch(l))
            {
                cur ??= new PdFunction();
                cur.Duties = SplitDuties(v);
            }
        }

        Flush();

        var fte = First("total number of staff fte supervised");
        var supervises = double.TryParse(LeadingNumber(fte), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 0
            ? "yes"
            : NoSupervision().IsMatch(fte.Trim())
                ? "no"
                : "unclear";

        List<string> List(string v) => v.Length > 0 ? SplitDuties(v) : [];

        var education = new List<string>(List(First("minimum education/experience")));
        education.AddRange(List(First("preferred education/experience")).Select(e => $"Preferred: {e}"));

        var ksas = new List<string>(List(First("minimum knowledge, skills, and abilities (ksa)")));
        ksas.AddRange(List(First("preferred knowledge, skills, and abilities (ksa)")));

        var proposed = JobCodeDigits().Match(First("proposed job title/job code"));

        return new PdWorkbook
        {
            WorkingTitle = First("position (working title)"),
            Summary = First("job summary"),
            Functions = RescalePercents(functions),
            Supervises = supervises,
            // The form combines education and experience in one cell and has no separate
            // work-experience field, so both requirement levels go to Education and Experience
            // stays empty rather than being filled with the wrong thing.
            Education = education,
            Experience = [],
            Ksas = ksas.Take(10).ToList(),
            ProposedTitleCode = proposed.Success ? proposed.Value : "",
        };
    }

    /// <summary>
    /// JS parseFloat semantics: read a leading numeric prefix, ignore the rest, and yield 0 where
    /// there is no number — the reference relies on <c>parseFloat(v) || 0</c>.
    /// </summary>
    private static double ParseFloatPrefix(string v)
    {
        var prefix = LeadingNumber(v);
        return prefix.Length > 0
               && double.TryParse(prefix, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
               && !double.IsNaN(d)
            ? d
            : 0;
    }

    private static string LeadingNumber(string v)
    {
        var s = v.TrimStart();
        var sb = new StringBuilder();
        var i = 0;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            sb.Append(s[i++]);
        }

        var seenDot = false;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || (s[i] == '.' && !seenDot)))
        {
            seenDot |= s[i] == '.';
            sb.Append(s[i++]);
        }

        var result = sb.ToString();
        return result is "" or "+" or "-" or "." ? "" : result;
    }

    /// <summary>
    /// How much real content a parsed sheet carries — used to pick the filled variant out of the
    /// near-identical blanks, and to reject a workbook where nothing was filled in.
    /// </summary>
    private static int Weight(PdWorkbook? p) =>
        p is null
            ? 0
            : p.Summary.Length + p.Functions.Sum(f => f.Name.Length + string.Concat(f.Duties).Length);

    public static PdWorkbook? Parse(Stream xlsx)
    {
        using var wb = new XLWorkbook(xlsx);
        var parsed = new List<(string Name, PdWorkbook Sheet, int Weight)>();

        foreach (var ws in wb.Worksheets)
        {
            var p = ParseSheet(ReadRows(ws));
            if (p is not null)
            {
                parsed.Add((ws.Name, p, Weight(p)));
            }
        }

        if (parsed.Count == 0)
        {
            return null;
        }

        // OrderByDescending, not List.Sort: LINQ ordering is stable and JS Array.sort has been
        // stable since ES2019, so equal-weight sheets must keep workbook order.
        var ranked = parsed.OrderByDescending(x => x.Weight).ToList();
        var best = ranked[0];

        // Every variant is a valid form, so an empty one still parses. If even the richest carries
        // no summary and no duties, nothing on this workbook was filled in.
        if (best.Weight == 0)
        {
            return null;
        }

        best.Sheet.SheetName = best.Name;
        best.Sheet.OtherFilledSheets = ranked.Skip(1).Where(x => x.Weight > 0).Select(x => x.Name).ToList();
        return best.Sheet;
    }

    /// <summary>
    /// Read a worksheet as rows of strings, mirroring SheetJS's
    /// <c>sheet_to_json(ws, {header: 1, blankrows: true, defval: ""})</c>: every row from 1 to the
    /// last used row INCLUDING blanks, padded to the used width.
    ///
    /// Blank rows must be kept. The label-keyed reads do not depend on row index, but the
    /// repeating-triplet scan depends on walk order matching the sheet, and dropping rows is what
    /// makes positional reads land on the neighbouring field.
    /// </summary>
    private static List<List<string>> ReadRows(IXLWorksheet ws)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        var rows = new List<List<string>>(lastRow);

        for (var r = 1; r <= lastRow; r++)
        {
            var row = new List<string>(lastCol);
            for (var c = 1; c <= lastCol; c++)
            {
                row.Add(CellToString(ws.Cell(r, c)));
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Mirror of <c>String(cell ?? "")</c> over the values SheetJS produces. The real workbook
    /// yields only strings and numbers; the rest are covered so an unexpected cell type degrades
    /// the same way rather than throwing.
    /// </summary>
    private static string CellToString(IXLCell cell)
    {
        var v = cell.Value;

        if (v.IsBlank)
        {
            return "";
        }

        if (v.IsText)
        {
            return v.GetText();
        }

        if (v.IsNumber)
        {
            return FormatNumber(v.GetNumber());
        }

        if (v.IsBoolean)
        {
            return v.GetBoolean() ? "true" : "false";
        }

        if (v.IsDateTime)
        {
            // SheetJS returns the Excel serial number for dates unless cellDates is set, which
            // the reference does not set.
            return FormatNumber(v.GetDateTime().ToOADate());
        }

        if (v.IsTimeSpan)
        {
            return FormatNumber(v.GetTimeSpan().TotalDays);
        }

        return v.IsError ? v.GetError().ToString() : cell.GetFormattedString();
    }

    /// <summary>
    /// JS number-to-string for the range these forms contain. .NET's shortest round-trip default
    /// agrees with JS for ordinary magnitudes; JS switches to exponential notation outside
    /// 1e-6..1e21, which no cell in a percent-and-headcount form reaches.
    /// </summary>
    private static string FormatNumber(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// Render a parsed workbook as the readable text that gets classified.
    ///
    /// The unit's PROPOSED job code is deliberately left out. It is the most interesting field on
    /// the form, but showing the model the answer the unit wants turns an independent
    /// classification into an agreement machine. It travels beside the result instead, for a human
    /// to compare against.
    /// </summary>
    public static string Render(PdWorkbook pd)
    {
        string Line(string label, List<string> xs) =>
            xs.Count > 0 ? $"\n\n{label}:\n{string.Join('\n', xs.Select(x => $"  - {x}"))}" : "";

        var fns = string.Join("\n\n", pd.Functions.Select(f =>
            $"{(f.PctTime != 0 ? $"{FormatNumber(f.PctTime)}% " : "")}{f.Name}\n" +
            string.Join('\n', f.Duties.Select(d => $"  - {d}"))));

        return $"""
               Working Title: {(pd.WorkingTitle.Length > 0 ? pd.WorkingTitle : "(none given)")}
               Supervises other employees: {pd.Supervises}

               Job Summary:
               {pd.Summary}

               Essential Responsibilities:
               {(fns.Length > 0 ? fns : "(none stated)")}
               """ + Line("Education", pd.Education) + Line("Knowledge, Skills and Abilities", pd.Ksas);
    }
}

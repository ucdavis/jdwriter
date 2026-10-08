using System.Text.RegularExpressions;
using ClosedXML.Excel;

using Server.Core.Ingest;

namespace Server.Core.Standards;

/// <summary>A parsed official UC job standard for one classification.</summary>
public sealed class ClassStandardRecord
{
    public string LongTitle { get; set; } = "";

    /// <summary>
    /// UC job code. Only layout B states it; where present it is the exact join key to a profile,
    /// since title spellings vary ("Analyst" vs "Anl") and codes do not.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>Classified Indicator (MSP / PSS / Academic).</summary>
    public string PersProg { get; set; } = "";

    /// <summary>Layout B omits salary grade entirely; it is backfilled from the title reference.</summary>
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

/// <summary>Which field a row's label feeds.</summary>
internal enum StandardField
{
    LongTitle,
    PersProg,
    Grade,
    Flsa,
    Union,
    GenericScope,
    CustomScope,
    KeyResponsibilities,
    Ksa,
    Education,
    Licenses,
    SpecialConditions,
}

/// <summary>
/// Parses UC "Job Standard" workbooks. Ported from the POC's src/lib/standards/parse.ts.
///
/// TWO layouts exist in the wild and they share almost nothing:
///
///   A. "Side by Side" — a Job Builder export. Labels run down column A ("Long Title:",
///      "Salary Grade:"), one column per class, and responsibilities are packed as bullets inside
///      a single cell.
///   B. Single-family export ("Table 1") — a UC website PDF converted to Excel. Labels run down
///      column B ("Job Title", "Job Code", "Key Resp 01".."15"), one responsibility PER ROW, the
///      page header repeats every dozen rows or so, and long cells wrap onto UNLABELED
///      continuation rows that can land after a page break.
///
/// Layout B has no "Long Title" row at all, which is why the layout-A scan silently matched
/// nothing and whole workbooks contributed zero standards. Each sheet is tried as A, then as B.
///
/// COVERAGE WARNING. Layout A is verified against 18 real sheets. Layout B is a BLIND PORT: no
/// real layout-B workbook is available, so it is verified only against synthetic inputs built from
/// the format as documented. That pins this implementation to the reference's behavior but proves
/// nothing about either one against a real UC website export. Revisit when such a file exists.
/// </summary>
public static partial class StandardsWorkbookParser
{
    [GeneratedRegex(@"[•\n]+")]
    private static partial Regex BulletSplit();

    [GeneratedRegex(@"^[•\t\s]+")]
    private static partial Regex LeadingBullet();

    [GeneratedRegex(@"\s+")]
    private static partial Regex AnyWhitespace();

    [GeneratedRegex("^key resp")]
    private static partial Regex KeyRespLabel();

    [GeneratedRegex("^ksa")]
    private static partial Regex KsaLabel();

    [GeneratedRegex("^education")]
    private static partial Regex EducationLabel();

    [GeneratedRegex("^(license|cert)")]
    private static partial Regex LicenseLabel();

    [GeneratedRegex("^spec(ial)? cond")]
    private static partial Regex SpecialConditionLabel();

    /// <summary>Trim only — deliberately no whitespace collapse, matching the reference.</summary>
    private static string Norm(string? v) => (v ?? "").Trim();

    private static string Low(string? v) => Norm(v).ToLowerInvariant();

    /// <summary>Split a cell's bulleted content into clean bullets.</summary>
    private static List<string> Bullets(string? cell)
    {
        var result = new List<string>();
        foreach (var part in BulletSplit().Split(Norm(cell)))
        {
            var b = AnyWhitespace().Replace(LeadingBullet().Replace(part, ""), " ").Trim();
            if (b.Length > 2)
            {
                result.Add(b);
            }
        }

        return result;
    }

    /// <summary>
    /// Layout A label matching. Order matters: the first rule that fires wins, and several are
    /// substring tests rather than prefix tests because the workbooks phrase headings loosely.
    /// </summary>
    private static StandardField? MatchLabel(string label)
    {
        var l = label.ToLowerInvariant();

        if (l.StartsWith("long title", StringComparison.Ordinal)) return StandardField.LongTitle;
        if (l.Contains("classified indicator", StringComparison.Ordinal)
            || l.Contains("pers prog", StringComparison.Ordinal)) return StandardField.PersProg;
        if (l.Contains("salary grade", StringComparison.Ordinal)) return StandardField.Grade;
        if (l.Contains("flsa", StringComparison.Ordinal)) return StandardField.Flsa;
        if (l.Contains("union code", StringComparison.Ordinal)) return StandardField.Union;
        if (l.Contains("generic scope", StringComparison.Ordinal)) return StandardField.GenericScope;
        if (l.Contains("custom scope", StringComparison.Ordinal)) return StandardField.CustomScope;
        if (l.Contains("key responsibilities", StringComparison.Ordinal)) return StandardField.KeyResponsibilities;
        if (l.Contains("knowledge, skills", StringComparison.Ordinal)) return StandardField.Ksa;
        if (l.StartsWith("education", StringComparison.Ordinal)) return StandardField.Education;
        if (l.Contains("licenses and certifications", StringComparison.Ordinal)) return StandardField.Licenses;
        if (l.Contains("special conditions", StringComparison.Ordinal)) return StandardField.SpecialConditions;

        return null;
    }

    private static readonly HashSet<StandardField> BulletFields =
    [
        StandardField.KeyResponsibilities,
        StandardField.Ksa,
        StandardField.Education,
        StandardField.Licenses,
        StandardField.SpecialConditions,
    ];

    private static void Assign(ClassStandardRecord std, StandardField field, string? cell)
    {
        if (BulletFields.Contains(field))
        {
            var bullets = Bullets(cell);
            switch (field)
            {
                case StandardField.KeyResponsibilities: std.KeyResponsibilities = bullets; break;
                case StandardField.Ksa: std.Ksa = bullets; break;
                case StandardField.Education: std.Education = bullets; break;
                case StandardField.Licenses: std.Licenses = bullets; break;
                case StandardField.SpecialConditions: std.SpecialConditions = bullets; break;
            }

            return;
        }

        var v = Norm(cell);
        switch (field)
        {
            case StandardField.LongTitle: std.LongTitle = v; break;
            case StandardField.PersProg: std.PersProg = v; break;
            case StandardField.Grade: std.Grade = v; break;
            case StandardField.Flsa: std.Flsa = v; break;
            case StandardField.Union: std.Union = v; break;
            case StandardField.GenericScope: std.GenericScope = v; break;
            case StandardField.CustomScope: std.CustomScope = v; break;
        }
    }

    public static List<ClassStandardRecord> Parse(Stream xlsx)
    {
        using var wb = new XLWorkbook(xlsx);
        var result = new List<ClassStandardRecord>();

        foreach (var ws in wb.Worksheets)
        {
            // Blank rows must be KEPT. They do not break the label-keyed walk, but layout B's
            // continuation pointer depends on walking rows in sheet order, and a fully blank row
            // sits inside the repeating page header.
            var rows = ReadRows(ws);

            // A is tried first, then B. Order is not currently observable — no sheet satisfies
            // both, because layout A has no column-B "Job Title" row and layout B has no
            // "Long Title" row — so this mirrors the reference rather than expressing a preference.
            var sideBySide = ParseSideBySide(rows);
            result.AddRange(sideBySide.Count > 0 ? sideBySide : ParseFamilyTable(rows));
        }

        return result;
    }

    // ---------------------------------------------------------------- layout A

    private static List<ClassStandardRecord> ParseSideBySide(List<List<string>> rows)
    {
        var result = new List<ClassStandardRecord>();
        var i = 0;

        while (i < rows.Count)
        {
            if (!Low(Cell(rows, i, 0)).StartsWith("long title", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            // This row's columns 1..N are the classes in this block.
            var titleRow = rows[i];
            var classCols = new List<int>();
            for (var c = 1; c < titleRow.Count; c++)
            {
                if (Norm(titleRow[c]).Length > 0)
                {
                    classCols.Add(c);
                }
            }

            // Collect labeled rows up to the next block. Starts at i, so the Long Title row itself
            // is included — it is what supplies the title.
            var labeled = new List<(StandardField Field, List<string> Row)>();
            var j = i;
            while (j < rows.Count
                   && !(j > i && Low(Cell(rows, j, 0)).StartsWith("long title", StringComparison.Ordinal)))
            {
                var f = MatchLabel(Norm(Cell(rows, j, 0)));
                if (f is not null)
                {
                    labeled.Add((f.Value, rows[j]));
                }

                j++;
            }

            foreach (var c in classCols)
            {
                var std = new ClassStandardRecord();
                foreach (var (field, row) in labeled)
                {
                    Assign(std, field, c < row.Count ? row[c] : "");
                }

                if (std.LongTitle.Length > 0)
                {
                    result.Add(std);
                }
            }

            i = j;
        }

        return result;
    }

    // ---------------------------------------------------------------- layout B

    /// <summary>
    /// Which metadata slot a repeating-header label fills.
    ///
    /// These labels carry per-class metadata and crucially must NOT reset the continuation
    /// pointer: a wrapped cell can resume immediately after a page break, so its text arrives
    /// several rows below the label it belongs to with a whole header block in between.
    ///
    /// Slots are named rather than typed as <see cref="StandardField"/> because layout B's
    /// "Job Code" has no layout-A equivalent.
    /// </summary>
    private static string? MetaTarget(string label) => label switch
    {
        "job title" => "longTitle",
        "job code" => "code",
        "per. program" or "per program" => "persProg",
        "flsa" => "flsa",
        _ => null,
    };

    /// <summary>
    /// Column-B label to list. Layout B numbers each item ("Key Resp 07"), so one row is one item
    /// and the numbering is ignored.
    /// </summary>
    private static string? FamilyField(string label)
    {
        if (KeyRespLabel().IsMatch(label)) return "keyResponsibilities";
        if (KsaLabel().IsMatch(label)) return "ksa";
        if (EducationLabel().IsMatch(label)) return "education";
        if (LicenseLabel().IsMatch(label)) return "licenses";
        if (SpecialConditionLabel().IsMatch(label)) return "specialConditions";
        if (label == "generic scope") return "genericScope";
        if (label == "custom scope") return "customScope";
        return null;
    }

    private static readonly HashSet<string> Scalars = ["genericScope", "customScope"];

    private static List<ClassStandardRecord> ParseFamilyTable(List<List<string>> rows)
    {
        // Class columns come from the FIRST "Job Title" row; every later repeat of the header lists
        // the same classes in the same columns.
        List<int>? cols = null;
        foreach (var r in rows)
        {
            if (Low(r.Count > 1 ? r[1] : "") == "job title")
            {
                cols = [];
                for (var c = 2; c < r.Count; c++)
                {
                    if (Norm(r[c]).Length > 0)
                    {
                        cols.Add(c);
                    }
                }

                break;
            }
        }

        if (cols is null || cols.Count == 0)
        {
            return [];
        }

        // Per class column, per field, a list of items. Items are written for EVERY class at the
        // same index — even when blank — so an index means the same item in every column, which is
        // what lets a continuation row find its own item again.
        var acc = new Dictionary<int, Dictionary<string, List<string>>>();
        var meta = new Dictionary<int, Dictionary<string, string>>();
        foreach (var c in cols)
        {
            acc[c] = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            meta[c] = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        List<string> ListFor(int c, string f)
        {
            if (!acc[c].TryGetValue(f, out var list))
            {
                list = [];
                acc[c][f] = list;
            }

            return list;
        }

        (string Field, int Idx)? pending = null;

        foreach (var r in rows)
        {
            var label = Low(r.Count > 1 ? r[1] : "");
            var colA = Norm(r.Count > 0 ? r[0] : "");

            var metaTarget = label.Length > 0 ? MetaTarget(label) : null;
            if (metaTarget is not null)
            {
                foreach (var c in cols)
                {
                    var v = Norm(c < r.Count ? r[c] : "");
                    // First value wins: the header repeats on every page with the same content.
                    if (v.Length > 0 && !meta[c].ContainsKey(metaTarget))
                    {
                        meta[c][metaTarget] = v;
                    }
                }

                continue; // header metadata — leave `pending` alone
            }

            var field = label.Length > 0 ? FamilyField(label) : null;
            if (field is not null)
            {
                var idx = ListFor(cols[0], field).Count;
                foreach (var c in cols)
                {
                    SetAt(ListFor(c, field), idx, AnyWhitespace().Replace(Norm(c < r.Count ? r[c] : ""), " ").Trim());
                }

                pending = (field, idx);
                continue;
            }

            // An unlabeled row with content in the class columns is wrapped text from whichever row
            // last set `pending`. Anything else — a page header, a blank row, a label we do not map
            // — is skipped WITHOUT clearing it.
            if (label.Length == 0 && colA.Length == 0 && pending is not null)
            {
                foreach (var c in cols)
                {
                    var v = AnyWhitespace().Replace(Norm(c < r.Count ? r[c] : ""), " ").Trim();
                    if (v.Length == 0)
                    {
                        continue;
                    }

                    var list = ListFor(c, pending.Value.Field);
                    SetAt(list, pending.Value.Idx, $"{At(list, pending.Value.Idx)} {v}".Trim());
                }
            }
        }

        var result = new List<ClassStandardRecord>();
        foreach (var c in cols)
        {
            var m = meta[c];

            // Unreachable in practice, in both implementations, and kept only to mirror the
            // reference: `cols` is built from the columns that carry a non-empty title in the FIRST
            // "Job Title" row, and that same row is what populates this slot — so it is always set
            // for every column in `cols`. Confirmed by mutation: removing the guard changes nothing.
            if (!m.TryGetValue("longTitle", out var longTitle) || longTitle.Length == 0)
            {
                continue;
            }

            List<string> Get(string f) =>
                (acc[c].TryGetValue(f, out var list) ? list : [])
                .Select(x => x.Trim())
                .Where(x => x.Length > 2)
                .ToList();

            string Scalar(string f) => Scalars.Contains(f) ? string.Join(' ', Get(f)) : "";

            result.Add(new ClassStandardRecord
            {
                LongTitle = longTitle,
                Code = m.GetValueOrDefault("code"),
                PersProg = m.GetValueOrDefault("persProg", ""),
                // Layout B omits salary grade; backfilled from the title reference at ingest.
                Grade = "",
                Flsa = m.GetValueOrDefault("flsa", ""),
                Union = "",
                GenericScope = Scalar("genericScope"),
                CustomScope = Scalar("customScope"),
                KeyResponsibilities = Get("keyResponsibilities"),
                Ksa = Get("ksa"),
                Education = Get("education"),
                Licenses = Get("licenses"),
                SpecialConditions = Get("specialConditions"),
            });
        }

        return result;
    }

    /// <summary>
    /// Write at an index, padding with empty strings. The reference assigns straight into a JS
    /// array at an arbitrary index; empties are filtered out later by the length-3 floor, so
    /// padding is equivalent to JavaScript's sparse holes here.
    /// </summary>
    private static void SetAt(List<string> list, int idx, string value)
    {
        while (list.Count <= idx)
        {
            list.Add("");
        }

        list[idx] = value;
    }

    private static string At(List<string> list, int idx) => idx < list.Count ? list[idx] : "";

    private static string Cell(List<List<string>> rows, int row, int col) =>
        row < rows.Count && col < rows[row].Count ? rows[row][col] : "";

    /// <summary>
    /// Read a worksheet as rows of strings, mirroring
    /// <c>sheet_to_json(ws, {header: 1, blankrows: true, defval: ""})</c>.
    /// </summary>
    private static List<List<string>> ReadRows(IXLWorksheet ws)
    {
        var (lastRow, lastCol) = WorksheetLimits.UsedRange(ws);
        var rows = new List<List<string>>(lastRow);

        for (var r = 1; r <= lastRow; r++)
        {
            var row = new List<string>(lastCol);
            for (var c = 1; c <= lastCol; c++)
            {
                row.Add(CellText(ws.Cell(r, c)));
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Mirror of <c>String(cell ?? "")</c>. Standards workbooks are text throughout, but job codes
    /// can arrive as numbers, and a code read as "111111" rather than "111111.0" is the difference
    /// between joining to a profile and not.
    /// </summary>
    private static string CellText(IXLCell cell)
    {
        var v = cell.Value;
        if (v.IsBlank) return "";
        if (v.IsText) return v.GetText();
        if (v.IsNumber) return v.GetNumber().ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (v.IsBoolean) return v.GetBoolean() ? "true" : "false";
        if (v.IsDateTime) return v.GetDateTime().ToOADate().ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return v.IsError ? v.GetError().ToString() : cell.GetFormattedString();
    }
}

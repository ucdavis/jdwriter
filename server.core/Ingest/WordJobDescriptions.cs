using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Server.Core.Ingest;

/// <summary>
/// Job descriptions from units outside the college, as they arrive: copied out of JDX into Word.
/// Two layouts, both parsed straight from the document — no model, wording kept exactly — so an
/// imported JD is the same kind of record as one exported from HRTMS within the unit:
///
/// 1. An HRTMS job description export, saved as Word: the same labelled rows ("UC Job Code:",
///    "Working Title:"), the "% TIME / Function / Duties" table, qualifications and conditions. Its
///    tables are rendered back to the HTML the HRTMS parser reads, and that parser — the one every
///    in-unit export goes through — does the rest. A cell of several paragraphs is a bullet list,
///    as it is in the HTML export.
/// 2. A job posting: "UC Job Title", "Job Summary", "Qualifications" / "Preferred Qualifications",
///    "Key Responsibilities" as "40% - FUNCTION" lines (functions and time, no duties), and a
///    "POSITION INFORMATION" list stating the job code, grade and union.
///
/// Anything else is not recognized here, and returns null.
/// </summary>
public static partial class WordJobDescriptions
{
    [GeneratedRegex(@"^(\d{1,3})\s*%\s*[-–—]\s*(.+)$")]
    private static partial Regex PercentFunction();

    /// <summary>"…payroll.Experience working…": two items pasted into one bullet with no space between.</summary>
    [GeneratedRegex(@"(?<=[a-z0-9)\]]\.)(?=[A-Z])")]
    private static partial Regex RunTogether();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Why a parsed JD is too incomplete for the corpus, or null when it will do: it needs
    /// responsibilities, with duties under them. (% time is not a test: real exports from within
    /// the unit don't always total 100, and they are kept as they are.)
    /// </summary>
    public static string? Incomplete(HrtmsRecord r) =>
        r.Responsibilities.Count == 0
            ? "It has no key responsibilities."
            : r.Responsibilities.All(x => x.Duties.Count == 0)
                ? "It lists no duties under its responsibilities (a job posting names functions only)."
                : null;

    public static HrtmsRecord? TryParse(byte[] docx, string sourceFile)
    {
        List<Block> blocks;
        try
        {
            using var stream = new MemoryStream(docx);
            using var doc = WordprocessingDocument.Open(stream, isEditable: false, new OpenSettings { MaxCharactersInPart = 20_000_000 });
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return null;
            }

            blocks = Blocks(body).ToList();
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or System.Xml.XmlException)
        {
            return null;
        }

        var cells = blocks.OfType<TableBlock>().SelectMany(t => t.Rows).SelectMany(r => r).ToList();
        if (cells.Contains("UC Job Code:") && cells.Contains("UCPath Position Number"))
        {
            var record = HrtmsParser.Parse(ToHtml(blocks), sourceFile);
            return string.IsNullOrWhiteSpace(record.UcJobCode) ? null : record;
        }

        var paragraphs = blocks.OfType<ParagraphBlock>().Where(p => p.Text.Length > 0).ToList();
        return paragraphs.Any(p => p.Text == "POSITION INFORMATION") && paragraphs.Any(p => p.Text == "Key Responsibilities")
            ? ParsePosting(paragraphs, sourceFile)
            : null;
    }

    // ---------------------------------------------------------------- document → blocks

    private abstract record Block;

    private sealed record ParagraphBlock(string Text, bool IsListItem) : Block;

    /// <summary>Rows of cell texts. A cell of several paragraphs is joined as "• a • b".</summary>
    private sealed record TableBlock(List<List<string>> Rows) : Block;

    private static IEnumerable<Block> Blocks(OpenXmlElement container)
    {
        foreach (var el in container.ChildElements)
        {
            switch (el)
            {
                case Paragraph p:
                    yield return new ParagraphBlock(Text(p), p.ParagraphProperties?.NumberingProperties is not null);
                    break;
                case Table t:
                    foreach (var b in TableBlocks(t))
                    {
                        yield return b;
                    }

                    break;
                case SdtBlock sdt when sdt.SdtContentBlock is not null:
                    foreach (var b in Blocks(sdt.SdtContentBlock))
                    {
                        yield return b;
                    }

                    break;
            }
        }
    }

    /// <summary>A table's rows; a table nested in a cell follows its row, so cell order is kept.</summary>
    private static IEnumerable<Block> TableBlocks(Table table)
    {
        var rows = new List<List<string>>();
        var nested = new List<Table>();
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<TableCell>())
            {
                var texts = cell.Descendants<Paragraph>()
                    .Where(p => p.Ancestors<Table>().First() == table)
                    .Select(Text)
                    .Where(t => t.Length > 0)
                    .ToList();
                cells.Add(texts.Count > 1 ? string.Join(" ", texts.Select(t => $"• {t}")) : texts.FirstOrDefault() ?? "");
                nested.AddRange(cell.Elements<Table>());
            }

            rows.Add(cells);
        }

        yield return new TableBlock(rows);
        foreach (var inner in nested)
        {
            foreach (var b in TableBlocks(inner))
            {
                yield return b;
            }
        }
    }

    private static string Text(Paragraph p)
    {
        var sb = new StringBuilder();
        foreach (var el in p.Descendants())
        {
            switch (el)
            {
                case Text t:
                    sb.Append(t.Text);
                    break;
                case TabChar or Break or CarriageReturn:
                    sb.Append(' ');
                    break;
            }
        }

        return Whitespace().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>The blocks as the HTML the HRTMS parser reads: label and value in adjacent cells.</summary>
    private static string ToHtml(List<Block> blocks)
    {
        var html = new StringBuilder("<html><body>");
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock p when p.Text.Length > 0:
                    html.Append("<p>").Append(WebUtility.HtmlEncode(p.Text)).Append("</p>");
                    break;
                case TableBlock t:
                    html.Append("<table>");
                    foreach (var row in t.Rows)
                    {
                        html.Append("<tr>");
                        foreach (var cell in row)
                        {
                            html.Append("<td>").Append(WebUtility.HtmlEncode(cell)).Append("</td>");
                        }

                        html.Append("</tr>");
                    }

                    html.Append("</table>");
                    break;
            }
        }

        return html.Append("</body></html>").ToString();
    }

    // ---------------------------------------------------------------- the job-posting layout

    private static readonly HashSet<string> PostingHeadings =
    [
        "UC Job Title", "City", "Job Summary", "Apply By Date", "Qualifications", "Preferred Qualifications",
        "Key Responsibilities", "Department Overview", "POSITION INFORMATION", "Benefits", "Physical Demands",
        "Environmental Demands", "Mental Demands", "Work Environment", "A Culture of Opportunity and Belonging",
        "Department Specific Job Scope",
    ];

    private static HrtmsRecord ParsePosting(List<ParagraphBlock> paragraphs, string sourceFile)
    {
        // The paragraphs under a heading, up to the next heading.
        List<ParagraphBlock> Under(string heading)
        {
            var start = paragraphs.FindIndex(p => p.Text == heading);
            if (start < 0)
            {
                return [];
            }

            return paragraphs.Skip(start + 1).TakeWhile(p => !PostingHeadings.Contains(p.Text)).ToList();
        }

        // "Label: value" items in POSITION INFORMATION.
        string Info(string label) =>
            Under("POSITION INFORMATION")
                .Select(p => p.Text)
                .Where(t => t.StartsWith(label + ":", StringComparison.Ordinal))
                .Select(t => t[(label.Length + 1)..].Trim())
                .FirstOrDefault() ?? "";

        static List<string> Items(IEnumerable<ParagraphBlock> ps) =>
        [
            .. ps.Where(p => p.IsListItem)
                .SelectMany(p => RunTogether().Split(p.Text))
                .Select(t => t.Trim())
                .Where(t => t.Length > 0),
        ];

        var title = Info("UC Job Title");
        return new HrtmsRecord
        {
            SourceFile = sourceFile,
            UcJobTitle = title.Length > 0 ? title : Under("UC Job Title").FirstOrDefault()?.Text ?? "",
            UcJobCode = Info("UC Job Code"),
            SalaryGrade = Info("Salary Grade"),
            UnionCode = Info("Union Representation"),
            JobSummary = string.Join("\n\n", Under("Job Summary").Select(p => p.Text)),
            Responsibilities =
            [
                .. Under("Key Responsibilities")
                    .Select(p => PercentFunction().Match(p.Text))
                    .Where(m => m.Success)
                    .Select(m => new HrtmsResponsibility { Pct = int.Parse(m.Groups[1].Value), FunctionName = m.Groups[2].Value.Trim() }),
            ],
            Qualifications = new HrtmsQualifications
            {
                KsaMin = Items(Under("Qualifications")),
                KsaPref = Items(Under("Preferred Qualifications")),
            },
        };
    }
}

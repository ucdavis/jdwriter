using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Server.Core.Ingest;

public enum DocKind
{
    Docx,
    Pdf,
    Xlsx,
    Html,
    Text,
}

/// <summary>
/// A failure the user can act on. Every message names the remedy, because the remedies differ —
/// convert a legacy format, OCR a scanned PDF, or paste the text — and a generic "could not read
/// file" leaves the user stuck.
/// </summary>
public sealed class ExtractionException : Exception
{
    public ExtractionException(string message) : base(message)
    {
    }
}

/// <summary>Plain text pulled out of an uploaded document, plus what the user should know about it.</summary>
public sealed class Extraction
{
    public string Text { get; set; } = "";
    public DocKind Kind { get; set; }
    public string Filename { get; set; } = "";

    /// <summary>
    /// Anything worth telling the user about how the text was recovered — page count, truncation,
    /// structure that was lost. Surfaced in the UI, never silent.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// A classification the document itself asserts (the PD form's "Proposed Job Title/Job Code").
    /// Held apart from <see cref="Text"/> on purpose: it is shown beside our verdict for comparison
    /// and must never reach the model, which would otherwise be told the answer it is working out.
    ///
    /// Unlike the reference, this carries only the raw code. Resolving its title and whether the
    /// class is ingested needs the database, and keeping that out of here is what lets the whole
    /// dispatch layer be tested without one.
    /// </summary>
    public string? ProposedCode { get; set; }
}

/// <summary>
/// Pulls plain text out of an uploaded document so it can be classified. Ported from the POC's
/// src/lib/intake/extract.ts.
///
/// Deliberately NOT a model call. Extraction is a solved deterministic problem, and paying for
/// tokens to read a file we can read for free would also mean the user cannot see what was
/// extracted before it gets classified.
///
/// PARITY IS PARTIAL, AND THE SPLIT IS DELIBERATE:
///
///   PORTABLE, verified byte-for-byte against fixtures/extract.json — <see cref="KindFor"/>,
///   <see cref="Tidy"/>, and the three size thresholds. The dispatch table is where a port is most
///   likely to drift, because C# has no <c>File.type</c> and gets its MIME from elsewhere.
///
///   NOT PORTABLE, covered by behavioral tests instead — docx and pdf text extraction.
///   mammoth/unpdf and OpenXml/PdfPig are different implementations and will disagree on
///   whitespace, paragraph joins and reading order. Demanding byte parity there would be a test
///   satisfiable only by reimplementing mammoth in C#.
/// </summary>
public static partial class TextExtractor
{
    private const int MaxBytes = 20 * 1024 * 1024;

    /// <summary>
    /// A job description is a few thousand words. Past this we are almost certainly looking at a
    /// bundled handbook or a whole requisition packet, and sending it to the model would be an
    /// expensive way to classify one position.
    /// </summary>
    private const int MaxChars = 400_000;

    /// <summary>
    /// An extraction that yields almost nothing is the signature of a scanned or image-only PDF.
    /// That needs a different fix from the user, so it must not read as "we classified your empty
    /// document".
    /// </summary>
    private const int MinUsefulChars = 120;

    [GeneratedRegex(@"\.([a-z0-9]+)$")]
    private static partial Regex Extension();

    [GeneratedRegex("\r\n?")]
    private static partial Regex Newlines();

    [GeneratedRegex("[ \t]+\n")]
    private static partial Regex TrailingSpaceBeforeNewline();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRun();

    [GeneratedRegex(@"[.\s]+$")]
    private static partial Regex TrailingPunctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex AnyWhitespace();

    [GeneratedRegex("[^0-9]")]
    private static partial Regex NonDigit();

    private static readonly string[] TextExtensions = ["txt", "md", "rtf"];

    /// <summary>
    /// Decide what a file is from its name and MIME type. Extension is checked first, so a correct
    /// extension beats a contradicting MIME.
    /// </summary>
    public static DocKind KindFor(string filename, string mime)
    {
        var m = Extension().Match((filename ?? "").ToLowerInvariant());
        var ext = m.Success ? m.Groups[1].Value : "";
        mime ??= "";

        if (ext == "docx" || mime.Contains("wordprocessingml", StringComparison.Ordinal)) return DocKind.Docx;
        if (ext == "pdf" || mime == "application/pdf") return DocKind.Pdf;
        if (ext is "xlsx" or "xlsm" || mime.Contains("spreadsheetml", StringComparison.Ordinal)) return DocKind.Xlsx;
        if (ext is "html" or "htm" || mime.Contains("html", StringComparison.Ordinal)) return DocKind.Html;
        if (TextExtensions.Contains(ext) || mime.StartsWith("text/", StringComparison.Ordinal)) return DocKind.Text;

        if (ext == "doc" || mime == "application/msword")
        {
            throw new ExtractionException(
                "Legacy .doc files can't be read directly. Open it in Word and save as .docx, or paste the text below.");
        }

        if (ext == "xls" || mime == "application/vnd.ms-excel")
        {
            throw new ExtractionException(
                "Legacy .xls files can't be read directly. Open it in Excel and save as .xlsx, or paste the text below.");
        }

        if (ext == "pages")
        {
            throw new ExtractionException(
                "Pages documents can't be read directly. Export as Word or PDF, or paste the text below.");
        }

        var what = ext.Length > 0 ? ext : mime.Length > 0 ? mime : "unknown";
        throw new ExtractionException(
            $"Unsupported file type “.{what}”. Upload a Word (.docx), PDF, HTML, or text file — or paste the text below.");
    }

    /// <summary>
    /// Word paragraphs arrive as bare lines; collapse runs of blank ones so the structure stays
    /// readable without spending tokens on whitespace.
    /// </summary>
    public static string Tidy(string s)
    {
        var t = Newlines().Replace(s ?? "", "\n");
        t = TrailingSpaceBeforeNewline().Replace(t, "\n");
        t = BlankRun().Replace(t, "\n\n");
        return t.Trim();
    }

    public static Extraction Extract(byte[] bytes, string filename, string mime)
    {
        filename = string.IsNullOrEmpty(filename) ? "uploaded document" : filename;

        if (bytes.Length == 0)
        {
            throw new ExtractionException($"“{filename}” is empty.");
        }

        if (bytes.Length > MaxBytes)
        {
            var mb = (bytes.Length / 1024.0 / 1024.0).ToString("F1", CultureInfo.InvariantCulture);
            throw new ExtractionException(
                $"“{filename}” is {mb} MB — the limit is {MaxBytes / 1024 / 1024} MB.");
        }

        // Before reading anything: an unsupported type should fail on its name, not on a parse.
        var kind = KindFor(filename, mime);

        string text;
        string? note = null;
        string? proposedCode = null;

        try
        {
            switch (kind)
            {
                case DocKind.Docx:
                    (text, note) = ExtractDocx(bytes);
                    break;

                case DocKind.Pdf:
                    (text, note) = ExtractPdf(bytes);
                    break;

                case DocKind.Xlsx:
                    (text, note, proposedCode) = ExtractXlsx(bytes);
                    break;

                case DocKind.Html:
                    // HTML passes through UNTOUCHED. An HRTMS export is parsed structurally
                    // downstream, and stripping tags here would destroy exactly that path — it is
                    // what lets a dropped .HTML file reach the free deterministic parser.
                    text = Encoding.UTF8.GetString(bytes);
                    break;

                default:
                    text = Tidy(Encoding.UTF8.GetString(bytes));
                    break;
            }
        }
        catch (Exception err)
        {
            // Library messages arrive with and without trailing punctuation; normalize so the
            // sentence built around them reads correctly either way.
            var why = TrailingPunctuation().Replace(err.Message.Trim(), "");
            throw new ExtractionException(
                $"Couldn't read “{filename}”: {why}. It may be password-protected or corrupt — try pasting the text below.");
        }

        if (text.Trim().Length < MinUsefulChars)
        {
            throw new ExtractionException(kind == DocKind.Pdf
                ? $"No selectable text found in “{filename}”. Scanned or image-only PDFs need OCR first — or paste the text below."
                : $"“{filename}” contained almost no readable text.");
        }

        if (text.Length > MaxChars)
        {
            text = text[..MaxChars];
            var k = (MaxChars / 1000.0).ToString("F0", CultureInfo.InvariantCulture);
            note = $"{(note is not null ? note + " " : "")}Truncated to the first {k}K characters — check that the responsibilities survived.";
        }

        return new Extraction
        {
            Text = text,
            Kind = kind,
            Filename = filename,
            Note = note,
            ProposedCode = proposedCode,
        };
    }

    /// <summary>
    /// Raw text rather than HTML: markup would cost tokens on the distill call without telling the
    /// model anything the line breaks do not.
    /// </summary>
    private static (string Text, string? Note) ExtractDocx(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return ("", null);
        }

        var paragraphs = body
            .Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
            .Select(p => p.InnerText)
            .ToList();

        var text = Tidy(string.Join("\n\n", paragraphs));

        // mammoth reports elements it could not represent. OpenXml has no equivalent, so this
        // counts the things that would have produced such a message. Deliberately an analogue,
        // not a port: a job description's content is text, so this is worth showing and is not an
        // error.
        var unconvertible = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>().Count()
                            + body.Descendants<DocumentFormat.OpenXml.Vml.Shape>().Count()
                            + body.Descendants<DocumentFormat.OpenXml.Wordprocessing.EmbeddedObject>().Count();

        var note = unconvertible > 0
            ? $"{unconvertible} element{(unconvertible == 1 ? "" : "s")} in the document couldn't be converted to text (usually images or embedded objects)."
            : null;

        return (text, note);
    }

    private static (string Text, string? Note) ExtractPdf(byte[] bytes)
    {
        using var pdf = PdfDocument.Open(bytes);

        var pages = new List<string>();
        foreach (var page in pdf.GetPages())
        {
            // Content-order extraction rather than raw glyph order: a two-column layout read
            // glyph-by-glyph interleaves the columns into nonsense.
            pages.Add(ContentOrderTextExtractor.GetText(page) ?? "");
        }

        var count = pdf.NumberOfPages;
        return (Tidy(string.Join("\n\n", pages)), $"{count} page{(count == 1 ? "" : "s")} read.");
    }

    private static (string Text, string? Note, string? ProposedCode) ExtractXlsx(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var pd = PdWorkbookParser.Parse(stream);

        if (pd is null)
        {
            stream.Position = 0;
            return (FlattenWorkbook(stream),
                "Not a recognized Position Description form — read the sheet with the most content.",
                null);
        }

        var other = pd.OtherFilledSheets.Count > 0
            ? $" Ignored {pd.OtherFilledSheets.Count} other filled sheet(s): {string.Join(", ", pd.OtherFilledSheets)}."
            : "";

        return (
            PdWorkbookParser.Render(pd),
            $"Position Description workbook — read the “{pd.SheetName}” sheet.{other}",
            pd.ProposedTitleCode.Length > 0 ? pd.ProposedTitleCode : null);
    }

    /// <summary>
    /// Fallback for a spreadsheet that is not the PD form: render the single richest sheet as rows
    /// of cells.
    ///
    /// Only one sheet, because workbooks in this domain carry dropdown-backing lookup tables
    /// (department lists, title lists) holding far more text than the form itself — dumping every
    /// sheet buries the job description in reference data.
    /// </summary>
    private static string FlattenWorkbook(Stream xlsx)
    {
        using var wb = new XLWorkbook(xlsx);
        var best = "";

        foreach (var ws in wb.Worksheets)
        {
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

            var lines = new List<string>();
            for (var r = 1; r <= lastRow; r++)
            {
                var cells = new List<string>();
                for (var c = 1; c <= lastCol; c++)
                {
                    var v = AnyWhitespace().Replace(ws.Cell(r, c).GetString() ?? "", " ").Trim();
                    if (v.Length > 0)
                    {
                        cells.Add(v);
                    }
                }

                // Blank rows are dropped here — note this differs from the PD parser, which keeps
                // them. This is a flattening for reading, not a positional walk.
                var line = string.Join(" | ", cells);
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }

            var body = string.Join('\n', lines);
            if (body.Length > best.Length)
            {
                best = $"Sheet: {ws.Name}\n\n{body}";
            }
        }

        return best;
    }

    /// <summary>
    /// Zero-pad a job code to six digits for comparison. Forms state codes unpadded ("7399") while
    /// the reference stores them padded, so both spellings must compare equal.
    /// </summary>
    public static string PadCode(string code) => NonDigit().Replace(code ?? "", "").PadLeft(6, '0');
}

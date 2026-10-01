using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Server.Core.Ingest;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Server.Tests.Ingest;

/// <summary>
/// Extraction is only PARTLY portable, and this file is split along that line on purpose.
///
/// The dispatch table, whitespace tidying and size thresholds are byte-parity tested against
/// fixtures/extract.json — that is where a port drifts, because C# has no <c>File.type</c> and gets
/// its MIME from somewhere else entirely.
///
/// docx and pdf text extraction get BEHAVIORAL tests instead. mammoth/unpdf and OpenXml/PdfPig are
/// different implementations that will disagree on whitespace, paragraph joins and reading order;
/// a byte-parity test there could only be satisfied by reimplementing mammoth in C#. Claiming
/// fixture coverage for them would be claiming coverage we do not have.
/// </summary>
public class TextExtractorTests
{
    private sealed class ExtractFixture
    {
        public Thresholds Thresholds { get; set; } = new();
        public List<DispatchCase> Dispatch { get; set; } = [];
        public List<TidyCase> Tidy { get; set; } = [];
    }

    private sealed class Thresholds
    {
        public int MAX_BYTES { get; set; }
        public int MAX_CHARS { get; set; }
        public int MIN_USEFUL_CHARS { get; set; }
    }

    private sealed class DispatchCase
    {
        public string Filename { get; set; } = "";
        public string Mime { get; set; } = "";
        public string? Kind { get; set; }
        public ErrorInfo? Error { get; set; }
    }

    private sealed class ErrorInfo
    {
        public string Type { get; set; } = "";
        public string Message { get; set; } = "";
    }

    private sealed class TidyCase
    {
        public string Why { get; set; } = "";
        public string Input { get; set; } = "";
        public string Output { get; set; } = "";
    }

    private static readonly ExtractFixture Fixture = Fixtures.Load<ExtractFixture>("extract.json");

    // ------------------------------------------------------------------ portable: dispatch

    [Fact]
    public void The_dispatch_table_matches_the_reference_exactly()
    {
        Fixture.Dispatch.Should().HaveCountGreaterThan(25);
        var mismatches = new List<string>();

        foreach (var c in Fixture.Dispatch)
        {
            var label = $"{(c.Filename.Length > 0 ? c.Filename : "(empty)")} / {(c.Mime.Length > 0 ? c.Mime : "(no mime)")}";

            if (c.Error is not null)
            {
                // The MESSAGE is the contract, not just the failure: it tells the user how to
                // unstick themselves, and the remedies differ per format.
                try
                {
                    var got = TextExtractor.KindFor(c.Filename, c.Mime);
                    mismatches.Add($"  {label}: expected error, got {got}");
                }
                catch (ExtractionException ex)
                {
                    if (ex.Message != c.Error.Message)
                    {
                        mismatches.Add($"  {label}:\n    expected: {c.Error.Message}\n    got:      {ex.Message}");
                    }
                }

                continue;
            }

            var expected = Enum.Parse<DocKind>(c.Kind!, ignoreCase: true);
            try
            {
                TextExtractor.KindFor(c.Filename, c.Mime).Should().Be(expected, "{0}", label);
            }
            catch (ExtractionException ex)
            {
                mismatches.Add($"  {label}: expected {expected}, threw: {ex.Message}");
            }
        }

        mismatches.Should().BeEmpty("{0} dispatch differences:\n{1}",
            mismatches.Count, string.Join('\n', mismatches));
    }

    [Fact]
    public void Uppercase_HTML_still_dispatches_as_html()
    {
        // The whole 1,367-file corpus is named ".HTML". If this regressed, every dropped export
        // would be rejected as an unknown type instead of reaching the free deterministic parser.
        TextExtractor.KindFor("FARM LABORER (40123456).HTML", "").Should().Be(DocKind.Html);
        TextExtractor.KindFor("JD.PDF", "").Should().Be(DocKind.Pdf);
        TextExtractor.KindFor("Position Description.XLSX", "").Should().Be(DocKind.Xlsx);
    }

    [Fact]
    public void A_correct_extension_beats_a_contradicting_mime()
    {
        TextExtractor.KindFor("jd.pdf", "text/plain").Should().Be(DocKind.Pdf);
        TextExtractor.KindFor("jd.docx", "application/pdf").Should().Be(DocKind.Docx);
    }

    [Fact]
    public void Only_the_last_extension_counts()
    {
        TextExtractor.KindFor("my.jd.v2.final.docx", "").Should().Be(DocKind.Docx);
        TextExtractor.KindFor("report.pdf.txt", "").Should().Be(DocKind.Text);
    }

    // ------------------------------------------------------------------ portable: tidy

    [Fact]
    public void Tidy_matches_the_reference_exactly()
    {
        Fixture.Tidy.Should().HaveCountGreaterThan(8);

        foreach (var c in Fixture.Tidy)
        {
            TextExtractor.Tidy(c.Input).Should().Be(c.Output, "{0}", c.Why);
        }
    }

    // ------------------------------------------------------------------ portable: thresholds

    [Fact]
    public void An_empty_file_is_rejected_by_name()
    {
        var act = () => TextExtractor.Extract([], "empty.txt", "");
        act.Should().Throw<ExtractionException>().WithMessage("*empty.txt*is empty*");
    }

    [Fact]
    public void An_oversized_file_is_rejected_before_being_read()
    {
        var oversized = new byte[Fixture.Thresholds.MAX_BYTES + 1];
        var act = () => TextExtractor.Extract(oversized, "huge.txt", "");
        act.Should().Throw<ExtractionException>()
            .WithMessage("*huge.txt*20.0 MB*the limit is 20 MB*");
    }

    [Fact]
    public void An_unsupported_type_fails_on_its_name_not_on_a_parse()
    {
        // Ordering matters: dispatch runs before any bytes are interpreted, so a .png full of
        // garbage reports "unsupported type" rather than a parser error.
        var act = () => TextExtractor.Extract([1, 2, 3, 4], "photo.png", "image/png");
        act.Should().Throw<ExtractionException>().WithMessage("*Unsupported file type*.png*");
    }

    [Fact]
    public void Text_below_the_useful_floor_is_rejected()
    {
        var thin = Encoding.UTF8.GetBytes("too short to classify");
        var act = () => TextExtractor.Extract(thin, "note.txt", "");
        act.Should().Throw<ExtractionException>().WithMessage("*contained almost no readable text*");
    }

    [Fact]
    public void Text_above_the_character_cap_is_truncated_with_a_note()
    {
        var huge = Encoding.UTF8.GetBytes(new string('a', Fixture.Thresholds.MAX_CHARS + 5_000));
        var result = TextExtractor.Extract(huge, "big.txt", "");

        result.Text.Should().HaveLength(Fixture.Thresholds.MAX_CHARS);
        result.Note.Should().Contain("Truncated to the first 400K characters");
    }

    [Fact]
    public void Html_passes_through_untouched()
    {
        // Not tidied, not stripped. An HRTMS export is parsed structurally downstream, and
        // normalizing here would destroy that path.
        var html = "<html>\r\n\r\n\r\n<td>Business Unit (Location):</td>   \n<td>DAVIS</td>"
                   + new string(' ', 200) + "</html>";
        var result = TextExtractor.Extract(Encoding.UTF8.GetBytes(html), "export.HTML", "");

        result.Kind.Should().Be(DocKind.Html);
        result.Text.Should().Be(html, "HTML must survive byte-for-byte");
    }

    [Fact]
    public void Plain_text_is_tidied()
    {
        var raw = "Line one   \r\n\r\n\r\n\r\nLine two\r\n" + new string('x', 150);
        var result = TextExtractor.Extract(Encoding.UTF8.GetBytes(raw), "notes.txt", "");

        result.Kind.Should().Be(DocKind.Text);
        result.Text.Should().StartWith("Line one\n\nLine two\n");
        result.Text.Should().NotContain("\r");
        result.Text.Should().NotContain("   \n");
    }

    // ------------------------------------------------------------------ NOT portable: behavioral

    private static byte[] BuildDocx(params string[] paragraphs)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                paragraphs.Select(p => new Paragraph(new Run(new Text(p) { Space = SpaceProcessingModeValues.Preserve })))));
            main.Document.Save();
        }

        return ms.ToArray();
    }

    [Fact]
    public void Docx_extraction_recovers_the_text_in_order()
    {
        // BEHAVIORAL, not byte-parity: OpenXml and mammoth will not agree on whitespace. What must
        // hold is that the content is all there, in document order, above the useful floor.
        var body = new string('y', 150);
        var bytes = BuildDocx(
            "Job Summary",
            "Analyzes evaluation data and reports findings.",
            body,
            "Minimum Qualifications");

        var result = TextExtractor.Extract(bytes, "jd.docx", "");

        result.Kind.Should().Be(DocKind.Docx);
        result.Text.Should().Contain("Job Summary");
        result.Text.Should().Contain("Analyzes evaluation data and reports findings.");
        result.Text.Should().Contain("Minimum Qualifications");

        result.Text.IndexOf("Job Summary", StringComparison.Ordinal)
            .Should().BeLessThan(result.Text.IndexOf("Minimum Qualifications", StringComparison.Ordinal),
                "paragraph order carries meaning in a job description");

        // Tidy is applied, so no CR and no runs of three-plus newlines survive.
        result.Text.Should().NotContain("\r");
        result.Text.Should().NotContain("\n\n\n");
    }

    [Fact]
    public void A_docx_with_almost_no_text_is_rejected()
    {
        var bytes = BuildDocx("Hi");
        var act = () => TextExtractor.Extract(bytes, "tiny.docx", "");
        act.Should().Throw<ExtractionException>().WithMessage("*contained almost no readable text*");
    }

    [Fact]
    public void A_corrupt_docx_reports_a_remediable_error()
    {
        // Not a zip at all. The message must point at password-protection or corruption and offer
        // the paste fallback, because that is the only route left for the user.
        var bytes = Encoding.UTF8.GetBytes(new string('z', 500));
        var act = () => TextExtractor.Extract(bytes, "broken.docx", "");

        act.Should().Throw<ExtractionException>()
            .WithMessage("*Couldn't read “broken.docx”*")
            .WithMessage("*password-protected or corrupt*")
            .WithMessage("*try pasting the text below*");
    }

    private static byte[] BuildPdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(595, 842);

        var y = 800;
        foreach (var line in lines)
        {
            page.AddText(line, 12, new UglyToad.PdfPig.Core.PdfPoint(50, y), font);
            y -= 20;
        }

        return builder.Build();
    }

    [Fact]
    public void Pdf_extraction_recovers_text_and_reports_the_page_count()
    {
        // BEHAVIORAL: PdfPig and unpdf will not agree on whitespace or reading order. What must
        // hold is that the words come back and the user is told how many pages were read.
        var bytes = BuildPdf(
            "Job Summary",
            "Coordinates evaluation activities across the college and reports outcomes.",
            "Minimum Qualifications include a bachelor's degree or equivalent experience.",
            "Preferred Qualifications include experience with survey instrumentation.");

        var result = TextExtractor.Extract(bytes, "jd.pdf", "");

        result.Kind.Should().Be(DocKind.Pdf);
        result.Text.Should().Contain("Job Summary");
        result.Text.Should().Contain("Minimum Qualifications");
        result.Note.Should().Be("1 page read.", "singular for a one-page document");
    }

    [Fact]
    public void An_image_only_pdf_is_told_to_use_OCR()
    {
        // A PDF with no selectable text is the scanned-document case, and it needs a DIFFERENT
        // remedy from an unsupported type — OCR, or pasting. Conflating the two leaves the user
        // trying to convert a file that is already the right format.
        var bytes = BuildPdf("x");
        var act = () => TextExtractor.Extract(bytes, "scan.pdf", "");

        act.Should().Throw<ExtractionException>()
            .WithMessage("*No selectable text found in “scan.pdf”*")
            .WithMessage("*need OCR first*");
    }

    // ------------------------------------------------------------------ xlsx delegation

    [Fact]
    public void A_PD_workbook_is_rendered_and_its_proposed_code_held_aside()
    {
        // The xlsx path delegates to the PD parser, so this checks the wiring rather than the
        // parsing: the rendered prompt comes back, the sheet is named in the note, and the proposed
        // code travels in its own field where the model cannot see it.
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "inputs", "pd",
            "proposed-code-unpadded.xlsx");
        File.Exists(path).Should().BeTrue();

        var result = TextExtractor.Extract(File.ReadAllBytes(path), "pd.xlsx", "");

        result.Kind.Should().Be(DocKind.Xlsx);
        result.ProposedCode.Should().Be("7399");
        result.Text.Should().NotContain("7399", "the proposed code must never reach the prompt");
        result.Note.Should().Contain("Position Description workbook");
        result.Note.Should().Contain("New Recruitment");
    }

    [Fact]
    public void A_non_PD_workbook_falls_back_to_flattening_one_sheet()
    {
        // Only the richest sheet, because these workbooks carry dropdown lookup tables holding far
        // more text than the form — dumping every sheet buries the job description.
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "inputs", "standards",
            "layout-b-basic.xlsx");
        File.Exists(path).Should().BeTrue();

        var result = TextExtractor.Extract(File.ReadAllBytes(path), "standards.xlsx", "");

        result.Kind.Should().Be(DocKind.Xlsx);
        result.ProposedCode.Should().BeNull();
        result.Note.Should().Contain("Not a recognized Position Description form");
        result.Text.Should().StartWith("Sheet: ");
        result.Text.Should().Contain(" | ", "cells are joined with a pipe");
    }

    [Fact]
    public void Job_codes_compare_equal_padded_or_not()
    {
        // Forms state "7399"; the reference stores "007399". Both must resolve to one class.
        TextExtractor.PadCode("7399").Should().Be("007399");
        TextExtractor.PadCode("007399").Should().Be("007399");
        TextExtractor.PadCode("").Should().Be("000000");

        // And why padding alone is NOT enough on raw form text: stripping non-digits from
        // "Project Policy Anl 4 / 7399" picks up the level number too. That is exactly why the PD
        // parser extracts the code with a 4-to-6 digit match FIRST and pads only the result.
        TextExtractor.PadCode("Project Policy Anl 4 / 7399").Should().Be("047399");
    }
}

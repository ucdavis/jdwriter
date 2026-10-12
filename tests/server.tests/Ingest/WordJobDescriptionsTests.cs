using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Intake;
using Server.Core.Titles;
using Server.Tests.Profiles;

namespace Server.Tests.Ingest;

/// <summary>
/// JDs from other units, copied out of JDX into Word, parsed straight from their layout — no model —
/// into the same record an in-unit HRTMS export makes. The documents here are SYNTHETIC replicas of
/// the two real layouts (an HRTMS export saved as Word; a job posting): the labels are the real ones,
/// every value is invented. Real exports are never committed.
/// </summary>
public class WordJobDescriptionsTests
{
    private static TableCell Cell(params string[] paragraphs) =>
        new([.. paragraphs.Select(t => new Paragraph(new Run(new Text(t) { Space = SpaceProcessingModeValues.Preserve })))]);

    /// <summary>A bullet: a list item, as JDX's numbered/bulleted paragraphs arrive.</summary>
    private static Paragraph Bullet(string t) =>
        new(new ParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 })),
            new Run(new Text(t)));

    private static TableCell BulletCell(params string[] items) => new([.. items.Select(Bullet)]);

    private static TableRow Row(params TableCell[] cells) => new(cells);

    private static byte[] Docx(params OpenXmlElement[] body)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(body));
            main.Document.Save();
        }

        return ms.ToArray();
    }

    /// <summary>An HRTMS job description export, as Word: label/value rows and the % TIME table.</summary>
    private static byte[] HrtmsExport(string code = "009605", string position = "40000001", bool withDuties = true) => Docx(
        new Table(
            Row(Cell("Business Unit (Location):"), Cell("DVCMP")),
            Row(Cell("Department Name:"), Cell("Synthetic Dept")),
            Row(Cell("Job Description Number"), Cell("100001")),
            Row(Cell("UCPath Position Number"), Cell(position)),
            Row(Cell("UC Job Title:"), Cell("LAB AST 1")),
            Row(Cell("UC Job Code:"), Cell(code)),
            Row(Cell("Working Title:"), Cell("Greenhouse Technician")),
            Row(Cell("Salary Grade:"), Cell("Grade 17")),
            Row(Cell("Job FLSA Status:"), Cell("Non-Exempt")),
            Row(Cell("Union Code (Collective Bargaining Unit):"), Cell("TX-Technical Professionals")),
            Row(Cell("Job Summary"), Cell("Supports plant research in the greenhouse."))),
        new Table(
            Row(Cell("Key Responsibilities - Total percent of time must equal 100%")),
            Row(Cell("% TIME"), Cell("Function"), Cell("Duties")),
            Row(Cell("60%"), Cell("PLANT CARE"), withDuties ? BulletCell("Waters and feeds plants daily.", "Records growth measurements.") : Cell("")),
            Row(Cell("40%"), Cell("RECORDS"), withDuties ? BulletCell("Keeps the greenhouse log.") : Cell(""))),
        new Paragraph(new Run(new Text("QUALIFICATIONS"))));

    /// <summary>A job posting: functions and time, no duties.</summary>
    private static byte[] Posting() => Docx(
        new Paragraph(new Run(new Text("UC Job Title"))),
        new Paragraph(new Run(new Text("LAB AST 1"))),
        new Paragraph(new Run(new Text("Job Summary"))),
        new Paragraph(new Run(new Text("Supports plant research."))),
        new Paragraph(new Run(new Text("Qualifications"))),
        Bullet("Experience in a greenhouse.Experience keeping records."),
        new Paragraph(new Run(new Text("Preferred Qualifications"))),
        Bullet("Pesticide applicator certificate."),
        new Paragraph(new Run(new Text("Key Responsibilities"))),
        Bullet("60% - PLANT CARE"),
        Bullet("40% - RECORDS"),
        new Paragraph(new Run(new Text("Department Overview"))),
        new Paragraph(new Run(new Text("POSITION INFORMATION"))),
        Bullet("UC Job Title: LAB AST 1"),
        Bullet("UC Job Code: 009605"),
        Bullet("Salary Grade: Grade 17"));

    [Fact]
    public void An_HRTMS_export_saved_as_Word_parses_like_the_HTML_export()
    {
        var r = WordJobDescriptions.TryParse(HrtmsExport(), "a.docx")!;

        r.UcJobCode.Should().Be("009605");
        r.UcPathPositionNumber.Should().Be("40000001");
        r.WorkingTitle.Should().Be("Greenhouse Technician");
        r.Responsibilities.Select(x => (x.Pct, x.FunctionName)).Should().Equal((60, "PLANT CARE"), (40, "RECORDS"));
        // Duties kept verbatim, one per bullet — not condensed.
        r.Responsibilities[0].Duties.Should().Equal("Waters and feeds plants daily.", "Records growth measurements.");
        WordJobDescriptions.Incomplete(r).Should().BeNull();
    }

    [Fact]
    public void A_posting_parses_but_is_incomplete_without_duties()
    {
        var r = WordJobDescriptions.TryParse(Posting(), "p.docx")!;

        r.UcJobCode.Should().Be("009605");
        r.Responsibilities.Select(x => (x.Pct, x.FunctionName)).Should().Equal((60, "PLANT CARE"), (40, "RECORDS"));
        r.Qualifications.KsaMin.Should().Equal("Experience in a greenhouse.", "Experience keeping records.");
        WordJobDescriptions.Incomplete(r).Should().Contain("no duties");
    }

    [Fact]
    public void An_unrecognized_document_is_not_parsed() =>
        WordJobDescriptions.TryParse(Docx(new Paragraph(new Run(new Text("Some memo")))), "m.docx").Should().BeNull();

    // ---------------------------------------------------------------- importing

    private sealed class Titles(params TitleCode[] rows) : ITitleCodeService
    {
        private readonly TitleCodeIndex _index = new(rows);
        public Task<TitleCodeIndex> GetAsync(CancellationToken ct = default) => Task.FromResult(_index);
        public void Invalidate() { }
    }

    private sealed class CountingDistiller : IDescriptionClassifier
    {
        public int Calls { get; private set; }

        public Task<DistilledJd> DistillAsync(string text, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new DistilledJd
            {
                Functions = [new DistilledFunction { Name = "Condensed", PctTime = 100, Duties = ["Condensed duty."] }],
            });
        }

        public Task<Classification> ClassifyAsync(string text, List<ClassProfile> profiles, string? proposedCode = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ProposedAssessment?> AssessProposedAsync(DistilledJd distilled, string code, List<ClassProfile> profiles, string ourPick, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static (AppDbContext Db, CorpusDocuments Documents, CountingDistiller Model) Harness()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"word_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        static TitleCode Tc(string code, string title) => new()
        {
            Code = code, Title = title, Source = "both",
            TitleKey = TitleNormalizer.TitleKey(title), TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
        };
        var model = new CountingDistiller();
        return (db, new CorpusDocuments(db, new Titles(Tc("009605", "LAB AST 1")), model, new FakeStructuredLlm()), model);
    }

    private const string DocxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    [Fact]
    public async Task An_export_from_another_unit_is_filed_by_its_code_verbatim_without_a_model_call()
    {
        var (db, documents, model) = Harness();

        var outcome = await documents.ImportAsync("a.docx", HrtmsExport(), DocxMime, userId: null);

        outcome.Result.Should().Be("added");
        outcome.ReadBy.Should().Be("parsed");
        model.Calls.Should().Be(0);
        var jd = await db.JobDescriptions.Include(j => j.Responsibilities).ThenInclude(r => r.Duties).SingleAsync();
        jd.Origin.Should().Be(CorpusOrigin.Imported);
        jd.UcJobCode.Should().Be("009605");
        jd.UcPathPositionNumber.Should().Be("40000001");
        jd.Responsibilities.OrderBy(r => r.Ordinal).First().Duties.OrderBy(d => d.Ordinal).Select(d => d.Text)
            .Should().Equal("Waters and feeds plants daily.", "Records growth measurements.");
    }

    [Fact]
    public async Task A_newer_export_of_the_same_position_replaces_the_earlier_import()
    {
        var (db, documents, _) = Harness();
        await documents.ImportAsync("a.docx", HrtmsExport(), DocxMime, null);

        // A re-export with different content (a duty added): same position.
        var revised = HrtmsExport();
        await documents.ImportAsync("a-revised.docx", Docx(
            new Table(
                Row(Cell("UCPath Position Number"), Cell("40000001")),
                Row(Cell("UC Job Code:"), Cell("009605")),
                Row(Cell("Job Summary"), Cell("Revised summary."))),
            new Table(
                Row(Cell("% TIME"), Cell("Function"), Cell("Duties")),
                Row(Cell("100%"), Cell("PLANT CARE"), BulletCell("Waters plants.", "Prunes plants."))),
            new Paragraph(new Run(new Text("QUALIFICATIONS")))), DocxMime, null);

        (await db.JobDescriptions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_incomplete_document_is_not_added()
    {
        var (db, documents, model) = Harness();

        var posting = await documents.ImportAsync("p.docx", Posting(), DocxMime, null);
        var noDuties = await documents.ImportAsync("n.docx", HrtmsExport(position: "40000002", withDuties: false), DocxMime, null);

        posting.Result.Should().Be("failed");
        posting.Error.Should().StartWith("Incomplete, so not added");
        noDuties.Error.Should().StartWith("Incomplete, so not added");
        model.Calls.Should().Be(0);
        (await db.JobDescriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Reparse_turns_model_read_imports_into_verbatim_records_and_discards_incomplete_ones()
    {
        var (db, documents, _) = Harness();
        // Two documents imported before the parser: each filed as a model-condensed record.
        foreach (var (id, bytes) in new[] { (1, HrtmsExport()), (2, Posting()) })
        {
            db.CorpusUploads.Add(new CorpusUpload
            {
                Id = id, FileName = $"doc{id}.docx", Sha256 = $"sha{id}", Content = bytes, SizeBytes = bytes.Length,
                Source = CorpusUploadSource.Document, Status = CorpusUploadStatus.Filed, UcJobCode = "009605",
            });
            db.JobDescriptions.Add(new JobDescription
            {
                SourceFile = $"import/document-{id}", UcJobCode = "009605", Origin = CorpusOrigin.Imported,
                Responsibilities = [new JdResponsibility { FunctionName = "Condensed", Pct = 100 }],
            });
        }

        await db.SaveChangesAsync();

        var summary = await documents.ReparseAsync();

        summary.Reparsed.Should().Be(1);
        summary.Discarded.Should().ContainSingle().Which.FileName.Should().Be("doc2.docx");
        var jds = await db.JobDescriptions.Include(j => j.Responsibilities).ToListAsync();
        jds.Should().ContainSingle().Which.Responsibilities.Select(r => r.FunctionName).Should().BeEquivalentTo(["PLANT CARE", "RECORDS"]);
        (await db.CorpusUploads.SingleAsync(u => u.Id == 2)).Status.Should().Be(CorpusUploadStatus.Failed);
    }
}

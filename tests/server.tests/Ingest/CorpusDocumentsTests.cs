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
/// JDs from units outside the college, imported from Word/PDF/text copies out of JDX. These pin that
/// a document is filed only under the class its stated title unambiguously names, that unusable
/// files fail before any model call, that a re-saved copy is a duplicate, and that what is filed is
/// marked as imported and joins the rebuild queue.
///
/// Every document here is synthetic. The titles are real UC payroll titles from the public reference.
/// </summary>
public class CorpusDocumentsTests
{
    private sealed class Titles(params TitleCode[] rows) : ITitleCodeService
    {
        private readonly TitleCodeIndex _index = new(rows);
        public Task<TitleCodeIndex> GetAsync(CancellationToken ct = default) => Task.FromResult(_index);
        public void Invalidate() { }
    }

    /// <summary>Distils every document to the same canned JD, and counts the calls.</summary>
    private sealed class CannedDistiller(DistilledJd result) : IDescriptionClassifier
    {
        public int Calls { get; private set; }

        public Task<DistilledJd> DistillAsync(string text, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }

        public Task<Classification> ClassifyAsync(
            string text, List<ClassProfile> profiles, string? proposedCode = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ProposedAssessment?> AssessProposedAsync(
            DistilledJd distilled, string code, List<ClassProfile> profiles, string ourPick, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static TitleCode Tc(string code, string title) => new()
    {
        Code = code,
        Title = title,
        Source = "both",
        TitleKey = TitleNormalizer.TitleKey(title),
        TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
    };

    private static readonly TitleCodeIndex Index = new([
        Tc("009605", "LAB AST 1"),
        Tc("004724", "FARM LABORER"),
        Tc("004920", "ADMINISTRATIVE OFFICER 3"),
    ]);

    private static DistilledJd Distilled() => new()
    {
        WorkingTitle = "Greenhouse Technician",
        Summary = "Supports plant research.",
        Functions =
        [
            new DistilledFunction { Name = "Plant care", PctTime = 60, Duties = ["Waters plants", "Records growth"] },
            new DistilledFunction { Name = "Records", PctTime = 40, Duties = ["Keeps logs"] },
        ],
        Experience = ["One year of lab work"],
    };

    private static (AppDbContext Db, CorpusDocuments Documents, CannedDistiller Distiller, FakeStructuredLlm Llm) Harness()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"documents_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var titles = new Titles(Tc("009605", "LAB AST 1"), Tc("004724", "FARM LABORER"));
        var distiller = new CannedDistiller(Distilled());
        var llm = new FakeStructuredLlm();
        return (db, new CorpusDocuments(db, titles, distiller, llm), distiller, llm);
    }

    /// <summary>A document long enough to be read (the extractor refuses near-empty text).</summary>
    private static byte[] Text(string s) =>
        Encoding.UTF8.GetBytes(s + "\n" + Body);

    private const string Body =
        "Job Summary\nSupports plant research in the greenhouse: watering, recording growth, and keeping " +
        "the logs the research team relies on. Works under general supervision.";

    // ------------------------------------------------------------------ finding the stated title

    [Theory]
    [InlineData("Job Title: LAB AST 1\nDepartment: Plant Sciences")]
    [InlineData("UC Job Title - Lab Ast 1\nSummary")]
    [InlineData("Job Title\nLAB AST 1\nDepartment")] // a Word table cell: label, then its value on the next line
    [InlineData("Department\tPlant Sciences\nJob Title\tLAB AST 1")] // a table row, tab-separated
    [InlineData("LAB AST 1\nJob Summary\nSupports research.")] // no label: a heading that is the title
    public void A_stated_title_resolves_to_its_class(string text)
    {
        var (code, problem) = CorpusDocuments.StatedTitle(text, Index);

        problem.Should().BeNull();
        code!.Code.Should().Be("009605");
    }

    [Fact]
    public void A_labelled_title_wins_over_a_bare_line_naming_another_class()
    {
        var (code, _) = CorpusDocuments.StatedTitle("FARM LABORER\nJob Title: LAB AST 1", Index);

        code!.Code.Should().Be("009605");
    }

    [Fact]
    public void The_working_title_is_not_the_classification()
    {
        var (code, _) = CorpusDocuments.StatedTitle("Working Title: FARM LABORER\nJob Title: LAB AST 1", Index);

        code!.Code.Should().Be("009605");
    }

    [Fact]
    public void Two_different_classes_are_reported_rather_than_guessed()
    {
        var (code, problem) = CorpusDocuments.StatedTitle("Job Title: LAB AST 1\nPayroll Title: FARM LABORER", Index);

        code.Should().BeNull();
        problem.Should().Contain("more than one UC job title");
    }

    [Fact]
    public void An_unknown_title_is_named_in_the_reason()
    {
        var (code, problem) = CorpusDocuments.StatedTitle("Job Title: Chief Plant Whisperer\nSummary", Index);

        code.Should().BeNull();
        problem.Should().Contain("Chief Plant Whisperer");
    }

    // ------------------------------------------------------------------ filing

    [Fact]
    public async Task A_document_is_filed_under_its_title_as_an_imported_jd()
    {
        var (db, documents, _, _) = Harness();

        var outcome = await documents.ImportAsync("greenhouse.txt", Text("Job Title: LAB AST 1\nSupports plant research."), "text/plain", userId: null);

        outcome.Result.Should().Be("added");
        outcome.UcJobCode.Should().Be("009605");
        var jd = await db.JobDescriptions.Include(j => j.Responsibilities).ThenInclude(r => r.Duties).SingleAsync();
        jd.Origin.Should().Be(CorpusOrigin.Imported);
        jd.UcJobCode.Should().Be("009605");
        jd.AddedAt.Should().NotBeNull();
        jd.Responsibilities.Select(r => (r.FunctionName, r.Pct)).Should().Equal(("Plant care", 60), ("Records", 40));
        jd.SourceFile.Should().StartWith("import/document-");

        var original = await db.CorpusUploads.SingleAsync();
        original.Source.Should().Be(CorpusUploadSource.Document);
        original.Status.Should().Be(CorpusUploadStatus.Filed);
        original.Content.Should().Equal(Text("Job Title: LAB AST 1\nSupports plant research."), "the original is kept, byte for byte");
    }

    /// <summary>
    /// A JD pasted from JDX into Word: the header is a table of label/value cells, then prose.
    /// BEHAVIORAL — what must hold is that the title in its cell is found through real extraction.
    /// </summary>
    [Fact]
    public async Task A_word_document_with_the_title_in_a_table_is_filed_under_it()
    {
        static TableCell Cell(string text) => new(new Paragraph(new Run(new Text(text))));
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Table(
                    new TableRow(Cell("Job Title"), Cell("Lab Ast 1")),
                    new TableRow(Cell("Department"), Cell("Synthetic Dept"))),
                new Paragraph(new Run(new Text(Body)))));
            main.Document.Save();
        }

        var (db, documents, _, _) = Harness();
        var outcome = await documents.ImportAsync(
            "jdx copy.docx", ms.ToArray(), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", userId: null);

        outcome.Result.Should().Be("added", outcome.Error);
        (await db.JobDescriptions.SingleAsync()).UcJobCode.Should().Be("009605");
    }

    [Fact]
    public async Task An_unmatched_title_fails_before_any_model_call_and_files_nothing()
    {
        var (db, documents, distiller, _) = Harness();

        var outcome = await documents.ImportAsync("x.txt", Text("Job Title: Chief Plant Whisperer\nWork."), "text/plain", userId: null);

        outcome.Result.Should().Be("failed");
        distiller.Calls.Should().Be(0);
        (await db.JobDescriptions.CountAsync()).Should().Be(0);
        (await db.CorpusUploads.CountAsync()).Should().Be(0, "a failed document is not stored, so a fixed copy can be added later");
    }

    [Fact]
    public async Task The_same_words_saved_again_are_a_duplicate()
    {
        var (db, documents, distiller, _) = Harness();
        await documents.ImportAsync("a.txt", Text("Job Title: LAB AST 1\nSupports plant research."), "text/plain", userId: null);

        // Different bytes (the spacing a re-save changes), same JD.
        var again = await documents.ImportAsync("a copy.txt", Text("Job Title: LAB AST 1   \nSupports plant research.\n\n"), "text/plain", userId: null);

        again.Result.Should().Be("duplicate");
        distiller.Calls.Should().Be(1);
        (await db.JobDescriptions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Without_an_AI_provider_it_says_so_and_files_nothing()
    {
        var (db, documents, distiller, llm) = Harness();
        llm.HasApiKey = false;

        var outcome = await documents.ImportAsync("a.txt", Text("Job Title: LAB AST 1\nWork."), "text/plain", userId: null);

        outcome.Result.Should().Be("failed");
        outcome.Error.Should().Contain("No AI provider");
        distiller.Calls.Should().Be(0);
        (await db.JobDescriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_unsupported_file_type_fails_with_the_extractors_reason()
    {
        var (_, documents, _, _) = Harness();

        var outcome = await documents.ImportAsync("photo.png", [1, 2, 3], "image/png", userId: null);

        outcome.Result.Should().Be("failed");
        outcome.Error.Should().Be("Only Word (.docx), PDF and text files can be added here.");
    }

    [Fact]
    public async Task A_legacy_doc_gets_the_save_as_docx_advice_without_the_paste_hint()
    {
        var (_, documents, _, _) = Harness();

        var outcome = await documents.ImportAsync("old.doc", [1, 2, 3], "application/msword", userId: null);

        outcome.Error.Should().Contain("save as .docx").And.NotContain("paste", "this panel has no text box");
    }

    [Fact]
    public async Task Html_is_turned_away_with_the_fix()
    {
        var (_, documents, distiller, _) = Harness();

        var outcome = await documents.ImportAsync("from word.htm", Text("<html><body><p>Job Title: LAB AST 1</p></body></html>"), "text/html", userId: null);

        outcome.Result.Should().Be("failed");
        outcome.Error.Should().Contain(".docx");
        distiller.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Imported_jds_join_the_rebuild_queue_under_their_own_count()
    {
        var (db, documents, _, _) = Harness();
        var titles = new Titles(Tc("009605", "LAB AST 1"));
        await documents.ImportAsync("a.txt", Text("Job Title: LAB AST 1\nWork."), "text/plain", userId: null);

        var queued = (await new CorpusUploads(db, titles, null!).PendingAsync()).Single();

        queued.Code.Should().Be("009605");
        queued.NewImported.Should().Be(1);
        queued.NewFiles.Should().Be(0);
    }
}

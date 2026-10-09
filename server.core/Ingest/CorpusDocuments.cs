using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Ai;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Intake;
using Server.Core.Titles;

namespace Server.Core.Ingest;

/// <summary>
/// Job descriptions from units outside the college, added to the corpus from ordinary documents.
///
/// Those JDs cannot be exported from HRTMS in its labelled format; they arrive as Word (or PDF, or
/// text) copies out of JDX, stating their UC job TITLE but not their code. So, per document:
///
/// 1. The title it states is resolved to a job code through the title reference, exactly as the
///    standards bootstrap resolves a standard's title. A title that does not resolve, or resolves
///    to more than one class, is reported and the document is not filed — evidence filed under the
///    wrong class would quietly skew that class's envelope, so this never guesses. It runs before
///    any model call, so an unusable file costs nothing.
/// 2. A superseded code is remapped to its successor, as every other path that files a JD does,
///    and the result must be on UC Davis payroll: a matrix-only class is one nobody could use.
/// 3. The model distils the document into the corpus shape (functions, % time, duties,
///    qualifications) — the same distillation Classify uses, and the same record shape.
/// 4. The JD is filed with <see cref="CorpusOrigin.Imported"/>, so it can always be told apart from
///    an HRTMS export, and the original is kept as bytes in the database only.
///
/// Filed JDs count at the class's next rebuild, where the admin queue lists them.
/// </summary>
public sealed partial class CorpusDocuments
{
    /// <summary>How far into a document its title is looked for: it is stated at the top.</summary>
    private const int TitleSearchLines = 40;

    /// <summary>A title cell is short. Longer lines are prose, never a title.</summary>
    private const int MaxTitleLength = 80;

    /// <summary>"Job Title:", "UC Job Title", "Payroll Title -" — a label naming the classification title.</summary>
    [GeneratedRegex(@"^\s*(?<label>[A-Za-z][A-Za-z /]{0,40}?title)\s*[:\-–—]?\s*(?<value>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex TitleLabel();

    private readonly AppDbContext _db;
    private readonly ITitleCodeService _titleCodes;
    private readonly IDescriptionClassifier _classifier;
    private readonly IStructuredLlm _llm;

    public CorpusDocuments(
        AppDbContext db, ITitleCodeService titleCodes, IDescriptionClassifier classifier, IStructuredLlm llm)
    {
        _db = db;
        _titleCodes = titleCodes;
        _classifier = classifier;
        _llm = llm;
    }

    /// <summary>
    /// Read, resolve, distil and file one document. Never throws for a bad document: every outcome,
    /// including a failure and its reason, comes back to the admin.
    /// </summary>
    public async Task<UploadOutcome> ImportAsync(
        string name, byte[] bytes, string mime, int? userId, CancellationToken ct = default)
    {
        var outcome = new UploadOutcome { FileName = name };

        DocKind kind;
        try
        {
            kind = TextExtractor.KindFor(name, mime);
        }
        catch (ExtractionException ex)
        {
            // A legacy .doc gets the extractor's own advice (save as .docx); anything else is simply
            // not a document this panel reads.
            return Failed(outcome, ex.Message.StartsWith("Unsupported file type", StringComparison.Ordinal)
                ? "Only Word (.docx), PDF and text files can be added here."
                : WithoutPasteHint(ex.Message));
        }

        if (kind == DocKind.Html)
        {
            // The extractor passes HTML through untouched, for the HRTMS parser. A Word-saved web
            // page is mostly markup, so it is turned away with the fix rather than read badly.
            return Failed(outcome,
                "HTML isn't read here. Save it from Word as a Word Document (.docx) — or, if it is an HRTMS export, add it under “Upload JDs” above.");
        }

        Extraction extraction;
        try
        {
            extraction = TextExtractor.Extract(bytes, name, mime);
        }
        catch (ExtractionException ex)
        {
            return Failed(outcome, WithoutPasteHint(ex.Message));
        }

        // Identity is the WORDS, not the file: Word rewrites a .docx on every save, so the same JD
        // saved twice has different bytes, and can differ in spacing, but has the same words.
        var words = Whitespace().Replace(extraction.Text, " ").Trim();
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(words)));
        if (await _db.CorpusUploads.AnyAsync(u => u.Sha256 == sha, ct))
        {
            outcome.Result = "duplicate";
            return outcome;
        }

        var index = await _titleCodes.GetAsync(ct);
        var stated = StatedTitle(extraction.Text, index);
        if (stated.Code is null)
        {
            return Failed(outcome, stated.Problem!);
        }

        // Filed only under a class UC Davis can use. A title can resolve to a matrix-only code — one
        // UC defines but UC Davis doesn't have on payroll — and a JD filed there would build a class
        // nobody could write against. Checked before the model call, on the code the JD would be
        // FILED under (after remapping).
        var code = index.ResolveCode(stated.Code.Code);
        var filedUnder = index.FindByCode(code);
        if (filedUnder is null || !filedUnder.IsInUse)
        {
            return Failed(outcome, $"Its title matches {stated.Code.Title} ({code}), which isn't on UC Davis payroll, so it can't be used here.");
        }

        if (!_llm.HasApiKey)
        {
            return Failed(outcome, "No AI provider is configured, so the document can't be read into the corpus.");
        }

        DistilledJd distilled;
        try
        {
            distilled = await _classifier.DistillAsync(extraction.Text, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(outcome, "The AI couldn't read this document. Try it again; if it fails twice, check the file opens in Word.");
        }

        if (distilled.Functions.Count == 0)
        {
            return Failed(outcome, "No responsibilities were found in it — is it a complete job description?");
        }

        var record = ClassifySubmissions.ToJobDescription(distilled);
        record.UcJobCode = code;
        record.UcJobTitle = filedUnder.Title;
        record.OriginalUcJobCode = string.Equals(stated.Code.Code, code, StringComparison.Ordinal) ? null : stated.Code.Code;
        record.Origin = CorpusOrigin.Imported;

        var now = DateTimeOffset.UtcNow;
        record.AddedAt = now;
        var original = new CorpusUpload
        {
            FileName = name,
            Sha256 = sha,
            Content = bytes,
            SizeBytes = bytes.Length,
            Source = CorpusUploadSource.Document,
            Status = CorpusUploadStatus.Filed,
            UcJobCode = record.UcJobCode,
            UcJobTitle = record.UcJobTitle,
            OriginalUcJobCode = record.OriginalUcJobCode,
            UploadedByUserId = userId,
            UploadedAt = now,
        };

        _db.CorpusUploads.Add(original);
        await _db.SaveChangesAsync(ct); // assigns the original's id, which names the corpus record

        record.SourceFile = $"import/document-{original.Id}";
        _db.JobDescriptions.Add(record);
        await _db.SaveChangesAsync(ct);

        outcome.Result = "added";
        outcome.UcJobCode = record.UcJobCode;
        outcome.Title = record.UcJobTitle;
        return outcome;
    }

    /// <summary>
    /// The UC job title a document states, resolved to its reference row.
    ///
    /// A LABELLED title ("Job Title: Financial Analyst 3", or the label alone with the title on the
    /// next line, as a Word table cell extracts) is trusted first. Only when there is none is a bare
    /// line near the top accepted — a heading that is itself a title. Working titles are skipped:
    /// they are the unit's name for the job, not its classification.
    ///
    /// Two different classes found at the same level is ambiguous, and returns no code: the admin is
    /// told which titles were seen, rather than the document being filed under either.
    /// </summary>
    public static (TitleCode? Code, string? Problem) StatedTitle(string text, TitleCodeIndex index)
    {
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Take(TitleSearchLines)
            .ToList();

        var labelled = new List<string>();
        var bare = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            foreach (var cell in lines[i].Split('\t', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var m = TitleLabel().Match(cell);
                if (m.Success)
                {
                    if (m.Groups["label"].Value.Contains("working", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = m.Groups["value"].Value.Trim();
                    if (value.Length == 0 && i + 1 < lines.Count)
                    {
                        value = lines[i + 1];
                    }

                    labelled.Add(value);
                }
                else
                {
                    bare.Add(cell);
                }
            }
        }

        foreach (var candidates in new[] { labelled, bare })
        {
            var found = candidates
                .Where(c => c.Length > 0 && c.Length <= MaxTitleLength)
                .Select(c => (Text: c, Code: index.FindTitleCode(c)))
                .Where(x => x.Code is not null)
                .ToList();
            var codes = found.Select(x => x.Code!).DistinctBy(c => c.Code).ToList();
            if (codes.Count == 1)
            {
                return (codes[0], null);
            }

            if (codes.Count > 1)
            {
                return (null, $"It names more than one UC job title ({string.Join(", ", found.Select(x => x.Text).Distinct())}), so it isn't clear which class it belongs to.");
            }
        }

        var shown = labelled.FirstOrDefault(l => l.Length > 0);
        return (null, shown is not null
            ? $"Its title “{shown}” doesn't match a UC job title, or matches more than one."
            : "No UC job title was found at the top of the document. Put the title first, as “Job Title: …”.");
    }

    /// <summary>
    /// The extractor's messages are written for Classify, which has a text box to paste into; this
    /// panel has none, so that suggestion is dropped.
    /// </summary>
    private static string WithoutPasteHint(string message) =>
        PasteHint().Replace(message, ".");

    [GeneratedRegex(@"(,| —) (or paste|try pasting) the text below\.$")]
    private static partial Regex PasteHint();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static UploadOutcome Failed(UploadOutcome outcome, string error)
    {
        outcome.Result = "failed";
        outcome.Error = error;
        return outcome;
    }
}

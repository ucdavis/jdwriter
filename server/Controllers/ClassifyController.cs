using Server.Helpers;
using Server.Core.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Ai;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Intake;
using Server.Core.Jd;
using Server.Core.Profiles;
using Server.Core.Titles;

namespace Server.Controllers;

/// <summary>
/// A unit already has a written description and wants to know where it classifies.
///
/// <paramref name="ProposedCode"/> arrives SEPARATELY from the text on purpose, and never reaches
/// the ranking prompt. A model shown the classification the unit wants ratifies it, which makes the
/// confidence number meaningless; it is argued afterwards by a second call instead.
/// </summary>
public sealed record ClassifyRequest(string Description, string? ProposedCode);

/// <summary>
/// Start a JD in <paramref name="Slug"/> from the description just classified. The distilled form is
/// the one the classify response returned, sent back as-is so the document is not read twice.
/// </summary>
public sealed record ClassifyStartRequest(string Slug, DistilledJd Distilled);

[ApiController]
[Route("api/classify")]
[Authorize(Roles = AppRoles.Author)]
public class ClassifyController : ApiControllerBase
{
    private const long MaxUploadBytes = 20 * 1024 * 1024;

    private readonly IDescriptionClassifier _classifier;
    private readonly IClassProfileRepository _profiles;
    private readonly ITitleCodeService _titleCodes;
    private readonly IStructuredLlm _llm;
    private readonly ClassifySubmissions _submissions;
    private readonly IFitRewriter _rewriter;
    private readonly AuthoredJdStore _saved;
    private readonly AppDbContext _db;
    private readonly ILogger<ClassifyController> _logger;

    public ClassifyController(
        IDescriptionClassifier classifier,
        IClassProfileRepository profiles,
        ITitleCodeService titleCodes,
        IStructuredLlm llm,
        ClassifySubmissions submissions,
        IFitRewriter rewriter,
        AuthoredJdStore saved,
        AppDbContext db,
        ILogger<ClassifyController> logger)
    {
        _classifier = classifier;
        _profiles = profiles;
        _titleCodes = titleCodes;
        _llm = llm;
        _submissions = submissions;
        _rewriter = rewriter;
        _saved = saved;
        _db = db;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Classify(ClassifyRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Description))
        {
            return BadRequest(new { message = "Paste or upload a description before classifying." });
        }

        if (!_llm.HasApiKey)
        {
            return StatusCode(503, new { message = "Classification is unavailable — no API key is configured." });
        }

        var profiles = await _profiles.GetAllAsync(ct);
        var result = await _classifier.ClassifyAsync(body.Description, profiles, body.ProposedCode, ct);

        // Every submission is filed into the corpus. Filing is bookkeeping, so it must never cost
        // the person their classification: a failure is logged and the result returned regardless.
        try
        {
            result.FiledUnder = await _submissions.FileAsync(body.Description, result, await User.IdAsync(_db, ct), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not file a classify submission into the corpus.");
        }

        return Ok(result);
    }

    /// <summary>
    /// Begin a JD in a recommended class from the description that was classified: the class's
    /// standard as the frame, the description's own work merged into it. The same mapping as
    /// "Rewrite to fit" — one model call that places the description's work in the class's functions
    /// by index; the text is the standard's and the description's, the % time is computed — saved as
    /// a draft the author opens in the build screen, where the envelope check polices what came in.
    /// </summary>
    [HttpPost("start-jd")]
    public async Task<IActionResult> StartJd(ClassifyStartRequest body, CancellationToken ct)
    {
        var problem = Validate(body.Distilled);
        if (problem is not null)
        {
            return BadRequest(new { message = problem });
        }

        var profile = await _profiles.GetBySlugAsync(body.Slug, ct);
        if (profile is null)
        {
            return NotFound(new { message = $"No class found for “{body.Slug}”." });
        }

        if (!_llm.HasApiKey)
        {
            return StatusCode(503, new { message = "Starting a JD from a description is unavailable — no AI provider is configured." });
        }

        FitRewrite rewrite;
        try
        {
            rewrite = await _rewriter.RewriteAsync(profile, ClassifySubmissions.ToJobDescription(body.Distilled), RewriteMode.KeepSpecifics, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (StructuredLlmException ex)
        {
            return StatusCode(502, new { message = ex.Message });
        }

        var id = await _saved.SaveDraftAsync(rewrite.Inputs, profile, await User.IdAsync(_db, ct), null, rewrite.DraftState, ct);
        return Ok(new
        {
            authoredJdId = id,
            slug = profile.Slug,
            title = profile.Title,
            rewrite.KeptFunctions,
            rewrite.DroppedFunctions,
            rewrite.CarriedDuties,
            rewrite.MatchedDuties,
            rewrite.Outside,
        });
    }

    /// <summary>
    /// The distilled JD comes back from the client, so it is bounded here before any of it reaches a
    /// prompt: a real description has a handful of functions with a handful of duties each.
    /// </summary>
    private static string? Validate(DistilledJd? d)
    {
        if (d is null || d.Functions.Count == 0)
        {
            return "Classify a description first — there is nothing to start a JD from.";
        }

        if (d.Functions.Count > 40 || d.Functions.Any(f => f.Duties.Count > 60))
        {
            return "That description is too large to start a JD from.";
        }

        var texts = d.Functions.SelectMany(f => f.Duties.Prepend(f.Name)).Append(d.WorkingTitle).Append(d.Summary);
        return texts.Any(t => (t ?? "").Length > 4000) ? "That description is too large to start a JD from." : null;
    }

    /// <summary>
    /// Pull text out of an uploaded document. Deterministic — never a model call — and it
    /// deliberately does NOT classify: the text lands in the textarea so a mangled PDF can be fixed
    /// before it costs an API call.
    /// </summary>
    [HttpPost("extract")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Extract(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Choose a file to upload." });
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        Extraction extraction;
        try
        {
            extraction = TextExtractor.Extract(ms.ToArray(), file.FileName, file.ContentType ?? "");
        }
        catch (ExtractionException ex)
        {
            // These messages name the remedy — convert the format, OCR the scan, paste the text —
            // so they are written for the user and surfaced verbatim.
            return BadRequest(new { message = ex.Message });
        }

        // Resolve the proposed code here rather than in the extractor, which is kept free of the
        // database so the whole dispatch layer stays testable without one.
        object? proposed = null;
        if (!string.IsNullOrEmpty(extraction.ProposedCode))
        {
            var index = await _titleCodes.GetAsync(ct);
            var hit = index.FindByCode(extraction.ProposedCode);
            var padded = TitleCodeIndex.Pad(extraction.ProposedCode);
            var ingested = (await _profiles.GetAllAsync(ct))
                .Any(p => TitleCodeIndex.Pad(p.UcJobCode) == padded);

            // `ingested` is the difference between "we disagree with you" and "we could not have
            // agreed": the classifier can only return classes it has ingested.
            proposed = new { code = extraction.ProposedCode, title = hit?.Title ?? "", ingested };
        }

        return Ok(new
        {
            text = extraction.Text,
            kind = extraction.Kind.ToString().ToLowerInvariant(),
            filename = extraction.Filename,
            note = extraction.Note,
            proposed,
        });
    }
}

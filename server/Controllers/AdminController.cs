using Server.Helpers;
using Server.Core.Ai;
using Server.Core.Data;
using Server.Core.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Profiles;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Controllers;

public sealed record IngestClassRequest(string Code, string? CorpusDir);
/// <summary>A class to bootstrap. <c>AllowNotActive</c> permits a job not active at UC Davis, by explicit choice only.</summary>
public sealed record BootstrapRequest(string Title, bool AllowNotActive = false);
public sealed record AdminGrantRequest(string LoginId);
public sealed record ApiKeyRequest(string Key);
public sealed record UploadIngestRequest(string Code);

public sealed record QualificationRulesRequest(bool Apply);

/// <summary>Whether a standard is a job code: <c>Match</c> true confirms it, false rejects the pairing.</summary>
public sealed record StandardMatchRequest(string StandardTitle, string UcJobCode, bool Match);

public sealed record StandardUnmatchRequest(string StandardTitle);

public sealed record ManualStandardRequest(string UcJobCode, StandardDraft Standard);

/// <summary>
/// Corpus and standards operations.
///
/// Ingest reads HRTMS exports from a directory, which exists on a developer's machine and not in a
/// deployed environment — hence the explicit configuration and the clear failure when it is absent,
/// rather than a confusing empty result. Bulk loading is the CLI's job (tools/jdw-cli); these
/// endpoints are for ingesting one class at a time from the backend screens.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = AppRoles.Admin)]
public class AdminController : ApiControllerBase
{
    private readonly IngestPipeline _pipeline;
    private readonly IBootstrapper _bootstrapper;
    private readonly ISupersessionReconciler _supersessions;
    private readonly IStandardsStore _standards;
    private readonly StandardsImporter _standardsImporter;
    private readonly IClassProfileRepository _profiles;
    private readonly ITitleCodeService _titleCodes;
    private readonly IConfiguration _config;
    private readonly AdminAccess _admins;
    private readonly AppDbContext _db;
    private readonly ApiKeySettings _apiKey;
    private readonly CorpusUploads _uploads;
    private readonly CorpusDocuments _documents;
    private readonly QualificationRulesPass _qualificationRules;
    private readonly StandardMatching _matching;
    private readonly JobsDirectory _jobs;
    private readonly ManualStandards _manualStandards;
    private readonly BlankStandardClasses _blankClasses;
    private readonly DatabaseSecurity _security;
    private readonly Server.Core.Analytics.AdminAnalytics _analytics;

    public AdminController(
        IngestPipeline pipeline,
        IBootstrapper bootstrapper,
        ISupersessionReconciler supersessions,
        IStandardsStore standards,
        StandardsImporter standardsImporter,
        IClassProfileRepository profiles,
        ITitleCodeService titleCodes,
        IConfiguration config,
        AdminAccess admins,
        AppDbContext db,
        ApiKeySettings apiKey,
        CorpusUploads uploads,
        CorpusDocuments documents,
        QualificationRulesPass qualificationRules,
        StandardMatching matching,
        JobsDirectory jobs,
        ManualStandards manualStandards,
        BlankStandardClasses blankClasses,
        DatabaseSecurity security,
        Server.Core.Analytics.AdminAnalytics analytics)
    {
        _pipeline = pipeline;
        _bootstrapper = bootstrapper;
        _supersessions = supersessions;
        _standards = standards;
        _standardsImporter = standardsImporter;
        _profiles = profiles;
        _titleCodes = titleCodes;
        _config = config;
        _admins = admins;
        _db = db;
        _apiKey = apiKey;
        _uploads = uploads;
        _documents = documents;
        _qualificationRules = qualificationRules;
        _matching = matching;
        _jobs = jobs;
        _manualStandards = manualStandards;
        _blankClasses = blankClasses;
        _security = security;
        _analytics = analytics;
    }

    /// <summary>Usage and class analytics for the admin dashboard.</summary>
    [HttpGet("analytics")]
    public async Task<IActionResult> Analytics(CancellationToken ct) =>
        Ok(await _analytics.BuildAsync(DateTimeOffset.UtcNow, ct));

    /// <summary>Whether the database is encrypted at rest, as SQL Server reports it.</summary>
    [HttpGet("settings/security")]
    public async Task<IActionResult> Security(CancellationToken ct) =>
        Ok(new { databaseEncryptedAtRest = await _security.EncryptedAtRestAsync(ct) });

    // ---------------------------------------------------------------- uploaded exports

    /// <summary>Upload ceiling for one request: a large class's exports, with headroom.</summary>
    private const long MaxUploadBytes = 200L * 1024 * 1024;

    /// <summary>
    /// Store and parse HRTMS exports. The response reports every file — added, duplicate, or
    /// failed with a reason — so one bad file never hides what happened to the rest.
    /// </summary>
    [HttpPost("uploads")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes, ValueCountLimit = 10_000)]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        var files = await UploadsAsync(MaxUploadBytes, ct);
        if (files.Count == 0)
        {
            return BadRequest(new { message = "Choose one or more HRTMS export files (.html) to upload." });
        }

        List<(string Name, byte[] Bytes)> read = [.. files.Select(f => (f.Name, f.Bytes))];
        var outcomes = await _uploads.UploadAsync(read, await User.IdAsync(_db, ct), ct);
        return Ok(new { files = outcomes });
    }

    /// <summary>Ceiling for one document: the extractor's own limit, plus multipart overhead.</summary>
    private const long MaxDocumentBytes = 21L * 1024 * 1024;

    /// <summary>
    /// Add one JD from a unit outside the college, from a Word, PDF or text copy. ONE document per
    /// request: each is a model call, so the client sends a folder file by file and shows each
    /// verdict as it lands, rather than letting one slow file time out the whole folder.
    /// </summary>
    [HttpPost("uploads/documents")]
    [RequestSizeLimit(MaxDocumentBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxDocumentBytes)]
    public async Task<IActionResult> ImportDocument(CancellationToken ct)
    {
        var file = (await UploadsAsync(MaxDocumentBytes, ct)).FirstOrDefault();
        if (file is null || file.Bytes.Length == 0)
        {
            return BadRequest(new { message = "Choose a job description document (.docx, .pdf or .txt) to add." });
        }

        var outcome = await _documents.ImportAsync(
            file.Name, file.Bytes, file.ContentType, await User.IdAsync(_db, ct), ct);
        return Ok(outcome);
    }

    /// <summary>
    /// Apply the education rules to every existing envelope: a preferred degree moves to preferred
    /// qualifications, and a degree reads "or equivalent experience". <c>apply: false</c> previews.
    /// </summary>
    [HttpPost("envelopes/qualification-rules")]
    public async Task<IActionResult> QualificationRules(QualificationRulesRequest body, CancellationToken ct) =>
        Ok(await _qualificationRules.RunAsync(body.Apply, ct));

    /// <summary>
    /// Re-read every imported Word document with the layout parser: verbatim records like in-unit
    /// exports; incomplete ones discarded; unrecognized layouts left as they were.
    /// </summary>
    [HttpPost("uploads/documents/reparse")]
    public async Task<IActionResult> ReparseDocuments(CancellationToken ct) =>
        Ok(await _documents.ReparseAsync(ct));

    [HttpGet("uploads/pending")]
    public async Task<IActionResult> UploadedPending(CancellationToken ct) =>
        Ok(new { classes = await _uploads.PendingAsync(ct) });

    /// <summary>
    /// Add one class's uploaded exports to the corpus and rebuild its envelope. Several model
    /// calls; the client runs classes one at a time.
    /// </summary>
    [HttpPost("uploads/ingest")]
    public async Task<IActionResult> IngestUploaded(UploadIngestRequest body, CancellationToken ct)
    {
        try
        {
            var profile = await _uploads.IngestAsync(body.Code, ct);
            return Ok(new { profile.Slug, profile.Title, profile.UcJobCode, profile.CorpusSize, profile.EnvelopeSource });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ---------------------------------------------------------------- Anthropic API key

    /// <summary>Which key is in use and where it came from. Never the key itself.</summary>
    [HttpGet("settings/api-key")]
    public async Task<IActionResult> ApiKeyStatus(CancellationToken ct) =>
        Ok(await _apiKey.GetStatusAsync(ct));

    /// <summary>
    /// Store a key entered by an admin, encrypted and audited, after checking it with the active provider. Write-only:
    /// the response is the status (last four characters), never the key.
    /// </summary>
    [HttpPut("settings/api-key")]
    public async Task<IActionResult> SetApiKey(ApiKeyRequest body, CancellationToken ct)
    {
        try
        {
            await _apiKey.SetAsync(body.Key, await User.IdAsync(_db, ct), ct);
            return Ok(await _apiKey.GetStatusAsync(ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyEntryDisabledException ex)
        {
            // This environment manages keys in Key Vault only.
            return StatusCode(403, new { message = ex.Message });
        }
    }

    /// <summary>Remove the app-entered key; the configured key, if any, takes over.</summary>
    [HttpDelete("settings/api-key")]
    public async Task<IActionResult> ClearApiKey(CancellationToken ct)
    {
        await _apiKey.ClearAsync(await User.IdAsync(_db, ct), ct);
        return Ok(await _apiKey.GetStatusAsync(ct));
    }

    // ---------------------------------------------------------------- admin whitelist

    [HttpGet("admins")]
    public async Task<IActionResult> Admins(CancellationToken ct) =>
        Ok(new { admins = await _admins.ListAsync(ct) });

    /// <summary>
    /// Whitelist a campus login. Takes effect on that person's next request — they need not have
    /// signed in before, and need not sign out and back in after.
    /// </summary>
    [HttpPost("admins")]
    public async Task<IActionResult> GrantAdmin(AdminGrantRequest body, CancellationToken ct)
    {
        try
        {
            var login = await _admins.GrantAsync(body.LoginId, await User.IdAsync(_db, ct), ct);
            return Ok(new { loginId = login });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("admins/{loginId}")]
    public async Task<IActionResult> RevokeAdmin(string loginId, CancellationToken ct)
    {
        try
        {
            await _admins.RevokeAsync(loginId, Server.Services.UserService.LoginIdOf(User), ct);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Where the HRTMS exports live. Configured via <c>Corpus:Directory</c>, overridable per
    /// request so an analyst can point at a freshly exported batch.
    /// </summary>
    /// <remarks>
    /// The override must sit inside the configured directory. Taken as-is, it let an admin point the
    /// reader at any folder the server can read.
    /// </remarks>
    private string? ResolveCorpusDir(string? fromRequest)
    {
        var configured = _config["Corpus:Directory"];
        if (string.IsNullOrWhiteSpace(fromRequest) || string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var root = Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var requested = Path.GetFullPath(fromRequest).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return requested.StartsWith(root, StringComparison.Ordinal) ? requested : null;
    }

    [HttpGet("ingest/pending")]
    public async Task<IActionResult> Pending([FromQuery] string? corpusDir, CancellationToken ct)
    {
        var dir = ResolveCorpusDir(corpusDir);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            // Normal in a deployed environment, which has no export directory: uploads are the
            // path there. Reported as a flag, not an error, so the panel can simply step aside.
            return Ok(new { configured = false, pending = Array.Empty<PendingClass>() });
        }

        return Ok(new { configured = true, pending = await _pipeline.PendingClassesAsync(dir, ct) });
    }

    /// <summary>
    /// Ingest one class: parse, consolidate, synthesize an envelope, and compute coverage. Slow and
    /// expensive — several model calls — which is why it is per-class rather than a bulk button.
    /// </summary>
    [HttpPost("ingest/class")]
    public async Task<IActionResult> IngestClass(IngestClassRequest body, CancellationToken ct)
    {
        var dir = ResolveCorpusDir(body.CorpusDir);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return BadRequest(new { message = "No corpus directory is configured." });
        }

        if (string.IsNullOrWhiteSpace(body.Code))
        {
            return BadRequest(new { message = "A UC job code is required." });
        }

        var profile = await _pipeline.IngestClassAsync(dir, body.Code, ct);
        return Ok(new { profile.Slug, profile.Title, profile.UcJobCode, profile.CorpusSize });
    }

    /// <summary>
    /// Upload Job Builder standards workbooks and merge them into the store: each standard is added
    /// or replaces the one with the same exact title; nothing else is touched.
    /// </summary>
    [HttpPost("standards/upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes, ValueCountLimit = 10_000)]
    public async Task<IActionResult> UploadStandards(CancellationToken ct)
    {
        var files = await UploadsAsync(MaxUploadBytes, ct);
        if (files.Count == 0)
        {
            return BadRequest(new { message = "Choose one or more Job Builder standards workbooks (.xlsx) to upload." });
        }

        List<(string Name, byte[] Bytes)> read = [.. files.Select(f => (f.Name, f.Bytes))];

        var report = await _standardsImporter.MergeAsync(read, await User.IdAsync(_db, ct), ct);
        var index = await _standards.GetIndexAsync(ct);
        var classes = await _profiles.GetDescriptorsAsync(ct: ct);
        return Ok(new
        {
            report.Files,
            report.Added,
            report.Updated,
            report.Total,
            UncodedSample = report.Uncoded.Take(8),
            LinkedCount = classes.Count(c => index.For(c.Title, c.UcJobCode) != null),
            TotalClasses = classes.Count,
        });
    }

    /// <summary>
    /// Rebuild the standards table from the Job Builder workbooks in <c>Standards:Directory</c>,
    /// then report how many ingested classes now link to a standard — so the analyst can see the
    /// match worked without reloading every page.
    /// </summary>
    [HttpPost("standards/ingest")]
    public async Task<IActionResult> IngestStandards(CancellationToken ct)
    {
        var dir = _config["Standards:Directory"];
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return BadRequest(new
            {
                message = "No standards directory is configured. Set Standards:Directory to the "
                          + "folder of Job Builder workbooks.",
            });
        }

        var report = await _standardsImporter.ImportAsync(dir, ct);
        var index = await _standards.GetIndexAsync(ct);
        var classes = await _profiles.GetDescriptorsAsync(ct: ct);
        var linked = classes.Where(c => index.For(c.Title, c.UcJobCode) != null).ToList();

        return Ok(new
        {
            report.Count,
            report.Coded,
            // Named rather than counted: "not a UC Davis title" and "ambiguous" need different
            // follow-up, and a silent gap in job codes is exactly what went unnoticed before.
            UncodedSample = report.Ambiguous.Concat(report.Unknown).Take(8),
            AmbiguousCount = report.Ambiguous.Count,
            Sample = report.Titles.Take(8),
            TotalClasses = classes.Count,
            LinkedCount = linked.Count,
            LinkedSample = linked.Take(6).Select(c => c.Title),
        });
    }

    /// <summary>
    /// Classes that could be created from an official standard alone, for families with no ingested
    /// JDs yet. Superseded codes are excluded — bootstrapping one would create a class under a dead
    /// classification.
    /// </summary>
    [HttpGet("bootstrap/candidates")]
    public async Task<IActionResult> Candidates(CancellationToken ct)
    {
        var result = await _bootstrapper.GetCandidatesAsync(ct);
        return Ok(new { candidates = result.Candidates, result.NotOnPayroll, result.NoCodeMatch, result.Blank });
    }

    /// <summary>
    /// Every UC job code and what it is attached to: its class, and its standard (by title or by
    /// hand). <c>scope</c> is "active" (on payroll, the default), "inactive", or "all".
    /// </summary>
    [HttpGet("jobs")]
    public async Task<IActionResult> Jobs([FromQuery] string scope = "active", CancellationToken ct = default) =>
        Ok(new { scope, jobs = await _jobs.ListAsync(scope, ct) });

    /// <summary>
    /// Standards whose title matches no job code exactly, each with up to three codes it might be,
    /// and the matches already confirmed by hand. Codes not active at UC Davis only on request.
    /// </summary>
    [HttpGet("standards/matches")]
    public async Task<IActionResult> StandardMatches([FromQuery] bool includeNotActive = false, CancellationToken ct = default) =>
        Ok(await _matching.ReviewAsync(includeNotActive, ct));

    /// <summary>Confirm or reject one standard–code pairing.</summary>
    [HttpPost("standards/matches")]
    public async Task<IActionResult> DecideStandardMatch(StandardMatchRequest body, CancellationToken ct)
    {
        try
        {
            await _matching.DecideAsync(body.StandardTitle, body.UcJobCode, body.Match, await User.IdAsync(_db, ct), ct);
            return Ok(new { ok = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Undo a confirmed match; the standard returns to the review.</summary>
    [HttpPost("standards/matches/undo")]
    public async Task<IActionResult> UndoStandardMatch(StandardUnmatchRequest body, CancellationToken ct)
    {
        await _matching.UnmatchAsync(body.StandardTitle, ct);
        return Ok(new { ok = true });
    }

    /// <summary>Starter classes built from blank standards, with any saved JDs against them. Read-only.</summary>
    [HttpGet("standards/blank-classes")]
    public async Task<IActionResult> BlankStandardClassesPreview(CancellationToken ct) =>
        Ok(new { classes = await _blankClasses.PreviewAsync(ct) });

    /// <summary>Remove starter classes built from blank standards; any with saved JDs are kept and reported.</summary>
    [HttpPost("standards/blank-classes/remove")]
    public async Task<IActionResult> RemoveBlankStandardClasses(CancellationToken ct) =>
        Ok(await _blankClasses.RemoveAsync(ct));

    /// <summary>Read a Word (or PDF, or text) export of a job standard into its sections, for review.</summary>
    [HttpPost("standards/read")]
    [RequestSizeLimit(MaxDocumentBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxDocumentBytes)]
    public async Task<IActionResult> ReadStandard(CancellationToken ct)
    {
        var file = (await UploadsAsync(MaxDocumentBytes, ct)).FirstOrDefault();
        if (file is null || file.Bytes.Length == 0)
        {
            return BadRequest(new { message = "Choose the standard's Word document." });
        }

        if (!_manualStandards.CanRead)
        {
            return StatusCode(503, new { message = "Reading a standard is unavailable — no AI provider is configured." });
        }

        try
        {
            var text = TextExtractor.Extract(file.Bytes, file.Name, file.ContentType).Text;
            return Ok(await _manualStandards.ReadAsync(text, ct));
        }
        catch (ExtractionException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Save a standard for a job, entered or reviewed by hand; it is linked to the job's code.</summary>
    [HttpPost("standards/manual")]
    public async Task<IActionResult> SaveManualStandard(ManualStandardRequest body, CancellationToken ct)
    {
        try
        {
            await _manualStandards.SaveAsync(body.UcJobCode, body.Standard, await User.IdAsync(_db, ct), ct);
            return Ok(new { ok = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap(BootstrapRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Title))
        {
            return BadRequest(new { message = "A class title is required." });
        }

        try
        {
            var profile = body.AllowNotActive
                ? await _bootstrapper.BootstrapNotActiveAsync(body.Title, ct)
                : await _bootstrapper.BootstrapAsync(body.Title, ct);
            return Ok(new { profile.Slug, profile.Title, profile.UcJobCode, profile.EnvelopeSource });
        }
        catch (InvalidOperationException ex)
        {
            // The bootstrapper refuses superseded codes by throwing and naming the successor, which
            // is precisely what the analyst needs to see.
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Profiles still filed under a code a union successor has superseded, and what retiring each
    /// would do. Read-only.
    /// </summary>
    [HttpGet("supersessions/retire")]
    public async Task<IActionResult> RetirementPreview(CancellationToken ct) =>
        Ok(await _supersessions.PreviewAsync(ct));

    /// <summary>
    /// Retire every profile under a superseded code: refile its corpus JDs to the successor, then
    /// merge it into the successor's profile, re-identify it as the successor, or remove it if it
    /// is a standard-only class with nothing attached. Idempotent.
    /// </summary>
    [HttpPost("supersessions/retire")]
    public async Task<IActionResult> Retire(CancellationToken ct) =>
        Ok(await _supersessions.RetireAsync(ct));

    /// <summary>
    /// Drop the cached reference data. Needed after a CLI import, because the caches key on cheap
    /// stamps that cannot see an in-place edit.
    /// </summary>
    [HttpPost("caches/invalidate")]
    public IActionResult InvalidateCaches()
    {
        _standards.Invalidate();
        _titleCodes.Invalidate();
        return Ok(new { ok = true });
    }
}

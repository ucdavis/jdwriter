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
public sealed record BootstrapRequest(string Title);
public sealed record AdminGrantRequest(string LoginId);
public sealed record ApiKeyRequest(string Key);
public sealed record UploadIngestRequest(string Code);

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
    private readonly IStandardsStore _standards;
    private readonly StandardsImporter _standardsImporter;
    private readonly IClassProfileRepository _profiles;
    private readonly ITitleCodeService _titleCodes;
    private readonly IConfiguration _config;
    private readonly AdminAccess _admins;
    private readonly AppDbContext _db;
    private readonly ApiKeySettings _apiKey;
    private readonly CorpusUploads _uploads;
    private readonly DatabaseSecurity _security;

    public AdminController(
        IngestPipeline pipeline,
        IBootstrapper bootstrapper,
        IStandardsStore standards,
        StandardsImporter standardsImporter,
        IClassProfileRepository profiles,
        ITitleCodeService titleCodes,
        IConfiguration config,
        AdminAccess admins,
        AppDbContext db,
        ApiKeySettings apiKey,
        CorpusUploads uploads,
        DatabaseSecurity security)
    {
        _pipeline = pipeline;
        _bootstrapper = bootstrapper;
        _standards = standards;
        _standardsImporter = standardsImporter;
        _profiles = profiles;
        _titleCodes = titleCodes;
        _config = config;
        _admins = admins;
        _db = db;
        _apiKey = apiKey;
        _uploads = uploads;
        _security = security;
    }

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
    public async Task<IActionResult> Upload([FromForm] List<IFormFile> files, CancellationToken ct)
    {
        if (files.Count == 0)
        {
            return BadRequest(new { message = "Choose one or more HRTMS export files (.html) to upload." });
        }

        var read = new List<(string Name, byte[] Bytes)>(files.Count);
        foreach (var file in files)
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            read.Add((file.FileName, buffer.ToArray()));
        }

        var outcomes = await _uploads.UploadAsync(read, await User.IdAsync(_db, ct), ct);
        return Ok(new { files = outcomes });
    }

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
    /// Store a key entered by an admin, encrypted, after checking it with Anthropic. Write-only:
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
    }

    /// <summary>Remove the app-entered key; the configured key, if any, takes over.</summary>
    [HttpDelete("settings/api-key")]
    public async Task<IActionResult> ClearApiKey(CancellationToken ct)
    {
        await _apiKey.ClearAsync(ct);
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
    private string? ResolveCorpusDir(string? fromRequest) =>
        !string.IsNullOrWhiteSpace(fromRequest) ? fromRequest : _config["Corpus:Directory"];

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
    public async Task<IActionResult> UploadStandards([FromForm] List<IFormFile> files, CancellationToken ct)
    {
        if (files.Count == 0)
        {
            return BadRequest(new { message = "Choose one or more Job Builder standards workbooks (.xlsx) to upload." });
        }

        var read = new List<(string Name, byte[] Bytes)>(files.Count);
        foreach (var file in files)
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            read.Add((file.FileName, buffer.ToArray()));
        }

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
            LinkedCount = classes.Count(c => index.ForTitle(c.Title) != null),
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
        var linked = classes.Where(c => index.ForTitle(c.Title) != null).ToList();

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
    public async Task<IActionResult> Candidates(CancellationToken ct) =>
        Ok(new { candidates = await _bootstrapper.GetCandidatesAsync(ct) });

    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap(BootstrapRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Title))
        {
            return BadRequest(new { message = "A class title is required." });
        }

        try
        {
            var profile = await _bootstrapper.BootstrapAsync(body.Title, ct);
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

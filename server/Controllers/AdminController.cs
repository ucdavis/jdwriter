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

    public AdminController(
        IngestPipeline pipeline,
        IBootstrapper bootstrapper,
        IStandardsStore standards,
        StandardsImporter standardsImporter,
        IClassProfileRepository profiles,
        ITitleCodeService titleCodes,
        IConfiguration config)
    {
        _pipeline = pipeline;
        _bootstrapper = bootstrapper;
        _standards = standards;
        _standardsImporter = standardsImporter;
        _profiles = profiles;
        _titleCodes = titleCodes;
        _config = config;
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
            return BadRequest(new
            {
                message = "No corpus directory is configured. Set Corpus:Directory, or pass one on "
                          + "the request — this environment may simply not have the HRTMS exports.",
            });
        }

        return Ok(new { pending = await _pipeline.PendingClassesAsync(dir, ct) });
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

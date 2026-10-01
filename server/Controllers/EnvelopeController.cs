using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Ai;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Profiles;

namespace Server.Controllers;

public sealed record EnvelopeSaveRequest(string Slug, EnvelopeWire Envelope);
public sealed record EnvelopeCoverageRequest(string Slug, EnvelopeWire Envelope);

/// <summary>
/// Envelope curation — the analyst half of the product. An envelope is the template every author in
/// the class writes against, so changing one changes what the whole campus produces.
/// </summary>
[ApiController]
[Route("api/envelope")]
[Authorize(Roles = AppRoles.Analyst)]
public class EnvelopeController : ApiControllerBase
{
    private readonly AppDbContext _db;
    private readonly IClassProfileRepository _profiles;
    private readonly EnvelopeCoverageChecker _coverage;
    private readonly IStructuredLlm _llm;

    public EnvelopeController(
        AppDbContext db,
        IClassProfileRepository profiles,
        EnvelopeCoverageChecker coverage,
        IStructuredLlm llm)
    {
        _db = db;
        _profiles = profiles;
        _coverage = coverage;
        _llm = llm;
    }

    [HttpPost("save")]
    public async Task<IActionResult> Save(EnvelopeSaveRequest body, CancellationToken ct)
    {
        var profile = await _db.ClassProfiles
            .Include(p => p.Envelope!).ThenInclude(e => e.KeyResponsibilities).ThenInclude(r => r.Duties)
            .Include(p => p.Envelope!).ThenInclude(e => e.Items)
            .FirstOrDefaultAsync(p => p.Slug == body.Slug, ct);

        if (profile is null)
        {
            return NotFound(new { message = $"No class found for “{body.Slug}”." });
        }

        // Ported from the POC's save route. An envelope is the standard every author in the class
        // starts from, so it is never valid off 100% — refused here however it was submitted,
        // rather than trusting the editor's disabled button.
        var total = body.Envelope.PctTotal();
        if (total != 100)
        {
            return BadRequest(new { message = $"Key Responsibilities must total 100% (got {total}%)." });
        }

        // Replace wholesale rather than diffing. The editor is affirmative and subtractive — the
        // analyst has decided the whole shape — and a partial merge would silently resurrect blocks
        // they deliberately dropped.
        if (profile.Envelope is not null)
        {
            _db.JobEnvelopes.Remove(profile.Envelope);
        }

        var envelope = body.Envelope.ToEntity();
        envelope.ClassProfileId = profile.Id;
        _db.JobEnvelopes.Add(envelope);

        // A hand-edited envelope is no longer the model's work, and the profile should say so —
        // an analyst reading it later needs to know where the words came from.
        profile.EnvelopeSource = EnvelopeSource.Manual;

        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true, slug = profile.Slug });
    }

    /// <summary>
    /// Does a CANDIDATE envelope still cover the class's real JDs?
    ///
    /// Takes the unsaved envelope on purpose. The question is whether an edit is safe to make, and
    /// checking the already-saved one answers a question nobody asked.
    /// </summary>
    [HttpPost("coverage")]
    public async Task<IActionResult> Coverage(EnvelopeCoverageRequest body, CancellationToken ct)
    {
        var profile = await _profiles.GetBySlugAsync(body.Slug, ct);
        if (profile is null)
        {
            return NotFound(new { message = $"No class found for “{body.Slug}”." });
        }

        if (!_llm.HasApiKey)
        {
            return StatusCode(503, new { message = "The coverage check is unavailable — no API key is configured." });
        }

        var jds = await _profiles.GetJobDescriptionsAsync(profile.UcJobCode, ct);
        var records = jds.Select(j => j.ToHrtmsRecord()).ToList();
        var report = await _coverage.CheckAsync(body.Envelope.ToEntity(), records, ct);
        return Ok(CoverageView.From(report));
    }
}

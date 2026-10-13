using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Titles;
using Server.Helpers;

namespace Server.Controllers;

public sealed record CodeActiveRequest(bool Active);

/// <summary>UC job codes: an admin's call on whether one is active at UC Davis.</summary>
[ApiController]
[Route("api/admin/codes")]
[Authorize(Roles = AppRoles.Admin)]
public class CodesController : ApiControllerBase
{
    /// <summary>The listing PDF is about a megabyte; this leaves room for a fuller one.</summary>
    private const long MaxListingBytes = 50L * 1024 * 1024;

    private readonly CodeActivity _activity;
    private readonly CtListingImport _ctListing;
    private readonly AppDbContext _db;

    public CodesController(CodeActivity activity, CtListingImport ctListing, AppDbContext db)
    {
        _activity = activity;
        _ctListing = ctListing;
        _db = db;
    }

    /// <summary>The Career Tracks listing as stored: how many codes, and when it was last imported.</summary>
    [HttpGet("ct-listing")]
    public async Task<IActionResult> CtListingStatus(CancellationToken ct)
    {
        var count = await _db.CtTitles.CountAsync(ct);
        return Ok(new { codes = count, importedAt = count == 0 ? (DateTimeOffset?)null : await _db.CtTitles.MaxAsync(t => t.ImportedAt, ct) });
    }

    /// <summary>What importing a Career Tracks title code listing would change. Writes nothing.</summary>
    [HttpPost("ct-listing/preview")]
    [RequestSizeLimit(MaxListingBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxListingBytes)]
    public Task<IActionResult> PreviewCtListing(IFormFile file, CancellationToken ct) =>
        RunCtListing(file, (rows, c) => _ctListing.PreviewAsync(rows, c), ct);

    /// <summary>Import the listing, as the preview described. The file is sent again: nothing is held between.</summary>
    [HttpPost("ct-listing/apply")]
    [RequestSizeLimit(MaxListingBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxListingBytes)]
    public async Task<IActionResult> ApplyCtListing(IFormFile file, CancellationToken ct)
    {
        var userId = await User.IdAsync(_db, ct);
        return await RunCtListing(file, (rows, c) => _ctListing.ApplyAsync(rows, userId, c), ct);
    }

    private async Task<IActionResult> RunCtListing(
        IFormFile? file, Func<List<CtListingRow>, CancellationToken, Task<CtListingPlan>> act, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Choose the listing report (PDF) to import." });
        }

        try
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            return Ok(await act(CtListingImport.Read(buffer.ToArray()), ct));
        }
        catch (CtListingRefusedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Mark a code active at UC Davis or not, over the payroll reference. Reports any supersession it changed.</summary>
    [HttpPost("{code}/active")]
    public async Task<IActionResult> SetActive(string code, CodeActiveRequest body, CancellationToken ct)
    {
        try
        {
            return Ok(await _activity.SetAsync(code, body.Active, await User.IdAsync(_db, ct), ct));
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}

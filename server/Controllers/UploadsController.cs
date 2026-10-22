using Azure;
using Azure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Domain;
using Server.Core.Uploads;
using Server.Helpers;

namespace Server.Controllers;

/// <summary>
/// Upload links. Before sending files, the client asks for one link per file; it puts each file there
/// and names the blobs in the upload request. When staging is off it is told to send the files directly.
/// </summary>
[ApiController]
[Route("api/uploads")]
[Authorize(Roles = AppRoles.Author)]
public class UploadsController : ApiControllerBase
{
    private const int MaxSlots = 500;

    private readonly IUploadStaging _staging;
    private readonly ILogger<UploadsController> _logger;

    public UploadsController(IUploadStaging staging, ILogger<UploadsController> logger)
    {
        _staging = staging;
        _logger = logger;
    }

    public sealed class StageRequest
    {
        public int Count { get; set; } = 1;
    }

    [HttpPost("stage")]
    public async Task<IActionResult> Stage(StageRequest body, CancellationToken ct)
    {
        if (!_staging.Enabled)
        {
            return Ok(new { direct = true });
        }

        if (body.Count < 1 || body.Count > MaxSlots)
        {
            return BadRequest(new { message = $"Upload between 1 and {MaxSlots} files at a time." });
        }

        var owner = UploadReader.Owner(User);
        if (owner is null)
        {
            return Forbid();
        }

        var slots = new List<StagedSlot>(body.Count);
        try
        {
            for (var i = 0; i < body.Count; i++)
            {
                slots.Add(await _staging.StageAsync(owner, ct));
            }
        }
        catch (Exception ex) when (ex is RequestFailedException or AuthenticationFailedException)
        {
            // Most often the app's identity hasn't been granted Storage Blob Data Contributor yet
            // (docs/DEPLOYMENT.md, "Upload storage"). Logged with the cause; the user is told plainly.
            _logger.LogError(ex, "Upload storage refused to sign an upload link");
            return StatusCode(503, new
            {
                message = "Uploads aren't available right now: JDWriter can't reach its upload storage. Ask a JDWriter administrator to check the upload storage setup.",
            });
        }

        return Ok(new { slots = slots.Select(s => new { blob = s.BlobName, url = s.UploadUrl, s.ExpiresAt }) });
    }
}

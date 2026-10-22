using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Server.Core.Uploads;
using Server.Helpers;

namespace Server.Controllers;

// base controller for all Api controllers
[Authorize]
[ApiController]
[Route("api/[controller]")]
[StagedUploadRefused]
public class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// The files in an upload request, whether sent in it or staged in blob storage first (see
    /// <see cref="UploadReader"/>). A staged file that can't be used answers 400, with its reason.
    /// </summary>
    protected Task<List<UploadedFile>> UploadsAsync(long maxBytesEach, CancellationToken ct) =>
        HttpContext.RequestServices.GetRequiredService<UploadReader>().ReadAsync(Request, maxBytesEach, ct);
}

/// <summary>A staged upload that can't be used is the user's to fix: a 400 that says why.</summary>
public sealed class StagedUploadRefusedAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is StagedUploadException refused)
        {
            context.Result = new BadRequestObjectResult(new { message = refused.Message });
            context.ExceptionHandled = true;
        }
    }
}
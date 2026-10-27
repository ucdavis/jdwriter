using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Server.Core.Ai;
using Server.Core.Uploads;
using Server.Helpers;

namespace Server.Controllers;

// base controller for all Api controllers
[Authorize]
[ApiController]
[Route("api/[controller]")]
[StagedUploadRefused]
[AiServiceFailed]
public class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// The files in an upload request, whether sent in it or staged in blob storage first (see
    /// <see cref="UploadReader"/>). A staged file that can't be used answers 400, with its reason.
    /// </summary>
    protected Task<List<UploadedFile>> UploadsAsync(long maxBytesEach, CancellationToken ct) =>
        HttpContext.RequestServices.GetRequiredService<UploadReader>().ReadAsync(Request, maxBytesEach, ct);
}

/// <summary>
/// The AI provider refused or failed a call that its endpoint doesn't handle itself — a rejected
/// key, a spent quota, an outage. Logged with the provider's detail; the user is told plainly, rather
/// than shown a bare 500. Endpoints with their own wording catch it first.
/// </summary>
public sealed class AiServiceFailedAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is StructuredLlmBusyException busy)
        {
            context.HttpContext.RequestServices.GetRequiredService<ILogger<AiServiceFailedAttribute>>()
                .LogWarning(busy, "The AI provider was busy through the retries");
            context.Result = new ObjectResult(new
            {
                message = "The AI service is busy right now. Nothing was lost — try again in a minute.",
            })
            { StatusCode = StatusCodes.Status503ServiceUnavailable };
            context.ExceptionHandled = true;
            return;
        }

        if (context.Exception is StructuredLlmException failed)
        {
            context.HttpContext.RequestServices.GetRequiredService<ILogger<AiServiceFailedAttribute>>()
                .LogError(failed, "The AI provider failed a call");
            context.Result = new ObjectResult(new
            {
                message = "The AI service didn't return a usable answer, so this couldn't be completed. Try again in a moment; if it keeps happening, ask a JDWriter administrator to check the AI provider in Settings.",
            })
            { StatusCode = StatusCodes.Status502BadGateway };
            context.ExceptionHandled = true;
        }
    }
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
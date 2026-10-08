using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Server.Helpers;

/// <summary>
/// Browser-facing defences that every response or API call needs.
///
/// The auth cookie is SameSite=Lax, and "same site" means ucdavis.edu: a page on any *.ucdavis.edu
/// host — including the other CAES People apps on this one — counts as same-site, so Lax alone does
/// not stop it posting a form or a no-cors fetch with the user's cookie. The JSON endpoints already
/// refuse those bodies, but multipart uploads and body-less POSTs do not.
/// </summary>
public static class WebHardening
{
    /// <summary>The header JDWriter's own client sends on every API call.</summary>
    public const string ClientHeader = "X-Requested-With";

    /// <summary>
    /// Requests that change state must carry <see cref="ClientHeader"/>. A browser can attach a custom
    /// header from another origin only after a CORS preflight, and JDWriter allows none — so a forged
    /// cross-site request cannot carry it, while fetchJson always does.
    /// </summary>
    public static bool IsForgeable(HttpRequest request) =>
        request.Path.StartsWithSegments("/api")
        && !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        && !request.Headers.ContainsKey(ClientHeader);

    public static IApplicationBuilder UseApiRequestGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (IsForgeable(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { message = "This request did not come from JDWriter." });
                return;
            }

            await next();
        });

    /// <summary>
    /// Headers on every response. The enforced policy limits only what cannot break a page — who may
    /// frame it, where forms and the base URL may point, plugins. The full script/style policy is sent
    /// report-only until it has been checked in a browser; then it should be enforced.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var h = context.Response.Headers;
                h.XContentTypeOptions = "nosniff";
                h.XFrameOptions = "SAMEORIGIN";
                h["Referrer-Policy"] = "strict-origin-when-cross-origin";
                h.ContentSecurityPolicy =
                    "frame-ancestors 'self'; base-uri 'self'; object-src 'none'; form-action 'self' https://login.microsoftonline.com";
                h["Content-Security-Policy-Report-Only"] =
                    "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; "
                    + "img-src 'self' data: https://ucdcdn.blob.core.windows.net https://ucdcdn.azureedge.net; "
                    + "font-src 'self' data: https://ucdcdn.blob.core.windows.net https://ucdcdn.azureedge.net; "
                    + "connect-src 'self'; frame-ancestors 'self'; base-uri 'self'; object-src 'none'";
                return Task.CompletedTask;
            });
            await next();
        });

    /// <summary>The rate-limit policy on author-reachable endpoints that call the AI model or parse uploads.</summary>
    public const string ModelPolicy = "model";

    /// <summary>
    /// Every signed-in user is an Author, and each of these requests costs a model call or real CPU.
    /// Per user, a fixed window generous for real work (a classify-and-build session is a handful of
    /// calls) and tight enough that one account cannot run up the provider bill or tie up the server.
    /// Admin bulk operations (Create all, ingest) are not limited: they run one class per request by
    /// design and are admin-only.
    /// </summary>
    public static IServiceCollection AddJdWriterRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { message = "Too many requests in a short time — wait a few minutes and try again." }, ct);
            };
            options.AddPolicy(ModelPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.User.Identity?.Name
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 40,
                        Window = TimeSpan.FromMinutes(10),
                        QueueLimit = 0,
                    }));
        });
}

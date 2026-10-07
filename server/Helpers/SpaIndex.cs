using System.Text.Encodings.Web;

namespace Server.Helpers;

/// <summary>
/// Serves the SPA's index.html with a <c>&lt;base href&gt;</c> for the app's mount point.
///
/// JDWriter runs at the root of a host in development and under a path in production
/// (people.caes.ucdavis.edu/jdwriter, beside the other CAES People apps). The client is built with
/// relative asset URLs and reads its router base path and API prefix from the base element, so one
/// build serves any mount point and the mount point is configuration (<c>App:PathBase</c>), not a
/// build input.
/// </summary>
public static class SpaIndex
{
    /// <summary>"/jdwriter/", "jdwriter" and "/jdwriter" all mean "/jdwriter"; blank or "/" means the root ("").</summary>
    public static string NormalizePathBase(string? value)
    {
        var trimmed = (value ?? "").Trim().Trim('/');
        return trimmed.Length == 0 ? "" : "/" + trimmed;
    }

    /// <summary>Insert the base element first in &lt;head&gt;, before anything that resolves a relative URL.</summary>
    public static string InjectBase(string html, string pathBase)
    {
        var tag = $"<base href=\"{HtmlEncoder.Default.Encode(pathBase)}/\" />";
        var head = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        return head < 0 ? tag + html : html.Insert(head + "<head>".Length, tag);
    }

    /// <summary>
    /// The fallback endpoint for client routes. The base comes from the request's PathBase, so it is
    /// right whether the mount point was configured here or the app is reached at its own root.
    /// </summary>
    public static RequestDelegate Endpoint(IWebHostEnvironment environment, Action<HttpContext> noStore) =>
        async context =>
        {
            var file = environment.WebRootFileProvider.GetFileInfo("index.html");
            if (!file.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            string html;
            using (var reader = new StreamReader(file.CreateReadStream()))
            {
                html = await reader.ReadToEndAsync(context.RequestAborted);
            }

            noStore(context);
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(InjectBase(html, context.Request.PathBase.Value ?? ""), context.RequestAborted);
        };
}

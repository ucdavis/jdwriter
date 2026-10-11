using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Server.Tests.Api;

/// <summary>
/// Every API path the client calls exists on a controller.
///
/// The client's tests answer from MSW mocks, so a route the server lost goes unnoticed there: this
/// happened when an edit removed the standard-match endpoints while their panel and its tests kept
/// passing. This reads the client's source for "/api/..." paths and the controllers' route
/// attributes, and fails on any client path no route serves.
/// </summary>
public partial class ClientRoutesTests
{
    [GeneratedRegex(@"[`'""](/api/[^`'""\s]*)")]
    private static partial Regex ApiPath();

    [GeneratedRegex(@"\$\{[^}]*\}")]
    private static partial Regex Interpolation();

    private static string ClientSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "client", "src")))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? throw new InvalidOperationException("client/src not found above the test assembly.")
            : Path.Combine(dir.FullName, "client", "src");
    }

    /// <summary>"/api/jobs?scope=${scope}" → ["api", "jobs"]; "/api/jds/${id}" → ["api", "jds", "*"].</summary>
    private static string[] Segments(string path) =>
        Interpolation().Replace(path.Split('?')[0], "*").Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static List<string> ClientPaths()
    {
        var src = ClientSource();
        return Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".ts", StringComparison.Ordinal) || f.EndsWith(".tsx", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}mocks{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.EndsWith("routeTree.gen.ts", StringComparison.Ordinal))
            // Code only: a comment's example path ("'/api/x'") is not a call.
            .SelectMany(f => File.ReadLines(f)
                .Where(l => !l.TrimStart().StartsWith('*') && !l.TrimStart().StartsWith("//", StringComparison.Ordinal))
                .SelectMany(l => ApiPath().Matches(l).Select(m => m.Groups[1].Value)))
            // A path that ends in an open interpolation is built further on; keep only complete ones.
            .Where(p => !p.EndsWith("${", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static List<string[]> ServerRoutes()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);
        var routes = new List<string[]>();
        foreach (var c in controllers)
        {
            var name = c.Name.EndsWith("Controller", StringComparison.Ordinal) ? c.Name[..^"Controller".Length] : c.Name;
            // A controller may answer under more than one prefix.
            var prefixes = c.GetCustomAttributes<RouteAttribute>()
                .Select(r => r.Template.Replace("[controller]", name.ToLowerInvariant(), StringComparison.Ordinal))
                .DefaultIfEmpty("")
                .ToList();
            foreach (var prefix in prefixes)
            {
                foreach (var m in c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    foreach (var http in m.GetCustomAttributes<HttpMethodAttribute>())
                    {
                        var template = http.Template is null ? prefix : http.Template.StartsWith('/') ? http.Template : $"{prefix}/{http.Template}";
                        routes.Add(Regex.Replace(template, @"\{[^}]*\}", "*").Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries));
                    }
                }
            }
        }

        return routes;
    }

    private static bool Serves(string[] route, string[] path) =>
        route.Length == path.Length
        && route.Zip(path).All(p => p.First == "*" || p.Second == "*" || string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Every_api_path_the_client_calls_has_a_route()
    {
        var routes = ServerRoutes();
        var paths = ClientPaths();

        paths.Should().Contain("/api/admin/standards/matches", "the scan must actually find the client's paths");
        paths.Where(p => !routes.Any(r => Serves(r, Segments(p)))).Should().BeEmpty();
    }
}

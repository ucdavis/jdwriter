using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Domain;
using Server.Core.Package;

namespace Server.Controllers;

/// <summary>
/// The environment package: everything this environment has built, in one file, to carry to
/// another. It holds real JDs — departments, position numbers, reporting lines — so admins only.
/// </summary>
[ApiController]
[Route("api/admin/package")]
[Authorize(Roles = AppRoles.Admin)]
public class PackageController : ApiControllerBase
{
    private readonly EnvironmentPackages _packages;
    private readonly IWebHostEnvironment _env;

    public PackageController(EnvironmentPackages packages, IWebHostEnvironment env)
    {
        _packages = packages;
        _env = env;
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var bytes = await _packages.ExportAsync(ct);
        var name = $"jdwriter-{_env.EnvironmentName.ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyy-MM-dd-HHmm}.jdwpkg.json.gz";
        return File(bytes, "application/gzip", name);
    }
}

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
    /// <summary>A package of every class and JD, compressed, is tens of megabytes; this leaves room to grow.</summary>
    private const long MaxPackageBytes = 500L * 1024 * 1024;

    private readonly EnvironmentPackages _packages;
    private readonly PackageImporter _importer;
    private readonly IWebHostEnvironment _env;

    public PackageController(EnvironmentPackages packages, PackageImporter importer, IWebHostEnvironment env)
    {
        _packages = packages;
        _importer = importer;
        _env = env;
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var bytes = await _packages.ExportAsync(ct);
        var name = $"jdwriter-{_env.EnvironmentName.ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyy-MM-dd-HHmm}.jdwpkg.json.gz";
        return File(bytes, "application/gzip", name);
    }

    /// <summary>What importing the package would add, update, keep and skip here. Writes nothing.</summary>
    [HttpPost("preview")]
    [RequestSizeLimit(MaxPackageBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPackageBytes)]
    public Task<IActionResult> Preview(IFormFile file, CancellationToken ct) =>
        Run(file, (p, c) => _importer.PreviewAsync(p, c), ct);

    /// <summary>Import the package, as the preview described, in one transaction. The file is sent again: nothing is held between the two.</summary>
    [HttpPost("apply")]
    [RequestSizeLimit(MaxPackageBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPackageBytes)]
    public Task<IActionResult> Apply(IFormFile file, CancellationToken ct) =>
        Run(file, (p, c) => _importer.ApplyAsync(p, c), ct);

    private async Task<IActionResult> Run(IFormFile? file, Func<EnvironmentPackage, CancellationToken, Task<ImportPlan>> act, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Choose a package file to import." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var package = await _importer.ReadAsync(stream, ct);
            return Ok(await act(package, ct));
        }
        catch (PackageRefusedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

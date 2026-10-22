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
    private readonly PackageOptions _options;

    public PackageController(EnvironmentPackages packages, PackageImporter importer, IWebHostEnvironment env, PackageOptions options)
    {
        _packages = packages;
        _importer = importer;
        _env = env;
        _options = options;
    }

    /// <summary>Which packages this environment imports: real (prod), scrubbed (test), or both (development).</summary>
    [HttpGet("info")]
    public IActionResult Info() => Ok(_options);

    /// <summary>The package; <c>scrubbed</c> for the test environment, which never holds real JDs.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] bool scrubbed = false, CancellationToken ct = default)
    {
        var bytes = await _packages.ExportAsync(scrubbed, ct);
        var name = $"jdwriter-{_env.EnvironmentName.ToLowerInvariant()}{(scrubbed ? "-scrubbed" : "")}-{DateTimeOffset.UtcNow:yyyy-MM-dd-HHmm}.jdwpkg.json.gz";
        return File(bytes, "application/gzip", name);
    }

    /// <summary>What importing the package would add, update, keep and skip here. Writes nothing.</summary>
    [HttpPost("preview")]
    [RequestSizeLimit(MaxPackageBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPackageBytes)]
    public Task<IActionResult> Preview(CancellationToken ct) =>
        Run((p, c) => _importer.PreviewAsync(p, c), ct);

    /// <summary>Import the package, as the preview described, in one transaction. The file is sent again: nothing is held between the two.</summary>
    [HttpPost("apply")]
    [RequestSizeLimit(MaxPackageBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPackageBytes)]
    public Task<IActionResult> Apply(CancellationToken ct) =>
        Run((p, c) => _importer.ApplyAsync(p, c), ct);

    private async Task<IActionResult> Run(Func<EnvironmentPackage, CancellationToken, Task<ImportPlan>> act, CancellationToken ct)
    {
        var file = (await UploadsAsync(MaxPackageBytes, ct)).FirstOrDefault();
        if (file is null || file.Bytes.Length == 0)
        {
            return BadRequest(new { message = "Choose a package file to import." });
        }

        try
        {
            await using var stream = new MemoryStream(file.Bytes);
            var package = await _importer.ReadAsync(stream, ct);
            return Ok(await act(package, ct));
        }
        catch (PackageRefusedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

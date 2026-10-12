using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    private readonly CodeActivity _activity;
    private readonly AppDbContext _db;

    public CodesController(CodeActivity activity, AppDbContext db)
    {
        _activity = activity;
        _db = db;
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

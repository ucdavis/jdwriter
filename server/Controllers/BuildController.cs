using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Ai;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Jd;
using Server.Core.Profiles;
using Server.Helpers;

namespace Server.Controllers;

/// <summary>
/// The wire shape: which class, plus the author's selections.
///
/// Composed rather than inherited because BuildInputs is sealed, and that is the right call — it is
/// a domain input, not a DTO base. The slug lives beside it rather than inside it, so the domain
/// type never carries a routing concern.
/// </summary>
public sealed class BuildRequest
{
    public string Slug { get; set; } = "";

    public string WorkingTitle { get; set; } = "";
    public string Department { get; set; } = "";
    public List<JdKeyResponsibility> KeptResponsibilities { get; set; } = [];
    public List<string> KeptCerts { get; set; } = [];
    public List<string> KeptEducation { get; set; } = [];
    public List<string> KeptWorkExperience { get; set; } = [];
    public List<string> KeptMinKSA { get; set; } = [];
    public List<string> KeptPrefKSA { get; set; } = [];
    public List<string> KeptWorkEnvironment { get; set; } = [];
    public List<string> AddedItems { get; set; } = [];
    public string Notes { get; set; } = "";

    /// <summary>
    /// The saved JD this build session already wrote, from a previous assemble's response. When
    /// it is the caller's own, re-assembling updates it instead of saving another copy.
    /// </summary>
    public int? AuthoredJdId { get; set; }

    /// <summary>
    /// The envelope check's verdict from the check step this assembly followed. Decides, with the
    /// allocation and whether anything changed, whether the JD joins the class's corpus.
    /// </summary>
    public EnvelopeVerdict? EnvelopeVerdict { get; set; }

    public BuildInputs ToInputs() => new()
    {
        WorkingTitle = WorkingTitle,
        Department = Department,
        KeptResponsibilities = KeptResponsibilities,
        KeptCerts = KeptCerts,
        KeptEducation = KeptEducation,
        KeptWorkExperience = KeptWorkExperience,
        KeptMinKSA = KeptMinKSA,
        KeptPrefKSA = KeptPrefKSA,
        KeptWorkEnvironment = KeptWorkEnvironment,
        AddedItems = AddedItems,
        Notes = Notes,
    };
}

/// <summary>The guided build: envelope check, then assembly with its compliance audit trail.</summary>
[ApiController]
[Route("api/build")]
[Authorize(Roles = AppRoles.Author)]
public class BuildController : ApiControllerBase
{
    private readonly IJdAssembler _assembler;
    private readonly IClassProfileRepository _profiles;
    private readonly AppDbContext _db;
    private readonly IStructuredLlm _llm;
    private readonly AuthoredJdStore _saved;

    public BuildController(
        IJdAssembler assembler,
        IClassProfileRepository profiles,
        AppDbContext db,
        IStructuredLlm llm,
        AuthoredJdStore saved)
    {
        _assembler = assembler;
        _profiles = profiles;
        _db = db;
        _llm = llm;
        _saved = saved;
    }

    /// <summary>
    /// Do the author's additions still belong in this class?
    ///
    /// The check polices ADDITIONS only, and is skipped outright when there are none — an author
    /// who merely drops standard duties has not left the envelope, and charging them a model call
    /// to hear so would be both slow and pointless.
    /// </summary>
    [HttpPost("check")]
    public async Task<IActionResult> Check(BuildRequest body, CancellationToken ct)
    {
        var profile = await _profiles.GetBySlugAsync(body.Slug, ct);
        if (profile is null)
        {
            return NotFound(new { message = $"No class found for “{body.Slug}”." });
        }

        if (!_assembler.HasAdditions(body.ToInputs()))
        {
            return Ok(new EnvelopeCheck
            {
                Verdict = EnvelopeVerdict.InEnvelope,
                Rationale = "Nothing was added, so this stays inside the class envelope.",
            });
        }

        if (!_llm.HasApiKey)
        {
            return StatusCode(503, new { message = "The envelope check is unavailable — no API key is configured." });
        }

        // Peer classes are supplied so an out-of-envelope verdict can route to a REAL neighbouring
        // class by index, rather than naming one the catalogue may not contain.
        var others = await _profiles.GetDescriptorsAsync(body.Slug, ct);
        return Ok(await _assembler.CheckEnvelopeAsync(profile, body.ToInputs(), others, ct));
    }

    [HttpPost("assemble")]
    public async Task<IActionResult> Assemble(BuildRequest body, CancellationToken ct)
    {
        var profile = await _profiles.GetBySlugAsync(body.Slug, ct);
        if (profile is null)
        {
            return NotFound(new { message = $"No class found for “{body.Slug}”." });
        }

        if (!_llm.HasApiKey)
        {
            return StatusCode(503, new { message = "Assembly is unavailable — no API key is configured." });
        }

        // Rules live in the database so HR can change policy language without a deployment.
        var rules = await _db.ComplianceRules.AsNoTracking().ToListAsync(ct);

        var inputs = body.ToInputs();
        var assembled = await _assembler.AssembleAsync(profile, inputs, rules, ct);

        // Every assembly is saved — as a Draft until the time totals exactly 100%. It is also
        // returned even when it cannot be published: the author has to SEE the draft in order to
        // decide where the unallocated time should go.
        assembled.AuthoredJdId = await _saved.SaveAsync(
            assembled, inputs, profile, await User.IdAsync(_db, ct), body.AuthoredJdId, ct);

        // An unchanged build never reached the model check, and the server skips it outright, so it
        // counts as in-envelope.
        var verdict = body.EnvelopeVerdict
                      ?? (inputs.AddedItems.Count == 0 ? Server.Core.Jd.EnvelopeVerdict.InEnvelope : null);
        await _saved.SyncCorpusAsync(assembled.AuthoredJdId.Value, assembled, inputs, profile, verdict, ct);
        return Ok(assembled);
    }
}

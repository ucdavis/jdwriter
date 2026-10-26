using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Domain;
using Server.Core.Vrf;
using Server.Helpers;

namespace Server.Controllers;

/// <summary>
/// The Vacancy Request Form: a narrative drafted from the author's notes, and the finished form as a
/// Word document. Download-only — nothing is stored or logged; the author attaches the file to their
/// AggieService ticket and HR re-keys it into the campus VRF.
/// </summary>
[ApiController]
[Route("api/vrf")]
[Authorize(Roles = AppRoles.Author)]
public class VrfController : ApiControllerBase
{
    private const int MaxText = 6_000;
    private const int MaxShort = 300;

    private readonly VrfNarrative _narrative;

    public VrfController(VrfNarrative narrative) => _narrative = narrative;

    public sealed class NarrativeRequest
    {
        public string Field { get; set; } = "";
        public string Notes { get; set; } = "";
        public VrfContext Context { get; set; } = new();
    }

    [HttpPost("narrative")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(WebHardening.ModelPolicy)]
    public async Task<IActionResult> Narrative(NarrativeRequest body, CancellationToken ct)
    {
        if (!VrfNarrative.IsNarrative(body.Field))
        {
            return BadRequest(new { message = "That isn't one of the form's narrative fields." });
        }

        if (body.Notes.Trim().Length == 0)
        {
            return BadRequest(new { message = "Write a few words first — the wand builds on your notes." });
        }

        if (TooLong(body))
        {
            return BadRequest(new { message = "That is longer than the form can take — shorten it and try again." });
        }

        if (!_narrative.CanWrite)
        {
            return StatusCode(503, new { message = "Drafting is unavailable — no AI provider is configured. Write the paragraph yourself." });
        }

        return Ok(new { paragraph = await _narrative.WriteAsync(body.Field, body.Notes, body.Context, ct) });
    }

    [HttpPost("docx")]
    public IActionResult Docx(VrfForm form)
    {
        var texts = typeof(VrfForm).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => (string?)p.GetValue(form) ?? "");
        if (texts.Any(t => t.Length > MaxText) || form.Reasons.Count > VrfCatalog.Reasons.Length || form.Reasons.Any(r => r.Length > MaxShort))
        {
            return BadRequest(new { message = "That is longer than the form can take — shorten it and try again." });
        }

        return File(
            VrfDocx.Build(form, DateOnly.FromDateTime(DateTime.Now)),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            VrfDocx.FileName(form));
    }

    private static bool TooLong(NarrativeRequest b)
    {
        var c = b.Context;
        var shorts = new[] { c.PayrollTitle, c.JobCode, c.WorkingTitle, c.BargainingUnit, c.SalaryGrade, c.Department, c.Division, c.ActionRequested, c.FillMethod, c.Vacancies, c.Fte };
        return b.Notes.Length > MaxText || shorts.Any(s => (s ?? "").Length > MaxShort)
               || c.Reasons.Count > VrfCatalog.Reasons.Length || c.Reasons.Any(r => (r ?? "").Length > MaxShort);
    }
}

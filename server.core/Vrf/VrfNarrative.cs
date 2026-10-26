using System.ComponentModel;
using System.Text;
using Server.Core.Ai;

namespace Server.Core.Vrf;

/// <summary>The position and request a narrative is about. Never who held the position before.</summary>
public sealed class VrfContext
{
    public string PayrollTitle { get; set; } = "";
    public string JobCode { get; set; } = "";
    public string WorkingTitle { get; set; } = "";
    public string BargainingUnit { get; set; } = "";
    public string SalaryGrade { get; set; } = "";
    public string Department { get; set; } = "";
    public string Division { get; set; } = "";
    public string ActionRequested { get; set; } = "";
    public string FillMethod { get; set; } = "";
    public string Vacancies { get; set; } = "";
    public string Fte { get; set; } = "";
    public List<string> Reasons { get; set; } = [];
}

public sealed class VrfNarrativeReply
{
    [Description("The paragraph for this field: 4-5 full sentences, about 90-130 words, ready to paste into the form.")]
    public string Paragraph { get; set; } = "";
}

/// <summary>
/// The "magic wand" on a VRF narrative: a few of the manager's own notes become a paragraph they can
/// edit. Adapted from Cthulhu's WMR "AI Expand" prompt, with one addition that matters on an approval
/// form: use only what the notes and the position say, and invent no figures, dates, names, funding
/// or savings. The model writes prose here by design — the author reviews and edits it.
/// </summary>
public sealed class VrfNarrative
{
    internal const string System = """
        You are a UC Davis HR specialist helping a hiring manager write a Vacancy Request Form. Given a field name, the question the form asks for it, context about the position, and a few keywords or notes from the manager, write one full, professional paragraph of 4-5 sentences (about 90-130 words) appropriate for an official HR request: state the point, support it with the specifics in the notes and the context, and say what it means for the department. Be specific and direct. Do not use jargon. Do not start with "I" or the manager's name. Use only what the notes and the context say: do not invent figures, dates, names, funding sources, or cost savings.
        """;

    private readonly IStructuredLlm _llm;

    public VrfNarrative(IStructuredLlm llm) => _llm = llm;

    public bool CanWrite => _llm.HasApiKey;

    public static bool IsNarrative(string field) => VrfCatalog.Narratives.Any(n => n.Key == field);

    public async Task<string> WriteAsync(string field, string notes, VrfContext context, CancellationToken ct = default)
    {
        var reply = await _llm.StructuredAsync<VrfNarrativeReply>(new StructuredRequest
        {
            System = System,
            User = UserPrompt(field, notes, context),
            Effort = LlmEffort.Low,
            MaxTokens = 2000,
            Label = "vrf.narrative",
        }, ct);
        return reply.Paragraph.Trim();
    }

    internal static string UserPrompt(string field, string notes, VrfContext c)
    {
        var (_, label, question) = VrfCatalog.Narratives.Single(n => n.Key == field);
        static string Or(string value, string fallback) => value.Trim().Length > 0 ? value.Trim() : fallback;

        var sb = new StringBuilder();
        sb.AppendLine($"Field: \"{label}\"");
        sb.AppendLine($"The form asks: {question}");
        sb.AppendLine();
        sb.AppendLine("Position:");
        sb.AppendLine($"- Payroll title: {Or(c.PayrollTitle, "(not given)")}{(c.JobCode.Trim().Length > 0 ? $" ({c.JobCode.Trim()})" : "")}");
        sb.AppendLine($"- Working title: {Or(c.WorkingTitle, "(not given)")}");
        sb.AppendLine($"- Department: {Or(c.Department, "(not given)")}{(c.Division.Trim().Length > 0 ? $", {c.Division.Trim()}" : "")}");
        sb.AppendLine($"- Bargaining unit: {Or(c.BargainingUnit, "(not given)")}; salary grade: {Or(c.SalaryGrade, "(not given)")}");
        sb.AppendLine();
        sb.AppendLine("Request:");
        sb.AppendLine($"- Action: {Or(c.ActionRequested, "(not given)")}; filled by: {Or(c.FillMethod, "(not given)")}");
        sb.AppendLine($"- Vacancies: {Or(c.Vacancies, "(not given)")}; FTE: {Or(c.Fte, "(not given)")}");
        sb.AppendLine($"- Reasons: {(c.Reasons.Count > 0 ? string.Join("; ", c.Reasons) : "(none selected)")}");
        sb.AppendLine();
        sb.AppendLine($"Manager's notes: {notes.Trim()}");
        sb.AppendLine();
        sb.AppendLine("Write the paragraph for this field.");
        return sb.ToString();
    }
}

namespace Server.Core.Vrf;

/// <summary>
/// A Vacancy Request Form as the author filled it in. Nothing here is stored or logged: the Word
/// file is built from it and sent back, and HR re-keys it into the campus VRF (a Smartsheet form).
/// The fields follow that form, in its order.
/// </summary>
public sealed class VrfForm
{
    public string Department { get; set; } = "";
    public string Division { get; set; } = "";
    public string ActionRequested { get; set; } = "";
    public string FillMethod { get; set; } = "";
    public string PositionNumber { get; set; } = "";
    public string DateVacated { get; set; } = "";
    public string JobOpeningId { get; set; } = "";
    public string PayrollTitle { get; set; } = "";
    public string JobCode { get; set; } = "";
    public string WorkingTitle { get; set; } = "";
    public string BargainingUnit { get; set; } = "";
    public string Vacancies { get; set; } = "";
    public string DirectReports { get; set; } = "";
    public string IndirectReports { get; set; } = "";
    public string Fte { get; set; } = "";
    public string FundingSecured { get; set; } = "";
    public string BudgetSource { get; set; } = "";
    public string FundingSources { get; set; } = "";
    public string SalaryGrade { get; set; } = "";
    public string SalaryMin { get; set; } = "";
    public string SalaryMid { get; set; } = "";
    public string SalaryMax { get; set; } = "";
    public List<string> Reasons { get; set; } = [];
    public string OtherReason { get; set; } = "";
    public string Rationale { get; set; } = "";
    public string CriticalRole { get; set; } = "";
    public string AdverseImpact { get; set; } = "";
    public string BudgetConsideration { get; set; } = "";
    public string AlternativeOptions { get; set; } = "";
    public string HiringManagerName { get; set; } = "";
    public string HiringManagerEmail { get; set; } = "";
    public string HrbpName { get; set; } = "";
    public string HrbpEmail { get; set; } = "";
    public string ExecutiveApprover { get; set; } = "";
    public string ExecutiveApproverEmail { get; set; } = "";
    public string Notes { get; set; } = "";
}

/// <summary>
/// What the form asks, in the campus VRF's order, and which questions apply to which request.
/// The client keeps the same rules (features/vrf/vrfModel.ts); a test holds the labels together.
/// The answer lists come from the official Word form, and the "applies" rules are our reading of
/// it: the Smartsheet's own branching should replace them once it's known.
/// </summary>
public static class VrfCatalog
{
    public static readonly string[] Actions =
    [
        "New Career Position",
        "Backfill Position",
        "New Contract Appt",
        "Per Diem",
        "New Short Term/Limited Appointment",
        "Contract/Limited Extension or Career Conversion",
        "Retiree/Rehire",
    ];

    public static readonly string[] FillMethods =
    [
        "Open Competitive Recruitment",
        "Internal Recruitment (Campuswide)",
        "Internal Recruitment (Dept. Limited)",
        "Direct Hire (STE- Limited Appt)",
        "Direct Hire (Career, Contract Appt w/ Waiver of Recruitment)",
        "Contract/Limited Appointment Extension Request",
    ];

    public static readonly string[] Reasons =
    [
        "Backfilling a vacancy",
        "Health and Safety",
        "Compliance with Federal, State, and/or Local laws/regulations",
        "Cost Savings",
        "Essential UC Davis Functions",
        "Revenue Generation",
        "Other",
    ];

    /// <summary>A position someone held: the date it was vacated or expires matters.</summary>
    public static bool ReplacesSomeone(VrfForm f) =>
        f.ActionRequested is "Backfill Position" or "Contract/Limited Extension or Career Conversion" or "Retiree/Rehire";

    /// <summary>A contract or limited appointment: whether its funding is secured matters.</summary>
    public static bool IsContract(VrfForm f) =>
        f.ActionRequested is "New Contract Appt" or "New Short Term/Limited Appointment" or "Contract/Limited Extension or Career Conversion";

    /// <summary>The five narratives, with the question the form asks for each.</summary>
    public static readonly (string Key, string Label, string Question)[] Narratives =
    [
        ("rationale", "Rationale", "Please outline the business case for this request."),
        ("criticalRole", "Critical Role", "Describe why this position is critical to your division."),
        ("adverseImpact", "Adverse Impact", "Describe the adverse impact of not filling this position for up to 6 months."),
        ("budgetConsideration", "Budget Consideration",
            "What cost-saving measures, if any, have been implemented to support this request? Have you considered other options such as TES or Limited Contract (one-time funding) in lieu of a permanent and ongoing financial commitment at this time?"),
        ("alternativeOptions", "Alternative Options", "If this request is not approved, how will the work associated with the position be accomplished?"),
    ];

    public sealed record Field(string Section, string Label, Func<VrfForm, string> Value, bool Required, Func<VrfForm, string?>? NotApplicable = null);

    /// <summary>Every field, in the campus form's order. NotApplicable gives what to write when the field doesn't apply.</summary>
    public static readonly Field[] Fields =
    [
        new("Position", "Department Name", f => f.Department, true),
        new("Position", "Division/School/College", f => f.Division, true),
        new("Request", "Action Requested", f => f.ActionRequested, true),
        new("Request", "How the role will be filled", f => f.FillMethod, true),
        // The campus form wants "N/A" when there's no number; the author just leaves it blank.
        new("Request", "Position Number", f => f.PositionNumber.Trim().Length > 0 ? f.PositionNumber : "N/A", false),
        new("Request", "Date Vacated or Expiration Date", f => f.DateVacated, true, f => ReplacesSomeone(f) ? null : "N/A (new position)"),
        new("Request", "Job Opening ID", f => f.JobOpeningId, false),
        new("Position", "Payroll Title", f => f.PayrollTitle, true),
        new("Position", "Job Code", f => f.JobCode, true),
        new("Position", "Working Title", f => f.WorkingTitle, true),
        new("Position", "Bargaining Unit", f => f.BargainingUnit, true),
        new("Request", "Number of Vacancies", f => f.Vacancies, true),
        new("Request", "Number of Direct Reports Supervised", f => f.DirectReports, false),
        new("Request", "Number of Indirect Reports Supervised", f => f.IndirectReports, false),
        new("Request", "FTE", f => f.Fte, true),
        new("Budget", "Is the funding for this contract secured?", f => f.FundingSecured, false, f => IsContract(f) ? null : "N/A (not a contract)"),
        new("Budget", "Budget Source", f => f.BudgetSource, true),
        new("Budget", "Sources of Funding (if multiple)", f => f.FundingSources, false),
        new("Salary", "Salary Grade/Steps", f => f.SalaryGrade, false),
        new("Salary", "Salary Range Minimum (Annual)", f => f.SalaryMin, false),
        new("Salary", "Salary Range Midpoint (Annual)", f => f.SalaryMid, false),
        new("Salary", "Salary Range Max (Annual)", f => f.SalaryMax, false),
        new("Why", "Reason(s) for Recruitment/Extension", f => string.Join("; ", f.Reasons), true),
        new("Why", "Other reason(s), explained", f => f.OtherReason, false, f => f.Reasons.Contains("Other") ? null : ""),
        new("Why", "Rationale", f => f.Rationale, true),
        new("Why", "Critical Role", f => f.CriticalRole, true),
        new("Why", "Adverse Impact", f => f.AdverseImpact, true),
        new("Why", "Budget Consideration", f => f.BudgetConsideration, true),
        new("Why", "Alternative Options", f => f.AlternativeOptions, true),
        new("People", "Hiring Manager Name", f => f.HiringManagerName, true),
        new("People", "Hiring Manager Email", f => f.HiringManagerEmail, true),
        new("People", "HR Business Partner Name", f => f.HrbpName, true),
        new("People", "HR Business Partner Email", f => f.HrbpEmail, true),
        new("People", "Executive Approver or Delegate", f => f.ExecutiveApprover, true),
        new("People", "Executive Approver Email", f => f.ExecutiveApproverEmail, true),
        new("People", "Additional Notes", f => f.Notes, false),
    ];

    /// <summary>What HR wants attached to the ticket, as the campus form lists it.</summary>
    public static readonly (string When, string[] Documents)[] Attachments =
    [
        ("All VRF submissions", ["Job description", "Org chart"]),
        ("Internal recruitments", ["Internal Recruitment Form"]),
        ("Waiver of recruitment (direct hire, career and contract appointments)", ["Waiver of Recruitment Form"]),
        ("Rehire retiree requests", ["Retire Employee Approvals Form"]),
        ("Extensions or exceptions to policy", ["Previous or current UBEN 138"]),
        ("Contract and limited appointment extensions", ["Extension request form", "Previous or current contract appointment offer letter"]),
    ];
}

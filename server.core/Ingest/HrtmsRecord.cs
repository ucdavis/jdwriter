namespace Server.Core.Ingest;

/// <summary>
/// One responsibility parsed from an HRTMS export: a named function, its share of time, and its
/// duty bullets.
/// </summary>
public sealed class HrtmsResponsibility
{
    /// <summary>Percent of time, or null when the export left the cell blank.</summary>
    public int? Pct { get; set; }

    public string FunctionName { get; set; } = "";
    public List<string> Duties { get; set; } = [];
}

/// <summary>Qualification block of an HRTMS export.</summary>
public sealed class HrtmsQualifications
{
    public List<string> Licenses { get; set; } = [];
    public bool? DriversLicenseRequired { get; set; }

    /// <summary>Free text, often blank for service roles — not a list in this format.</summary>
    public string Education { get; set; } = "";

    public List<string> MinExperience { get; set; } = [];
    public List<string> KsaMin { get; set; } = [];
    public List<string> KsaPref { get; set; } = [];
}

/// <summary>
/// Physical / environmental / mental requirement grid.
///
/// Unmarked rows are represented as a null band rather than being absent, mirroring the source.
/// In the entire current corpus these grids ship blank, so <see cref="Populated"/> is false for
/// all 1,367 exports and the axes carry only their row labels.
/// </summary>
public sealed class HrtmsPem
{
    public bool Populated { get; set; }

    /// <summary>Ordered: the row order is part of the grid's meaning, so not a dictionary.</summary>
    public List<(string Row, string? Band)> Physical { get; set; } = [];
    public List<(string Row, string? Band)> Environmental { get; set; } = [];
    public List<(string Row, string? Band)> Mental { get; set; } = [];
}

/// <summary>
/// The structured result of parsing one HRTMS "Job Description Management" HTML export.
///
/// Deliberately a plain parse result rather than the <c>JobDescription</c> EF entity: the parser
/// stays pure and testable, and the corpus loader is what maps this onto entities. Field names
/// mirror the labels UC Davis uses in the export.
/// </summary>
public sealed class HrtmsRecord
{
    /// <summary>Passed in by the caller, not parsed from the document.</summary>
    public string SourceFile { get; set; } = "";

    // organization
    public string BusinessUnit { get; set; } = "";
    public string Division { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public string DepartmentCode { get; set; } = "";

    // position identity
    public string JdNumber { get; set; } = "";
    public string UcPathPositionNumber { get; set; } = "";
    public string UcJobTitle { get; set; } = "";
    public string UcJobCode { get; set; } = "";

    /// <summary>
    /// Set by the ingest pipeline when <see cref="UcJobCode"/> is remapped to a live successor.
    /// The parser itself never sets it.
    /// </summary>
    public string? OriginalUcJobCode { get; set; }

    public string WorkingTitle { get; set; } = "";
    public string CtJobFamily { get; set; } = "";
    public string CtJobFunction { get; set; } = "";
    public string PersonnelProgram { get; set; } = "";
    public string SalaryGrade { get; set; } = "";
    public string FlsaStatus { get; set; } = "";
    public string UnionCode { get; set; } = "";

    // scope — tri-state, where null means the export did not say
    public bool? Supervises { get; set; }
    public bool? Leads { get; set; }
    public string ReportsToPositionNumber { get; set; } = "";
    public bool? WorksOutdoorsOver50pct { get; set; }

    // narrative
    public string JobSummary { get; set; } = "";
    public List<HrtmsResponsibility> Responsibilities { get; set; } = [];

    public HrtmsQualifications Qualifications { get; set; } = new();
    public List<string> ConditionsOfEmployment { get; set; } = [];
    public List<string> WorkEnvironment { get; set; } = [];
    public HrtmsPem Pem { get; set; } = new();
}

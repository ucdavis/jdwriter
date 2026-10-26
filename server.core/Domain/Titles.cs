namespace Server.Core.Domain;

/// <summary>
/// A row of the authoritative UC Davis title reference (data/title-codes.csv/.json in the POC),
/// built from the UCD JobDescMatrix workbook plus OCR of the payroll title screenshots.
/// </summary>
public class TitleCode
{
    public int Id { get; set; }

    /// <summary>
    /// UC job code. NOT unique: the reference lists some titles twice, once as a title UCD
    /// could use and once as one actually on payroll. Indexed, never a key.
    /// </summary>
    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>
    /// Loose normalized key — abbreviations expanded, bargaining-unit suffixes DROPPED.
    /// For search and for linking a standard to a profile. Never for resolving a job code.
    /// </summary>
    public string TitleKey { get; set; } = "";

    /// <summary>
    /// Strict normalized key — abbreviations expanded, bargaining-unit suffixes KEPT.
    /// The only key valid for code resolution: "PROJECT POLICY ANL 1" is 007396 and
    /// "PROJECT POLICY ANL 1 RP" is 005255. Collapsing the two keys once merged 221 reference
    /// entries and resolved them to whichever code happened to be indexed first, which made
    /// matches silently wrong rather than merely missing.
    /// </summary>
    public string TitleCodeKey { get; set; } = "";

    public string Grade { get; set; } = "";
    public string Function { get; set; } = "";
    public string Family { get; set; } = "";

    /// <summary>
    /// Provenance: "matrix" (a title UCD could use), "payroll_list" (observed on payroll), or
    /// "both". Only the latter two count as in use — see <see cref="IsInUse"/>.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>Seen on UC Davis payroll, as the reference records it.</summary>
    public bool OnPayroll => Source == "both" || Source == "payroll_list";

    /// <summary>
    /// An admin's call on whether the code is active at UC Davis, overriding the reference
    /// (<see cref="CodeActiveOverride"/>). Applied when the index loads; not stored on the row,
    /// which a reference import replaces.
    /// </summary>
    public bool? ActiveOverride { get; set; }

    /// <summary>
    /// A title actually in use at UC Davis, as opposed to merely possible: on payroll, unless an
    /// admin has said otherwise. Matrix-only titles are excluded from browse and from
    /// classification: authoring against one would produce a JD under a classification nobody holds.
    /// </summary>
    public bool IsInUse => ActiveOverride ?? OnPayroll;

    /// <summary>
    /// The code's row in the Career Tracks title code listing, when it has one: its full title, level,
    /// origin. Laid over the reference when the index loads (<see cref="CtTitle"/>); not stored here.
    /// </summary>
    public CtTitle? Ct { get; set; }
}

/// <summary>
/// A job code as the UC Career Tracks title code listing states it — imported from the report, kept
/// apart from <see cref="TitleCode"/> (which a reference import replaces), and laid over it: its full
/// title links payroll's abbreviations to the standards' spelling, its level says who supervises, and
/// its grade and family, being newer, win over the reference's.
/// </summary>
public class CtTitle
{
    public int Id { get; set; }
    public string UcJobCode { get; set; } = "";

    /// <summary>The full title ("Intellectual Property Manager 2"), not payroll's ("IP MGR 2").</summary>
    public string Title { get; set; } = "";

    public string JobFamily { get; set; } = "";
    public string JobFunction { get; set; } = "";

    /// <summary>C (campus), MC (medical center), OP (Office of the President), AG, PS.</summary>
    public string Origin { get; set; } = "";

    /// <summary>P1–P5 professional, S1–S3 supervisor, M1–M4 manager, ME executive, L lead.</summary>
    public string LevelCode { get; set; } = "";

    public DateOnly? LastUpdated { get; set; }

    /// <summary>Used by the Office of the President alone: never active at UC Davis unless an admin says so.</summary>
    public bool OpOnly { get; set; }

    public string Flsa { get; set; } = "";
    public string PersonnelProgram { get; set; } = "";
    public string JobStatus { get; set; } = "";
    public string Grade { get; set; } = "";

    /// <summary>When the listing it came from was imported. Newer wins in an environment package.</summary>
    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>A supervisor (S), manager (M) or executive (ME) level.</summary>
    public bool IsSupervisory => LevelCode.StartsWith('S') || LevelCode.StartsWith('M');
}

/// <summary>
/// An admin's call that a job code is, or is not, active at UC Davis, whatever the payroll
/// reference says. One per code; kept apart from <see cref="TitleCode"/> so a reference import
/// doesn't erase it. Deleted when it would agree with the reference.
/// </summary>
public class CodeActiveOverride
{
    public int Id { get; set; }
    public string UcJobCode { get; set; } = "";
    public bool IsActive { get; set; }
    public int? DecidedByUserId { get; set; }
    public AppUser? DecidedBy { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
}

/// <summary>
/// A job code retired by accretion into a bargaining unit — RP, CX, TX, RX or HX — and the
/// suffixed code that replaced it.
///
/// DERIVED, never authored. This cannot be inferred from the JD corpus: exports filed under a
/// superseded code still read "99 - Non-Represented (PPSM)" because they predate the accretion.
/// Nor does a union code imply supersession — most represented titles have no suffixed variant
/// and are perfectly current. The only reliable signal is a base title holding BOTH an in-use
/// non-represented code and an in-use suffixed one. RP alone yields the POC's 29 pairs; the
/// other units add 44 more.
///
/// Stored rather than recomputed per request because it is read on nearly every corpus path;
/// it must be rebuilt whenever <see cref="TitleCode"/> rows change.
/// </summary>
public class Supersession
{
    public int Id { get; set; }

    /// <summary>The deprecated non-represented code.</summary>
    public string FromCode { get; set; } = "";
    public string FromTitle { get; set; } = "";

    /// <summary>The represented successor.</summary>
    public string ToCode { get; set; } = "";
    public string ToTitle { get; set; } = "";
}

/// <summary>
/// One row of a salary structure: a grade's range for one pay type. Career Tracks ranges (UC Davis,
/// non-represented, with its geographic differential) come from Compensation's workbook; represented
/// step scales would sit beside them under their own scale name.
/// </summary>
public class SalaryRange
{
    public int Id { get; set; }

    /// <summary>"Career Tracks" today.</summary>
    public string Scale { get; set; } = "";

    public int Grade { get; set; }

    /// <summary>Annual, Monthly, Biweekly or Hourly.</summary>
    public string PayType { get; set; } = "";

    public decimal Minimum { get; set; }
    public decimal FirstQuartile { get; set; }
    public decimal Midpoint { get; set; }
    public decimal ThirdQuartile { get; set; }
    public decimal Maximum { get; set; }

    public DateOnly? EffectiveDate { get; set; }

    /// <summary>What the workbook says about the scale, e.g. "Geo Diff 112% for Non-Represented Staff".</summary>
    public string Note { get; set; } = "";

    public DateTimeOffset ImportedAt { get; set; }
}

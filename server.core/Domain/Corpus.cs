namespace Server.Core.Domain;

/// <summary>
/// One real job description, parsed from an HRTMS "Job Description Management" HTML export.
/// Field names mirror the labels UC Davis uses in that export.
///
/// This is the most sensitive table in the schema: department, position number, and reporting
/// line identify a real position and in practice its incumbent. Seed the real corpus into
/// production only; test environments get a scrubbed subset.
/// </summary>
public class JobDescription
{
    public int Id { get; set; }

    /// <summary>
    /// Path of the source export, relative to the corpus root. Unique — it is the identity of a
    /// JD, and re-ingesting must update a record rather than duplicate it. Note this string is
    /// itself an identifier: HRTMS names exports "&lt;TITLE&gt; (&lt;position number&gt;).HTML".
    /// </summary>
    public string SourceFile { get; set; } = "";

    // ---- organization
    public string BusinessUnit { get; set; } = "";
    public string Division { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public string DepartmentCode { get; set; } = "";

    // ---- position identity
    public string JdNumber { get; set; } = "";
    public string UcPathPositionNumber { get; set; } = "";
    public string UcJobTitle { get; set; } = "";

    /// <summary>
    /// The class key. Rewritten to the successor at parse time when the exported code has been
    /// superseded, so grouping, slug, aggregate and profile identity all follow and a re-ingest
    /// cannot resurrect a dead class.
    /// </summary>
    public string UcJobCode { get; set; } = "";

    /// <summary>
    /// What the export actually said, when <see cref="UcJobCode"/> was remapped. Null when no
    /// remap happened. Kept so the rewrite is auditable rather than invisible.
    /// </summary>
    public string? OriginalUcJobCode { get; set; }

    public string WorkingTitle { get; set; } = "";
    public string CtJobFamily { get; set; } = "";
    public string CtJobFunction { get; set; } = "";

    /// <summary>PSS / MSP / Academic.</summary>
    public string PersonnelProgram { get; set; } = "";

    public string SalaryGrade { get; set; } = "";
    public string FlsaStatus { get; set; } = "";

    /// <summary>
    /// Collective bargaining unit. Not a supersession signal: JDs under a superseded code still
    /// read non-represented because they predate the accretion.
    /// </summary>
    public string UnionCode { get; set; } = "";

    // ---- scope. Tri-state: null means the export did not say, which is the common case
    // (1,027 of 1,367 for supervises), and is distinct from an explicit "no".
    public bool? Supervises { get; set; }
    public bool? Leads { get; set; }
    public string ReportsToPositionNumber { get; set; } = "";
    public bool? WorksOutdoorsOver50pct { get; set; }

    // ---- narrative
    public string JobSummary { get; set; } = "";

    /// <summary>
    /// Whether the PEM grid carried any marks. False across the entire current corpus — these
    /// grids ship blank — so nothing may depend on PEM data being present.
    /// </summary>
    public bool PemPopulated { get; set; }

    /// <summary>Drivers-licence requirement, a scalar on the qualifications block.</summary>
    public bool? DriversLicenseRequired { get; set; }

    /// <summary>Where this JD came from. Everything loaded or uploaded from HRTMS is Export.</summary>
    public CorpusOrigin Origin { get; set; }

    /// <summary>For an Authored JD: the saved JD it copies. Deleting that JD deletes this.</summary>
    public int? AuthoredJdId { get; set; }
    public AuthoredJd? AuthoredJd { get; set; }

    /// <summary>
    /// When it joined the corpus through the app; null for the initial CLI load. Compared with the
    /// class's last ingest to find classes with new evidence waiting.
    /// </summary>
    public DateTimeOffset? AddedAt { get; set; }

    public List<JdResponsibility> Responsibilities { get; set; } = [];
    public List<JdQualificationItem> Qualifications { get; set; } = [];
    public List<JdPemEntry> PemEntries { get; set; } = [];
}

/// <summary>
/// One key responsibility: a named function with a share of time and its duty bullets.
/// </summary>
public class JdResponsibility
{
    public int Id { get; set; }
    public int JobDescriptionId { get; set; }
    public JobDescription? JobDescription { get; set; }

    public int Ordinal { get; set; }

    /// <summary>
    /// Percent of time, or null when the export left it blank.
    ///
    /// Deliberately unconstrained. Percentages across one JD are SUPPOSED to sum to 100 and
    /// almost always do — but six real JDs in the corpus range from 0 to 200, so a check
    /// constraint requiring 100 would reject genuine data at load. Validate and report; do not
    /// enforce.
    /// </summary>
    public int? Pct { get; set; }

    public string FunctionName { get; set; } = "";

    public List<JdDuty> Duties { get; set; } = [];
}

/// <summary>One duty bullet under a responsibility. ~21,000 rows across the corpus.</summary>
public class JdDuty : ITextItem
{
    public int Id { get; set; }
    public int JdResponsibilityId { get; set; }
    public JdResponsibility? JdResponsibility { get; set; }

    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// A qualification line from one JD. Education arrives as a single free-text field rather than a
/// list, so at most one row carries <see cref="JdQualificationKind.Education"/>.
/// ConditionOfEmployment is modeled but empty corpus-wide.
/// </summary>
public class JdQualificationItem : ITextItem
{
    public int Id { get; set; }
    public int JobDescriptionId { get; set; }
    public JobDescription? JobDescription { get; set; }

    public JdQualificationKind Kind { get; set; }
    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// One marked cell of the physical / environmental / mental requirement grid. Unmarked rows are
/// absent rather than stored as null, so this table is empty for the current corpus — every
/// export ships the grid blank. Modeled because authored JDs and future exports will use it.
/// </summary>
public class JdPemEntry
{
    public int Id { get; set; }
    public int JobDescriptionId { get; set; }
    public JobDescription? JobDescription { get; set; }

    public PemAxis Axis { get; set; }
    public string RowName { get; set; } = "";
    public PemBand Band { get; set; }
}

/// <summary>
/// One HRTMS export an admin uploaded, kept as the original bytes. HRTMS exports carry UCPath
/// position numbers and reporting lines, so these live only in the database — which Azure SQL
/// encrypts at rest — and never on a server's file system.
/// </summary>
public class CorpusUpload
{
    public int Id { get; set; }

    /// <summary>
    /// The uploaded name, including any folder the browser supplied. HRTMS names embed the
    /// position number, so this is an identifier too and is never logged.
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>SHA-256 of the bytes. Unique: uploading the same file twice stores it once.</summary>
    public string Sha256 { get; set; } = "";

    public byte[] Content { get; set; } = [];
    public int SizeBytes { get; set; }

    public CorpusUploadStatus Status { get; set; }

    /// <summary>How it arrived: an admin upload, or a description submitted on the Classify page.</summary>
    public CorpusUploadSource Source { get; set; }

    /// <summary>Why the file could not be used, when <see cref="Status"/> is Failed.</summary>
    public string? Error { get; set; }

    /// <summary>The live class it belongs to, after supersession.</summary>
    public string? UcJobCode { get; set; }

    /// <summary>The code the export itself stated, when supersession remapped it.</summary>
    public string? OriginalUcJobCode { get; set; }

    public string? UcJobTitle { get; set; }

    public int? UploadedByUserId { get; set; }
    public AppUser? UploadedBy { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? IngestedAt { get; set; }
}

public enum CorpusUploadStatus
{
    /// <summary>Parsed and waiting to be ingested into its class.</summary>
    Pending,

    /// <summary>Added to the corpus and its class re-ingested.</summary>
    Ingested,

    /// <summary>Not a usable HRTMS export; see <see cref="CorpusUpload.Error"/>.</summary>
    Failed,

    /// <summary>
    /// Filed straight into a class's corpus (Classify submissions, imported documents). Counts at the class's next
    /// rebuild; the admin queue shows it as new evidence.
    /// </summary>
    Filed,
}

public enum CorpusUploadSource
{
    Upload,
    Classify,

    /// <summary>A Word, PDF or text JD from a unit outside the college, filed by the title it states.</summary>
    Document,
}

public enum CorpusOrigin
{
    /// <summary>An HRTMS export — loaded by the CLI or uploaded by an admin.</summary>
    Export,

    /// <summary>A JD written in this app that changed its envelope and passed the envelope check.</summary>
    Authored,

    /// <summary>A description submitted on the Classify page, filed under a class.</summary>
    Classify,

    /// <summary>
    /// A JD from a unit outside the college, read from a Word, PDF or text copy (JDX cannot export
    /// HRTMS's format) and filed under the class its stated title resolves to.
    /// </summary>
    Imported,
}

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

    /// <summary>
    /// A title actually in use at UC Davis, as opposed to merely possible. Matrix-only titles
    /// are excluded from browse and from classification: authoring against one would produce a
    /// JD under a classification nobody holds.
    /// </summary>
    public bool IsInUse => Source == "both" || Source == "payroll_list";
}

/// <summary>
/// A job code retired by accretion into the RP (Research and Public Service Professionals)
/// bargaining unit, and the code that replaced it.
///
/// DERIVED, never authored. This cannot be inferred from the JD corpus: exports filed under a
/// superseded code still read "99 - Non-Represented (PPSM)" because they predate the accretion.
/// Nor does a union code imply supersession — most represented titles have no suffixed variant
/// and are perfectly current. The only reliable signal is a base title holding BOTH an in-use
/// non-RP code and an in-use RP code, which yields 29 pairs with no ambiguity.
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

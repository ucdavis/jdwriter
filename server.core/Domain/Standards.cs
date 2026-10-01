namespace Server.Core.Domain;

/// <summary>
/// An official UC job standard for one classification, parsed from a Job Builder or UC website
/// workbook.
///
/// Standards COMPLEMENT the corpus-derived envelope rather than replacing it. The corpus drives
/// the % time / function / duties, because reflecting what UC Davis JDs actually say is the
/// point of this system; the standard supplies authoritative KSAs, education, scope, and the
/// fixed grade/FLSA/union attributes.
/// </summary>
public class JobStandard
{
    public int Id { get; set; }

    public string LongTitle { get; set; } = "";

    /// <summary>
    /// UC job code. Only the single-family workbook layout states one; for the Side-by-Side
    /// layout it is resolved from the title at ingest. Where present it is the exact join key
    /// to a profile — title spellings vary ("Analyst" vs "Anl"), codes do not.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>Classified indicator: MSP / PSS / Academic.</summary>
    public string PersProg { get; set; } = "";

    /// <summary>
    /// Salary grade. Neither workbook layout is reliable here and the title reference is
    /// authoritative, so a resolved code backfills this at ingest.
    /// </summary>
    public string Grade { get; set; } = "";

    public string Flsa { get; set; } = "";
    public string Union { get; set; } = "";
    public string GenericScope { get; set; } = "";
    public string CustomScope { get; set; } = "";

    /// <summary>Loose key, for linking a standard to a profile by title.</summary>
    public string TitleKey { get; set; } = "";

    /// <summary>
    /// Strict key. The store MUST dedup on this: the loose key merges "Analyst 3 RP" into
    /// "Analyst 3 RP GF" and silently discards around 25 real standards, which are different
    /// classifications carrying different codes.
    /// </summary>
    public string TitleCodeKey { get; set; } = "";

    public List<JobStandardItem> Items { get; set; } = [];
}

/// <summary>One bulleted line from a job standard, tagged by which section it came from.</summary>
public class JobStandardItem : ITextItem
{
    public int Id { get; set; }
    public int JobStandardId { get; set; }
    public JobStandard? JobStandard { get; set; }

    public StandardItemKind Kind { get; set; }
    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
}

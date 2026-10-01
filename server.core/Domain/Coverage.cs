namespace Server.Core.Domain;

/// <summary>
/// Backwards coverage: how well this class's envelope still accounts for the real JDs it was
/// derived from. Computed at ingest and stored on the profile so it travels with it rather than
/// depending on a parsed-records cache.
///
/// Worth distinguishing from the other coverage check in the system. This one is computed from
/// CONSOLIDATED members and is therefore unaffected by how the envelope is worded. The check
/// that actually guards against an envelope drifting away from its corpus is the one that maps
/// raw JD functions onto envelope functions — that is what runs before and after applying an
/// official standard, so a standard cannot silently degrade fit.
/// </summary>
public class CoverageReport
{
    public int Id { get; set; }

    /// <summary>One report per profile.</summary>
    public int ClassProfileId { get; set; }
    public ClassProfile? ClassProfile { get; set; }

    /// <summary>Number of JDs compared.</summary>
    public int N { get; set; }

    public double MeanCoverage { get; set; }

    /// <summary>Share of JDs at 90% coverage or better.</summary>
    public double WellCoveredPct { get; set; }

    public List<JdCoverage> PerJd { get; set; } = [];
}

/// <summary>How much of one JD the envelope accounts for.</summary>
public class JdCoverage
{
    public int Id { get; set; }
    public int CoverageReportId { get; set; }
    public CoverageReport? CoverageReport { get; set; }

    public int Ordinal { get; set; }

    /// <summary>
    /// Identifies the JD by its export path rather than by foreign key, matching the source
    /// model: a coverage report is a snapshot and stays readable even if the corpus is reloaded.
    /// </summary>
    public string SourceFile { get; set; } = "";

    public double CoveredPct { get; set; }

    public List<JdCoverageUncovered> Uncovered { get; set; } = [];
}

/// <summary>
/// A function in a real JD that the envelope does not account for, and the share of that JD's
/// time it holds. These are the idiosyncratic items the review-and-nudge workflow proposes
/// pulling back toward the standard.
/// </summary>
public class JdCoverageUncovered
{
    public int Id { get; set; }
    public int JdCoverageId { get; set; }
    public JdCoverage? JdCoverage { get; set; }

    public int Ordinal { get; set; }
    public string Name { get; set; } = "";
    public double Pct { get; set; }
}

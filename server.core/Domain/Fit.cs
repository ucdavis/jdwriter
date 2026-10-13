namespace Server.Core.Domain;

/// <summary>How a low-coverage JD was dealt with.</summary>
public enum FitReviewKind
{
    /// <summary>It genuinely belongs in its class, idiosyncratic work and all.</summary>
    Belongs,

    /// <summary>Rewritten to fit a class: the draft carries the work forward.</summary>
    Rewritten,
}

/// <summary>
/// A low-coverage JD that has been dealt with on Reclassification — found to belong in its class, or
/// rewritten to fit one — and so taken out of the work queue. Keyed by the JD's name, which means the same
/// JD in every environment, and recorded against the class it was reviewed in — moved to another
/// class, or replaced by a newer export, the JD is up for review again.
/// </summary>
public class JdFitReview
{
    public int Id { get; set; }
    public string SourceFile { get; set; } = "";
    public string UcJobCode { get; set; } = "";

    public FitReviewKind Kind { get; set; }

    /// <summary>For a rewrite: the class it was rewritten to fit.</summary>
    public string? RewrittenTo { get; set; }

    /// <summary>Optional: why it belongs, for whoever looks next.</summary>
    public string? Note { get; set; }

    public int? ReviewedByUserId { get; set; }
    public AppUser? ReviewedBy { get; set; }

    /// <summary>Kept by name too: the user row doesn't travel in an environment package.</summary>
    public string? ReviewedByName { get; set; }

    public DateTimeOffset ReviewedAt { get; set; }
}

namespace Server.Core.Domain;

/// <summary>
/// An admin's finding that a JD with low coverage of its class genuinely belongs there: reviewed on
/// Reclassification, and taken out of its work queue. Keyed by the JD's name, which means the same
/// JD in every environment, and recorded against the class it was reviewed in — moved to another
/// class, or replaced by a newer export, the JD is up for review again.
/// </summary>
public class JdFitReview
{
    public int Id { get; set; }
    public string SourceFile { get; set; } = "";
    public string UcJobCode { get; set; } = "";

    /// <summary>Optional: why it belongs, for whoever looks next.</summary>
    public string? Note { get; set; }

    public int? ReviewedByUserId { get; set; }
    public AppUser? ReviewedBy { get; set; }

    /// <summary>Kept by name too: the user row doesn't travel in an environment package.</summary>
    public string? ReviewedByName { get; set; }

    public DateTimeOffset ReviewedAt { get; set; }
}

namespace Server.Core.Domain;

/// <summary>
/// The readable "Job Envelope" — a generic, standardized JD template for one class, with the
/// specifics (sites, units, crops) sanded off. It is meant to work for most positions in the
/// class with under 10% unit customization, and to bound what a request may add before it
/// really belongs to a neighbouring class.
/// </summary>
public class JobEnvelope
{
    public int Id { get; set; }

    /// <summary>One envelope per profile.</summary>
    public int ClassProfileId { get; set; }
    public ClassProfile? ClassProfile { get; set; }

    public string Summary { get; set; } = "";

    /// <summary>One paragraph on the normal scope, level and conditions of the class.</summary>
    public string ScopeStatement { get; set; } = "";

    public List<EnvelopeResponsibility> KeyResponsibilities { get; set; } = [];
    public List<EnvelopeListItem> Items { get; set; } = [];
}

/// <summary>
/// One key responsibility of an envelope: a broad function, its share of time, and duty bullets
/// the author keeps or drops.
/// </summary>
public class EnvelopeResponsibility
{
    public int Id { get; set; }
    public int JobEnvelopeId { get; set; }
    public JobEnvelope? JobEnvelope { get; set; }

    public int Ordinal { get; set; }
    public string FunctionName { get; set; } = "";

    /// <summary>
    /// Percent of time. Across an envelope's responsibilities these are expected to sum to 100,
    /// and unlike the corpus they are OURS to get right — recompute in code after any edit
    /// rather than trusting a model to preserve the arithmetic.
    /// </summary>
    public int PctTime { get; set; }

    public List<EnvelopeDuty> Duties { get; set; } = [];
}

/// <summary>A selectable duty bullet under an envelope responsibility.</summary>
public class EnvelopeDuty : ITextItem
{
    public int Id { get; set; }
    public int EnvelopeResponsibilityId { get; set; }
    public EnvelopeResponsibility? EnvelopeResponsibility { get; set; }

    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// One line of an envelope's list-valued sections — qualifications, conditions, and the
/// out-of-envelope signals that route a request to a different class.
/// </summary>
public class EnvelopeListItem : ITextItem
{
    public int Id { get; set; }
    public int JobEnvelopeId { get; set; }
    public JobEnvelope? JobEnvelope { get; set; }

    public EnvelopeListKind Kind { get; set; }
    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
}

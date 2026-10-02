namespace Server.Core.Domain;

/// <summary>
/// The averaged, standardized view of one UC job class, computed from its corpus of parsed JDs.
/// This is what authoring reads to present standard qualifications and to bound a request.
/// </summary>
public class ClassProfile
{
    public int Id { get; set; }

    /// <summary>Stable URL identity, "&lt;code&gt;-&lt;slugified title&gt;". Unique.</summary>
    public string Slug { get; set; } = "";

    /// <summary>
    /// UC job code. Authoritative for corpus-derived profiles, where it comes from the HRTMS
    /// export itself and must never be overwritten by title matching. Bootstrap-derived profiles
    /// got theirs from title resolution instead.
    /// </summary>
    public string UcJobCode { get; set; } = "";

    public string Title { get; set; } = "";
    public string CtJobFamily { get; set; } = "";

    /// <summary>
    /// Career Tracks function. Printed per class in the classifier's stage-1 shortlist, so a
    /// blank one measurably degrades recall.
    /// </summary>
    public string CtJobFunction { get; set; } = "";

    public string PersonnelProgram { get; set; } = "";

    /// <summary>Number of JDs aggregated into this profile.</summary>
    public int CorpusSize { get; set; }

    /// <summary>One representative job summary, used when no envelope has been synthesized.</summary>
    public string RepresentativeSummary { get; set; } = "";

    /// <summary>Null until an envelope exists.</summary>
    public EnvelopeSource? EnvelopeSource { get; set; }

    public string GeneratedNote { get; set; } = "";

    /// <summary>When the class was last built or rebuilt from its corpus. Null for the CLI load.</summary>
    public DateTimeOffset? LastIngestedAt { get; set; }

    public JobEnvelope? Envelope { get; set; }
    public CoverageReport? Coverage { get; set; }

    public List<ProfileDistribution> Distributions { get; set; } = [];
    public List<ProfileFunction> Functions { get; set; } = [];
    public List<ProfileQualItem> Qualifications { get; set; } = [];
    public List<ConsolidatedFunction> ConsolidatedFunctions { get; set; } = [];
    public List<ConsolidatedQual> ConsolidatedQuals { get; set; } = [];
    public List<ProfileDroppedItem> DroppedItems { get; set; } = [];
    public List<ProfileSourceFile> SourceFiles { get; set; } = [];
}

/// <summary>
/// Provenance: which export files fed this profile. Kept as rows rather than a joined string so
/// a profile can be rebuilt from, and audited against, the corpus.
/// </summary>
public class ProfileSourceFile
{
    public int Id { get; set; }
    public int ClassProfileId { get; set; }
    public ClassProfile? ClassProfile { get; set; }

    public int Ordinal { get; set; }
    public string SourceFile { get; set; } = "";
}

/// <summary>
/// How one consensus attribute is distributed across the class's corpus.
/// </summary>
public class ProfileDistribution
{
    public int Id { get; set; }
    public int ClassProfileId { get; set; }
    public ClassProfile? ClassProfile { get; set; }

    public DistributionField Field { get; set; }

    /// <summary>
    /// The modal value when one clearly dominates, else null.
    ///
    /// Booleans are stored as "true"/"false" so one table serves both the string-valued fields
    /// (grade, FLSA, union) and the tri-state ones (supervises, leads, outdoors). Note this
    /// inherits an ambiguity from the source model: null means EITHER "no clear consensus" OR
    /// "the consensus is the null value". The POC has the same ambiguity and tolerates it, so
    /// it is mirrored rather than fixed — resolving it here would break fixture parity.
    /// </summary>
    public string? Consensus { get; set; }

    /// <summary>Share of the corpus holding the consensus value, 0..1.</summary>
    public double Agreement { get; set; }

    public List<ProfileDistributionValue> Values { get; set; } = [];
}

/// <summary>
/// One observed value and its count, ordered by descending count.
/// <see cref="Value"/> is nullable on purpose: "the export did not say" is a real observation
/// for the tri-state fields and must not be dropped.
/// </summary>
public class ProfileDistributionValue
{
    public int Id { get; set; }
    public int ProfileDistributionId { get; set; }
    public ProfileDistribution? ProfileDistribution { get; set; }

    public int Ordinal { get; set; }
    public string? Value { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// A responsibility function seen across the corpus, with its observed share of time.
/// The NumericRange of the source model is flattened into four columns.
/// </summary>
public class ProfileFunction
{
    public int Id { get; set; }
    public int ClassProfileId { get; set; }
    public ClassProfile? ClassProfile { get; set; }

    public int Ordinal { get; set; }
    public string Name { get; set; } = "";

    public double PctMin { get; set; }
    public double PctMax { get; set; }
    public double PctMean { get; set; }

    /// <summary>How many JDs contributed to the percentage range.</summary>
    public int PctN { get; set; }

    /// <summary>Share of the class's JDs that include this function, 0..1.</summary>
    public double Prevalence { get; set; }

    public List<ProfileFunctionSampleDuty> SampleDuties { get; set; } = [];
}

/// <summary>A representative duty bullet for a profile function.</summary>
public class ProfileFunctionSampleDuty : ITextItem
{
    public int Id { get; set; }
    public int ProfileFunctionId { get; set; }
    public ProfileFunction? ProfileFunction { get; set; }

    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// A qualification line aggregated across the corpus, with the share of JDs that required it.
/// </summary>
public class ProfileQualItem : ITextItem
{
    public int Id { get; set; }
    public int ClassProfileId { get; set; }
    public ClassProfile? ClassProfile { get; set; }

    public ProfileQualKind Kind { get; set; }
    public int Ordinal { get; set; }
    public string Text { get; set; } = "";

    /// <summary>Share of the corpus stating this requirement, 0..1.</summary>
    public double Freq { get; set; }
}

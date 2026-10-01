namespace Server.Core.Domain;

/// <summary>
/// Eight tables in this schema are the same shape: an ordered, kind-tagged line of text
/// belonging to a parent. They stay separate tables so every foreign key is real and a
/// cascade delete means what it says, but they share this contract so the EF configuration
/// and the migration loader can treat them uniformly.
/// </summary>
public interface ITextItem
{
    int Id { get; set; }

    /// <summary>
    /// Position within its list. These lists are ordered — a JD's duties and an envelope's
    /// key responsibilities both read in sequence — and SQL has no inherent row order, so
    /// every query that materializes one must sort by this.
    /// </summary>
    int Ordinal { get; set; }

    string Text { get; set; }
}

// ---------------------------------------------------------------- kind discriminators
//
// Persisted as strings, not ints. This database is meant to be queried directly by HR
// analysts for cross-class reporting, and "KsaMin" in a result grid is worth more than "3".
// It also means inserting a new enum member cannot silently re-map existing rows.

/// <summary>Qualification lines parsed from a real HRTMS job description.</summary>
public enum JdQualificationKind
{
    License,
    Education,
    MinExperience,
    KsaMin,
    KsaPref,
    ConditionOfEmployment,
    WorkEnvironment,
}

/// <summary>
/// Qualification lines aggregated across a class's corpus, each carrying the share of JDs
/// that stated it.
/// </summary>
public enum ProfileQualKind
{
    License,
    Education,
    MinExperience,
    KsaMin,
    KsaPref,
    WorkEnvironment,
}

/// <summary>Bulleted sections of an official UC job standard.</summary>
public enum StandardItemKind
{
    KeyResponsibility,
    Ksa,
    Education,
    License,
    SpecialCondition,
}

/// <summary>
/// The list-valued sections of a synthesized job envelope. OutOfEnvelope is not a
/// qualification at all — it holds the signals that a request belongs to a different class —
/// but it is the same shape and travels with the envelope.
/// </summary>
public enum EnvelopeListKind
{
    RequiredCertification,
    Education,
    WorkExperience,
    MinQualification,
    PrefQualification,
    ConditionOfEmployment,
    WorkEnvironment,
    PhysicalRequirement,
    OutOfEnvelope,
}

/// <summary>The list-valued sections of a finished, authored job description.</summary>
public enum AuthoredJdListKind
{
    LicensesCertifications,
    Education,
    WorkExperience,
    MinKsa,
    PrefKsa,
    ConditionOfEmployment,
    WorkEnvironment,
    PhysicalRequirement,
}

/// <summary>Which consensus attribute a stored distribution describes.</summary>
public enum DistributionField
{
    SalaryGrade,
    FlsaStatus,
    UnionCode,
    Supervises,
    Leads,
    WorksOutdoorsOver50pct,
}

/// <summary>Axis of the PEM (physical / environmental / mental) requirement grid.</summary>
public enum PemAxis
{
    Physical,
    Environmental,
    Mental,
}

/// <summary>
/// PEM frequency bands. An unmarked row is represented by the absence of a row rather than a
/// member here, matching the source, where null means "not marked".
/// </summary>
public enum PemBand
{
    Never,
    Occasional,
    Frequent,
    Continuous,
}

/// <summary>How a class profile's envelope was produced.</summary>
public enum EnvelopeSource
{
    /// <summary>Synthesized by the model from an ingested JD corpus.</summary>
    Claude,

    /// <summary>Built deterministically from corpus aggregates, with no model call.</summary>
    Deterministic,

    /// <summary>Edited by a human.</summary>
    Manual,

    /// <summary>Bootstrapped from an official job standard, with no JD corpus yet.</summary>
    Standard,
}

/// <summary>Whether a compliance edit came from a deterministic rule or from the model.</summary>
public enum ComplianceEditSource
{
    Rule,
    Llm,
}

/// <summary>What a dropped item was dropped from during consolidation.</summary>
public enum DroppedItemKind
{
    Function,
    Qualification,
}

/// <summary>Which consolidated qualification list an entry belongs to.</summary>
public enum ConsolidatedQualKind
{
    Certification,
    MinQualification,
}

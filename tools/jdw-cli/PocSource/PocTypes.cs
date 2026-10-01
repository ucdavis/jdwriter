using System.Text.Json.Serialization;

namespace Jdw.Cli.PocSource;

// Shapes of the POC's on-disk JSON, deserialized verbatim.
//
// These deliberately mirror the TypeScript rather than the EF entities. Mapping happens in one
// explicit place (CorpusMigrator) so that a change in either the POC's output or the schema
// surfaces as a compile error there, instead of as a silently-skipped field during a load.

/// <summary>A row of data/title-codes.json.</summary>
public sealed class PocTitleCode
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Grade { get; set; } = "";
    public string Function { get; set; } = "";
    public string Family { get; set; } = "";
    public string Source { get; set; } = "";
}

/// <summary>A row of data/standards.json.</summary>
public sealed class PocStandard
{
    public string LongTitle { get; set; } = "";
    public string? Code { get; set; }
    public string PersProg { get; set; } = "";
    public string Grade { get; set; } = "";
    public string Flsa { get; set; } = "";
    public string Union { get; set; } = "";
    public string GenericScope { get; set; } = "";
    public string CustomScope { get; set; } = "";
    public List<string> KeyResponsibilities { get; set; } = [];
    public List<string> Ksa { get; set; } = [];
    public List<string> Education { get; set; } = [];
    public List<string> Licenses { get; set; } = [];
    public List<string> SpecialConditions { get; set; } = [];
}

/// <summary>
/// A row of data/compliance-rules.json.
///
/// The POC file uses snake_case and has no stable identifier, so the JSON property names are
/// mapped explicitly and a key is derived at load — see CorpusMigrator.
/// </summary>
public sealed class PocComplianceRule
{
    [JsonPropertyName("rule_type")]
    public string RuleType { get; set; } = "";

    public string Pattern { get; set; } = "";
    public string Replacement { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// A distribution over observed values.
///
/// <see cref="Consensus"/> and each <see cref="PocDistributionValue.Value"/> are nullable because
/// the tri-state fields (supervises, leads, outdoors) record "the export did not say" as a real
/// observation — it covers 1,027 of 1,367 JDs for supervises and must not be dropped.
/// </summary>
public sealed class PocDistribution
{
    public List<PocDistributionValue> Values { get; set; } = [];

    /// <summary>
    /// Boolean for the tri-state fields, string for the rest — hence the converter. Null means
    /// either "no clear consensus" or "the consensus is the null value"; the POC has that same
    /// ambiguity and the schema mirrors it deliberately.
    /// </summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Consensus { get; set; }

    public double Agreement { get; set; }
}

public sealed class PocDistributionValue
{
    /// <summary>
    /// Arrives as a JSON boolean for supervises / leads / worksOutdoorsOver50pct and as a string
    /// for salary grade / FLSA / union.
    /// </summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Value { get; set; }

    public int Count { get; set; }
}

public sealed class PocNumericRange
{
    public double Min { get; set; }
    public double Max { get; set; }
    public double Mean { get; set; }
    public int N { get; set; }
}

public sealed class PocFunctionProfile
{
    public string Name { get; set; } = "";
    public PocNumericRange Pct { get; set; } = new();
    public double Prevalence { get; set; }
    public List<string> SampleDuties { get; set; } = [];
}

/// <summary>A qualification line with the share of the corpus that stated it.</summary>
public sealed class PocFreqText
{
    public string Text { get; set; } = "";
    public double Freq { get; set; }
}

public sealed class PocKeyResponsibility
{
    public string FunctionName { get; set; } = "";
    public int PctTime { get; set; }
    public List<string> Duties { get; set; } = [];
}

public sealed class PocEnvelope
{
    public string Summary { get; set; } = "";
    public string ScopeStatement { get; set; } = "";
    public List<PocKeyResponsibility> KeyResponsibilities { get; set; } = [];
    public List<string> RequiredCertifications { get; set; } = [];
    public List<string> Education { get; set; } = [];
    public List<string> WorkExperience { get; set; } = [];
    public List<string> MinQualifications { get; set; } = [];
    public List<string> PrefQualifications { get; set; } = [];
    public List<string> ConditionsOfEmployment { get; set; } = [];
    public List<string> WorkEnvironment { get; set; } = [];
    public List<string> PhysicalRequirements { get; set; } = [];
    public List<string> OutOfEnvelope { get; set; } = [];
}

public sealed class PocConsolidatedFunction
{
    public string Name { get; set; } = "";
    public double MeanPct { get; set; }
    public double TemplatePct { get; set; }
    public double MinPct { get; set; }
    public double MaxPct { get; set; }
    public double Prevalence { get; set; }
    public List<string> Members { get; set; } = [];
    public List<string> SampleDuties { get; set; } = [];
}

public sealed class PocConsolidatedQual
{
    public string Name { get; set; } = "";
    public double Freq { get; set; }
    public List<string> Members { get; set; } = [];
}

public sealed class PocConsolidated
{
    public List<PocConsolidatedFunction> FunctionGroups { get; set; } = [];
    public List<PocConsolidatedQual> Certifications { get; set; } = [];
    public List<PocConsolidatedQual> MinQualifications { get; set; } = [];
    public List<string> DroppedFunctions { get; set; } = [];
    public List<string> DroppedQualifications { get; set; } = [];
}

public sealed class PocJdCoverage
{
    public string SourceFile { get; set; } = "";
    public double CoveredPct { get; set; }
    public List<PocUncovered> Uncovered { get; set; } = [];
}

public sealed class PocUncovered
{
    public string Name { get; set; } = "";
    public double Pct { get; set; }
}

public sealed class PocCoverageReport
{
    public int N { get; set; }
    public double MeanCoverage { get; set; }
    public double WellCoveredPct { get; set; }
    public List<PocJdCoverage> PerJd { get; set; } = [];
}

/// <summary>One file from data/profiles/.</summary>
public sealed class PocProfile
{
    public string Slug { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string CtJobFamily { get; set; } = "";
    public string CtJobFunction { get; set; } = "";
    public string PersonnelProgram { get; set; } = "";
    public int CorpusSize { get; set; }

    public PocDistribution SalaryGrade { get; set; } = new();
    public PocDistribution FlsaStatus { get; set; } = new();
    public PocDistribution UnionCode { get; set; } = new();
    public PocDistribution Supervises { get; set; } = new();
    public PocDistribution Leads { get; set; } = new();
    public PocDistribution WorksOutdoorsOver50pct { get; set; } = new();

    public List<PocFunctionProfile> Functions { get; set; } = [];
    public string RepresentativeSummary { get; set; } = "";

    public List<PocFreqText> Licenses { get; set; } = [];
    public List<PocFreqText> Education { get; set; } = [];
    public List<PocFreqText> MinExperience { get; set; } = [];
    public List<PocFreqText> KsaMin { get; set; } = [];
    public List<PocFreqText> KsaPref { get; set; } = [];
    public List<PocFreqText> WorkEnvironment { get; set; } = [];

    public PocEnvelope? Envelope { get; set; }
    public string? EnvelopeSource { get; set; }
    public PocConsolidated? Consolidated { get; set; }
    public PocCoverageReport? Coverage { get; set; }

    public List<string> SourceFiles { get; set; } = [];
    public string GeneratedNote { get; set; } = "";
}

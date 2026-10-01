using Server.Core.Domain;
using Server.Core.Jd;
using CoreJd = Server.Core.Jd.Jd;

namespace Server.Tests.Jd;

/// <summary>
/// A constructed class profile and build inputs, small enough that an expected prompt can be
/// written out in full and compared exactly.
/// </summary>
internal static class JdTestData
{
    public static ClassProfile Profile()
    {
        var profile = new ClassProfile
        {
            Id = 1,
            Slug = "006256-rsch-data-anl-2",
            UcJobCode = "006256",
            Title = "Rsch Data Anl 2",
            CtJobFamily = "Research",
            CtJobFunction = "Research Data Analysis",
            PersonnelProgram = "PSS",
            CorpusSize = 9,
            RepresentativeSummary = "Analyzes research data for the department.",
            EnvelopeSource = EnvelopeSource.Claude,
        };

        profile.Distributions.Add(new ProfileDistribution
        {
            Field = DistributionField.SalaryGrade, Consensus = "Grade 21", Agreement = 0.8,
        });
        profile.Distributions.Add(new ProfileDistribution
        {
            Field = DistributionField.FlsaStatus, Consensus = "Exempt", Agreement = 0.9,
        });
        profile.Distributions.Add(new ProfileDistribution
        {
            Field = DistributionField.UnionCode, Consensus = "99 - Non-Represented (PPSM)", Agreement = 1.0,
        });

        var envelope = new JobEnvelope
        {
            ClassProfileId = 1,
            Summary = "Applies professional research data concepts to analyse study data.",
            ScopeStatement = "Works under general supervision within an established program.",
        };

        var analysis = new EnvelopeResponsibility
        {
            Ordinal = 0, FunctionName = "DATA ANALYSIS", PctTime = 60,
        };
        analysis.Duties.Add(new EnvelopeDuty { Ordinal = 0, Text = "Cleans and validates study datasets." });
        analysis.Duties.Add(new EnvelopeDuty { Ordinal = 1, Text = "Runs statistical analyses." });

        var reporting = new EnvelopeResponsibility
        {
            Ordinal = 1, FunctionName = "REPORTING", PctTime = 40,
        };
        reporting.Duties.Add(new EnvelopeDuty { Ordinal = 0, Text = "Prepares written summaries of findings." });

        envelope.KeyResponsibilities.Add(analysis);
        envelope.KeyResponsibilities.Add(reporting);

        void Item(EnvelopeListKind kind, int ordinal, string text) =>
            envelope.Items.Add(new EnvelopeListItem { Kind = kind, Ordinal = ordinal, Text = text });

        Item(EnvelopeListKind.MinQualification, 0, "Working knowledge of statistical methods.");
        Item(EnvelopeListKind.MinQualification, 1, "Skill in written communication.");
        Item(EnvelopeListKind.RequiredCertification, 0, "None required.");
        Item(EnvelopeListKind.OutOfEnvelope, 0, "Supervising career staff.");
        Item(EnvelopeListKind.OutOfEnvelope, 1, "Managing a budget.");
        Item(EnvelopeListKind.ConditionOfEmployment, 0, "Background check required.");
        Item(EnvelopeListKind.PhysicalRequirement, 0, "Work is sedentary, with reasonable accommodation available.");

        profile.Envelope = envelope;
        return profile;
    }

    public static BuildInputs Inputs() => new()
    {
        WorkingTitle = "Evaluation Analyst",
        Department = "Office of Evaluation",
        KeptResponsibilities =
        [
            new JdKeyResponsibility
            {
                FunctionName = "DATA ANALYSIS",
                PctTime = 60,
                Duties = ["Cleans and validates study datasets.", "Runs statistical analyses."],
            },
            new JdKeyResponsibility
            {
                FunctionName = "REPORTING",
                PctTime = 40,
                Duties = ["Prepares written summaries of findings."],
            },
        ],
        KeptCerts = [],
        KeptEducation = ["Bachelor's degree or equivalent experience."],
        KeptWorkExperience = ["Two years of related experience."],
        KeptMinKSA = ["Working knowledge of statistical methods."],
        KeptPrefKSA = [],
        KeptWorkEnvironment = ["Standard office environment."],
        AddedItems = [],
        Notes = "",
    };

    /// <summary>The 11 rules migrated from the POC's data/compliance-rules.json.</summary>
    public static List<ComplianceRule> Rules() =>
    [
        Rule("inclusive", @"\brockstar\b", "expert", "Replace startup jargon with professional language"),
        Rule("inclusive", @"\bninja\b", "specialist", "Replace jargon with professional language"),
        Rule("inclusive", @"\bguru\b", "expert", "Replace cultural-appropriation term with professional language"),
        Rule("inclusive", @"\bchairman\b", "chair", "Gender-neutral title"),
        Rule("inclusive", @"\bsalesman\b", "salesperson", "Gender-neutral title"),
        Rule("inclusive", @"\bmanpower\b", "workforce", "Gender-neutral term"),
        Rule("inclusive", @"\bman-hours\b", "work-hours", "Gender-neutral measurement"),
        Rule("inclusive", @"\bhe or she\b", "they", "Singular they for inclusive pronouns"),
        Rule("inclusive", @"\bhis or her\b", "their", "Singular they for inclusive pronouns"),
        Rule("EEO", @"\byoung\b", "early-career", "Avoid age-coded language"),
        Rule("EEO", @"\bdigital native\b", "skilled with digital tools", "Avoid age-coded language"),
    ];

    private static ComplianceRule Rule(string key, string pattern, string replacement, string reason) => new()
    {
        Key = key,
        Pattern = pattern,
        Replacement = replacement,
        Reason = reason,
        Enabled = true,
    };

    public static CoreJd Draft() => new()
    {
        JobSummary = "Analyzes study data and reports findings.",
        KeyResponsibilities =
        [
            new JdKeyResponsibility
            {
                FunctionName = "DATA ANALYSIS",
                PctTime = 60,
                Duties = ["Cleans datasets.", "Runs analyses."],
            },
            new JdKeyResponsibility
            {
                FunctionName = "REPORTING",
                PctTime = 40,
                Duties = ["Writes summaries."],
            },
        ],
        LicensesCertifications = ["Valid driver's license."],
        Education = ["Bachelor's degree."],
        WorkExperience = ["Two years of experience."],
        MinKSA = ["Statistical knowledge."],
        PrefKSA = ["Survey design."],
        ConditionsOfEmployment = ["Background check."],
        WorkEnvironment = ["Office environment."],
        PhysicalRequirements = ["Sedentary work."],
    };
}

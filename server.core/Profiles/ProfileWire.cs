using Server.Core.Domain;
using Server.Core.Titles;
using Server.Core.Jd;
using Server.Core.Standards;

namespace Server.Core.Profiles;

/// <summary>
/// The envelope as it crosses the wire: flat named lists, duties as plain strings, no ids.
///
/// This is the POC's JobEnvelope shape, and the client is written against it. The normalized
/// entity is not a usable wire format — its list sections are one <c>items</c> table discriminated
/// by kind, every row drags an id and a null back-reference, and a client posting
/// <c>duties: ["…"]</c> back cannot bind to <c>List&lt;EnvelopeDuty&gt;</c> at all. So every
/// endpoint that sends or receives an envelope converts here, at the boundary.
/// </summary>
public sealed class EnvelopeWire
{
    public string Summary { get; set; } = "";
    public string ScopeStatement { get; set; } = "";
    public List<JdKeyResponsibility> KeyResponsibilities { get; set; } = [];
    public List<string> RequiredCertifications { get; set; } = [];
    public List<string> Education { get; set; } = [];
    public List<string> WorkExperience { get; set; } = [];
    public List<string> MinQualifications { get; set; } = [];
    public List<string> PrefQualifications { get; set; } = [];
    public List<string> ConditionsOfEmployment { get; set; } = [];
    public List<string> WorkEnvironment { get; set; } = [];
    public List<string> PhysicalRequirements { get; set; } = [];
    public List<string> OutOfEnvelope { get; set; } = [];

    public int PctTotal() => KeyResponsibilities.Sum(r => r.PctTime);

    public static EnvelopeWire From(JobEnvelope e)
    {
        List<string> Items(EnvelopeListKind kind) =>
            [.. e.Items.Where(i => i.Kind == kind).OrderBy(i => i.Ordinal).Select(i => i.Text)];

        return new EnvelopeWire
        {
            Summary = e.Summary,
            ScopeStatement = e.ScopeStatement,
            KeyResponsibilities =
            [
                .. e.KeyResponsibilities.OrderBy(r => r.Ordinal).Select(r => new JdKeyResponsibility
                {
                    FunctionName = r.FunctionName,
                    PctTime = r.PctTime,
                    Duties = [.. r.Duties.OrderBy(d => d.Ordinal).Select(d => d.Text)],
                }),
            ],
            RequiredCertifications = Items(EnvelopeListKind.RequiredCertification),
            Education = Items(EnvelopeListKind.Education),
            WorkExperience = Items(EnvelopeListKind.WorkExperience),
            MinQualifications = Items(EnvelopeListKind.MinQualification),
            PrefQualifications = Items(EnvelopeListKind.PrefQualification),
            ConditionsOfEmployment = Items(EnvelopeListKind.ConditionOfEmployment),
            WorkEnvironment = Items(EnvelopeListKind.WorkEnvironment),
            PhysicalRequirements = Items(EnvelopeListKind.PhysicalRequirement),
            OutOfEnvelope = Items(EnvelopeListKind.OutOfEnvelope),
        };
    }

    /// <summary>
    /// A new, unattached entity. Ordinals come from list position, which is the only ordering the
    /// wire carries. Percentages are copied as sent — validating them is the caller's decision.
    /// </summary>
    public JobEnvelope ToEntity()
    {
        var envelope = new JobEnvelope { Summary = Summary, ScopeStatement = ScopeStatement };

        for (var i = 0; i < KeyResponsibilities.Count; i++)
        {
            var src = KeyResponsibilities[i];
            var r = new EnvelopeResponsibility { Ordinal = i, FunctionName = src.FunctionName, PctTime = src.PctTime };
            for (var j = 0; j < src.Duties.Count; j++)
            {
                r.Duties.Add(new EnvelopeDuty { Ordinal = j, Text = src.Duties[j] });
            }

            envelope.KeyResponsibilities.Add(r);
        }

        void AddItems(EnvelopeListKind kind, List<string> items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                envelope.Items.Add(new EnvelopeListItem { Kind = kind, Ordinal = i, Text = items[i] });
            }
        }

        AddItems(EnvelopeListKind.RequiredCertification, RequiredCertifications);
        AddItems(EnvelopeListKind.Education, Education);
        AddItems(EnvelopeListKind.WorkExperience, WorkExperience);
        AddItems(EnvelopeListKind.MinQualification, MinQualifications);
        AddItems(EnvelopeListKind.PrefQualification, PrefQualifications);
        AddItems(EnvelopeListKind.ConditionOfEmployment, ConditionsOfEmployment);
        AddItems(EnvelopeListKind.WorkEnvironment, WorkEnvironment);
        AddItems(EnvelopeListKind.PhysicalRequirement, PhysicalRequirements);
        AddItems(EnvelopeListKind.OutOfEnvelope, OutOfEnvelope);

        return envelope;
    }
}

/// <summary>A consensus attribute as the client reads it.</summary>
public sealed class DistributionWire
{
    public double Agreement { get; set; }
    public string? Consensus { get; set; }
}

/// <summary>
/// One class as the class, build, edit and standard screens read it — the POC's profile subset
/// those screens use, with each consensus attribute under its own name.
/// </summary>
public sealed class ClassProfileView
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public string CtJobFamily { get; set; } = "";
    public string CtJobFunction { get; set; } = "";
    public string PersonnelProgram { get; set; } = "";
    public int CorpusSize { get; set; }
    public EnvelopeSource? EnvelopeSource { get; set; }
    public EnvelopeWire? Envelope { get; set; }
    public DistributionWire SalaryGrade { get; set; } = new();
    public DistributionWire FlsaStatus { get; set; } = new();
    public DistributionWire UnionCode { get; set; } = new();

    /// <summary>The bargaining unit to show: the title's union suffix, else the corpus consensus.</summary>
    public string? BargainingUnit { get; set; }

    /// <summary>Union-represented.</summary>
    public bool IsRepresented { get; set; }

    /// <summary>
    /// Why this class's positions can't supervise — not a supervisor or manager class, or union-
    /// represented — or null when they can. The build locks Supervises at No when it is set.
    /// </summary>
    public string? CannotSupervise { get; set; }

    /// <summary>A supervisor or manager class: the build starts Supervises and Leads at Yes.</summary>
    public bool IsSupervisory { get; set; }
    public DistributionWire Supervises { get; set; } = new();
    public DistributionWire Leads { get; set; } = new();
    public DistributionWire WorksOutdoorsOver50pct { get; set; } = new();

    /// <summary>A Health Center (HC) class: for Health Center positions only.</summary>
    public bool HealthCenterOnly { get; set; }

    /// <summary>The class's other version — its HC code, or the regular one — when it has one.</summary>
    public HealthCenterTwin? HealthCenterTwin { get; set; }

    /// <summary>How much an author may reasonably add, in percent: 30 for senior and supervisory classes, else 10.</summary>
    public int CustomizationTarget { get; set; } = 10;

    /// <summary>Null for most classes — a gap in standards coverage, not a matching failure.</summary>
    public ClassStandardRecord? Standard { get; set; }

    public static ClassProfileView From(
        ClassProfile p, ClassStandardRecord? standard, HealthCenterTwin? healthCenterTwin = null)
    {
        DistributionWire Dist(DistributionField field)
        {
            var d = p.Distributions.FirstOrDefault(x => x.Field == field);
            if (d == null)
            {
                return new DistributionWire();
            }

            return new DistributionWire { Agreement = d.Agreement, Consensus = d.Consensus };
        }

        return new ClassProfileView
        {
            Slug = p.Slug,
            Title = p.Title,
            UcJobCode = p.UcJobCode,
            CtJobFamily = p.CtJobFamily,
            CtJobFunction = p.CtJobFunction,
            PersonnelProgram = p.PersonnelProgram,
            CorpusSize = p.CorpusSize,
            EnvelopeSource = p.EnvelopeSource,
            Envelope = p.Envelope != null ? EnvelopeWire.From(p.Envelope) : null,
            SalaryGrade = Dist(DistributionField.SalaryGrade),
            FlsaStatus = Dist(DistributionField.FlsaStatus),
            UnionCode = Dist(DistributionField.UnionCode),
            BargainingUnit = BargainingUnits.For(p),
            IsRepresented = BargainingUnits.IsRepresented(p),
            CannotSupervise = Supervision.WhyNot(p),
            IsSupervisory = Supervision.IsSupervisory(p.Title),
            HealthCenterOnly = HealthCenter.IsHealthCenter(p.Title),
            HealthCenterTwin = healthCenterTwin,
            CustomizationTarget = Customization.TargetPct(p.Title),
            Supervises = Dist(DistributionField.Supervises),
            Leads = Dist(DistributionField.Leads),
            WorksOutdoorsOver50pct = Dist(DistributionField.WorksOutdoorsOver50pct),
            Standard = standard,
        };
    }
}

/// <summary>A stored coverage report without its entity plumbing.</summary>
public sealed class CoverageView
{
    public int N { get; set; }
    public double MeanCoverage { get; set; }
    public double WellCoveredPct { get; set; }
    public List<JdCoverageView> PerJd { get; set; } = [];

    public static CoverageView From(CoverageReport c) => new()
    {
        N = c.N,
        MeanCoverage = c.MeanCoverage,
        WellCoveredPct = c.WellCoveredPct,
        PerJd =
        [
            .. c.PerJd.OrderBy(j => j.Ordinal).Select(j => new JdCoverageView
            {
                SourceFile = j.SourceFile,
                CoveredPct = j.CoveredPct,
                Uncovered =
                [
                    .. j.Uncovered.OrderBy(u => u.Ordinal)
                        .Select(u => new IdiosyncraticFunction { Name = u.Name, Pct = u.Pct }),
                ],
            }),
        ],
    };
}

public sealed class JdCoverageView
{
    public string SourceFile { get; set; } = "";
    public double CoveredPct { get; set; }
    public List<IdiosyncraticFunction> Uncovered { get; set; } = [];
}

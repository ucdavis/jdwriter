using System.ComponentModel;
using System.Text.Json.Serialization;
using Server.Core.Domain;

namespace Server.Core.Jd;

/// <summary>
/// One key responsibility of a drafted job description: a function, its share of time, and duty
/// bullets. Mirrors the HRTMS/JDXpert format so the result can be handed back to HR directly.
/// </summary>
public sealed class JdKeyResponsibility
{
    [Description("Broad function name.")]
    public string FunctionName { get; set; } = "";

    [Description("Percent of time; across all responsibilities these must sum to 100.")]
    public int PctTime { get; set; }

    [Description("Duty bullets under this function.")]
    public List<string> Duties { get; set; } = [];

    public JdKeyResponsibility Clone() => new()
    {
        FunctionName = FunctionName,
        PctTime = PctTime,
        Duties = [.. Duties],
    };
}

/// <summary>
/// The structured, UC-standard job description.
///
/// Mutable by design: the compliance passes address individual text lines by index and write back
/// through <see cref="ComplianceEngine.TextFields"/>, which is what lets a model return only the
/// lines it wants changed instead of re-emitting the whole document.
/// </summary>
public sealed class Jd
{
    [Description("2-3 sentence Job Summary.")]
    public string JobSummary { get; set; } = "";

    [Description("Functions with % time (summing to 100) and bulleted duties, ordered by % time.")]
    public List<JdKeyResponsibility> KeyResponsibilities { get; set; } = [];

    [Description("Licenses & certifications required.")]
    public List<string> LicensesCertifications { get; set; } = [];

    [Description("Education requirement(s).")]
    public List<string> Education { get; set; } = [];

    [Description("Minimum work experience.")]
    public List<string> WorkExperience { get; set; } = [];

    [Description("Minimum knowledge, skills & abilities.")]
    public List<string> MinKSA { get; set; } = [];

    [Description("Preferred knowledge, skills & abilities.")]
    public List<string> PrefKSA { get; set; } = [];

    [Description("Conditions of employment.")]
    public List<string> ConditionsOfEmployment { get; set; } = [];

    [Description("Work environment.")]
    public List<string> WorkEnvironment { get; set; } = [];

    [Description("Physical requirements framed with reasonable-accommodation (ADA) language.")]
    public List<string> PhysicalRequirements { get; set; } = [];

    /// <summary>
    /// A deep copy. The compliance passes mutate through bound setters, so every pass works on its
    /// own copy — otherwise an edit applied to a draft would also alter the "before" text recorded
    /// in the audit trail.
    /// </summary>
    public Jd Clone() => new()
    {
        JobSummary = JobSummary,
        KeyResponsibilities = [.. KeyResponsibilities.Select(r => r.Clone())],
        LicensesCertifications = [.. LicensesCertifications],
        Education = [.. Education],
        WorkExperience = [.. WorkExperience],
        MinKSA = [.. MinKSA],
        PrefKSA = [.. PrefKSA],
        ConditionsOfEmployment = [.. ConditionsOfEmployment],
        WorkEnvironment = [.. WorkEnvironment],
        PhysicalRequirements = [.. PhysicalRequirements],
    };
}

/// <summary>
/// One entry in the compliance audit trail, as the assembler produces it.
///
/// Named ...Record to keep it distinct from the <see cref="Domain.ComplianceEdit"/> EF entity; this
/// is the in-flight result, that is the persisted row.
/// </summary>
public sealed class ComplianceEditRecord
{
    /// <summary>
    /// Prefix marking an entry that records a rule which FAILED to apply rather than one that did.
    ///
    /// A rule-failure entry is deliberately shaped so it cannot be mistaken for an edit: Before and
    /// After are both empty (every real edit has two different non-empty values) and Section names
    /// the rule rather than a document path. The marker is what lets a reviewer or a UI filter for
    /// them reliably instead of pattern-matching prose.
    /// </summary>
    public const string RuleFailedMarker = "RULE NOT APPLIED";

    /// <summary>Where the edit landed, e.g. "keyResponsibilities[2].duties[0]".</summary>
    public string Section { get; set; } = "";

    public ComplianceEditSource Source { get; set; }
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public string Reason { get; set; } = "";

    /// <summary>
    /// True when this entry records a rule that could not be applied, rather than a change that was
    /// made. Callers showing an audit trail must surface these differently — a broken compliance
    /// rule on an HR document is the case that matters most and the easiest to overlook.
    /// </summary>
    public bool IsRuleFailure =>
        Reason.StartsWith(RuleFailedMarker, StringComparison.Ordinal);
}

/// <summary>A finished draft plus the fixed attributes carried from the class standard.</summary>
public sealed class AssembledJd
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string WorkingTitle { get; set; } = "";
    public string Department { get; set; } = "";
    public string UcJobCode { get; set; } = "";

    /// <summary>
    /// Null when the class has no consensus value. A class bootstrapped from a standard with no
    /// corpus has nothing authoritative to copy, and inventing one would be worse than blank.
    /// </summary>
    public string? SalaryGrade { get; set; }
    public string? FlsaStatus { get; set; }
    public string? BargainingUnit { get; set; }

    public Jd Jd { get; set; } = new();

    /// <summary>
    /// Percentage points of the author's time still unaccounted for: 100 minus the sum of the kept
    /// shares. Zero when the job is fully allocated; negative when over-allocated past 100.
    ///
    /// Kept shares are carried through EXACTLY as the author set them — never rescaled. Rescaling
    /// would silently inflate (a kept 50% publishing as 71% with nobody told the number moved), and
    /// ignoring the shortfall would publish an HRTMS-invalid job description. So the gap is reported
    /// and <see cref="CanPublish"/> withholds the draft until the author reallocates it.
    ///
    /// A caller showing this to an author should say which way it runs: a positive value is time not
    /// yet assigned, a negative one is time assigned twice.
    /// </summary>
    public int UnallocatedPct { get; set; }

    /// <summary>
    /// Whether this draft may be persisted. False while any of the author's time is unaccounted
    /// for, in either direction.
    ///
    /// A draft that cannot be published is still worth returning: the author needs to SEE the work
    /// in order to reallocate, so assembly succeeds and only persistence refuses.
    /// </summary>
    public bool CanPublish => UnallocatedPct == 0;

    /// <summary>Draft or Ready, from the allocation. Mirrors what is saved.</summary>
    public AuthoredJdStatus Status => CanPublish ? AuthoredJdStatus.Ready : AuthoredJdStatus.Draft;

    /// <summary>The saved record this assembly was written to; send it back to update it.</summary>
    public int? AuthoredJdId { get; set; }

    public List<ComplianceEditRecord> ComplianceEdits { get; set; } = [];

    /// <summary>
    /// The entity for this draft: a new record carrying everything the author produced, its status,
    /// and the author's additions beyond the envelope.
    ///
    /// Drafts are saved — every assembly is kept, because those are what will be fed back into the
    /// corpus. The 100% invariant survives as the status: <see cref="AuthoredJdStatus.Ready"/> is
    /// assigned here from <see cref="CanPublish"/> and nowhere else, so a JD whose time is under- or
    /// over-allocated can only ever be a Draft. Nothing is rescaled to make it Ready.
    /// </summary>
    public AuthoredJd ToEntity(
        int classProfileId,
        int? createdByUserId = null,
        BuildInputs? inputs = null,
        EnvelopeSource? envelopeSource = null)
    {
        var now = DateTimeOffset.UtcNow;

        var entity = new AuthoredJd
        {
            ClassProfileId = classProfileId,
            Title = Title,
            WorkingTitle = WorkingTitle,
            Department = Department,
            UcJobCode = UcJobCode,
            SalaryGrade = SalaryGrade,
            FlsaStatus = FlsaStatus,
            BargainingUnit = BargainingUnit,
            JobSummary = Jd.JobSummary,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            UpdatedAt = now,
            Status = CanPublish ? AuthoredJdStatus.Ready : AuthoredJdStatus.Draft,
            UnallocatedPct = UnallocatedPct,
            Notes = inputs?.Notes ?? "",
            EnvelopeSource = envelopeSource,
        };

        for (var i = 0; i < Jd.KeyResponsibilities.Count; i++)
        {
            var r = Jd.KeyResponsibilities[i];
            var responsibility = new AuthoredJdResponsibility
            {
                Ordinal = i,
                FunctionName = r.FunctionName,
                PctTime = r.PctTime,
            };

            for (var j = 0; j < r.Duties.Count; j++)
            {
                responsibility.Duties.Add(new AuthoredJdDuty { Ordinal = j, Text = r.Duties[j] });
            }

            entity.KeyResponsibilities.Add(responsibility);
        }

        void AddItems(AuthoredJdListKind kind, List<string> items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                entity.Items.Add(new AuthoredJdListItem { Kind = kind, Ordinal = i, Text = items[i] });
            }
        }

        AddItems(AuthoredJdListKind.LicensesCertifications, Jd.LicensesCertifications);
        AddItems(AuthoredJdListKind.Education, Jd.Education);
        AddItems(AuthoredJdListKind.WorkExperience, Jd.WorkExperience);
        AddItems(AuthoredJdListKind.MinKsa, Jd.MinKSA);
        AddItems(AuthoredJdListKind.PrefKsa, Jd.PrefKSA);
        AddItems(AuthoredJdListKind.ConditionOfEmployment, Jd.ConditionsOfEmployment);
        AddItems(AuthoredJdListKind.WorkEnvironment, Jd.WorkEnvironment);
        AddItems(AuthoredJdListKind.PhysicalRequirement, Jd.PhysicalRequirements);
        AddItems(AuthoredJdListKind.AuthorAddition, inputs?.AddedItems ?? []);

        for (var i = 0; i < ComplianceEdits.Count; i++)
        {
            var e = ComplianceEdits[i];
            entity.ComplianceEdits.Add(new Domain.ComplianceEdit
            {
                Ordinal = i,
                Section = e.Section,
                Source = e.Source,
                Before = e.Before,
                After = e.After,
                Reason = e.Reason,
            });
        }

        return entity;
    }
}

/// <summary>Whether a request still fits the class it was started from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EnvelopeVerdict>))]
public enum EnvelopeVerdict
{
    [JsonStringEnumMemberName("in_envelope")]
    InEnvelope,

    [JsonStringEnumMemberName("borderline")]
    Borderline,

    [JsonStringEnumMemberName("out_of_envelope")]
    OutOfEnvelope,
}

/// <summary>Envelope-fit assessment for a manager's request.</summary>
public sealed class EnvelopeCheck
{
    [Description("Whether the request fits this class.")]
    public EnvelopeVerdict Verdict { get; set; }

    [Description("Which out-of-envelope signals (if any) the request triggered.")]
    public List<string> MatchedSignals { get; set; } = [];

    [Description("If out_of_envelope, the name of a better-matching UC class; otherwise empty string.")]
    public string SuggestedClass { get; set; } = "";

    [Description("If one of the provided KNOWN INGESTED CLASSES is the better match, its exact slug; otherwise empty string.")]
    public string SuggestedSlug { get; set; } = "";

    [Description("1-2 sentence explanation for the verdict.")]
    public string Rationale { get; set; } = "";
}

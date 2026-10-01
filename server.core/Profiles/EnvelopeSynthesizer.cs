using System.ComponentModel;
using Server.Core.Ai;
using Server.Core.Domain;
using Server.Core.Standards;

namespace Server.Core.Profiles;

/// <summary>
/// Synthesizes the readable "Job Envelope" — a generic, standardized JD template for one class,
/// broad enough that most positions could adopt it with under 10% unit-specific edits. Ported from
/// the POC's src/lib/profile/describe.ts.
///
/// The division of labour is deliberate and must survive: function NAMES and DUTIES come from the
/// model (generic, sanded of specifics); the % time per function comes from the deterministic
/// consolidated statistics. The model is never asked what share of time a function takes.
/// </summary>
public sealed class EnvelopeSynthesizer
{
    private readonly IStructuredLlm _llm;
    private readonly IStandardLookup _standards;

    public EnvelopeSynthesizer(IStructuredLlm llm, IStandardLookup standards)
    {
        _llm = llm;
        _standards = standards;
    }

    // ---------------------------------------------------------------- model contracts

    private sealed class RespShape
    {
        [Description("Broad, generic function name (no specific site/unit/crop).")]
        public string FunctionName { get; set; } = "";

        [Description("3-6 generic duty bullets for this function, broad enough to cover the class.")]
        public List<string> Duties { get; set; } = [];
    }

    private sealed class EnvelopeShape
    {
        [Description("2-3 sentence plain-language description of the standard role.")]
        public string Summary { get; set; } = "";

        [Description("One paragraph on scope: level, supervision, salary grade, FLSA, unit, conditions.")]
        public string ScopeStatement { get; set; } = "";

        [Description("One entry per provided function category, in the SAME order given. These are the Key Responsibilities.")]
        public List<RespShape> Responsibilities { get; set; } = [];

        [Description("Licenses/certifications commonly required (e.g. driver's license, forklift).")]
        public List<string> RequiredCertifications { get; set; } = [];

        [Description("Education requirement(s), broad and generic. If the corpus shows none required, return a single item like 'No formal education required; equivalent experience accepted.'")]
        public List<string> Education { get; set; } = [];

        [Description("1-3 broad minimum work-experience statements (generalized).")]
        public List<string> WorkExperience { get; set; } = [];

        [Description("Up to 7 broad, generic minimum qualifications (KSAs). Never more than 7.")]
        public List<string> MinQualifications { get; set; } = [];

        [Description("2-4 preferred qualifications.")]
        public List<string> PrefQualifications { get; set; } = [];

        [Description("2-4 broad work-environment statements (schedule, indoor/outdoor, conditions).")]
        public List<string> WorkEnvironment { get; set; } = [];

        [Description("2-3 physical requirement bullets framed with reasonable-accommodation (ADA) language, appropriate to this class.")]
        public List<string> PhysicalRequirements { get; set; } = [];

        [Description("3-6 concrete signals that a request falls OUTSIDE this class and suggests a different class.")]
        public List<string> OutOfEnvelope { get; set; } = [];
    }

    private sealed class StdRespShape
    {
        [Description("Broad, generic function name.")]
        public string FunctionName { get; set; } = "";

        [Description("ESTIMATED share of time for this function (best judgment); all must sum to ~100.")]
        public double PctTime { get; set; }

        [Description("3-6 generic duty bullets for this function.")]
        public List<string> Duties { get; set; } = [];
    }

    private sealed class StdEnvelopeShape
    {
        [Description("2-3 sentence plain-language description of the standard role.")]
        public string Summary { get; set; } = "";

        [Description("One paragraph on scope: level, supervision, grade, FLSA, conditions.")]
        public string ScopeStatement { get; set; } = "";

        [Description("Group the standard's key responsibilities into 2-4 broad functions.")]
        public List<StdRespShape> Responsibilities { get; set; } = [];

        public List<string> RequiredCertifications { get; set; } = [];
        public List<string> Education { get; set; } = [];
        public List<string> WorkExperience { get; set; } = [];

        [Description("Up to 7 broad minimum qualifications (KSAs).")]
        public List<string> MinQualifications { get; set; } = [];

        public List<string> PrefQualifications { get; set; } = [];
        public List<string> WorkEnvironment { get; set; } = [];

        [Description("2-3 ADA-framed physical requirement bullets.")]
        public List<string> PhysicalRequirements { get; set; } = [];

        [Description("3-6 signals a request belongs to a different class.")]
        public List<string> OutOfEnvelope { get; set; } = [];
    }

    // ---------------------------------------------------------------- prompts

    public const string CorpusSystem = """
        You are a UC Davis HR classification analyst. You are given aggregate data
        computed from a corpus of real job descriptions for a single UC job class. Write the
        standardized, GENERIC "Job Envelope" — a template broad enough that most positions in this
        classification could adopt it with less than 10% unit-specific edits. It must mirror the real
        JD structure: Key Responsibilities as functions, each with bulleted duties.

        Rules:
        - GENERALIZE / sand off specifics. The corpus mentions particular fields, crops, greenhouses,
          ranches, units, programs, buildings, and equipment. STRIP all of that. Write language broad
          enough to cover the full range of positions. E.g. "irrigate, weed, and harvest a variety of
          crops using appropriate methods and equipment", NOT "drip irrigation of row crops at Russell
          Ranch." Never name a specific site, unit, crop, or program.
        - Return ONE responsibility per provided function category, in the SAME order. Give each a
          broad functionName and 3-6 generic duty bullets.
        - Minimum qualifications: no more than 7, broad and generic.
        - Ground everything in the provided data; do not invent capabilities the data doesn't support.
        - Ignore boilerplate (smoking policy, principles-of-community, "performs other duties as
          assigned", background-check text). Keep language inclusive and ADA/EEO-aware.
        - outOfEnvelope: the boundary signals that define when a request has drifted out of this class.

        If an OFFICIAL UC JOB STANDARD is provided, treat it as the AUTHORITATIVE baseline for the
        qualification sections (minQualifications/KSAs, education, certifications) and the scope: prefer
        its official language, and reconcile it with what the corpus shows. But the Key Responsibilities
        (% time · function · duties) must stay grounded in the ACTUAL JDs — the standard's responsibilities
        are generic scope; the corpus reflects what these positions really do.
        """;

    public const string StandardOnlySystem = """
        You are a UC Davis HR classification analyst. Build a standardized, GENERIC
        "Job Envelope" for a class using ONLY its OFFICIAL UC JOB STANDARD — there is no corpus of real
        job descriptions yet, so ground everything in the standard.

        Rules:
        - Group the standard's key responsibilities into 2-4 BROAD functions, each with a generic
          functionName and 3-6 duty bullets. Assign each function an APPROXIMATE % time using your
          professional judgment; the values must sum to ~100. These are ESTIMATES to be refined once real
          JDs are ingested — do not present them as observed.
        - Use the standard's KSAs, education, licenses/certifications, and scope as the AUTHORITATIVE
          baseline for the qualification sections. Keep minQualifications to at most 7, broad and generic.
        - Generalize / sand off any unit-specific specifics. Keep language inclusive and ADA/EEO-aware.
        - outOfEnvelope: the boundary signals that define when a request has drifted out of this class.
        """;

    /// <summary>
    /// Standard UC boilerplate, identical across every UC job description. Attached in code rather
    /// than asked of the model — it is fixed policy text, not a judgement.
    /// </summary>
    public static readonly string[] StandardConditions =
    [
        "This is a critical position, as defined by UC policy, and employment is contingent upon successfully clearing a criminal background check; the check may include drug screening, medical evaluation, and/or a functional capacity assessment where applicable.",
        "Smoke and Tobacco Free Environment: smoking and tobacco use are strictly prohibited on all University-owned or leased properties.",
        "Employees are expected to adhere to the UC Davis Principles of Community.",
    ];

    private static double RoundHalfUp(double x) => Math.Floor(x + 0.5);

    private static string Consensus(ClassProfile p, DistributionField field) =>
        p.Distributions.FirstOrDefault(d => d.Field == field)?.Consensus ?? "";

    /// <summary>
    /// Yes/no/unspecified counts for a tri-state attribute. "Unspecified" is reported rather than
    /// folded into "no", because for supervision it is the majority answer and the difference
    /// matters to how the scope statement reads.
    /// </summary>
    private static string DescribeBool(ClassProfile p, DistributionField field)
    {
        var d = p.Distributions.FirstOrDefault(x => x.Field == field);
        var yes = d?.Values.FirstOrDefault(v => v.Value == "true")?.Count ?? 0;
        var no = d?.Values.FirstOrDefault(v => v.Value == "false")?.Count ?? 0;
        var unknown = d?.Values.FirstOrDefault(v => v.Value is null)?.Count ?? 0;
        return $"{yes} yes / {no} no / {unknown} unspecified";
    }

    private static List<ProfileQualItem> Quals(ClassProfile p, ProfileQualKind kind) =>
        p.Qualifications.Where(q => q.Kind == kind).OrderBy(q => q.Ordinal).ToList();

    private static string FreqLines(IEnumerable<ProfileQualItem> items, int take)
    {
        var lines = items.Take(take)
            .Select(e => $"- ({RoundHalfUp(e.Freq * 100):0}%) {e.Text}")
            .ToList();
        return lines.Count > 0 ? string.Join('\n', lines) : "- none recorded";
    }

    private static string BulletLines(IEnumerable<string> items)
    {
        var lines = items.Select(t => $"- {t}").ToList();
        return lines.Count > 0 ? string.Join('\n', lines) : "- none";
    }

    /// <summary>
    /// If an official standard exists, include it as the authoritative baseline for qualifications
    /// and scope. Returns "" when none — the caller's template already provides the surrounding
    /// blank line.
    /// </summary>
    public static string StandardBlock(ClassStandardRecord? s)
    {
        if (s is null)
        {
            return "";
        }

        return $"""

            OFFICIAL UC JOB STANDARD (authoritative baseline for quals & scope):
            Generic scope: {s.GenericScope}
            Custom scope: {s.CustomScope}
            Standard KSAs:
            {BulletLines(s.Ksa)}
            Standard education:
            {BulletLines(s.Education)}
            Standard licenses/certifications:
            {BulletLines(s.Licenses)}
            Grade: {s.Grade} · FLSA: {s.Flsa} · Pers Prog: {s.PersProg} · Union: {s.Union}

            """;
    }

    /// <summary>Exposed for prompt-assembly tests: this is exactly what the model receives.</summary>
    public static string BuildCorpusUser(ClassProfile p, ClassStandardRecord? standard)
    {
        var cats = p.ConsolidatedFunctions.OrderBy(c => c.Ordinal).ToList();

        var fns = cats.Count > 0
            ? string.Join('\n', cats.Select((f, i) =>
                $"{i + 1}. {f.Name} — {f.TemplatePct:0}% of time (in {RoundHalfUp(f.Prevalence * 100):0}% of JDs). "
                + $"Example duties to generalize: {string.Join(" | ", f.SampleDuties.OrderBy(d => d.Ordinal).Take(6).Select(d => d.Text))}"))
            : string.Join('\n', p.Functions.OrderBy(f => f.Ordinal)
                // Either common enough to matter, or big enough to matter where it appears.
                .Where(f => f.Prevalence >= 0.15 || f.PctMean >= 20)
                .Select((f, i) =>
                    $"{i + 1}. {f.Name} — ~{RoundHalfUp(f.PctMean):0}% time. "
                    + $"Sample: {string.Join(" | ", f.SampleDuties.OrderBy(d => d.Ordinal).Take(5).Select(d => d.Text))}"));

        var certList = cats.Count > 0 || p.ConsolidatedQuals.Count > 0
            ? p.ConsolidatedQuals.Where(q => q.Kind == ConsolidatedQualKind.Certification)
                .OrderBy(q => q.Ordinal).Select(q => q.Name).ToList()
            : Quals(p, ProfileQualKind.License).Select(l => l.Text).ToList();

        var ksaList = p.ConsolidatedQuals.Count > 0
            ? p.ConsolidatedQuals.Where(q => q.Kind == ConsolidatedQualKind.MinQualification)
                .OrderBy(q => q.Ordinal).Select(q => q.Name).ToList()
            : Quals(p, ProfileQualKind.KsaMin).Take(10).Select(k => k.Text).ToList();

        return $"""
            CLASS: {p.Title} (UC job code {p.UcJobCode})
            Career Tracks: {p.CtJobFamily} / {p.CtJobFunction} · Program: {p.PersonnelProgram}
            Corpus size: {p.CorpusSize} job descriptions.

            CONSENSUS ATTRIBUTES:
            - Salary grade: {Or(Consensus(p, DistributionField.SalaryGrade), "varies")} · FLSA: {Or(Consensus(p, DistributionField.FlsaStatus), "varies")} · Unit: {Or(Consensus(p, DistributionField.UnionCode), "varies")}
            - Supervises employees: {DescribeBool(p, DistributionField.Supervises)} · Leads: {DescribeBool(p, DistributionField.Leads)} · Outdoors >50%: {DescribeBool(p, DistributionField.WorksOutdoorsOver50pct)}

            REPRESENTATIVE JOB SUMMARY (strip its specifics when generalizing):
            {p.RepresentativeSummary}

            FUNCTION CATEGORIES — write ONE responsibility per line item, in this order:
            {fns}

            CONSOLIDATED CERTIFICATIONS:
            {BulletLines(certList)}

            CONSOLIDATED MINIMUM QUALIFICATIONS (up to 7):
            {BulletLines(ksaList)}
            {StandardBlock(standard)}
            EDUCATION (frequency across corpus):
            {FreqLines(Quals(p, ProfileQualKind.Education), 6)}

            MINIMUM WORK EXPERIENCE (frequency):
            {FreqLines(Quals(p, ProfileQualKind.MinExperience), 8)}

            WORK ENVIRONMENT (frequency):
            {FreqLines(Quals(p, ProfileQualKind.WorkEnvironment), 8)}

            Produce the standardized, generic Job Envelope, generalizing every section.
            """;
    }

    private static string Or(string value, string fallback) =>
        string.IsNullOrEmpty(value) ? fallback : value;

    public static string BuildStandardUser(ClassStandardRecord s, StandardMeta meta) => $"""
        CLASS: {meta.Title}{(string.IsNullOrEmpty(meta.Code) ? "" : $" (UC job code {meta.Code})")}
        Career Tracks: {Or(meta.Family, "—")} / {Or(meta.Function, "—")} · Program: {Or(Or(meta.Program, s.PersProg), "—")}
        Fixed attributes: Grade {Or(s.Grade, "varies")} · FLSA {Or(s.Flsa, "varies")} · Union {Or(s.Union, "varies")}

        OFFICIAL STANDARD — GENERIC SCOPE:
        {Or(s.GenericScope, "—")}

        OFFICIAL STANDARD — CUSTOM/ADDITIONAL SCOPE:
        {Or(s.CustomScope, "—")}

        OFFICIAL STANDARD — KEY RESPONSIBILITIES (group these into 2-4 functions):
        {BulletLines(s.KeyResponsibilities)}

        OFFICIAL STANDARD — KNOWLEDGE, SKILLS & ABILITIES:
        {BulletLines(s.Ksa)}

        OFFICIAL STANDARD — EDUCATION:
        {BulletLines(s.Education)}

        OFFICIAL STANDARD — LICENSES & CERTIFICATIONS:
        {BulletLines(s.Licenses)}

        OFFICIAL STANDARD — SPECIAL CONDITIONS:
        {BulletLines(s.SpecialConditions)}

        Produce the standardized, generic Job Envelope grounded entirely in this standard.
        """;

    /// <summary>Identity for a class being bootstrapped from a standard alone.</summary>
    public sealed record StandardMeta(string Title, string Code, string Family, string Function, string Program);

    // ---------------------------------------------------------------- assembly

    /// <summary>
    /// Distribute % time so a set of responsibilities sums to exactly 100, the largest taking the
    /// rounding remainder. Recomputed in code every time — a model is never trusted to keep this
    /// arithmetic straight.
    /// </summary>
    private static void NormalizePct(List<EnvelopeResponsibility> rs)
    {
        var sum = rs.Sum(r => r.PctTime);
        if (sum == 0)
        {
            sum = 1;
        }

        foreach (var r in rs)
        {
            r.PctTime = (int)Math.Floor(r.PctTime / (double)sum * 100 + 0.5);
        }

        var diff = 100 - rs.Sum(r => r.PctTime);
        if (rs.Count > 0 && diff != 0)
        {
            var top = rs[0];
            foreach (var r in rs)
            {
                if (r.PctTime > top.PctTime)
                {
                    top = r;
                }
            }

            top.PctTime += diff;
        }
    }

    private static JobEnvelope Build(
        string summary,
        string scopeStatement,
        List<(string Name, int Pct, List<string> Duties)> responsibilities,
        IEnumerable<string> certifications,
        IEnumerable<string> education,
        IEnumerable<string> workExperience,
        IEnumerable<string> minQualifications,
        IEnumerable<string> prefQualifications,
        IEnumerable<string> workEnvironment,
        IEnumerable<string> physicalRequirements,
        IEnumerable<string> outOfEnvelope)
    {
        var envelope = new JobEnvelope { Summary = summary, ScopeStatement = scopeStatement };

        var resps = new List<EnvelopeResponsibility>();
        for (var i = 0; i < responsibilities.Count; i++)
        {
            var (name, pct, duties) = responsibilities[i];
            var r = new EnvelopeResponsibility { Ordinal = i, FunctionName = name, PctTime = pct };
            for (var j = 0; j < duties.Count; j++)
            {
                r.Duties.Add(new EnvelopeDuty { Ordinal = j, Text = duties[j] });
            }

            resps.Add(r);
        }

        NormalizePct(resps);
        envelope.KeyResponsibilities.AddRange(resps);

        void AddItems(EnvelopeListKind kind, IEnumerable<string> items)
        {
            var i = 0;
            foreach (var text in items)
            {
                envelope.Items.Add(new EnvelopeListItem { Kind = kind, Ordinal = i++, Text = text });
            }
        }

        AddItems(EnvelopeListKind.RequiredCertification, certifications);
        AddItems(EnvelopeListKind.Education, education);
        AddItems(EnvelopeListKind.WorkExperience, workExperience);
        AddItems(EnvelopeListKind.MinQualification, minQualifications);
        AddItems(EnvelopeListKind.PrefQualification, prefQualifications);
        AddItems(EnvelopeListKind.ConditionOfEmployment, StandardConditions);
        AddItems(EnvelopeListKind.WorkEnvironment, workEnvironment);
        AddItems(EnvelopeListKind.PhysicalRequirement, physicalRequirements);
        AddItems(EnvelopeListKind.OutOfEnvelope, outOfEnvelope);

        return envelope;
    }

    /// <summary>
    /// Model-synthesized envelope from a corpus. Function names and duties come from the model;
    /// the % time per function comes from the deterministic consolidated statistics.
    /// </summary>
    public async Task<JobEnvelope> SynthesizeAsync(ClassProfile p, CancellationToken ct = default)
    {
        var standard = await _standards.ForTitleAsync(p.Title, ct);

        var raw = await _llm.StructuredAsync<EnvelopeShape>(new StructuredRequest
        {
            System = CorpusSystem,
            User = BuildCorpusUser(p, standard),
            Effort = LlmEffort.Medium,
            MaxTokens = 16000,
            Label = "describe.envelope",
        }, ct);

        var cats = p.ConsolidatedFunctions.OrderBy(c => c.Ordinal).ToList();
        var fallbackPct = (int)Math.Floor(100.0 / Math.Max(1, raw.Responsibilities.Count) + 0.5);

        var responsibilities = raw.Responsibilities
            .Select((r, i) => (
                r.FunctionName,
                // The share of time is OURS, taken from the consolidated statistics rather than
                // from anything the model said.
                Pct: i < cats.Count ? (int)cats[i].TemplatePct : fallbackPct,
                r.Duties))
            .ToList();

        return Build(
            raw.Summary, raw.ScopeStatement, responsibilities,
            raw.RequiredCertifications, raw.Education, raw.WorkExperience,
            raw.MinQualifications.Take(7), raw.PrefQualifications,
            raw.WorkEnvironment, raw.PhysicalRequirements, raw.OutOfEnvelope);
    }

    /// <summary>
    /// Build an envelope from an official standard when there is no JD corpus yet. The % time is
    /// ESTIMATED by the model — the standard does not state it — and is explicitly provisional
    /// until real JDs are ingested. Everything else comes from the authoritative standard.
    /// </summary>
    public async Task<JobEnvelope> SynthesizeFromStandardAsync(
        ClassStandardRecord s, StandardMeta meta, CancellationToken ct = default)
    {
        var raw = await _llm.StructuredAsync<StdEnvelopeShape>(new StructuredRequest
        {
            System = StandardOnlySystem,
            User = BuildStandardUser(s, meta),
            Effort = LlmEffort.Medium,
            MaxTokens = 16000,
            Label = "describe.standardEnvelope",
        }, ct);

        var responsibilities = raw.Responsibilities
            .Select(r => (r.FunctionName, Pct: (int)Math.Floor(r.PctTime + 0.5), r.Duties))
            .ToList();

        return Build(
            raw.Summary, raw.ScopeStatement, responsibilities,
            raw.RequiredCertifications, raw.Education, raw.WorkExperience,
            raw.MinQualifications.Take(7), raw.PrefQualifications,
            raw.WorkEnvironment, raw.PhysicalRequirements, raw.OutOfEnvelope);
    }

    /// <summary>Deterministic fallback: map a standard straight through, with no model call.</summary>
    public static JobEnvelope DeterministicFromStandard(ClassStandardRecord s)
    {
        var sources = s.KeyResponsibilities.Count > 0 ? s.KeyResponsibilities : ["Core responsibilities"];

        var responsibilities = sources
            .Select(d => (
                Name: d.Length > 60 ? d[..57] + "…" : d,
                Pct: 100,
                Duties: new List<string> { d }))
            .ToList();

        var scope = $"Grade {Or(s.Grade, "varies")} · {Or(s.Flsa, "FLSA varies")} · unit {Or(s.Union, "varies")}. {s.CustomScope}".Trim();

        return Build(
            Or(Or(s.GenericScope, s.CustomScope), "Standardized role built from the official UC job standard."),
            scope,
            responsibilities,
            s.Licenses, s.Education, [], s.Ksa.Take(7), [], [],
            ["Must be able to perform the essential functions of the position, with or without reasonable accommodation."],
            []);
    }

    /// <summary>
    /// Deterministic fallback for a corpus profile, used when no API key is configured. Produces a
    /// usable envelope from the aggregates alone rather than failing the ingest.
    /// </summary>
    public static JobEnvelope Deterministic(ClassProfile p)
    {
        var cats = p.ConsolidatedFunctions.OrderBy(c => c.Ordinal).ToList();

        var responsibilities = cats.Count > 0
            ? cats.Select(f => (
                Name: f.Name,
                Pct: (int)f.TemplatePct,
                Duties: f.SampleDuties.OrderBy(d => d.Ordinal).Take(5).Select(d => d.Text).ToList())).ToList()
            : p.Functions.OrderBy(f => f.Ordinal)
                .Where(f => f.Prevalence >= 0.3)
                .Select(f => (
                    Name: f.Name,
                    Pct: (int)Math.Floor(f.PctMean + 0.5),
                    Duties: f.SampleDuties.OrderBy(d => d.Ordinal).Take(4).Select(d => d.Text).ToList()))
                .ToList();

        var supervisesYes = p.Distributions
            .FirstOrDefault(d => d.Field == DistributionField.Supervises)
            ?.Values.FirstOrDefault(v => v.Value == "true")?.Count ?? 0;

        var certs = p.ConsolidatedQuals.Count > 0
            ? p.ConsolidatedQuals.Where(q => q.Kind == ConsolidatedQualKind.Certification)
                .OrderBy(q => q.Ordinal).Select(q => q.Name)
            : Quals(p, ProfileQualKind.License).Select(l => l.Text);

        var minQuals = p.ConsolidatedQuals.Count > 0
            ? p.ConsolidatedQuals.Where(q => q.Kind == ConsolidatedQualKind.MinQualification)
                .OrderBy(q => q.Ordinal).Select(q => q.Name)
            : Quals(p, ProfileQualKind.KsaMin).Select(k => k.Text);

        return Build(
            $"{p.Title} ({p.CtJobFunction}, {p.PersonnelProgram}) — standardized template learned from {p.CorpusSize} job descriptions.",
            $"Salary grade {Or(Consensus(p, DistributionField.SalaryGrade), "varies")}, "
            + $"{Or(Consensus(p, DistributionField.FlsaStatus), "FLSA varies")}, "
            + $"bargaining unit {Or(Consensus(p, DistributionField.UnionCode), "varies")}. "
            + (supervisesYes == 0 ? "Non-supervisory." : "Some positions supervise."),
            responsibilities,
            certs.Take(5),
            Quals(p, ProfileQualKind.Education).Take(3).Select(e => e.Text),
            Quals(p, ProfileQualKind.MinExperience).Take(3).Select(e => e.Text),
            minQuals.Take(7),
            Quals(p, ProfileQualKind.KsaPref).Take(4).Select(k => k.Text),
            Quals(p, ProfileQualKind.WorkEnvironment).Take(4).Select(e => e.Text),
            ["Must be able to perform the essential functions of the position, with or without reasonable accommodation."],
            [
                "Supervising or leading other employees",
                "Exempt / salaried (non-hourly) status",
                "Requiring a bachelor's or advanced degree",
                "A pay band materially above this grade",
            ]);
    }
}

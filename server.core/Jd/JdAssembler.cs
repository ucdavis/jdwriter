using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Server.Core.Ai;
using Server.Core.Domain;
using Server.Core.Titles;
using Server.Core.Profiles;

namespace Server.Core.Jd;

/// <summary>
/// An affirmative build: the manager starts from the standard (the envelope) and keeps or drops
/// responsibilities and their duties, then optionally adds a few of their own.
///
/// Subtractive rather than free-text on purpose — the envelope is meant to work for most positions
/// in the class with under 10% unit customization, and the envelope check only has to police what
/// was ADDED.
/// </summary>
public sealed class BuildInputs
{
    public string WorkingTitle { get; set; } = "";
    public string Department { get; set; } = "";

    /// <summary>Included functions: name, % time, and kept plus added duties.</summary>
    public List<JdKeyResponsibility> KeptResponsibilities { get; set; } = [];

    public List<string> KeptCerts { get; set; } = [];
    public List<string> KeptEducation { get; set; } = [];
    public List<string> KeptWorkExperience { get; set; } = [];
    public List<string> KeptMinKSA { get; set; } = [];
    public List<string> KeptPrefKSA { get; set; } = [];
    public List<string> KeptWorkEnvironment { get; set; } = [];

    /// <summary>Every user-added item across all sections, for the envelope check.</summary>
    public List<string> AddedItems { get; set; } = [];

    public string Notes { get; set; } = "";

    /// <summary>Whether the position supervises others. Null when not stated. Never true for a represented class.</summary>
    public bool? Supervises { get; set; }

    /// <summary>How many people it supervises, when it does.</summary>
    public int? SupervisesCount { get; set; }

    /// <summary>Whether the position leads others' work (without supervising them). Null when not stated.</summary>
    public bool? Leads { get; set; }
}

public interface IJdAssembler
{
    /// <summary>
    /// Whether anything was added. When nothing was, the envelope check is skipped entirely — kept
    /// and dropped standard items are inherently in scope, so there is nothing to police.
    /// </summary>
    bool HasAdditions(BuildInputs inputs);

    Task<EnvelopeCheck> CheckEnvelopeAsync(
        ClassProfile profile,
        BuildInputs inputs,
        IReadOnlyList<ClassDescriptor>? others = null,
        CancellationToken ct = default);

    /// <summary>
    /// Is there a class that fits this build better than its own? Unlike the out-of-envelope
    /// alternative, "stay" is a valid answer: the build may stretch its class and still belong in it.
    /// </summary>
    Task<BetterFit> FindBetterFitAsync(
        ClassProfile profile,
        BuildInputs inputs,
        IReadOnlyList<ClassDescriptor> others,
        CancellationToken ct = default);

    Task<AssembledJd> AssembleAsync(
        ClassProfile profile,
        BuildInputs inputs,
        IReadOnlyList<ComplianceRule> rules,
        CancellationToken ct = default);
}

/// <summary>
/// JD assembly and two-pass compliance, grounded in the class envelope. Ported from the POC's
/// src/lib/jd/assemble.ts.
///
/// The division of labour is the point. The model writes the summary, polishes duty bullets, and
/// judges cross-section duplication. Everything else the model used to retype — the six kept
/// qualification sections, function names, percentages, and the two standard boilerplate sections
/// — is assembled in code, so it cannot drift. A model used as a data bus is both the cost driver
/// and a silent reliability risk: a dropped duty bullet is still schema-valid, so nothing catches
/// it.
/// </summary>
public sealed class JdAssembler : IJdAssembler
{
    private readonly IStructuredLlm _llm;
    private readonly ILogger<JdAssembler>? _logger;

    public JdAssembler(IStructuredLlm llm, ILogger<JdAssembler>? logger = null)
    {
        _llm = llm;
        _logger = logger;
    }

    // ---------------------------------------------------------------- response shapes

    private sealed class VerdictResponse
    {
        [Description("Whether the additions still fit this class.")]
        public EnvelopeVerdict Verdict { get; set; }

        [Description("Which out-of-envelope signals (if any) the additions triggered.")]
        public List<string> MatchedSignals { get; set; } = [];

        [Description("1-2 sentence explanation for the verdict.")]
        public string Rationale { get; set; } = "";
    }

    private sealed class Alternative
    {
        [Description("Number of the best-matching class from the catalog, or -1 if none fits.")]
        public int Index { get; set; }

        [Description("When index is -1, the name of the UC class that would fit; otherwise empty.")]
        public string FallbackClass { get; set; } = "";
    }

    private sealed class BetterFitResponse
    {
        [Description("Number of a catalog class that fits the described position clearly better than the current class, or -1 if the current class is still the best fit.")]
        public int Index { get; set; }

        [Description("One or two sentences: why that class fits better, or why the current class is still right.")]
        public string Rationale { get; set; } = "";
    }

    private sealed class GeneratedFunction
    {
        [Description("The number of the function, exactly as listed.")]
        public int Index { get; set; }

        [Description("Its duty bullets, polished. Drop redundant ones.")]
        public List<string> Duties { get; set; } = [];
    }

    private sealed class RemovedDuplicate
    {
        [Description("Number of a qualification item to remove as redundant.")]
        public int Index { get; set; }

        [Description("What it duplicates.")]
        public string Reason { get; set; } = "";
    }

    private sealed class Generated
    {
        [Description("2-3 sentence Job Summary for this position.")]
        public string JobSummary { get; set; } = "";

        [Description("One entry per listed function, in any order.")]
        public List<GeneratedFunction> Functions { get; set; } = [];

        [Description("Qualification items that duplicate a duty or another item. Usually empty.")]
        public List<RemovedDuplicate> RemoveDuplicates { get; set; } = [];
    }

    private sealed class ComplianceEdits
    {
        public List<ComplianceEditLine> Edits { get; set; } = [];
    }

    private sealed class ComplianceEditLine
    {
        [Description("The number of the line to change, exactly as listed.")]
        public int Index { get; set; }

        [Description("The corrected text for that line, in full.")]
        public string After { get; set; } = "";

        [Description("Why compliance requires the change.")]
        public string Reason { get; set; } = "";
    }

    // ---------------------------------------------------------------- prompts
    //
    // Ported byte-for-byte. Do not reflow, reword, or "improve" these: a behaviour change here is
    // indistinguishable from a port bug, and the wording is load-bearing.

    internal const string CheckSystem = """
        You are a UC Davis HR classification analyst. The manager started from
        this class's standard components and is ADDING a few of their own. Given the class envelope
        (its standard scope and out-of-envelope signals) and the manager's ADDITIONS, decide whether
        the additions still fit this class. Only the additions can push out of the envelope — kept/
        dropped standard items are inherently in-scope. If an addition clearly triggers an
        out-of-envelope signal, return out_of_envelope. If it mildly stretches the class, borderline.
        Otherwise in_envelope. Cite the specific signal(s) you matched.
        """;

    internal const string AltSystem = """
        You are a UC Davis HR classification analyst. A manager's additions have
        pushed a position outside its current job class. Given the additions, why it left the class, and
        a numbered catalog of known classes, pick the class that best fits the work as described.

        Return the number of the best match. Only pick a class that genuinely fits the described work —
        if none of them does, return index -1 and name the UC class that would fit in fallbackClass.
        """;

    // Not a port: written for JDWriter. The out-of-envelope alternative (AltSystem) asks where a
    // position that LEFT its class should go; this asks whether a position that still fits has a
    // better home, so staying put must be an answer the model can give.
    internal const string BetterFitSystem = """
        You are a UC Davis HR classification analyst. A manager is building a job description in a job class.
        Given the position as they have built it — the standard work they kept and anything they added — the
        class it is in now, and a numbered catalog of other job classes, decide whether one of those classes
        fits the described work clearly better than the current class.

        Return its number, or -1 if the current class is still the best fit. Prefer -1 unless another class
        is a clearly better match for the work as a whole, not merely for one addition. Explain briefly.
        """;

    internal const string GenSystem = """
        You are an expert UC Davis HR job description writer. The manager tailored this
        class's standard: they KEPT a subset of the standard components and may have ADDED some. You are
        given the kept functions with their duty bullets, and a numbered list of kept qualification items.

        Your job is three things — nothing else is yours to change:
        1. jobSummary: 2-3 sentences describing this position, grounded in the kept work.
        2. functions: for each listed function, return its duty bullets polished into clean, parallel,
           inclusive UC-standard phrasing. Return one entry per function, keyed by its number. Keep the
           substance of every bullet; merge two bullets only when they say the same thing.
        3. removeDuplicates: qualification items that restate a duty or another qualification (including
           the same idea worded differently, e.g. "coordinate travel" vs "arrange travel and
           entertainment"). Name the item's number and what it duplicates. Most builds need none.

        Do NOT reintroduce components the manager dropped, and do NOT invent content that was neither kept
        nor added — the result must remain SUBSTANTIALLY SIMILAR to the class standard. Function names,
        % time, and the standard boilerplate sections are fixed and handled outside your response. Use
        inclusive, gender-neutral, ADA/EEO-aware language.
        """;

    internal const string ComplianceSystem = """
        You are a UC Davis HR compliance specialist. You receive the numbered
        text lines of a draft job description that has already had a deterministic rules pass. Review them
        for ADA, EEO, and inclusive-language compliance.

        Return ONLY the lines that must change. Omit every line that is already compliant — an empty list
        is the correct answer for a clean draft. For each change give the line's index, the full corrected
        text, and the reason.

        Change the minimum necessary: do not restyle wording that is already compliant, do not add or
        remove requirements, and do not tighten or loosen qualifications beyond what compliance demands.
        Never merge, split, or reorder lines — each edit replaces exactly one numbered line.
        """;

    // ---------------------------------------------------------------- prompt assembly

    /// <summary>
    /// The build with every addition declared. The client reports what it kept and what it added, but
    /// "kept" is only text it sends: an item that is not in the class envelope is an addition however
    /// it arrived, so the envelope check — and the corpus that learns from finished JDs — must see it
    /// as one. Without this, rewriting "kept" items skipped the check entirely.
    /// </summary>
    public static BuildInputs WithUndeclaredAdditions(ClassProfile profile, BuildInputs inputs)
    {
        var envelope = profile.Envelope is null ? new EnvelopeWire() : EnvelopeWire.From(profile.Envelope);
        var standard = envelope.KeyResponsibilities.SelectMany(r => r.Duties)
            .Concat(envelope.RequiredCertifications).Concat(envelope.Education).Concat(envelope.WorkExperience)
            .Concat(envelope.MinQualifications).Concat(envelope.PrefQualifications).Concat(envelope.WorkEnvironment)
            .Select(ItemKey)
            .ToHashSet(StringComparer.Ordinal);
        var declared = inputs.AddedItems.Select(ItemKey).ToHashSet(StringComparer.Ordinal);

        var undeclared = inputs.KeptResponsibilities.SelectMany(r => r.Duties)
            .Concat(inputs.KeptCerts).Concat(inputs.KeptEducation).Concat(inputs.KeptWorkExperience)
            .Concat(inputs.KeptMinKSA).Concat(inputs.KeptPrefKSA).Concat(inputs.KeptWorkEnvironment)
            .Where(t => !standard.Contains(ItemKey(t)) && declared.Add(ItemKey(t)))
            .ToList();
        if (undeclared.Count == 0)
        {
            return inputs;
        }

        return new BuildInputs
        {
            WorkingTitle = inputs.WorkingTitle,
            Department = inputs.Department,
            KeptResponsibilities = inputs.KeptResponsibilities,
            KeptCerts = inputs.KeptCerts,
            KeptEducation = inputs.KeptEducation,
            KeptWorkExperience = inputs.KeptWorkExperience,
            KeptMinKSA = inputs.KeptMinKSA,
            KeptPrefKSA = inputs.KeptPrefKSA,
            KeptWorkEnvironment = inputs.KeptWorkEnvironment,
            AddedItems = [.. inputs.AddedItems, .. undeclared],
            Notes = inputs.Notes,
        };
    }

    /// <summary>Case, punctuation and spacing do not make an item new.</summary>
    private static string ItemKey(string text) =>
        string.Join(' ', new string((text ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public bool HasAdditions(BuildInputs inputs) =>
        inputs.AddedItems.Count > 0 || inputs.Notes.Trim().Length > 0;

    internal static string AdditionsText(BuildInputs inputs)
    {
        var parts = new List<string>();

        if (inputs.AddedItems.Count > 0)
        {
            parts.Add($"Added items:\n{string.Join('\n', inputs.AddedItems.Select(d => $"- {d}"))}");
        }

        if (inputs.Notes.Trim().Length > 0)
        {
            parts.Add($"Notes: {inputs.Notes.Trim()}");
        }

        return parts.Count > 0
            ? string.Join("\n\n", parts)
            : "(no additions — using the standard as-is)";
    }

    /// <summary>Consensus value for one attribute, or null when the class has none.</summary>
    private static string? Consensus(ClassProfile p, DistributionField field) =>
        p.Distributions.FirstOrDefault(d => d.Field == field)?.Consensus;

    /// <summary>One envelope list section, in stored order.</summary>
    private static List<string> EnvelopeItems(ClassProfile p, EnvelopeListKind kind) =>
        p.Envelope is null
            ? []
            : p.Envelope.Items
                .Where(i => i.Kind == kind)
                .OrderBy(i => i.Ordinal)
                .Select(i => i.Text)
                .ToList();

    private static List<EnvelopeResponsibility> EnvelopeResponsibilities(ClassProfile p) =>
        p.Envelope is null
            ? []
            : p.Envelope.KeyResponsibilities.OrderBy(r => r.Ordinal).ToList();

    internal static string EnvelopeContext(ClassProfile p)
    {
        var envelope = p.Envelope;

        var responsibilities = string.Join('\n', EnvelopeResponsibilities(p).Select(r =>
            $"- {r.PctTime}% {r.FunctionName}: {string.Join("; ", r.Duties.OrderBy(d => d.Ordinal).Select(d => d.Text))}"));
        var minQualifications = string.Join('\n',
            EnvelopeItems(p, EnvelopeListKind.MinQualification).Select(d => $"- {d}"));
        var certifications = string.Join('\n',
            EnvelopeItems(p, EnvelopeListKind.RequiredCertification).Select(d => $"- {d}"));
        var outOfEnvelope = string.Join('\n',
            EnvelopeItems(p, EnvelopeListKind.OutOfEnvelope).Select(d => $"- {d}"));

        return $"""
            CLASS: {p.Title} (UC job code {p.UcJobCode})
            Career Tracks: {p.CtJobFamily} / {p.CtJobFunction} · {p.PersonnelProgram}
            Salary grade: {Consensus(p, DistributionField.SalaryGrade) ?? "varies"} · FLSA: {Consensus(p, DistributionField.FlsaStatus) ?? "varies"} · Unit: {Consensus(p, DistributionField.UnionCode) ?? "varies"}
            Standard summary: {(envelope is not null ? envelope.Summary : p.RepresentativeSummary)}
            Scope: {(envelope is not null ? envelope.ScopeStatement : "")}
            Standard key responsibilities:
            {responsibilities}
            Standard minimum KSAs:
            {minQualifications}
            Typical certifications:
            {certifications}
            Out-of-envelope signals (these indicate a DIFFERENT class):
            {outOfEnvelope}
            """;
    }

    // ---------------------------------------------------------------- envelope fit check

    /// <summary>
    /// Two stages, because the two questions have very different context needs.
    ///
    /// Stage 1 ("does this still fit?") needs only the current envelope. Stage 2 ("then what fits
    /// better?") needs the peer catalog — several times the tokens — and only runs when stage 1
    /// says the build actually left the envelope. Most builds are in_envelope, so most pay stage 1
    /// alone; the earlier single call paid for the whole catalog every time to populate one
    /// optional field.
    ///
    /// A deterministic prefilter was tried first and removed: the distinctive cue words in a
    /// manager's additions ("supervise", "vendor", "symposium") have df=0 against the short
    /// envelope summaries, so lexical scoring matched only generic words and ranked essentially at
    /// random. Its failure mode was omitting the correct class SILENTLY. Stage 2 sees the full
    /// catalog instead, so there is nothing to omit.
    /// </summary>
    public async Task<EnvelopeCheck> CheckEnvelopeAsync(
        ClassProfile profile,
        BuildInputs inputs,
        IReadOnlyList<ClassDescriptor>? others = null,
        CancellationToken ct = default)
    {
        var verdict = await _llm.StructuredAsync<VerdictResponse>(new StructuredRequest
        {
            System = CheckSystem,
            User = $"{EnvelopeContext(profile)}\n\nMANAGER'S ADDITIONS:\n{AdditionsText(inputs)}\n\nWorking title: {(inputs.WorkingTitle.Length > 0 ? inputs.WorkingTitle : "(none)")}\nDepartment: {(inputs.Department.Length > 0 ? inputs.Department : "(none)")}",
            Effort = LlmEffort.Low,
            Label = "build.checkEnvelope",
        }, ct);

        var result = new EnvelopeCheck
        {
            Verdict = verdict.Verdict,
            MatchedSignals = verdict.MatchedSignals,
            Rationale = verdict.Rationale,
            SuggestedClass = "",
            SuggestedSlug = "",
        };

        if (others is null || others.Count == 0 || verdict.Verdict == EnvelopeVerdict.InEnvelope)
        {
            return result;
        }

        // Borderline stretches the class but still fits it. The manager is shown what else might fit,
        // without being routed away — a second query (product decision, 2026-10-08), asked as "is
        // anything clearly better?" so that staying put is an answer rather than a failure to find one.
        if (verdict.Verdict == EnvelopeVerdict.Borderline)
        {
            var better = await FindBetterFitAsync(profile, inputs, others, ct);
            result.SuggestedClass = better.SuggestedClass;
            result.SuggestedSlug = better.SuggestedSlug;
            result.SuggestedRationale = better.Rationale;
            result.BetterFitChecked = true;
            return result;
        }

        var catalog = string.Join('\n', others.Select((o, i) =>
            $"{i}: {o.Title} (code {o.UcJobCode}) — {o.Summary}"));
        var signals = verdict.MatchedSignals.Count > 0
            ? string.Join("; ", verdict.MatchedSignals)
            : "(none named)";

        var alternative = await _llm.StructuredAsync<Alternative>(new StructuredRequest
        {
            System = AltSystem,
            User = $"""
                CURRENT CLASS: {profile.Title} (code {profile.UcJobCode})
                MANAGER'S ADDITIONS:
                {AdditionsText(inputs)}

                WHY IT LEFT THE CLASS: {verdict.Rationale}
                SIGNALS TRIGGERED: {signals}

                CATALOG:
                {catalog}
                """,
            Effort = LlmEffort.Low,
            Label = "build.checkAlternative",
        }, ct);

        // Resolve the index to a slug HERE — the model never handles slug strings, so it cannot
        // fluff one into a silently dropped result.
        var picked = alternative.Index >= 0 && alternative.Index < others.Count
            ? others[alternative.Index]
            : null;

        if (picked is not null)
        {
            result.SuggestedClass = picked.Title;
            result.SuggestedSlug = picked.Slug;
            return result;
        }

        // Never route the manager to the class they are already in.
        var fallback = alternative.FallbackClass.Trim();
        if (fallback.Length > 0
            && !fallback.Equals(profile.Title, StringComparison.OrdinalIgnoreCase))
        {
            result.SuggestedClass = fallback;
            result.SuggestedSlug = "";
        }

        return result;
    }

    public async Task<BetterFit> FindBetterFitAsync(
        ClassProfile profile,
        BuildInputs inputs,
        IReadOnlyList<ClassDescriptor> others,
        CancellationToken ct = default)
    {
        if (others.Count == 0)
        {
            return new BetterFit { Rationale = "There are no other classes to compare against." };
        }

        var catalog = string.Join('\n', others.Select((o, i) => $"{i}: {o.Title} (code {o.UcJobCode}) — {o.Summary}"));
        var kept = string.Join('\n', inputs.KeptResponsibilities.Select(r =>
            $"- {r.PctTime}% {r.FunctionName}: {string.Join("; ", r.Duties)}"));
        var additions = HasAdditions(inputs) ? AdditionsText(inputs) : "(none)";

        var response = await _llm.StructuredAsync<BetterFitResponse>(new StructuredRequest
        {
            System = BetterFitSystem,
            User = $"""
                CURRENT CLASS: {profile.Title} (code {profile.UcJobCode})
                Working title: {(inputs.WorkingTitle.Length > 0 ? inputs.WorkingTitle : "(none)")}

                THE POSITION AS BUILT — functions kept, with % time and duties:
                {kept}

                MANAGER'S ADDITIONS:
                {additions}

                CATALOG:
                {catalog}
                """,
            Effort = LlmEffort.Low,
            Label = "build.betterFit",
        }, ct);

        // The index is resolved here; the model never handles a slug. The current class is not in
        // the catalog, so any valid index is a different class.
        var picked = response.Index >= 0 && response.Index < others.Count ? others[response.Index] : null;
        return new BetterFit
        {
            SuggestedClass = picked?.Title ?? "",
            SuggestedSlug = picked?.Slug ?? "",
            Rationale = response.Rationale.Trim(),
        };
    }

    // ---------------------------------------------------------------- generation

    /// <summary>
    /// The kept qualification sections, in the order they are numbered for the model. Built
    /// identically for the prompt and for assembly, so an index means the same thing on both sides.
    /// </summary>
    private static readonly (string Name, Func<BuildInputs, List<string>> Select)[] QualSections =
    [
        ("licensesCertifications", i => i.KeptCerts),
        ("education", i => i.KeptEducation),
        ("workExperience", i => i.KeptWorkExperience),
        ("minKSA", i => i.KeptMinKSA),
        ("prefKSA", i => i.KeptPrefKSA),
        ("workEnvironment", i => i.KeptWorkEnvironment),
    ];

    private readonly record struct QualItem(string Section, string Text);

    private static List<QualItem> QualItems(BuildInputs inputs)
    {
        var items = new List<QualItem>();
        foreach (var (name, select) in QualSections)
        {
            foreach (var text in select(inputs))
            {
                items.Add(new QualItem(name, text));
            }
        }

        return items;
    }

    internal static string FunctionBlock(BuildInputs inputs) =>
        string.Join('\n', inputs.KeptResponsibilities.Select((r, i) =>
            $"{i}: {r.PctTime}% — {r.FunctionName}\n{string.Join('\n', r.Duties.Select(d => $"   - {d}"))}"));

    internal static string QualBlock(BuildInputs inputs) =>
        string.Join('\n', QualItems(inputs).Select((q, i) => $"{i}: [{q.Section}] {q.Text}"));

    internal static string GenerateUserPrompt(ClassProfile p, BuildInputs inputs)
    {
        var functions = FunctionBlock(inputs);
        var quals = QualBlock(inputs);

        return $"""
            {EnvelopeContext(p)}

            KEPT FUNCTIONS (numbered; % time is fixed):
            {(functions.Length > 0 ? functions : "(none kept)")}

            KEPT QUALIFICATION ITEMS (numbered):
            {(quals.Length > 0 ? quals : "(none kept)")}

            MANAGER'S ADDITIONS:
            {AdditionsText(inputs)}

            Working title: {(inputs.WorkingTitle.Length > 0 ? inputs.WorkingTitle : p.Title)}
            Department: {(inputs.Department.Length > 0 ? inputs.Department : "(unspecified)")}
            """;
    }

    private async Task<Jd> GenerateAsync(ClassProfile p, BuildInputs inputs, CancellationToken ct)
    {
        var quals = QualItems(inputs);

        var generated = await _llm.StructuredAsync<Generated>(new StructuredRequest
        {
            System = GenSystem,
            User = GenerateUserPrompt(p, inputs),
            Effort = LlmEffort.Medium,
            MaxTokens = 8000,
            Label = "build.generateJd",
        }, ct);

        // ---- assemble in code ----------------------------------------------------
        var dropped = generated.RemoveDuplicates
            .Select(d => d.Index)
            .Where(i => i >= 0 && i < quals.Count)
            .ToHashSet();

        List<string> Section(string name) => quals
            .Select((q, i) => (q, i))
            .Where(x => x.q.Section == name && !dropped.Contains(x.i))
            .Select(x => x.q.Text)
            .ToList();

        // A Map built from pairs lets a later duplicate overwrite an earlier one, so if the model
        // returns a function twice the LAST entry wins. Matching that exactly rather than choosing
        // the arguably-better first-wins, because divergence here is invisible until it matters.
        var polished = new Dictionary<int, List<string>>();
        foreach (var f in generated.Functions)
        {
            polished[f.Index] = f.Duties;
        }

        var responsibilities = inputs.KeptResponsibilities
            .Select((r, i) =>
            {
                var duties = (polished.TryGetValue(i, out var p2) ? p2 : r.Duties)
                    .Select(d => d.Trim())
                    .Where(d => d.Length > 0)
                    .ToList();

                return new JdKeyResponsibility
                {
                    FunctionName = r.FunctionName,
                    PctTime = r.PctTime,
                    // Fall back to the manager's own bullets if the model skipped this function.
                    Duties = duties.Count > 0 ? duties : [.. r.Duties],
                };
            })
            .OrderByDescending(r => r.PctTime)
            .ToList();

        return new Jd
        {
            JobSummary = generated.JobSummary,
            KeyResponsibilities = responsibilities,
            LicensesCertifications = Section("licensesCertifications"),
            Education = Section("education"),
            WorkExperience = Section("workExperience"),
            MinKSA = Section("minKSA"),
            PrefKSA = Section("prefKSA"),
            ConditionsOfEmployment = EnvelopeItems(p, EnvelopeListKind.ConditionOfEmployment),
            WorkEnvironment = Section("workEnvironment"),
            PhysicalRequirements = EnvelopeItems(p, EnvelopeListKind.PhysicalRequirement),
        };
    }

    /// <summary>
    /// How many percentage points of the author's time are unaccounted for: 100 minus the sum of
    /// the kept shares. Zero when the kept set already accounts for the whole job.
    ///
    /// Deliberately NOT rescaled. Kept shares carry through exactly as the author set them, because
    /// the two alternatives are both worse:
    ///
    ///   • Rescaling silently inflates. An author who kept a 50% responsibility would find it
    ///     published as 71% with nobody told the number moved.
    ///   • Leaving the shortfall unreported publishes an HRTMS-invalid job description.
    ///
    /// So the gap is surfaced instead, and <see cref="AssembledJd.CanPublish"/> refuses until the
    /// author has reallocated it. Negative when the author has over-allocated past 100, which is
    /// equally unpublishable.
    /// </summary>
    internal static int UnallocatedPct(IEnumerable<JdKeyResponsibility> responsibilities) =>
        100 - responsibilities.Sum(r => r.PctTime);

    // ---------------------------------------------------------------- compliance

    private async Task<EditResult> LlmCompliancePassAsync(Jd jd, CancellationToken ct)
    {
        // Read the field text from a clone: this pass only reads here, and the edits are applied
        // against the original by ApplyTextEdits, which clones again.
        var fields = ComplianceEngine.TextFields(jd.Clone());
        if (fields.Count == 0)
        {
            return new EditResult { Jd = jd, Edits = [], Dropped = 0 };
        }

        var lines = string.Join('\n', fields.Select((f, i) => $"{i}: {f.Text}"));

        var response = await _llm.StructuredAsync<ComplianceEdits>(new StructuredRequest
        {
            System = ComplianceSystem,
            User = $"Draft JD lines:\n{lines}",
            Effort = LlmEffort.Low,
            MaxTokens = 4000,
            Label = "build.compliance",
        }, ct);

        var edits = response.Edits
            .Select(e => new TextEdit { Index = e.Index, After = e.After, Reason = e.Reason })
            .ToList();

        return ComplianceEngine.ApplyTextEdits(jd, edits, ComplianceEditSource.Llm);
    }

    // ---------------------------------------------------------------- orchestration

    /// <summary>
    /// True when the author kept the envelope exactly as offered and wrote no notes — nothing for
    /// a model to tailor, review, or learn from.
    /// </summary>
    public static bool IsUnchanged(ClassProfile profile, BuildInputs inputs) =>
        inputs.Notes.Trim().Length == 0 && !CorpusContribution.Differs(inputs, profile.Envelope);

    public async Task<AssembledJd> AssembleAsync(
        ClassProfile profile,
        BuildInputs inputs,
        IReadOnlyList<ComplianceRule> rules,
        CancellationToken ct = default)
    {
        // Unchanged: the envelope IS the JD. Its text was already written and polished when the
        // class was built, so there is nothing for generation or the model compliance review to
        // do — assemble it directly, with the deterministic rule pass only. No model call, so it
        // is instant, works with no provider configured, and costs nothing (product decision,
        // 2026-10-05).
        if (IsUnchanged(profile, inputs))
        {
            var fromEnvelope = ComplianceEngine.ApplyRulePass(FromEnvelope(profile, inputs), rules, _logger);
            var assembled = Assembled(profile, inputs, fromEnvelope.Jd, fromEnvelope.Edits);
            assembled.FromEnvelope = true;
            return assembled;
        }

        var draft = await GenerateAsync(profile, inputs, ct);

        // Deterministic rules first: cheap, auditable, reproducible, and they keep the model from
        // spending tokens on substitutions a regex can make.
        var ruled = ComplianceEngine.ApplyRulePass(draft, rules, _logger);
        var reviewed = await LlmCompliancePassAsync(ruled.Jd, ct);

        return Assembled(profile, inputs, reviewed.Jd, [.. ruled.Edits, .. reviewed.Edits]);
    }

    /// <summary>The envelope, as kept, laid out as a JD — what generation would start from.</summary>
    private static Jd FromEnvelope(ClassProfile p, BuildInputs inputs) => new()
    {
        JobSummary = p.Envelope?.Summary ?? "",
        KeyResponsibilities =
        [
            .. inputs.KeptResponsibilities
                .Select(r => r.Clone())
                .OrderByDescending(r => r.PctTime),
        ],
        LicensesCertifications = [.. inputs.KeptCerts],
        Education = [.. inputs.KeptEducation],
        WorkExperience = [.. inputs.KeptWorkExperience],
        MinKSA = [.. inputs.KeptMinKSA],
        PrefKSA = [.. inputs.KeptPrefKSA],
        ConditionsOfEmployment = EnvelopeItems(p, EnvelopeListKind.ConditionOfEmployment),
        WorkEnvironment = [.. inputs.KeptWorkEnvironment],
        PhysicalRequirements = EnvelopeItems(p, EnvelopeListKind.PhysicalRequirement),
    };

    private static AssembledJd Assembled(
        ClassProfile profile, BuildInputs inputs, Jd jd, List<ComplianceEditRecord> edits) =>
        new()
        {
            Slug = profile.Slug,
            Title = profile.Title,
            WorkingTitle = inputs.WorkingTitle.Length > 0 ? inputs.WorkingTitle : profile.Title,
            Department = inputs.Department,
            UcJobCode = profile.UcJobCode,
            SalaryGrade = Consensus(profile, DistributionField.SalaryGrade),
            FlsaStatus = Consensus(profile, DistributionField.FlsaStatus),
            BargainingUnit = BargainingUnits.For(profile),
            Supervises = inputs.Supervises,
            SupervisesCount = inputs.Supervises == true ? inputs.SupervisesCount : null,
            Leads = inputs.Leads,
            Jd = jd,
            UnallocatedPct = UnallocatedPct(jd.KeyResponsibilities),
            ComplianceEdits = edits,
        };
}

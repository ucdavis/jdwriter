using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Server.Core.Ai;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Core.Intake;

// ---------------------------------------------------------------- distillation
//
// Every [Description] is the ported text of the POC's Zod .describe() calls, VERBATIM. They are
// prompt engineering, not documentation: StructuredSchema copies them onto the wire schema, so
// rewording one changes what the model returns.

public sealed class DistilledFunction
{
    [Description("Short name for this responsibility function.")]
    public string Name { get; set; } = "";

    [Description("% time stated in the document, or 0 if none is stated.")]
    public double PctTime { get; set; }

    [Description("The duty statements under this function, condensed.")]
    public List<string> Duties { get; set; } = [];
}

/// <summary>A pasted description reduced to the same shape a catalog entry has.</summary>
public sealed class DistilledJd
{
    [Description("The working/position title as written, or '' if absent.")]
    public string WorkingTitle { get; set; } = "";

    [Description("The job summary, in the document's own terms. 2-3 sentences.")]
    public string Summary { get; set; } = "";

    [Description("The responsibility functions, in document order.")]
    public List<DistilledFunction> Functions { get; set; } = [];

    [Description("Does this position formally supervise other employees?")]
    public string Supervises { get; set; } = "unclear";

    [Description("Education requirements, if stated.")]
    public List<string> Education { get; set; } = [];

    [Description("Work-experience requirements, if stated.")]
    public List<string> Experience { get; set; } = [];

    [Description("Knowledge/skills/abilities, condensed. At most 10.")]
    public List<string> Ksas { get; set; } = [];

    /// <summary>
    /// "hrtms" when the document was parsed deterministically, "text" when a model distilled it.
    /// Set by the pipeline, never by the model — hence kept out of the schema entirely.
    /// </summary>
    [JsonIgnore]
    public string Source { get; set; } = "text";
}

// ---------------------------------------------------------------- ranking

internal sealed class ClassifyRankedMatch
{
    [Description("Number of a class from the catalog.")]
    public int Index { get; set; }

    [Description("0-100 confidence that this description belongs in this class.")]
    public double Confidence { get; set; }

    [Description("Is the described work below, at, or above this class's level?")]
    public string LevelFit { get; set; } = "unclear";

    [Description("One sentence on the level judgment, citing the evidence.")]
    public string LevelNote { get; set; } = "";

    [Description("One or two sentences on why this class does or doesn't fit.")]
    public string Rationale { get; set; } = "";

    [Description("Numbers of the description's functions this class's envelope covers.")]
    public List<int> MatchedFunctions { get; set; } = [];

    [Description("Numbers of the description's functions that fall outside this class.")]
    public List<int> UnmatchedFunctions { get; set; } = [];
}

internal sealed class ClassifyRankResponse
{
    public List<ClassifyRankedMatch> Matches { get; set; } = [];
}

// ---------------------------------------------------------------- proposed-class assessment

internal sealed class ProposedContradiction
{
    [Description("What about the proposed class the description does not match.")]
    public string Point { get; set; } = "";

    [Description("The wording or duty from the description that shows it.")]
    public string Evidence { get; set; } = "";
}

internal sealed class ProposedCompareResponse
{
    [Description("Does the described work belong in the proposed class?")]
    public string Fits { get; set; } = "partly";

    [Description("One sentence: the single strongest reason it does or does not fit.")]
    public string Summary { get; set; } = "";

    [Description("Concrete conflicts between the description and the proposed class. Empty if none.")]
    public List<ProposedContradiction> Contradicts { get; set; } = [];

    [Description("Work the proposed class expects that the description does not contain.")]
    public List<string> Missing { get; set; } = [];

    [Description("Points that genuinely do fit the proposed class.")]
    public List<string> Supports { get; set; } = [];
}

public sealed class ProposedContradictionResult
{
    public string Point { get; set; } = "";
    public string Evidence { get; set; } = "";
}

/// <summary>
/// A reasoned argument about the class the UNIT asked for, produced only after the independent
/// ranking is finished and never fed back into it.
/// </summary>
public sealed class ProposedAssessment
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Fits { get; set; } = "partly";
    public string Summary { get; set; } = "";
    public List<ProposedContradictionResult> Contradicts { get; set; } = [];
    public List<string> Missing { get; set; } = [];
    public List<string> Supports { get; set; } = [];

    /// <summary>
    /// Set when the proposed code is superseded and the comparison ran against its successor —
    /// otherwise the reasoning would appear to be about a class the unit never named.
    /// </summary>
    public string? ComparedAs { get; set; }

    /// <summary>
    /// "envelope", "standard", or "none". No envelope and no official standard means saying so
    /// rather than reasoning from a bare title, which would be indistinguishable from real analysis.
    /// </summary>
    public string Basis { get; set; } = "none";
}

// ---------------------------------------------------------------- assembly

public sealed class ClassificationMatch
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public int Confidence { get; set; }
    public string LevelFit { get; set; } = "unclear";
    public string LevelNote { get; set; } = "";
    public string Rationale { get; set; } = "";

    /// <summary>% of the description's work, by stated time, that this class covers.</summary>
    public int CoveredPct { get; set; }

    public List<string> InClass { get; set; } = [];
    public List<string> OutOfClass { get; set; } = [];

    /// <summary>A Health Center (HC) class: for Health Center positions only.</summary>
    public bool HealthCenterOnly { get; set; }

    /// <summary>The class's other version — its HC code, or the regular one — when it has one.</summary>
    public HealthCenterTwin? HealthCenterTwin { get; set; }
}

public sealed class Classification
{
    public DistilledJd Distilled { get; set; } = new();
    public List<ClassificationMatch> Matches { get; set; } = [];
    public string Verdict { get; set; } = "weak";
    public string VerdictNote { get; set; } = "";
    public ProposedAssessment? Proposed { get; set; }

    /// <summary>
    /// The class code this submission was filed under in the corpus, or null when it was not filed
    /// (already submitted, or no class to file it under). Set by the endpoint after filing.
    /// </summary>
    public string? FiledUnder { get; set; }
}

/// <summary>The class the unit proposed, rendered for comparison, plus what it was built from.</summary>
public sealed class ProposedContext
{
    public string Title { get; set; } = "";
    public string? ComparedAs { get; set; }
    public string Basis { get; set; } = "none";
    public string Block { get; set; } = "";
}

public interface IDescriptionClassifier
{
    Task<DistilledJd> DistillAsync(string text, CancellationToken ct = default);

    Task<Classification> ClassifyAsync(
        string text, List<ClassProfile> profiles, string? proposedCode = null, CancellationToken ct = default);

    Task<ProposedAssessment?> AssessProposedAsync(
        DistilledJd distilled, string code, List<ClassProfile> profiles, string ourPick,
        CancellationToken ct = default);
}

/// <summary>
/// Classification of an EXISTING job description: a unit pastes what it already has, and we say
/// which job class it belongs to, how confident we are, and why. Ported from the POC's
/// src/lib/intake/classify.ts.
///
/// The mirror image of <see cref="IntakeMatcher"/>. That path takes two sentences from a manager
/// with nothing written yet; this one takes a whole document — 2K-6K tokens of real JD, most of it
/// boilerplate, department mission text and EEO language that says nothing about the class.
///
/// Three stages:
///   1. Distill — reduce the document to the shape a catalog entry has. Feeding the raw document
///      into the search stages would send that payload TWICE and pit a long messy document against
///      terse abstractions. An HRTMS export skips this entirely: the parser already extracts the
///      same fields, which is free and more accurate than asking a model to re-read a table.
///   2. Shortlist + rank — reuses the matcher's stage 1 verbatim, then ranks with a richer schema:
///      level fit, and WHICH of the description's functions the class covers.
///   3. Assemble — verdict, coverage % and the in/out-of-class lists are all computed in CODE from
///      indices the model returned. A coverage figure a model asserted is unfalsifiable; one
///      recomputed from its own mapping is not.
/// </summary>
public sealed partial class DescriptionClassifier : IDescriptionClassifier
{
    public const int MaxResults = 4;

    public const string DistillSystem =
        """
        You are a UC Davis HR job analyst. You are given the full text of a job
        description a department has already written. Reduce it to its classification-relevant content.

        Keep: the job summary, every responsibility function with its % time and duties, supervisory
        scope, and the stated education / experience / KSA requirements.

        Discard: department mission statements, EEO and background-check boilerplate, application
        instructions, salary ranges, benefits, and anything about the hiring process rather than the work.

        Preserve the document's own level language — "under general supervision", "independently",
        "serves as lead", "recognized expert" — because it decides which LEVEL of a class this is.
        Condense wording, but do not invent duties, requirements, or % allocations that are not there.
        If the document states no % time, use 0.
        """;

    public const string RankSystem =
        """
        You are a UC Davis HR classification analyst. A department has submitted a job
        description it already wrote, distilled below. Decide which known job class it should be
        classified into.

        For each candidate class, judge TWO things separately:
          • TOPIC — is this the same kind of work as the class's responsibilities?
          • LEVEL — is the described scope, independence, and supervisory responsibility below, at, or
            above this class? A description can be squarely the right family and still be the wrong level.

        Then map the description's numbered functions onto the class: list the numbers your envelope
        covers and the numbers it does not. A function is covered if its work plausibly falls under the
        class's remit even if worded differently. Every function number appears in exactly one of the
        two lists.

        Some classes include an OFFICIAL UC JOB STANDARD (scope + standard duties). Treat it as the
        authoritative statement of the class's intended level and duty profile.

        Calibrate confidence honestly. A department's own wording is often aspirational or inherited from
        a neighboring class, so do not let a working title carry the decision — the duties do. If nothing
        fits well, say so with low confidence rather than forcing a match.
        """;

    public const string CompareSystem =
        """
        You are a UC Davis HR classification analyst. A department submitted a job
        description and asked for it to be classified into a specific job class. An independent analysis has
        already been done without seeing their request; your job now is to assess THEIR proposed class on
        its merits.

        Work from the description's actual duties, level of independence, and scope. Be concrete and
        specific: every conflict you list must point at particular wording or a particular duty in the
        description, not a general impression. If the proposed class genuinely fits, say so plainly — this
        is an assessment, not a search for objections.

        Distinguish two different problems: work the description contains that falls OUTSIDE the proposed
        class, and work the proposed class expects that the description does not contain. A description can
        fail to fit for either reason, and they call for different fixes.
        """;

    [GeneratedRegex(@"<\s*(html|body|table|div|td|p|span)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LooksLikeHtml();

    private readonly IStructuredLlm _llm;
    private readonly IIntakeMatcher _matcher;
    private readonly IStandardsStore _standards;
    private readonly ITitleCodeService _titleCodes;

    public DescriptionClassifier(
        IStructuredLlm llm,
        IIntakeMatcher matcher,
        IStandardsStore standards,
        ITitleCodeService titleCodes)
    {
        _llm = llm;
        _matcher = matcher;
        _standards = standards;
        _titleCodes = titleCodes;
    }

    // ------------------------------------------------------------- distillation

    /// <summary>An HRTMS export already has every field we would ask a model to extract.</summary>
    public static DistilledJd FromRecord(HrtmsRecord r) => new()
    {
        WorkingTitle = !string.IsNullOrEmpty(r.WorkingTitle) ? r.WorkingTitle : r.UcJobTitle,
        Summary = r.JobSummary,
        Functions = r.Responsibilities.Select(f => new DistilledFunction
        {
            Name = f.FunctionName,
            PctTime = f.Pct ?? 0,
            Duties = [.. f.Duties],
        }).ToList(),
        Supervises = r.Supervises is null ? "unclear" : r.Supervises.Value ? "yes" : "no",
        Education = string.IsNullOrEmpty(r.Qualifications.Education) ? [] : [r.Qualifications.Education],
        Experience = [.. r.Qualifications.MinExperience],
        Ksas = [.. r.Qualifications.KsaMin.Concat(r.Qualifications.KsaPref).Take(10)],
        Source = "hrtms",
    };

    /// <summary>
    /// Did the HRTMS parse actually find a job description? A document with no title, no summary and
    /// no responsibilities is not an export, whatever its markup looked like.
    /// </summary>
    private static bool LooksLikeRealExport(DistilledJd d) =>
        d.Functions.Count > 0
        || !string.IsNullOrWhiteSpace(d.Summary)
        || !string.IsNullOrWhiteSpace(d.WorkingTitle);

    public async Task<DistilledJd> DistillAsync(string text, CancellationToken ct = default)
    {
        if (LooksLikeHtml().IsMatch(text))
        {
            try
            {
                var parsed = FromRecord(HrtmsParser.Parse(text, "pasted"));

                // DELIBERATE DIVERGENCE, fixing a latent bug in the reference.
                //
                // The reference wraps this in try/catch and comments that a non-HRTMS document
                // should "fall through to the model rather than failing the whole classification".
                // But HrtmsParser never throws on arbitrary HTML — it returns an all-EMPTY record.
                // So the catch never fires, and any HTML that is not a real HRTMS export distills to
                // an empty JD tagged "hrtms", which then classifies against nothing.
                //
                // Verified against the POC: a `<div>marketing page</div>` yields empty title, empty
                // summary and zero responsibilities WITHOUT throwing. Checking for a usable parse is
                // what the reference's own comment actually describes.
                if (LooksLikeRealExport(parsed))
                {
                    return parsed;
                }
            }
            catch
            {
                // A genuine parse failure lands here; same outcome either way.
            }
        }

        var raw = await _llm.StructuredAsync<DistilledJd>(new StructuredRequest
        {
            System = DistillSystem,
            User = $"JOB DESCRIPTION:\n{text}",
            Effort = LlmEffort.Low,
            MaxTokens = 8000,
            Label = "classify.distill",
        }, ct);

        raw.Source = "text";
        return raw;
    }

    // ------------------------------------------------------------- request rendering

    /// <summary>
    /// The distilled JD rendered as the "request" the matcher stages consume.
    ///
    /// Stage 1 gets the TERSE form. It only decides which classes are worth comparing, and it
    /// compares against terse catalog entries — sending it every duty bullet and KSA pays for
    /// context that cannot change a plausibility call, and pits a long document against one-line
    /// class entries.
    /// </summary>
    public static string AsRequest(DistilledJd d, bool terse = false)
    {
        var fns = terse
            ? string.Join("\n", d.Functions.Select(f => $"- {f.Name}"))
            : string.Join("\n", d.Functions.Select(f =>
                $"- {f.Name}{(f.PctTime != 0 ? $" ({FormatPct(f.PctTime)}%)" : "")}: {string.Join("; ", f.Duties)}"));

        string Line(string label, List<string> xs) =>
            !terse && xs.Count > 0 ? $"\n{label}: {string.Join("; ", xs)}" : "";

        var title = string.IsNullOrEmpty(d.WorkingTitle) ? "(none given)" : d.WorkingTitle;

        return $"Working title: {title}\n"
               + $"Summary: {d.Summary}\n"
               + $"Supervises others: {d.Supervises}\n"
               + "Responsibilities:\n"
               + (fns.Length > 0 ? fns : "(none stated)")
               + Line("Education", d.Education)
               + Line("Experience", d.Experience)
               + Line("KSAs", d.Ksas);
    }

    /// <summary>Level facts the envelope alone does not carry — what the real corpus looks like.</summary>
    public static string LevelFacts(ClassProfile p)
    {
        var sup = Consensus(p, DistributionField.Supervises);
        var supTxt = sup is null ? "mixed/unknown" : sup == "true" ? "yes" : "no";
        var grade = Consensus(p, DistributionField.SalaryGrade) ?? "—";
        var program = string.IsNullOrEmpty(p.PersonnelProgram) ? "—" : p.PersonnelProgram;
        var basis = p.CorpusSize != 0
            ? $"observed across {p.CorpusSize} real JDs"
            : "from the official standard; no JD corpus yet";

        return $"  typical: grade {grade}, {program}, supervises: {supTxt} ({basis})";
    }

    // ------------------------------------------------------------- pure computation
    //
    // Per the project rule, the model makes the judgments and code does the arithmetic.

    /// <summary>
    /// Share of the description's work the class covers, weighted by stated % time. Recomputed here
    /// from the model's own mapping rather than asked for: a model's arithmetic over its own list is
    /// the one number we can cheaply get right.
    /// </summary>
    public static int Coverage(DistilledJd d, List<int> matched)
    {
        if (d.Functions.Count == 0)
        {
            return 0;
        }

        var valid = matched.Where(i => i >= 0 && i < d.Functions.Count).ToList();
        var totalPct = d.Functions.Sum(f => f.PctTime);

        // No % allocations in the source (common in hand-written JDs) — count functions instead.
        if (totalPct <= 0)
        {
            return IntakeMatcher.RoundHalfUp(valid.Count / (double)d.Functions.Count * 100);
        }

        var hit = valid.Sum(i => d.Functions[i].PctTime);
        return IntakeMatcher.RoundHalfUp(hit / totalPct * 100);
    }

    /// <summary>
    /// Split the description's functions into covered and not, from the model's matched list ALONE.
    ///
    /// The schema also asks for `unmatchedFunctions` — that question is what makes the model account
    /// for every function rather than listing only the easy hits — but the displayed split is
    /// derived here so the two lists always partition the functions and always agree with the
    /// coverage number shown above them.
    /// </summary>
    public static (List<string> InClass, List<string> OutOfClass) Split(DistilledJd d, List<int> matched)
    {
        var hit = matched.Where(i => i >= 0 && i < d.Functions.Count).ToHashSet();
        var inClass = new List<string>();
        var outOfClass = new List<string>();

        for (var i = 0; i < d.Functions.Count; i++)
        {
            (hit.Contains(i) ? inClass : outOfClass).Add(d.Functions[i].Name);
        }

        return (inClass, outOfClass);
    }

    /// <summary>
    /// The verdict is what an HR reviewer actually acts on, so it is decided by RULES here rather
    /// than by the model: a close call between two classes and a confident single match need
    /// different handling, and the thresholds should be inspectable.
    /// </summary>
    public static (string Verdict, string VerdictNote) Judge(List<ClassificationMatch> matches)
    {
        var top = matches.Count > 0 ? matches[0] : null;
        var second = matches.Count > 1 ? matches[1] : null;

        if (top is null || top.Confidence < 45)
        {
            return ("weak",
                "No class fits this description well. It may span two classes, or belong to one not yet ingested — route it to a classification analyst.");
        }

        // Checked BEFORE level mismatch, matching the reference: when two classes are this close,
        // which level is right is not yet the question.
        if (second is not null && top.Confidence - second.Confidence < 10)
        {
            return ("close-call",
                $"This description sits between {top.Title} and {second.Title}. The choice will turn on level and scope — compare both before deciding.");
        }

        if (top.LevelFit == "above" || top.LevelFit == "below")
        {
            return ("level-mismatch",
                $"The work looks like {top.Title}, but the described scope reads {top.LevelFit} that class's level. Check the neighboring level before classifying.");
        }

        var tail = top.CoveredPct < 100
            ? $", with {100 - top.CoveredPct}% of its stated time outside the class envelope."
            : ".";

        return ("clear", $"This description classifies as {top.Title}{tail}");
    }

    // ------------------------------------------------------------- classification

    public async Task<Classification> ClassifyAsync(
        string text, List<ClassProfile> profiles, string? proposedCode = null, CancellationToken ct = default)
    {
        var distilled = await DistillAsync(text, ct);

        if (profiles.Count == 0)
        {
            return new Classification
            {
                Distilled = distilled,
                Matches = [],
                Verdict = "weak",
                VerdictNote = "No job classes are ingested yet.",
            };
        }

        var request = AsRequest(distilled);

        // The proposed code is NOT passed here, deliberately. See AssessProposedAsync.
        var candidates = await _matcher.ShortlistAsync(AsRequest(distilled, terse: true), profiles, ct);
        var standards = await _standards.GetIndexAsync(ct);

        var raw = await _llm.StructuredAsync<ClassifyRankResponse>(new StructuredRequest
        {
            System = RankSystem,
            User = BuildRankUser(distilled, request, candidates, standards),
            Effort = LlmEffort.Medium,
            MaxTokens = 6000,
            Label = "classify.rank",
        }, ct);

        var matches = new List<ClassificationMatch>();
        foreach (var m in raw.Matches)
        {
            // An index out of range means the model named a class that was not on the menu; drop it
            // rather than guessing which one it meant.
            if (m.Index < 0 || m.Index >= candidates.Count)
            {
                continue;
            }

            var p = candidates[m.Index];
            var (inClass, outOfClass) = Split(distilled, m.MatchedFunctions);

            matches.Add(new ClassificationMatch
            {
                Slug = p.Slug,
                Title = p.Title,
                UcJobCode = p.UcJobCode,
                Confidence = IntakeMatcher.RoundHalfUp(m.Confidence),
                LevelFit = m.LevelFit,
                LevelNote = m.LevelNote,
                Rationale = m.Rationale,
                CoveredPct = Coverage(distilled, m.MatchedFunctions),
                InClass = inClass,
                OutOfClass = outOfClass,
            });
        }

        matches = matches.OrderByDescending(x => x.Confidence).Take(MaxResults).ToList();

        var (verdict, verdictNote) = Judge(matches);
        var result = new Classification
        {
            Distilled = distilled,
            Matches = matches,
            Verdict = verdict,
            VerdictNote = verdictNote,
        };

        // Only worth a call when the unit actually named a class. Agreement still gets assessed —
        // "why this fits" is as useful to an analyst as "why it does not".
        if (!string.IsNullOrEmpty(proposedCode) && matches.Count > 0)
        {
            result.Proposed = await AssessProposedAsync(distilled, proposedCode!, profiles, matches[0].Title, ct);
        }

        return result;
    }

    public static string BuildRankUser(
        DistilledJd distilled, string request, List<ClassProfile> candidates, StandardsIndex standards)
    {
        var catalog = string.Join("\n\n", candidates.Select((p, i) =>
            $"{IntakeMatcher.DetailedEntry(p, i, standards.ForTitle(p.Title))}\n{LevelFacts(p)}"));

        var fnBlock = distilled.Functions.Count > 0
            ? string.Join("\n", distilled.Functions.Select((f, i) =>
                $"{i}: {f.Name}{(f.PctTime != 0 ? $" ({FormatPct(f.PctTime)}% time)" : "")} — {string.Join("; ", f.Duties)}"))
            : "(the description states no distinct responsibility functions)";

        return $"""
                CANDIDATE JOB CLASSES:
                {catalog}

                SUBMITTED DESCRIPTION:
                {request}

                THE DESCRIPTION'S NUMBERED FUNCTIONS:
                {fnBlock}

                Return the best-fitting classes, most confident first.
                """;
    }

    // ------------------------------------------------------------- proposed assessment

    /// <summary>
    /// Everything we know about the class the unit asked for. A profile's envelope is the richest
    /// source; the official standard is the fallback; a bare title-code entry is not enough to
    /// reason from and is reported as such.
    /// </summary>
    public static ProposedContext? BuildProposedContext(
        string code,
        List<ClassProfile> profiles,
        TitleCodeIndex titleCodes,
        StandardsIndex standards)
    {
        // A superseded code has no class of its own any more, so compare against the successor and
        // say that is what happened.
        var sup = titleCodes.SupersededBy(code);
        var live = TitleCodeIndex.Pad(titleCodes.ResolveCode(code));
        var entry = titleCodes.FindByCode(code);
        var title = entry?.Title ?? (sup is not null ? sup.FromTitle : $"job code {code}");
        var comparedAs = sup?.ToTitle;

        var profile = profiles.FirstOrDefault(p => TitleCodeIndex.Pad(p.UcJobCode) == live);
        if (profile?.Envelope is not null)
        {
            var e = profile.Envelope;
            var responsibilities = string.Join("\n", e.KeyResponsibilities
                .OrderBy(r => r.Ordinal)
                .Select(r =>
                    $"  - {r.FunctionName} ({r.PctTime}%): {string.Join("; ", r.Duties.OrderBy(d => d.Ordinal).Select(d => d.Text))}"));

            var minQuals = string.Join("; ", EnvelopeItems(e, EnvelopeListKind.MinQualification));
            var grade = Consensus(profile, DistributionField.SalaryGrade) ?? "—";
            var program = string.IsNullOrEmpty(profile.PersonnelProgram) ? "—" : profile.PersonnelProgram;

            return new ProposedContext
            {
                Title = title,
                ComparedAs = comparedAs,
                Basis = "envelope",
                Block = $"{profile.Title} (code {profile.UcJobCode}, {profile.CtJobFunction})\n"
                        + $"summary: {e.Summary}\n"
                        + $"scope: {e.ScopeStatement}\n"
                        + "key responsibilities:\n"
                        + $"{responsibilities}\n"
                        + $"minimum qualifications: {minQuals}\n"
                        + $"typical: grade {grade}, {program}, from {profile.CorpusSize} real JDs",
            };
        }

        var std = standards.ForCode(live) ?? (entry is not null ? standards.ForTitle(entry.Title) : null);
        if (std is not null)
        {
            var responsibilities = string.Join("\n", std.KeyResponsibilities.Select(d => $"  - {d}"));
            return new ProposedContext
            {
                Title = title,
                ComparedAs = comparedAs,
                Basis = "standard",
                Block = $"{std.LongTitle} (code {std.Code ?? code})\n"
                        + $"generic scope: {std.GenericScope}\n"
                        + $"custom scope: {std.CustomScope}\n"
                        + "standard key responsibilities:\n"
                        + $"{responsibilities}\n"
                        + $"knowledge, skills and abilities: {string.Join("; ", std.Ksa.Take(8))}\n"
                        + $"education: {string.Join("; ", std.Education)}",
            };
        }

        return entry is not null
            ? new ProposedContext { Title = title, ComparedAs = comparedAs, Basis = "none", Block = "" }
            : null;
    }

    /// <summary>
    /// Assess the class the unit asked for, AFTER the independent ranking is settled.
    ///
    /// The proposed code is withheld from the ranking prompt entirely and argued only here. That is
    /// ANTI-ANCHORING, not secrecy or cost: envelopes and standards go to the model in full either
    /// way, but a model shown the answer the unit wants tends to ratify it, which makes the
    /// confidence number meaningless. Nothing from this call feeds back into the ranking.
    /// </summary>
    public async Task<ProposedAssessment?> AssessProposedAsync(
        DistilledJd distilled,
        string code,
        List<ClassProfile> profiles,
        string ourPick,
        CancellationToken ct = default)
    {
        var titleCodes = await _titleCodes.GetAsync(ct);
        var standards = await _standards.GetIndexAsync(ct);
        var ctx = BuildProposedContext(code, profiles, titleCodes, standards);

        if (ctx is null)
        {
            return null;
        }

        if (ctx.Basis == "none")
        {
            // No envelope and no standard: say so rather than reasoning from a bare title, which
            // would be indistinguishable from a real analysis.
            return new ProposedAssessment
            {
                Code = code,
                Title = ctx.Title,
                ComparedAs = ctx.ComparedAs,
                Basis = "none",
                Fits = "partly",
                Summary = $"No envelope or official standard is on file for {ctx.Title}, so its fit cannot be assessed — ingest that class or load its job standard to compare.",
                Contradicts = [],
                Missing = [],
                Supports = [],
            };
        }

        var raw = await _llm.StructuredAsync<ProposedCompareResponse>(new StructuredRequest
        {
            System = CompareSystem,
            User = BuildCompareUser(ctx, distilled, ourPick),
            Effort = LlmEffort.Medium,
            MaxTokens = 4000,
            Label = "classify.proposed",
        }, ct);

        return new ProposedAssessment
        {
            Code = code,
            Title = ctx.Title,
            ComparedAs = ctx.ComparedAs,
            Basis = ctx.Basis,
            Fits = raw.Fits,
            Summary = raw.Summary,
            Contradicts = raw.Contradicts
                .Select(c => new ProposedContradictionResult { Point = c.Point, Evidence = c.Evidence })
                .ToList(),
            Missing = raw.Missing,
            Supports = raw.Supports,
        };
    }

    public static string BuildCompareUser(ProposedContext ctx, DistilledJd distilled, string ourPick) =>
        $"""
         THE CLASS THE DEPARTMENT PROPOSED:
         {ctx.Block}

         THE SUBMITTED DESCRIPTION:
         {AsRequest(distilled)}

         For reference, an independent analysis of this description landed on "{ourPick}". Do not defer to
         it — assess the proposed class directly against the description above.
         """;

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// JS renders a whole-valued number without a decimal point ("85", not "85.0"), and PctTime is a
    /// double because a mixed PD form legitimately keeps fractional values.
    /// </summary>
    internal static string FormatPct(double v) =>
        v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static string? Consensus(ClassProfile p, DistributionField field) =>
        p.Distributions.FirstOrDefault(d => d.Field == field)?.Consensus;

    private static List<string> EnvelopeItems(JobEnvelope e, EnvelopeListKind kind) =>
        e.Items.Where(i => i.Kind == kind).OrderBy(i => i.Ordinal).Select(i => i.Text).ToList();
}

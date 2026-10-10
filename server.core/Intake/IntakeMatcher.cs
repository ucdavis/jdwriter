using System.ComponentModel;
using Server.Core.Ai;
using Server.Core.Domain;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Core.Intake;

/// <summary>One class the matcher considers a fit for a manager's request.</summary>
public sealed class ClassMatch
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public int Confidence { get; set; }
    public string Rationale { get; set; } = "";

    /// <summary>A Health Center (HC) class: for Health Center positions only.</summary>
    public bool HealthCenterOnly { get; set; }

    /// <summary>The class's other version — its HC code, or the regular one — when it has one.</summary>
    public HealthCenterTwin? HealthCenterTwin { get; set; }
}

// ---------------------------------------------------------------- model response shapes
//
// Descriptions are the ported text of the POC's Zod .describe() calls, VERBATIM. They are prompt
// engineering, not documentation: StructuredSchema copies them onto the wire schema.

internal sealed class ShortlistResponse
{
    [Description("Numbers of the plausibly-matching classes, most promising first.")]
    public List<int> Candidates { get; set; } = [];
}

internal sealed class RankedMatchResponse
{
    [Description("Number of a class from the catalog.")]
    public int Index { get; set; }

    [Description("0-100 confidence that this class fits the request.")]
    public double Confidence { get; set; }

    [Description("One sentence tying the request to this class's work.")]
    public string Rationale { get; set; } = "";
}

internal sealed class RankResponse
{
    public List<RankedMatchResponse> Matches { get; set; } = [];
}

public interface IIntakeMatcher
{
    /// <summary>Stage 1 — narrow the whole catalog down to a shortlist of plausible classes.</summary>
    Task<List<ClassProfile>> ShortlistAsync(string request, List<ClassProfile> profiles, CancellationToken ct = default);

    Task<List<ClassMatch>> MatchAsync(string request, List<ClassProfile> profiles, CancellationToken ct = default);
}

/// <summary>
/// Semantic intake matching: given a hiring manager's free-text request, match it to the
/// best-fitting ingested job class(es) using each class's envelope. Ported from the POC's
/// src/lib/intake/match.ts.
///
/// Two stages, so cost scales with the number of FINALISTS rather than the size of the catalog. A
/// single call sending every class its full summary plus official standard cost ~20K tokens per
/// search and grew with every ingest.
///
///   Stage 1 (shortlist) — every class described tersely: title, Career Tracks function, and
///     function names only. Cheap per class, so nothing is hidden from the model and recall holds.
///   Stage 2 (rank) — full detail for the shortlisted classes only, which is where the expensive
///     context actually earns its place.
///
/// Both stages address classes by INDEX. The single-call version asked the model to reproduce exact
/// slug strings and silently dropped any match whose slug did not resolve, so a fluffed string
/// became a missing result with no signal at all.
/// </summary>
public sealed class IntakeMatcher : IIntakeMatcher
{
    /// <summary>
    /// How many classes stage 1 forwards, and the catalog size below which the shortlist stage is
    /// pure overhead.
    ///
    /// Sized for RECALL, not economy. At 10 slots a cluster of similar titles could crowd out a
    /// genuinely better match before stage 2 ever saw it — observed roughly one run in four
    /// dropping Financial Anl 3 from a "research lab budget and purchasing" search. Widening costs
    /// ~2K tokens in stage 2 and buys back the headroom. Stage 1 is the ONLY place a class can be
    /// lost outright, so it errs wide.
    /// </summary>
    public const int ShortlistTarget = 16;

    public const int SkipShortlistBelow = 14;
    public const int MaxResults = 4;

    // Interpolated, not hardcoded: the reference builds this prompt from SHORTLIST_TARGET, so the
    // number the model is told must follow the constant rather than drift from it.
    public static readonly string ShortlistSystem =
        $"""
        You are a UC Davis HR classification analyst. Given a hiring manager's
        free-text description of a role and a terse catalog of known job classes (title, Career Tracks
        function, and the names of their key responsibility functions), select every class that could
        plausibly fit the described work — by topic and by level.

        Return up to {ShortlistTarget} class numbers, most promising first. Be inclusive rather than
        decisive: a later step compares the finalists in detail, so it is much worse to omit a plausible
        class here than to include a marginal one. If little fits, return the closest few anyway.
        """;

    public const string RankSystem =
        """
        You are a UC Davis HR classification analyst. Given a hiring manager's
        free-text description of a role and a catalog of candidate job classes (title, summary, key
        responsibilities), identify which class(es) best fit the described work. Return ranked matches,
        most confident first, each with a 0-100 confidence and a one-sentence rationale grounded in BOTH
        the request and the class's actual responsibilities — distinguishing topic *and* level. If nothing
        fits well, return the closest options with appropriately low confidence.

        Some classes include an OFFICIAL UC JOB STANDARD (scope + standard duties). Treat it as an
        authoritative signal of the class's intended level and duty profile — use it, together with the
        corpus responsibilities, to judge where the described work should land.
        """;

    private readonly IStructuredLlm _llm;
    private readonly IStandardsStore _standards;

    public IntakeMatcher(IStructuredLlm llm, IStandardsStore standards)
    {
        _llm = llm;
        _standards = standards;
    }

    /// <summary>
    /// Terse stage-1 entry: enough to judge plausibility, cheap enough for the whole catalog.
    /// </summary>
    public static string TerseEntry(ClassProfile p, int i)
    {
        var responsibilities = EnvelopeFunctionNames(p);
        var fns = string.Join("; ", responsibilities);
        var function = string.IsNullOrEmpty(p.CtJobFunction) ? "—" : p.CtJobFunction;
        return $"{i}: {p.Title} ({function})" + (fns.Length > 0 ? $" — {fns}" : "");
    }

    /// <summary>
    /// Full stage-2 entry: the context that actually decides topic and level.
    ///
    /// The standard is a PARAMETER rather than a store lookup, unlike the reference. That keeps the
    /// function pure and testable against a fixture without a database standing behind it.
    /// </summary>
    public static string DetailedEntry(ClassProfile p, int i, ClassStandardRecord? std)
    {
        var scope = "";
        var stdDuties = "";
        var stdBlock = "";

        if (std is not null)
        {
            var raw = !string.IsNullOrEmpty(std.GenericScope) ? std.GenericScope : std.CustomScope;
            scope = Clip(raw ?? "", 400);
            stdDuties = string.Join("; ", std.KeyResponsibilities
                .Take(6)
                .Select(d => Clip(CollapseWhitespace(d), 120)));
            stdBlock = $"\n  official standard scope: {scope}\n  official standard duties: {stdDuties}";
        }

        var summary = p.Envelope is not null
            ? p.Envelope.Summary
            : Clip(p.RepresentativeSummary, 200);

        var responsibilities = string.Join("; ", EnvelopeFunctionNames(p));

        return $"{i}: {p.Title} (code {p.UcJobCode}, {p.CtJobFunction})\n"
               + $"  summary: {summary}\n"
               + $"  responsibilities: {responsibilities}{stdBlock}";
    }

    public async Task<List<ClassProfile>> ShortlistAsync(
        string request, List<ClassProfile> profiles, CancellationToken ct = default)
    {
        if (profiles.Count < SkipShortlistBelow)
        {
            return profiles;
        }

        var raw = await _llm.StructuredAsync<ShortlistResponse>(new StructuredRequest
        {
            System = ShortlistSystem,
            User = BuildShortlistUser(request, profiles),
            Effort = LlmEffort.Low,
            MaxTokens = 1000,
            Label = "intake.shortlist",
        }, ct);

        var picked = raw.Candidates
            .Distinct()
            .Where(i => i >= 0 && i < profiles.Count)
            .Take(ShortlistTarget)
            .Select(i => profiles[i])
            .ToList();

        // A shortlist that comes back empty or unusable must not silently zero out the search —
        // fall back to ranking the full catalog.
        return picked.Count > 0 ? picked : profiles;
    }

    public async Task<List<ClassMatch>> MatchAsync(
        string request, List<ClassProfile> profiles, CancellationToken ct = default)
    {
        if (profiles.Count == 0)
        {
            return [];
        }

        var candidates = await ShortlistAsync(request, profiles, ct);
        var index = await _standards.GetIndexAsync(ct);

        var raw = await _llm.StructuredAsync<RankResponse>(new StructuredRequest
        {
            System = RankSystem,
            User = BuildRankUser(request, candidates, index),
            Effort = LlmEffort.Low,
            MaxTokens = 4000,
            Label = "intake.rank",
        }, ct);

        var matches = new List<ClassMatch>();
        foreach (var m in raw.Matches)
        {
            // Index out of range means the model named a class that was not on the menu; drop it
            // rather than guessing which one it meant.
            if (m.Index < 0 || m.Index >= candidates.Count)
            {
                continue;
            }

            var p = candidates[m.Index];
            matches.Add(new ClassMatch
            {
                Slug = p.Slug,
                Title = p.Title,
                UcJobCode = p.UcJobCode,
                Confidence = RoundHalfUp(m.Confidence),
                Rationale = m.Rationale,
            });
        }

        return matches
            .OrderByDescending(x => x.Confidence)
            .Take(MaxResults)
            .ToList();
    }

    public static string BuildShortlistUser(string request, List<ClassProfile> profiles)
    {
        var catalog = string.Join("\n", profiles.Select((p, i) => TerseEntry(p, i)));
        return $"KNOWN JOB CLASSES:\n{catalog}\n\nHIRING MANAGER'S REQUEST:\n{request}\n\nSelect the classes worth comparing in detail.";
    }

    public static string BuildRankUser(string request, List<ClassProfile> candidates, StandardsIndex standards)
    {
        var catalog = string.Join("\n\n",
            candidates.Select((p, i) => DetailedEntry(p, i, standards.ForTitle(p.Title))));
        return $"CANDIDATE JOB CLASSES:\n{catalog}\n\nHIRING MANAGER'S REQUEST:\n{request}\n\nReturn the best-matching classes, most confident first.";
    }

    /// <summary>Envelope key-responsibility function names, in order. Empty when no envelope.</summary>
    internal static List<string> EnvelopeFunctionNames(ClassProfile p) =>
        p.Envelope is null
            ? []
            : p.Envelope.KeyResponsibilities.OrderBy(r => r.Ordinal).Select(r => r.FunctionName).ToList();

    /// <summary>JS Math.round is half-up; .NET Math.Round is banker's rounding by default.</summary>
    internal static int RoundHalfUp(double v) => (int)Math.Floor(v + 0.5);

    internal static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    internal static string CollapseWhitespace(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
}

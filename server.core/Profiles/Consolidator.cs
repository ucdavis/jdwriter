using System.Text.RegularExpressions;
using Server.Core.Ai;
using Server.Core.Domain;
using Server.Core.Ingest;

namespace Server.Core.Profiles;

/// <summary>What consolidation produced, ready to attach to a profile.</summary>
public sealed class ConsolidationResult
{
    public List<ConsolidatedFunction> FunctionGroups { get; set; } = [];
    public List<ConsolidatedQual> Certifications { get; set; } = [];
    public List<ConsolidatedQual> MinQualifications { get; set; } = [];
    public List<string> DroppedFunctions { get; set; } = [];
    public List<string> DroppedQualifications { get; set; } = [];
}

/// <summary>
/// Semantic consolidation in TWO passes. Ported from the POC's src/lib/profile/consolidate.ts.
///
///   Pass 1 — cluster the raw parsed functions, licences and KSAs into categories, merging
///            near-duplicates and related items and dropping outliers.
///   Pass 2 — merge those categories again into the fewest broad categories.
///
/// This file is where the governing rule is most visible: **Claude makes the grouping judgements;
/// every %/frequency number is recomputed deterministically from the records.** The model is never
/// asked for a statistic, so the numbers cannot drift from the source data no matter what it says.
/// A model used as a data bus is both the cost driver and a silent reliability risk — a dropped
/// item is still schema-valid, so nothing would catch it.
/// </summary>
public sealed partial class Consolidator
{
    private readonly IStructuredLlm _llm;

    public Consolidator(IStructuredLlm llm)
    {
        _llm = llm;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex AnyWhitespace();

    [GeneratedRegex(@"\w\S*")]
    private static partial Regex Word();

    [GeneratedRegex("[^a-z0-9 ]")]
    private static partial Regex NonKeyChar();

    [GeneratedRegex(@"^\D+")]
    private static partial Regex LeadingNonDigits();

    private static string Canon(string s) => AnyWhitespace().Replace(s.Trim(), " ").ToLowerInvariant();

    private static string Titleize(string s) => Word().Replace(s, m =>
        char.ToUpperInvariant(m.Value[0]) + m.Value[1..].ToLowerInvariant());

    private static string NormKey(string s) =>
        AnyWhitespace().Replace(NonKeyChar().Replace(s.ToLowerInvariant(), ""), " ").Trim();

    private static double Mean(IReadOnlyCollection<double> xs) =>
        xs.Count > 0 ? xs.Sum() / xs.Count : 0;

    /// <summary>JS Math.round is half-up; .NET Math.Round is banker's rounding by default.</summary>
    private static double RoundHalfUp(double x) => Math.Floor(x + 0.5);

    /// <summary>
    /// Ids arrive as "F3" / "C0" / "M12". Strip the prefix and read the number; anything
    /// unparseable resolves to an index that the range checks then discard.
    /// </summary>
    private static int IndexOfId(string id)
    {
        var digits = LeadingNonDigits().Replace(id ?? "", "");
        return int.TryParse(digits, out var i) ? i : -1;
    }

    // ---------------------------------------------------------------- raw extraction

    private sealed class RawFn
    {
        public string Canon { get; init; } = "";
        public string Name { get; init; } = "";

        /// <summary>Document index to summed percent. A JD repeating a function accumulates.</summary>
        public Dictionary<int, double> DocPct { get; } = [];

        public List<string> Duties { get; } = [];
    }

    private static List<RawFn> RawFunctions(IReadOnlyList<HrtmsRecord> records)
    {
        var order = new List<string>();
        var map = new Dictionary<string, RawFn>(StringComparer.Ordinal);

        for (var doc = 0; doc < records.Count; doc++)
        {
            foreach (var f in records[doc].Responsibilities)
            {
                var k = Canon(f.FunctionName);
                if (!map.TryGetValue(k, out var e))
                {
                    e = new RawFn { Canon = k, Name = Titleize(f.FunctionName) };
                    map[k] = e;
                    order.Add(k);
                }

                e.DocPct[doc] = e.DocPct.GetValueOrDefault(doc) + (f.Pct ?? 0);
                e.Duties.AddRange(f.Duties);
            }
        }

        return order.Select(k => map[k]).ToList();
    }

    private sealed class RawQual
    {
        public string Key { get; init; } = "";
        public string Text { get; init; } = "";
        public HashSet<int> Docs { get; } = [];
    }

    private static List<RawQual> RawQuals(
        IReadOnlyList<HrtmsRecord> records, Func<HrtmsRecord, List<string>> select)
    {
        var order = new List<string>();
        var map = new Dictionary<string, RawQual>(StringComparer.Ordinal);

        for (var doc = 0; doc < records.Count; doc++)
        {
            foreach (var raw in select(records[doc]))
            {
                var key = NormKey(raw);
                if (key.Length < 3)
                {
                    continue;
                }

                if (!map.TryGetValue(key, out var e))
                {
                    e = new RawQual { Key = key, Text = raw };
                    map[key] = e;
                    order.Add(key);
                }

                e.Docs.Add(doc);
            }
        }

        return order.Select(k => map[k]).OrderByDescending(e => e.Docs.Count).ToList();
    }

    // ---------------------------------------------------------------- honest recomputation

    /// <summary>
    /// Recompute a category's statistics from its member functions. Nothing here comes from the
    /// model — it supplied only which raw names belong together.
    /// </summary>
    private static ConsolidatedFunction FunctionStats(
        int recordCount, List<RawFn> fns, HashSet<string> memberCanons, string name)
    {
        var docPct = new Dictionary<int, double>();
        var duties = new List<string>();

        foreach (var f in fns)
        {
            if (!memberCanons.Contains(f.Canon))
            {
                continue;
            }

            foreach (var (doc, pct) in f.DocPct)
            {
                docPct[doc] = docPct.GetValueOrDefault(doc) + pct;
            }

            duties.AddRange(f.Duties);
        }

        var vals = docPct.Values.ToList();
        var total = vals.Sum();

        var result = new ConsolidatedFunction
        {
            Name = name,
            // Mean among the JDs that HAVE this category — not across all of them.
            MeanPct = RoundHalfUp(Mean(vals)),
            // Share of a typical JD's 100%, across ALL JDs. This is the one that sums to ~100
            // after normalization; MeanPct does not and must not be confused with it.
            TemplatePct = recordCount > 0 ? total / recordCount : 0,
            MinPct = vals.Count > 0 ? vals.Min() : 0,
            MaxPct = vals.Count > 0 ? vals.Max() : 0,
            Prevalence = recordCount > 0 ? docPct.Count / (double)recordCount : 0,
        };

        // Members in `fns` order rather than set order, so the list is stable across runs.
        var members = fns.Where(f => memberCanons.Contains(f.Canon)).Select(f => f.Name).ToList();
        for (var i = 0; i < members.Count; i++)
        {
            result.Members.Add(new ConsolidatedFunctionMember { Ordinal = i, Text = members[i] });
        }

        var samples = duties.Distinct(StringComparer.Ordinal).Take(6).ToList();
        for (var i = 0; i < samples.Count; i++)
        {
            result.SampleDuties.Add(new ConsolidatedFunctionSampleDuty { Ordinal = i, Text = samples[i] });
        }

        return result;
    }

    /// <summary>
    /// Scale a set of categories so their template percentages sum to exactly 100, giving the
    /// rounding remainder to the largest — so the template reads like a real job description
    /// rather than summing to 99 or 101.
    /// </summary>
    private static List<ConsolidatedFunction> NormalizeTemplatePct(List<ConsolidatedFunction> groups)
    {
        var sum = groups.Sum(g => g.TemplatePct);
        if (sum == 0)
        {
            sum = 1;
        }

        foreach (var g in groups)
        {
            g.TemplatePct = RoundHalfUp(g.TemplatePct / sum * 100);
        }

        var diff = 100 - groups.Sum(g => g.TemplatePct);
        if (groups.Count > 0 && diff != 0)
        {
            // First maximum wins, matching the reference's `a.templatePct >= b.templatePct ? a : b`.
            var top = groups[0];
            foreach (var g in groups)
            {
                if (g.TemplatePct > top.TemplatePct)
                {
                    top = g;
                }
            }

            top.TemplatePct += diff;
        }

        return groups;
    }

    /// <summary>
    /// The raw material of a consolidation prompt, in the form the model sees it. Public so the
    /// prompt can be asserted directly in a test — prompt drift during a port is the realistic
    /// failure here, and catching it must not require an API call.
    /// </summary>
    public sealed record ConsolidationInput(
        int RecordCount,
        IReadOnlyList<(string Name, double MeanPct, int DocCount)> Functions,
        IReadOnlyList<(string Text, int DocCount)> Certifications,
        IReadOnlyList<(string Text, int DocCount)> Ksas);

    /// <summary>Extract the prompt inputs from records. Deterministic; no model involved.</summary>
    public static ConsolidationInput ExtractInput(IReadOnlyList<HrtmsRecord> records) =>
        ToInput(
            records.Count,
            RawFunctions(records),
            RawQuals(records, r => r.Qualifications.Licenses),
            RawQuals(records, r => r.Qualifications.KsaMin));

    /// <summary>
    /// Shared by the public extractor and the orchestrator, so scanning the corpus for raw items
    /// happens once per ingest rather than twice.
    /// </summary>
    private static ConsolidationInput ToInput(
        int recordCount, List<RawFn> fns, List<RawQual> certs, List<RawQual> ksas) =>
        new(
            recordCount,
            [.. fns.Select(f => (f.Name, Mean(f.DocPct.Values.ToList()), f.DocPct.Count))],
            [.. certs.Select(c => (c.Text, c.Docs.Count))],
            [.. ksas.Select(k => (k.Text, k.Docs.Count))]);

    // ---------------------------------------------------------------- model contracts

    private sealed class Group
    {
        public string Name { get; set; } = "";
        public List<string> MemberIds { get; set; } = [];
    }

    private sealed class Pass1Result
    {
        public List<Group> FunctionGroups { get; set; } = [];
        public List<string> DroppedFunctionIds { get; set; } = [];
        public List<Group> CertGroups { get; set; } = [];
        public List<string> DroppedCertIds { get; set; } = [];
        public List<Group> MinQualGroups { get; set; } = [];
        public List<string> DroppedMinQualIds { get; set; } = [];
    }

    private sealed class Pass2Group
    {
        public string Name { get; set; } = "";
        public List<int> MemberIndices { get; set; } = [];
    }

    private sealed class Pass2Result
    {
        public List<Pass2Group> Groups { get; set; } = [];
    }

    public const string Pass1System = """
        You are a UC Davis HR job-analysis expert. You are given the raw, messy
        lists of responsibility functions, licenses/certifications, and minimum qualifications
        extracted from many real job descriptions for one job class. CONSOLIDATE them using judgment
        into a broad, GENERIC standard that covers the full range of positions in this classification.

        For each list:
        - Group items describing the SAME or CLOSELY RELATED work/requirement, even when worded
          differently ("Forklift certificate must be obtained within 6 months" = "Forklift certificate";
          "Cultural Operations" and "Plant Collection" are the same broad field-work category).
        - Category names must be BROAD and GENERIC — do NOT bake in specifics (a particular field,
          crop, greenhouse, ranch, unit, or program). They should read as generic to any position in
          the class.
        - Aim for about 3 broad function categories (never more than 4).
        - Produce no more than 7 minimum-qualification categories.
        - DROP true outliers: idiosyncratic items in a single JD that don't generalize, and boilerplate
          (smoking policy, principles of community, "performs other duties as assigned", background checks).
        - Assign every non-dropped item id to exactly ONE group, using the exact ids provided.
        """;

    public const string Pass2System = """
        You are a UC Davis HR job-analysis expert. You are given a first-pass set of
        responsibility categories for one job class. Merge them into about 3 broad, generic categories
        (3, or 4 only if the class genuinely spans four distinct kinds of work). Do NOT over-merge below
        3 unless the class truly has only one or two kinds of work. Keep category names broad and generic
        (no specific fields, crops, sites, or units). Give each final category a clear name and list the
        indices of the first-pass categories it absorbs. Every input category goes into exactly one.
        """;

    private static string ListBlock(string prefix, IReadOnlyList<(string Label, string Stat)> items) =>
        string.Join('\n', items.Select((it, i) => $"{prefix}{i}: {it.Label} — {it.Stat}"));

    /// <summary>The exact string the model receives for pass 1. Public so tests can assert it.</summary>
    public static string BuildPass1User(ConsolidationInput input)
    {
        var recordCount = input.RecordCount;

        var fnBlock = ListBlock("F", [.. input.Functions.Select(f => (
            f.Name,
            $"~{RoundHalfUp(f.MeanPct):0}% time, {f.DocCount}/{recordCount} JDs"))]);

        var certBlock = ListBlock("C", [.. input.Certifications.Select(c => (c.Text, $"{c.DocCount}/{recordCount} JDs"))]);
        var ksaBlock = ListBlock("M", [.. input.Ksas.Select(k => (k.Text, $"{k.DocCount}/{recordCount} JDs"))]);

        return $"""
            RESPONSIBILITY FUNCTIONS:
            {fnBlock}

            LICENSES / CERTIFICATIONS:
            {(certBlock.Length > 0 ? certBlock : "(none)")}

            MINIMUM QUALIFICATIONS (KSAs):
            {(ksaBlock.Length > 0 ? ksaBlock : "(none)")}

            Consolidate each list into broad categories per the rules.
            """;
    }

    // ---------------------------------------------------------------- orchestration

    private sealed record Category(string Name, HashSet<string> Canons);

    public async Task<ConsolidationResult> ConsolidateAsync(
        IReadOnlyList<HrtmsRecord> records, CancellationToken ct = default)
    {
        var n = records.Count;
        var fns = RawFunctions(records);
        var certs = RawQuals(records, r => r.Qualifications.Licenses);
        var ksas = RawQuals(records, r => r.Qualifications.KsaMin);

        var p1 = await _llm.StructuredAsync<Pass1Result>(new StructuredRequest
        {
            System = Pass1System,
            User = BuildPass1User(ToInput(n, fns, certs, ksas)),
            Effort = LlmEffort.Medium,
            MaxTokens = 20000,
            Label = "consolidate.pass1",
        }, ct);

        var p1cats = p1.FunctionGroups
            .Select(grp =>
            {
                var members = grp.MemberIds.Select(IndexOfId).Where(i => i >= 0 && i < fns.Count).ToList();
                return new Category(grp.Name, [.. members.Select(i => fns[i].Canon)]);
            })
            .Where(c => c.Canons.Count > 0)
            .ToList();

        // Pass 2 runs ONLY when pass 1 overshot. Merging three categories into three costs a call
        // and changes nothing.
        var finalCats = p1cats;
        if (p1cats.Count > 4)
        {
            var p2User = $"FIRST-PASS CATEGORIES:\n"
                + string.Join('\n', p1cats.Select((c, i) => $"{i}: {c.Name} ({c.Canons.Count} member functions)"))
                + "\n\nMerge into the fewest broad categories.";

            var p2 = await _llm.StructuredAsync<Pass2Result>(new StructuredRequest
            {
                System = Pass2System,
                User = p2User,
                Effort = LlmEffort.Low,
                MaxTokens = 4000,
                Label = "consolidate.pass2",
            }, ct);

            finalCats = p2.Groups
                .Select(g =>
                {
                    var canons = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var i in g.MemberIndices)
                    {
                        if (i >= 0 && i < p1cats.Count)
                        {
                            canons.UnionWith(p1cats[i].Canons);
                        }
                    }

                    return new Category(g.Name, canons);
                })
                .Where(c => c.Canons.Count > 0)
                .ToList();

            // Safety net: a category pass 2 failed to place is kept rather than silently dropped.
            // Losing a whole category of work would be invisible in the output.
            var placed = new HashSet<string>(finalCats.SelectMany(c => c.Canons), StringComparer.Ordinal);
            foreach (var c in p1cats)
            {
                if (c.Canons.Any(x => !placed.Contains(x)))
                {
                    finalCats.Add(c);
                }
            }
        }

        var functionGroups = NormalizeTemplatePct(
                [.. finalCats.Select(c => FunctionStats(n, fns, c.Canons, c.Name))])
            .OrderByDescending(g => g.TemplatePct)
            .ToList();

        for (var i = 0; i < functionGroups.Count; i++)
        {
            functionGroups[i].Ordinal = i;
        }

        var result = new ConsolidationResult
        {
            FunctionGroups = functionGroups,
            Certifications = GroupQuals(p1.CertGroups, certs, n, ConsolidatedQualKind.Certification),
            // Hard cap at 7 KSAs — the envelope is meant to be usable, not exhaustive.
            MinQualifications = GroupQuals(p1.MinQualGroups, ksas, n, ConsolidatedQualKind.MinQualification)
                .Take(7).ToList(),
            DroppedFunctions = DroppedNames(p1.DroppedFunctionIds, [.. fns.Select(f => f.Name)]),
        };

        result.DroppedQualifications =
        [
            .. DroppedNames(p1.DroppedCertIds, [.. certs.Select(c => c.Text)]),
            .. DroppedNames(p1.DroppedMinQualIds, [.. ksas.Select(k => k.Text)]),
        ];

        return result;
    }

    private static List<ConsolidatedQual> GroupQuals(
        List<Group> groups, List<RawQual> raw, int recordCount, ConsolidatedQualKind kind)
    {
        var result = groups
            .Select(grp =>
            {
                var members = grp.MemberIds.Select(IndexOfId).Where(i => i >= 0 && i < raw.Count).ToList();
                if (members.Count == 0)
                {
                    return null;
                }

                var docs = new HashSet<int>();
                foreach (var i in members)
                {
                    docs.UnionWith(raw[i].Docs);
                }

                var q = new ConsolidatedQual
                {
                    Kind = kind,
                    Name = grp.Name,
                    // Frequency recomputed from the documents, never taken from the model.
                    Freq = recordCount > 0 ? docs.Count / (double)recordCount : 0,
                };

                for (var m = 0; m < members.Count; m++)
                {
                    q.Members.Add(new ConsolidatedQualMember { Ordinal = m, Text = raw[members[m]].Text });
                }

                return q;
            })
            .Where(q => q is not null)
            .Select(q => q!)
            .OrderByDescending(q => q.Freq)
            .ToList();

        for (var i = 0; i < result.Count; i++)
        {
            result[i].Ordinal = i;
        }

        return result;
    }

    private static List<string> DroppedNames(List<string> ids, List<string> names) =>
        ids.Select(IndexOfId).Where(i => i >= 0 && i < names.Count).Select(i => names[i]).ToList();
}

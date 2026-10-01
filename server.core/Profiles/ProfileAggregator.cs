using System.Text.RegularExpressions;
using Server.Core.Domain;
using Server.Core.Ingest;

namespace Server.Core.Profiles;

/// <summary>
/// Builds a class profile from parsed JD records. Ported from the POC's
/// src/lib/profile/aggregate.ts.
///
/// Entirely deterministic — no model call anywhere. Every number here is computed from the records,
/// which is the first half of the rule the whole system rests on: the model decides, code assembles.
/// The envelope, consolidation and coverage are layered on afterwards.
/// </summary>
public static partial class ProfileAggregator
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex AnyWhitespace();

    [GeneratedRegex(@"\w\S*")]
    private static partial Regex Word();

    [GeneratedRegex("[^a-z0-9 ]")]
    private static partial Regex NonKeyChar();

    /// <summary>Normalize a function name so "Cultural Operations" == "CULTURAL OPERATIONS".</summary>
    private static string Canon(string s) => AnyWhitespace().Replace(s.Trim(), " ").ToLowerInvariant();

    /// <summary>
    /// Title Case, matching the reference's <c>/\w\S*/g</c> replacement exactly: a word character
    /// followed by any run of non-space characters, uppercased at the first position and lowercased
    /// after. Note this differs from a naive per-word titleizer on tokens like "24/7".
    /// </summary>
    public static string Titleize(string s) => Word().Replace(s, m =>
    {
        var w = m.Value;
        return char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
    });

    /// <summary>
    /// One observed value and how many records held it, plus the modal value when one clearly
    /// dominates. Ties keep first-seen order, because JavaScript's sort is stable and the reference
    /// relies on that.
    /// </summary>
    private sealed record Counted(string? Value, int Count);

    private static (List<Counted> Values, string? Consensus, double Agreement) Distribute(
        IReadOnlyList<string?> values)
    {
        // Insertion order preserved so ties break the same way the reference's Map iteration does.
        var order = new List<string?>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var v in values)
        {
            // The reference keys on JSON.stringify, which distinguishes null from the string
            // "null". A sentinel that cannot collide with a real value does the same job.
            var key = v is null ? "\u0000null" : "s:" + v;
            if (counts.TryGetValue(key, out var n))
            {
                counts[key] = n + 1;
            }
            else
            {
                counts[key] = 1;
                order.Add(v);
            }
        }

        var ordered = order
            .Select(v => new Counted(v, counts[v is null ? "\u0000null" : "s:" + v]))
            .OrderByDescending(e => e.Count)
            .ToList();

        var top = ordered.FirstOrDefault();
        var agreement = values.Count > 0 ? (top?.Count ?? 0) / (double)values.Count : 0;

        // Half the corpus or more is the bar for a consensus. Below that the class genuinely varies
        // and claiming a single value would be misleading.
        return (ordered, agreement >= 0.5 ? top?.Value : null, agreement);
    }

    /// <summary>Booleans travel as "true"/"false"/null so one shape serves every field.</summary>
    private static string? BoolToken(bool? b) => b is null ? null : b.Value ? "true" : "false";

    private sealed record Range(double Min, double Max, double Mean, int N);

    private static Range RangeOf(IReadOnlyList<int> nums) =>
        nums.Count == 0
            ? new Range(0, 0, 0, 0)
            : new Range(nums.Min(), nums.Max(), nums.Sum() / (double)nums.Count, nums.Count);

    /// <summary>
    /// Frequency of free-text items with light near-duplicate merging, keyed on a lowercased,
    /// punctuation-stripped form. Returns the share of the corpus that mentions each.
    ///
    /// Counted per DOCUMENT, not per occurrence: a JD repeating a requirement three times still
    /// contributes one.
    /// </summary>
    private static List<(string Text, double Freq)> FreqItems(
        IReadOnlyList<List<string>> lists, int corpusSize)
    {
        var order = new List<string>();
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var docs = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        for (var doc = 0; doc < lists.Count; doc++)
        {
            foreach (var raw in lists[doc])
            {
                var key = AnyWhitespace()
                    .Replace(NonKeyChar().Replace(raw.ToLowerInvariant(), ""), " ")
                    .Trim();

                if (key.Length < 3)
                {
                    continue;
                }

                if (!docs.TryGetValue(key, out var set))
                {
                    set = [];
                    docs[key] = set;
                    // First raw spelling seen wins as the display text.
                    texts[key] = raw;
                    order.Add(key);
                }

                set.Add(doc);
            }
        }

        return order
            .Select(k => (Text: texts[k], Freq: corpusSize > 0 ? docs[k].Count / (double)corpusSize : 0))
            .OrderByDescending(x => x.Freq)
            .ToList();
    }

    public const string GeneratedNoteText =
        "Computed deterministically from parsed HRTMS records. Narrative fields " +
        "(duties, KSAs) are representative samples; AI synthesis of a single clean " +
        "'standard' phrasing is a later step.";

    /// <summary>
    /// Aggregate a class's records into a profile. The envelope, consolidation and coverage are
    /// left null — they are separate steps, and two of them cost a model call.
    /// </summary>
    public static ClassProfile Aggregate(IReadOnlyList<HrtmsRecord> records, string slug)
    {
        ArgumentOutOfRangeException.ThrowIfZero(records.Count);

        var n = records.Count;

        // Cluster responsibility functions by canonical name, tracking which documents each
        // appeared in so prevalence is a document share rather than an occurrence count.
        var order = new List<string>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var pcts = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var fnDocs = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var duties = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        for (var doc = 0; doc < n; doc++)
        {
            foreach (var f in records[doc].Responsibilities)
            {
                var k = Canon(f.FunctionName);
                if (!fnDocs.ContainsKey(k))
                {
                    order.Add(k);
                    names[k] = Titleize(f.FunctionName);
                    pcts[k] = [];
                    fnDocs[k] = [];
                    duties[k] = [];
                }

                if (f.Pct is not null)
                {
                    pcts[k].Add(f.Pct.Value);
                }

                fnDocs[k].Add(doc);
                duties[k].AddRange(f.Duties);
            }
        }

        var functions = order
            .Select(k =>
            {
                var r = RangeOf(pcts[k]);
                return new
                {
                    Name = names[k],
                    Range = r,
                    Prevalence = fnDocs[k].Count / (double)n,
                    // Distinct preserves first-seen order, matching `[...new Set(duties)]`.
                    Samples = duties[k].Distinct(StringComparer.Ordinal).Take(8).ToList(),
                };
            })
            .OrderByDescending(x => x.Range.Mean)
            .ToList();

        // The longest job summary stands in for the class. Stable ordering means ties fall to the
        // first record, as in the reference.
        var rep = records.OrderByDescending(r => r.JobSummary.Length).First();

        var (gradeVals, gradeCons, gradeAgree) = Distribute([.. records.Select(r => (string?)r.SalaryGrade)]);
        var (flsaVals, flsaCons, flsaAgree) = Distribute([.. records.Select(r => (string?)r.FlsaStatus)]);
        var (unionVals, unionCons, unionAgree) = Distribute([.. records.Select(r => (string?)r.UnionCode)]);
        var (supVals, supCons, supAgree) = Distribute([.. records.Select(r => BoolToken(r.Supervises))]);
        var (leadVals, leadCons, leadAgree) = Distribute([.. records.Select(r => BoolToken(r.Leads))]);
        var (outVals, outCons, outAgree) = Distribute([.. records.Select(r => BoolToken(r.WorksOutdoorsOver50pct))]);

        var (_, codeConsensus, _) = Distribute([.. records.Select(r => (string?)r.UcJobCode)]);
        var (_, familyConsensus, _) = Distribute([.. records.Select(r => (string?)r.CtJobFamily)]);
        var (_, functionConsensus, _) = Distribute([.. records.Select(r => (string?)r.CtJobFunction)]);
        var (_, programConsensus, _) = Distribute([.. records.Select(r => (string?)r.PersonnelProgram)]);

        var profile = new ClassProfile
        {
            Slug = slug,
            // The code comes from the export itself and is authoritative; consensus only guards
            // against a stray mis-keyed record.
            UcJobCode = codeConsensus ?? records[0].UcJobCode,
            Title = Titleize(records[0].UcJobTitle),
            CtJobFamily = familyConsensus ?? "",
            CtJobFunction = functionConsensus ?? "",
            PersonnelProgram = programConsensus ?? "",
            CorpusSize = n,
            RepresentativeSummary = rep.JobSummary,
            EnvelopeSource = null,
            GeneratedNote = GeneratedNoteText,
        };

        void AddDistribution(DistributionField field, List<Counted> values, string? consensus, double agreement)
        {
            var d = new ProfileDistribution
            {
                Field = field,
                Consensus = consensus,
                Agreement = agreement,
            };

            for (var i = 0; i < values.Count; i++)
            {
                d.Values.Add(new ProfileDistributionValue
                {
                    Ordinal = i,
                    // Null stays null: "the export did not say" is a real observation, and for
                    // `supervises` it is the majority one.
                    Value = values[i].Value,
                    Count = values[i].Count,
                });
            }

            profile.Distributions.Add(d);
        }

        AddDistribution(DistributionField.SalaryGrade, gradeVals, gradeCons, gradeAgree);
        AddDistribution(DistributionField.FlsaStatus, flsaVals, flsaCons, flsaAgree);
        AddDistribution(DistributionField.UnionCode, unionVals, unionCons, unionAgree);
        AddDistribution(DistributionField.Supervises, supVals, supCons, supAgree);
        AddDistribution(DistributionField.Leads, leadVals, leadCons, leadAgree);
        AddDistribution(DistributionField.WorksOutdoorsOver50pct, outVals, outCons, outAgree);

        for (var i = 0; i < functions.Count; i++)
        {
            var f = functions[i];
            var pf = new ProfileFunction
            {
                Ordinal = i,
                Name = f.Name,
                PctMin = f.Range.Min,
                PctMax = f.Range.Max,
                PctMean = f.Range.Mean,
                PctN = f.Range.N,
                Prevalence = f.Prevalence,
            };

            for (var j = 0; j < f.Samples.Count; j++)
            {
                pf.SampleDuties.Add(new ProfileFunctionSampleDuty { Ordinal = j, Text = f.Samples[j] });
            }

            profile.Functions.Add(pf);
        }

        void AddQuals(ProfileQualKind kind, IReadOnlyList<List<string>> lists)
        {
            var items = FreqItems(lists, n);
            for (var i = 0; i < items.Count; i++)
            {
                profile.Qualifications.Add(new ProfileQualItem
                {
                    Kind = kind,
                    Ordinal = i,
                    Text = items[i].Text,
                    Freq = items[i].Freq,
                });
            }
        }

        AddQuals(ProfileQualKind.License, [.. records.Select(r => r.Qualifications.Licenses)]);
        AddQuals(ProfileQualKind.MinExperience, [.. records.Select(r => r.Qualifications.MinExperience)]);
        // Education arrives as a single free-text field rather than a list, so it is wrapped —
        // and an empty one contributes nothing rather than an empty entry.
        AddQuals(ProfileQualKind.Education, [.. records.Select(r =>
            string.IsNullOrEmpty(r.Qualifications.Education) ? new List<string>() : [r.Qualifications.Education])]);
        AddQuals(ProfileQualKind.KsaMin, [.. records.Select(r => r.Qualifications.KsaMin)]);
        AddQuals(ProfileQualKind.KsaPref, [.. records.Select(r => r.Qualifications.KsaPref)]);
        AddQuals(ProfileQualKind.WorkEnvironment, [.. records.Select(r => r.WorkEnvironment)]);

        for (var i = 0; i < records.Count; i++)
        {
            profile.SourceFiles.Add(new ProfileSourceFile { Ordinal = i, SourceFile = records[i].SourceFile });
        }

        return profile;
    }
}

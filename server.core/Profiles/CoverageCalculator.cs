using System.Text.RegularExpressions;
using Server.Core.Domain;
using Server.Core.Ingest;

namespace Server.Core.Profiles;

/// <summary>
/// Backwards coverage: how much of each real JD's responsibility time falls inside the envelope's
/// standard categories, versus idiosyncratic work needing unit-specific customization. Ported from
/// the POC's src/lib/profile/coverage.ts.
///
/// A JD "matches" the template at 90% coverage or better — under 10% idiosyncratic, which is the
/// long-term target for the whole corpus.
///
/// Note there are TWO coverage checks in this system and they answer different questions. This one
/// takes the set of raw function names that count as covered and is computed from CONSOLIDATED
/// members, so it is unaffected by how the envelope is worded. The other —
/// <see cref="EnvelopeCoverageChecker"/> — asks a model to map raw JD functions onto the envelope's
/// own functions, and is the real guard that an edited envelope still covers its corpus.
/// </summary>
public static partial class CoverageCalculator
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex AnyWhitespace();

    private static string Canon(string s) => AnyWhitespace().Replace(s.Trim(), " ").ToLowerInvariant();

    /// <summary>
    /// Coverage of a class's records against its own consolidated categories. The covered set is
    /// every raw function name absorbed into a consolidated group.
    /// </summary>
    public static CoverageReport ComputeCoverage(ClassProfile profile, IReadOnlyList<HrtmsRecord> records)
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in profile.ConsolidatedFunctions)
        {
            foreach (var member in group.Members)
            {
                covered.Add(Canon(member.Text));
            }
        }

        return FromCoveredSet(records, covered);
    }

    /// <summary>
    /// Coverage given the set of canonicalized raw function names that count as covered. Shared by
    /// the consolidated view and by the check of an edited envelope.
    /// </summary>
    public static CoverageReport FromCoveredSet(
        IReadOnlyList<HrtmsRecord> records, IReadOnlySet<string> covered)
    {
        var perJd = new List<JdCoverage>(records.Count);

        foreach (var r in records)
        {
            var total = 0;
            var cov = 0;
            var uncovered = new List<(string Name, double Pct)>();

            foreach (var f in r.Responsibilities)
            {
                var pct = f.Pct ?? 0;
                total += pct;

                if (covered.Contains(Canon(f.FunctionName)))
                {
                    cov += pct;
                }
                else if (pct > 0)
                {
                    uncovered.Add((f.FunctionName, pct));
                }
            }

            var jd = new JdCoverage
            {
                SourceFile = r.SourceFile,
                // A JD with no stated percentages is treated as fully covered rather than as a
                // zero — absence of data is not evidence of a mismatch.
                CoveredPct = total > 0 ? Math.Floor(cov / (double)total * 100 + 0.5) : 100,
            };

            var sorted = uncovered.OrderByDescending(u => u.Pct).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                jd.Uncovered.Add(new JdCoverageUncovered
                {
                    Ordinal = i,
                    Name = sorted[i].Name,
                    Pct = sorted[i].Pct,
                });
            }

            perJd.Add(jd);
        }

        var report = new CoverageReport
        {
            N = perJd.Count,
            MeanCoverage = perJd.Count > 0
                ? Math.Floor(perJd.Sum(x => x.CoveredPct) / perJd.Count + 0.5)
                : 0,
            WellCoveredPct = perJd.Count > 0
                ? perJd.Count(x => x.CoveredPct >= 90) / (double)perJd.Count
                : 0,
        };

        // Worst fit first: this list is a work queue for the review-and-nudge workflow, not a
        // catalogue, so the JDs needing attention belong at the top.
        var ordered = perJd.OrderBy(x => x.CoveredPct).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Ordinal = i;
            report.PerJd.Add(ordered[i]);
        }

        return report;
    }
}

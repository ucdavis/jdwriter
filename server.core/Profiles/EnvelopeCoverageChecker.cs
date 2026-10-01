using System.ComponentModel;
using System.Text.RegularExpressions;
using Server.Core.Ai;
using Server.Core.Domain;
using Server.Core.Ingest;

namespace Server.Core.Profiles;

/// <summary>
/// Checks an envelope against the class's real JDs. Ported from the POC's
/// src/lib/profile/coverageCheck.ts.
///
/// The model maps each distinct raw JD function onto one of the envelope's Key Responsibility
/// functions (or marks it uncovered); coverage is then computed deterministically from that mapping.
/// The model decides what counts as the same kind of work — it is never asked for a percentage.
///
/// This is the coverage check that actually guards against drift. The stored backwards-coverage on a
/// profile is computed from CONSOLIDATED members and so cannot notice that an envelope has been
/// reworded away from its corpus; this one can, which is why it runs before and after applying an
/// official standard — so a standard cannot silently degrade fit.
/// </summary>
public sealed partial class EnvelopeCoverageChecker
{
    private readonly IStructuredLlm _llm;

    public EnvelopeCoverageChecker(IStructuredLlm llm)
    {
        _llm = llm;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex AnyWhitespace();

    [GeneratedRegex(@"^\D+")]
    private static partial Regex LeadingNonDigits();

    private static string Canon(string s) => AnyWhitespace().Replace(s.Trim(), " ").ToLowerInvariant();

    private sealed class Mapping
    {
        [Description("The raw function id, e.g. R3.")]
        public string Id { get; set; } = "";

        [Description("Index of the envelope function that covers this raw function, or -1 if none do.")]
        public int EnvelopeIndex { get; set; }
    }

    private sealed class MapResult
    {
        public List<Mapping> Mappings { get; set; } = [];
    }

    public const string System = """
        You are a UC Davis HR job analyst. You are given a standardized envelope's Key
        Responsibility functions (each an index, a broad function name, and example duties) and a list
        of raw function names pulled from the real job descriptions for this class. For EACH raw
        function, decide which envelope function's scope covers that work and return its index; return
        -1 only if none of the envelope functions reasonably cover it. Be inclusive: a raw function is
        covered if its work plausibly falls under an envelope function's remit, even if worded very
        differently. Map every raw function id exactly once.
        """;

    /// <summary>Exposed for prompt-assembly tests.</summary>
    public static string BuildUser(JobEnvelope envelope, IReadOnlyList<(string Canon, string Name)> raws)
    {
        var envBlock = string.Join('\n', envelope.KeyResponsibilities
            .OrderBy(r => r.Ordinal)
            .Select((r, i) => $"{i}: {r.FunctionName} — e.g. "
                + string.Join("; ", r.Duties.OrderBy(d => d.Ordinal).Take(3).Select(d => d.Text))));

        var rawBlock = string.Join('\n', raws.Select((r, i) => $"R{i}: {r.Name}"));

        return $"ENVELOPE KEY RESPONSIBILITY FUNCTIONS:\n{envBlock}\n\nRAW JD FUNCTIONS TO MAP:\n{rawBlock}";
    }

    public async Task<CoverageReport> CheckAsync(
        JobEnvelope envelope, IReadOnlyList<HrtmsRecord> records, CancellationToken ct = default)
    {
        // Distinct raw functions across the corpus, first spelling seen winning as the label.
        var order = new List<string>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var r in records)
        {
            foreach (var f in r.Responsibilities)
            {
                var k = Canon(f.FunctionName);
                if (!names.ContainsKey(k))
                {
                    names[k] = f.FunctionName;
                    order.Add(k);
                }
            }
        }

        var raws = order.Select(k => (Canon: k, Name: names[k])).ToList();

        // Nothing to map means nothing is covered — and no call is worth making.
        if (raws.Count == 0 || envelope.KeyResponsibilities.Count == 0)
        {
            return CoverageCalculator.FromCoveredSet(records, new HashSet<string>(StringComparer.Ordinal));
        }

        var res = await _llm.StructuredAsync<MapResult>(new StructuredRequest
        {
            System = System,
            User = BuildUser(envelope, raws),
            Effort = LlmEffort.Low,
            MaxTokens = 8000,
            Label = "envelope.coverageCheck",
        }, ct);

        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in res.Mappings)
        {
            var digits = LeadingNonDigits().Replace(m.Id ?? "", "");
            if (!int.TryParse(digits, out var i))
            {
                continue;
            }

            // A negative index is the model saying "nothing covers this", which is a valid answer.
            if (m.EnvelopeIndex >= 0 && i >= 0 && i < raws.Count)
            {
                covered.Add(raws[i].Canon);
            }
        }

        return CoverageCalculator.FromCoveredSet(records, covered);
    }
}

using System.Text.RegularExpressions;
using Server.Core.Domain;

namespace Server.Core.Titles;

/// <summary>Why a title did not resolve to a job code.</summary>
public enum TitleCodeResolution
{
    /// <summary>Resolved to exactly one code.</summary>
    Ok,

    /// <summary>Not a UC Davis title at all — the standards cover the whole UC system.</summary>
    Unknown,

    /// <summary>Listed against several in-use codes. Needs a human, not a guess.</summary>
    Ambiguous,
}

/// <summary>
/// An immutable in-memory index over the title reference. Ported from the POC's
/// src/lib/titleCodes.ts and verified against fixtures/titleCodes.*.json.
///
/// Deliberately has no EF or filesystem dependency: all the logic worth testing lives here, and
/// <see cref="TitleCodeService"/> is the thin thing that loads rows and caches an instance. The
/// POC's mtime-keyed caching is gone entirely — it existed to work around Next.js module
/// instances not sharing state, which is not a problem this stack has.
/// </summary>
public sealed partial class TitleCodeIndex
{
    private readonly List<TitleCode> _all;
    private readonly Dictionary<string, List<TitleCode>> _byStrictKey;
    private readonly Dictionary<string, Supersession> _supersededFrom;
    private readonly List<Supersession> _supersessions;
    private readonly List<string> _ambiguousSupersessions;

    [GeneratedRegex("[^0-9]")]
    private static partial Regex NonDigit();

    /// <summary>
    /// Bargaining units whose accretion issues a suffixed successor code: RP (Research and Public
    /// Service Professionals), CX (Clerical), TX (Technical), RX (Research Support), HX (Health
    /// Care Professionals) and SV (Student Services and Advising Professionals, UAW).
    ///
    /// SV was once listed here as a supervisor variant, wrongly: it is a bargaining unit, and its
    /// titles are "bargaining unit only with no uncovered positions" (UCnet), so "ACAD ACHIEVEMENT
    /// CNSLR 2 SV" (004972) replaces 004500 exactly as an RP code replaces its predecessor. Missing
    /// it left 44 student-services classes unpaired, and a JD filed under a retired code built a
    /// duplicate beside its successor.
    ///
    /// Deliberately NOT every suffix. NEX means non-exempt ("SRA 2" and "SRA 2 NEX" are both
    /// current, and treating it as a union would refile 118 JDs under the wrong class); EX marks
    /// exempt medical-center professions; LD and PD are lead and per-diem variants.
    ///
    /// GF (grandfathered) is stripped from the base too, as the POC did, so a GF variant is a
    /// second candidate in its group and the group is reported as ambiguous rather than guessed —
    /// which is why "RSCH AND DEV ENGR 4" (with an in-use "... TX GF") is not paired.
    /// </summary>
    [GeneratedRegex(@"\s+(RP|CX|TX|RX|HX|SV|GF)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnionSuffix();

    [GeneratedRegex(@"\b(RP|CX|TX|RX|HX|SV)\b", RegexOptions.IgnoreCase)]
    private static partial Regex IsUnionTitle();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Codes are stored zero-padded to six digits, but forms are filled in without the padding
    /// ("7399"), so both spellings must land on the same record.
    /// </summary>
    public static string Pad(string code) => NonDigit().Replace(code ?? "", "").PadLeft(6, '0');

    public TitleCodeIndex(IEnumerable<TitleCode> titleCodes)
    {
        _all = [.. titleCodes];

        // Indexed on the STRICT key, keeping every code that shares one. A title mapping to more
        // than one code is a question the title alone cannot answer, and a wrong job code is
        // worse than a missing one.
        _byStrictKey = new Dictionary<string, List<TitleCode>>(StringComparer.Ordinal);
        foreach (var t in _all)
        {
            var key = TitleNormalizer.TitleCodeKey(t.Title);
            if (_byStrictKey.TryGetValue(key, out var list))
            {
                list.Add(t);
            }
            else
            {
                _byStrictKey[key] = [t];
            }
        }

        (_supersessions, _ambiguousSupersessions, _supersededFrom) = DeriveSupersessions(_all);
    }

    /// <summary>Every row in the reference.</summary>
    public IReadOnlyList<TitleCode> All => _all;

    /// <summary>
    /// Resolve a title to its job code. Returns null rather than guessing when the title is
    /// ambiguous: a caller can report a gap, but it cannot detect a confidently-wrong code.
    /// </summary>
    public TitleCode? FindTitleCode(string title)
    {
        if (!_byStrictKey.TryGetValue(TitleNormalizer.TitleCodeKey(title), out var hits) || hits.Count == 0)
        {
            return null;
        }

        if (hits.Count == 1)
        {
            return hits[0];
        }

        // The reference lists some titles twice, once as a title UCD could use and once as one
        // actually on payroll. The in-use record is the real classification.
        var inUse = hits.Where(t => t.IsInUse).ToList();
        return inUse.Count == 1 ? inUse[0] : null;
    }

    /// <summary>
    /// Why a title did not resolve. "Not a UC Davis title" and "ambiguous, needs a human" want
    /// different responses, so they are not collapsed into one failure bucket.
    /// </summary>
    public TitleCodeResolution ExplainTitleCode(string title)
    {
        if (!_byStrictKey.TryGetValue(TitleNormalizer.TitleCodeKey(title), out var hits) || hits.Count == 0)
        {
            return TitleCodeResolution.Unknown;
        }

        return hits.Count == 1 || hits.Count(t => t.IsInUse) == 1
            ? TitleCodeResolution.Ok
            : TitleCodeResolution.Ambiguous;
    }

    /// <summary>
    /// Resolve a job code to its reference row. Note the stored code is padded but NOT
    /// digit-stripped, matching the reference implementation: only the input is normalized.
    /// </summary>
    public TitleCode? FindByCode(string code)
    {
        var key = Pad(code);
        return _all.FirstOrDefault(t => t.Code.PadLeft(6, '0') == key);
    }

    /// <summary>
    /// Titles actually in use at UC Davis, excluding superseded codes.
    ///
    /// Superseded codes are still "in use" as far as the reference is concerned, but offering
    /// one as a class to author against would produce a JD under a dead classification.
    /// </summary>
    public IReadOnlyList<TitleCode> InUseTitleCodes() =>
        _all.Where(t => t.IsInUse && !IsSuperseded(t.Code)).ToList();

    public IReadOnlyList<Supersession> AllSupersessions() => _supersessions;

    /// <summary>
    /// Base titles the reference cannot resolve — more than one candidate on either side of the
    /// pairing. Reported rather than guessed at, because deprecating the wrong code would hide a
    /// live class.
    /// </summary>
    public IReadOnlyList<string> AmbiguousSupersessions() => _ambiguousSupersessions;

    public Supersession? SupersededBy(string code) =>
        _supersededFrom.TryGetValue(Pad(code), out var s) ? s : null;

    public bool IsSuperseded(string code) => _supersededFrom.ContainsKey(Pad(code));

    /// <summary>The code a class should actually be filed under today.</summary>
    public string ResolveCode(string code) =>
        _supersededFrom.TryGetValue(Pad(code), out var s) ? s.ToCode : code;

    /// <summary>
    /// Derive union accretion pairs.
    ///
    /// When a population is accreted into a bargaining unit (RP, CX, TX, RX or HX — see
    /// <see cref="UnionSuffix"/>), UC issues a NEW job code with the unit as a suffix and the
    /// non-represented code it replaces is left with no incumbents. Both codes stay in the
    /// reference, so the pairing has to be computed.
    ///
    /// The POC derived RP pairs only. Its 29 are reproduced exactly (the parity fixture pins them);
    /// the other units extend the same rule rather than changing it.
    ///
    /// This CANNOT be inferred from the JD corpus: exports under a superseded code still read
    /// "99 - Non-Represented (PPSM)" because they predate the accretion. Nor does a union code
    /// imply supersession — most represented titles have no suffixed variant and are current.
    /// The only reliable signal is a base title holding BOTH an in-use non-represented code and an
    /// in-use suffixed one.
    /// </summary>
    private static (List<Supersession>, List<string>, Dictionary<string, Supersession>) DeriveSupersessions(
        List<TitleCode> all)
    {
        // Insertion order is preserved explicitly so the ambiguity report is deterministic; a
        // plain Dictionary makes no such guarantee, and the reference implementation iterates a
        // JS Map in insertion order.
        var order = new List<string>();
        var byBase = new Dictionary<string, List<TitleCode>>(StringComparer.Ordinal);

        foreach (var t in all)
        {
            // A code nobody is paid under cannot supersede anything.
            if (!t.IsInUse)
            {
                continue;
            }

            var b = Whitespace().Replace(UnionSuffix().Replace(t.Title, " "), " ").Trim();
            if (byBase.TryGetValue(b, out var list))
            {
                list.Add(t);
            }
            else
            {
                byBase[b] = [t];
                order.Add(b);
            }
        }

        var map = new Dictionary<string, Supersession>(StringComparer.Ordinal);
        var ambiguous = new List<string>();

        foreach (var b in order)
        {
            var group = byBase[b];
            var reps = group.Where(t => IsUnionTitle().IsMatch(t.Title)).ToList();
            var plain = group.Where(t => !IsUnionTitle().IsMatch(t.Title)).ToList();

            if (reps.Count == 0 || plain.Count == 0)
            {
                continue;
            }

            if (reps.Count > 1 || plain.Count > 1)
            {
                ambiguous.Add($"{b} ({string.Join(", ", group.Select(t => $"{t.Code} {t.Title}"))})");
                continue;
            }

            map[Pad(plain[0].Code)] = new Supersession
            {
                FromCode = plain[0].Code,
                FromTitle = plain[0].Title,
                ToCode = reps[0].Code,
                ToTitle = reps[0].Title,
            };
        }

        // Ordered by retired code. The reference implementation orders by title for display, but
        // codes are unique and collation-free, which is what lets the parity test assert an exact
        // sequence instead of falling back to a set comparison.
        var sorted = map.Values.OrderBy(s => s.FromCode, StringComparer.Ordinal).ToList();
        return (sorted, ambiguous, map);
    }
}

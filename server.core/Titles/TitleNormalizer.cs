using System.Text.RegularExpressions;

namespace Server.Core.Titles;

/// <summary>
/// UC title normalization. Ported from the POC's src/lib/titles.ts, and verified against
/// fixtures/titles.normalization.json over 3,733 real titles.
///
/// The whole point of this class is the TWO keys it produces off one normalization pass:
///
///   TitleKey     — loose. Bargaining-unit and variant suffixes are DROPPED. Use for search and
///                  for linking an official standard to a class profile.
///   TitleCodeKey — strict. Those suffixes are KEPT. Use ONLY for resolving a job code.
///
/// They were once a single key, and merging them collapsed 221 reference entries onto shared
/// keys. Because resolution took first-wins, the result was not missing matches but confidently
/// WRONG ones: "PROJECT POLICY ANL 1" is 007396 and "PROJECT POLICY ANL 1 RP" is 005255, and the
/// loose key cannot tell them apart. Do not re-merge them.
/// </summary>
public static partial class TitleNormalizer
{
    /// <summary>
    /// Common UC title abbreviations, expanded so "acad" and "academic" agree in either
    /// direction. Several entries were added by auditing official job-standard titles against
    /// the title-code reference, where each miss was a whole family of failed matches.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Abbreviations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["acad"] = "academic", ["adv"] = "advanced", ["admin"] = "administrative",
            ["adm"] = "administrator", ["anl"] = "analyst", ["ast"] = "assistant",
            ["anml"] = "animal", ["assoc"] = "associate", ["cmty"] = "community",
            ["comm"] = "communications", ["coord"] = "coordinator", ["ctr"] = "center",
            ["dev"] = "development", ["educ"] = "education", ["eng"] = "engineer",
            ["ext"] = "external", ["fin"] = "financial", ["info"] = "information",
            ["it"] = "information technology", ["lab"] = "laboratory", ["ld"] = "lead",
            ["mach"] = "machinery", ["mech"] = "mechanic", ["mgmt"] = "management",
            ["mgt"] = "management", ["mgr"] = "manager", ["mktg"] = "marketing",
            ["mus"] = "museum", ["ofcr"] = "officer", ["prg"] = "program",
            ["prgm"] = "program", ["prn"] = "principal", ["progr"] = "programmer",
            ["proj"] = "project", ["rel"] = "relations", ["resc"] = "resource",
            ["rsch"] = "research", ["sci"] = "scientist", ["spec"] = "specialist",
            ["sr"] = "senior", ["sra"] = "staff research associate", ["stdt"] = "student",
            ["supp"] = "support", ["supv"] = "supervisor", ["svc"] = "services",
            ["svcs"] = "services", ["sys"] = "systems", ["tchl"] = "technical",
            ["tchn"] = "technician", ["tech"] = "technician", ["bus"] = "business",
            ["grad"] = "graduate", ["undergrad"] = "undergraduate",
            // found by auditing job-standard titles against the reference
            ["plnr"] = "planner", ["opr"] = "operator", ["secr"] = "secretary",
            ["hr"] = "human resources", ["pract"] = "practitioner", ["chf"] = "chief",
            ["asst"] = "assistant",
            // seen across the in-use UC Davis title list
            ["profl"] = "professional", ["clin"] = "clinical", ["engr"] = "engineer",
            ["crd"] = "coordinator", ["med"] = "medical", ["exec"] = "executive",
            ["repr"] = "representative", ["pat"] = "patient", ["beh"] = "behavioral",
            ["hosp"] = "hospital", ["scrty"] = "security", ["ath"] = "athletic",
            ["cnslr"] = "counselor", ["recrmt"] = "recruitment", ["fac"] = "facilities",
            ["pd"] = "per diem",
        };

    /// <summary>
    /// Bargaining-unit and variant suffixes. Ignored when asking "is this the same class?" but
    /// NEVER when resolving a job code.
    ///
    /// Note "pd" appears here AND in <see cref="Abbreviations"/>: strictly it expands to
    /// "per diem", loosely it is dropped. That is intentional, not a collision.
    /// </summary>
    public static readonly IReadOnlySet<string> VariantSuffixes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "cx", "tx", "rx", "px", "sx", "nex", "ex", "99", "rp", "sv", "gf", "hc", "pd", "me",
        };

    /// <summary>
    /// Grammatical connectors, dropped from both sides so "Project and Policy Analyst" and
    /// "PROJECT POLICY ANL" agree.
    /// </summary>
    private static readonly IReadOnlySet<string> Connectors =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "and", "or", "of", "the", "for", "a", "an", "to", "in",
        };

    /// <summary>
    /// Word families UC spells inconsistently between the standards and the title codes —
    /// "BUS SYS ANL SUPV 2" is written "Business Systems Analysis Supervisor 2" on its standard.
    /// Applied AFTER abbreviation expansion, so "admin" -> "administrative" -> "administrator"
    /// resolves in one pass.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> WordFamilies =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["analysis"] = "analyst", ["analyses"] = "analyst", ["programming"] = "programmer",
            ["administration"] = "administrator", ["administrative"] = "administrator",
            ["management"] = "manager", ["supervision"] = "supervisor",
        };

    /// <summary>
    /// Expansions safe only when resolving a code. "me" is a pronoun someone could plausibly
    /// type into the class search, but as a title suffix it means MSP Executive.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CodeOnlyAbbreviations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["me"] = "msp executive",
        };

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthesized();

    [GeneratedRegex("[^a-z0-9 ]")]
    private static partial Regex NonTitleChar();

    /// <summary>
    /// Singular/plural varies freely ("System Administrator 3" vs "SYS ADM 3", which expands to
    /// "systems"). Strips a trailing -s except where it is part of the stem — "business",
    /// "status", "analysis" (the last already canonicalized above).
    /// </summary>
    private static string Singular(string w) =>
        w.Length >= 4
        && w.EndsWith('s')
        && !(w.EndsWith("ss", StringComparison.Ordinal)
             || w.EndsWith("us", StringComparison.Ordinal)
             || w.EndsWith("is", StringComparison.Ordinal))
            ? w[..^1]
            : w;

    /// <summary>
    /// The shared normalization. <paramref name="keepVariants"/> is the ONLY difference between
    /// the loose and strict keys. Step order is load-bearing and matches the TypeScript exactly:
    /// ampersand expansion happens before connectors are dropped (so "R&amp;D" loses its "and"),
    /// and abbreviations expand before word families and singularization are applied.
    /// </summary>
    private static List<string> Normalize(string s, bool keepVariants)
    {
        var lowered = (s ?? string.Empty).ToLowerInvariant().Replace("&", " and ", StringComparison.Ordinal);
        var stripped = NonTitleChar().Replace(Parenthesized().Replace(lowered, " "), " ");

        var result = new List<string>();
        foreach (var raw in stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Connectors.Contains(raw))
            {
                continue;
            }

            if (!keepVariants && VariantSuffixes.Contains(raw))
            {
                continue;
            }

            string expanded;
            if (keepVariants && CodeOnlyAbbreviations.TryGetValue(raw, out var codeOnly))
            {
                expanded = codeOnly;
            }
            else if (Abbreviations.TryGetValue(raw, out var abbr))
            {
                expanded = abbr;
            }
            else
            {
                expanded = raw;
            }

            // An expansion can be multi-word ("staff research associate"), and each resulting
            // word goes through the family canon and singularization on its own.
            foreach (var word in expanded.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                result.Add(Singular(WordFamilies.TryGetValue(word, out var canon) ? canon : word));
            }
        }

        return result;
    }

    /// <summary>Tokenized loose form: abbreviations expanded, variant suffixes dropped.</summary>
    public static IReadOnlyList<string> TitleWords(string s) => Normalize(s, keepVariants: false);

    /// <summary>Loose comparable key. For search and standard-to-profile linking.</summary>
    public static string TitleKey(string s) => string.Join(' ', Normalize(s, keepVariants: false)).Trim();

    /// <summary>
    /// Strict key for job-code resolution: keeps the variant suffix that separates two real
    /// codes. Never use this for fuzzy search.
    /// </summary>
    public static string TitleCodeKey(string s) => string.Join(' ', Normalize(s, keepVariants: true)).Trim();

    /// <summary>
    /// Does a search query match a title, tolerant of abbreviations in either direction? Every
    /// expanded query word must appear in the expanded title.
    /// </summary>
    public static bool TitleMatches(string query, string title)
    {
        var key = TitleKey(title);
        foreach (var w in TitleWords(query))
        {
            if (!key.Contains(w, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

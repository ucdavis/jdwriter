using System.Text.RegularExpressions;
using Server.Core.Domain;

namespace Server.Core.Titles;

/// <summary>
/// A class's bargaining unit, and whether its positions are represented.
///
/// The title's union suffix is authoritative: "Academic Achievement Counselor 2 SV" is in the SV
/// unit whatever its corpus says. The corpus consensus can say "99 - Non-Represented" for a
/// represented class, because the exports predate the accretion that created the suffixed code
/// (see <see cref="TitleCodeIndex"/>). Only when the title carries no suffix does the consensus
/// decide.
///
/// The suffixes are the bargaining units that appear as title suffixes on UC Davis payroll — the
/// same set the supersession pairing uses. "EX" also ends some titles, but there it marks exempt
/// medical-center professions, not a unit.
/// </summary>
public static partial class BargainingUnits
{
    /// <summary>A unit suffix at the end of the title, optionally followed by GF (grandfathered).</summary>
    [GeneratedRegex(@"\b(RP|CX|TX|RX|HX|SV)(?:\s+GF)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TitleSuffix();

    /// <summary>The unit a title's suffix names ("SV"), or null when it has none.</summary>
    public static string? FromTitle(string title)
    {
        var m = TitleSuffix().Match(title ?? "");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>The bargaining unit to show and save for a class: the title's suffix, else the corpus consensus.</summary>
    public static string? For(ClassProfile profile) =>
        FromTitle(profile.Title) ?? Consensus(profile);

    /// <summary>
    /// Whether the class's positions are union-represented. Represented positions may lead but may
    /// not supervise. An unknown unit (no suffix, no consensus) is treated as not represented, so
    /// the rule never blocks on missing data.
    /// </summary>
    public static bool IsRepresented(ClassProfile profile)
    {
        if (FromTitle(profile.Title) is not null)
        {
            return true;
        }

        var unit = Consensus(profile)?.Trim() ?? "";
        if (unit.Length == 0)
        {
            return false;
        }

        // "99", "99 - Non-Represented (PPSM)": the code is the leading token.
        var code = new string(unit.TakeWhile(char.IsLetterOrDigit).ToArray());
        return code != "99";
    }

    private static string? Consensus(ClassProfile profile) =>
        profile.Distributions.FirstOrDefault(d => d.Field == DistributionField.UnionCode)?.Consensus;
}

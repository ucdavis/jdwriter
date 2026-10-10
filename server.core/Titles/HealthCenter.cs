using System.Text.RegularExpressions;
using Server.Core.Domain;

namespace Server.Core.Titles;

/// <summary>The other version of a class: its Health Center (HC) code, or the regular one.</summary>
public sealed class HealthCenterTwin
{
    public string UcJobCode { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>The twin's class in JDWriter, when it has one.</summary>
    public string? Slug { get; set; }

    /// <summary>True when the twin is the Health Center version.</summary>
    public bool HealthCenter { get; set; }
}

/// <summary>
/// Health Center (HC) classes. An HC code is its own classification, used only at the Health
/// Center and often with different duties, so it is never the same class as the title without HC.
///
/// The ported title normalizer treats "hc" as a droppable variant suffix in the loose key — right
/// for search, wrong for "is this the same class?". That port is byte-for-byte, so instead of
/// changing it, every same-class comparison goes through <see cref="ClassKey"/>, which keeps HC.
/// A few HC codes have a regular twin ("ACCOUNTING MGR 2" / "ACCOUNTING MGR 2 HC"); most are
/// Health Center classes with none.
/// </summary>
public static partial class HealthCenter
{
    [GeneratedRegex(@"\bHC\b", RegexOptions.IgnoreCase)]
    private static partial Regex HcToken();

    public static bool IsHealthCenter(string title) => HcToken().IsMatch(title ?? "");

    /// <summary>The loose title key, plus HC: what two titles must share to be the same class.</summary>
    public static string ClassKey(string title) =>
        TitleNormalizer.TitleKey(title) + (IsHealthCenter(title) ? " |hc" : "");

    /// <summary>
    /// The in-use code that is this class's other version — same title, HC toggled — or null when it
    /// has none, or more than one (not guessed).
    /// </summary>
    public static HealthCenterTwin? TwinOf(
        string title, string code, TitleCodeIndex index, IReadOnlyDictionary<string, ClassRef> classByCode)
    {
        var key = TitleNormalizer.TitleKey(title);
        var hc = IsHealthCenter(title);
        var twins = index.InUseTitleCodes()
            .Where(t => IsHealthCenter(t.Title) != hc
                        && TitleCodeIndex.Pad(t.Code) != TitleCodeIndex.Pad(code)
                        && TitleNormalizer.TitleKey(t.Title) == key)
            .ToList();
        if (twins.Count != 1)
        {
            return null;
        }

        var twin = twins[0];
        // Named as JDWriter names its class when it has one, else from the payroll title.
        var known = classByCode.GetValueOrDefault(TitleCodeIndex.Pad(twin.Code));
        return new HealthCenterTwin
        {
            UcJobCode = twin.Code,
            Title = known?.Title ?? HcToken().Replace(Profiles.ProfileAggregator.Titleize(twin.Title), "HC"),
            Slug = known?.Slug,
            HealthCenter = !hc,
        };
    }

    /// <summary>Existing classes by padded job code, for naming and linking a twin.</summary>
    public static IReadOnlyDictionary<string, ClassRef> ClassesByCode(IEnumerable<(string Code, string Slug, string Title)> classes) =>
        classes
            .GroupBy(c => TitleCodeIndex.Pad(c.Code))
            .ToDictionary(g => g.Key, g => new ClassRef(g.First().Slug, g.First().Title), StringComparer.Ordinal);
}

/// <summary>An existing class, as a twin links to it.</summary>
public sealed record ClassRef(string Slug, string Title);

/// <summary>How much an author may reasonably add to a class's envelope.</summary>
public static partial class Customization
{
    [GeneratedRegex(@"\b(\d{1,2})\b")]
    private static partial Regex LevelNumber();

    /// <summary>
    /// The customization target, in percent. Most positions in a class need little beyond its
    /// standard (10%); senior individual contributors (level 4 and up) and supervisors and managers
    /// shape their roles more, and may reasonably add up to 30%.
    /// </summary>
    public static int TargetPct(string title) =>
        Supervision.IsSupervisory(title) || Level(title) >= 4 ? 30 : 10;

    /// <summary>The class's level: its last standalone number ("Financial Analyst 4 CX" is 4).</summary>
    public static int? Level(string title)
    {
        var matches = LevelNumber().Matches(title ?? "");
        return matches.Count == 0 ? null : int.Parse(matches[^1].Groups[1].Value);
    }
}

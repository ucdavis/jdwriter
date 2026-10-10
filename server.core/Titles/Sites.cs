using System.Text.RegularExpressions;
using Server.Core.Domain;

namespace Server.Core.Titles;

/// <summary>
/// The other version of a class at a different site: its Health Center or Student Health Center
/// code, or the regular one.
/// </summary>
public sealed class SiteTwin
{
    public string UcJobCode { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>The twin's class in JDWriter, when it has one.</summary>
    public string? Slug { get; set; }

    /// <summary>The twin's site ("Health Center", "Student Health Center"), or null for the regular class.</summary>
    public string? Site { get; set; }
}

/// <summary>
/// Site-specific classes: Health Center (HC) and Student Health Center (SHS) codes. Each is its own
/// classification, used only at its site and often with different duties, so it is never the same
/// class as the title without the site marker.
///
/// The ported title normalizer drops "hc" from its loose key as a variant suffix — right for search,
/// wrong for "is this the same class?" — and keeps "shs". That port is byte-for-byte, so every
/// same-class comparison goes through <see cref="ClassKey"/> instead, which keeps the site. A few
/// site codes have a regular twin ("CLIN LAB SUPV 2" / "CLIN LAB SHS SUPV 2"); most have none.
/// A title with both markers ("HC ADM SHS MGR 1", HC as health care) is Student Health.
/// </summary>
public static partial class Sites
{
    public const string HealthCenter = "Health Center";
    public const string StudentHealth = "Student Health Center";

    [GeneratedRegex(@"\bHC\b", RegexOptions.IgnoreCase)]
    private static partial Regex HcToken();

    [GeneratedRegex(@"\bSHS\b", RegexOptions.IgnoreCase)]
    private static partial Regex ShsToken();

    [GeneratedRegex(@"\b(?:HC|SHS)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SiteTokens();

    /// <summary>The site a class is for — Student Health Center, Health Center — or null for a regular class.</summary>
    public static string? SiteOf(string title)
    {
        title ??= "";
        return ShsToken().IsMatch(title) ? StudentHealth : HcToken().IsMatch(title) ? HealthCenter : null;
    }

    /// <summary>The loose key of the title without its site markers: what twins share.</summary>
    private static string BaseKey(string title) => TitleNormalizer.TitleKey(SiteTokens().Replace(title ?? "", " "));

    /// <summary>What two titles must share to be the same class: the base key, and the site.</summary>
    public static string ClassKey(string title) => BaseKey(title) + (SiteOf(title) is { } site ? $" |{site}" : "");

    /// <summary>
    /// The class's versions at other sites, among in-use codes. A regular class lists each site
    /// version it has; a site class lists its regular version. A site with more than one candidate is
    /// left out rather than guessed.
    /// </summary>
    public static List<SiteTwin> TwinsOf(
        string title, string code, TitleCodeIndex index, IReadOnlyDictionary<string, ClassRef> classByCode)
    {
        var key = BaseKey(title);
        var site = SiteOf(title);
        return index.InUseTitleCodes()
            .Where(t => TitleCodeIndex.Pad(t.Code) != TitleCodeIndex.Pad(code)
                        && BaseKey(t.Title) == key
                        && SiteOf(t.Title) != site
                        // A site class points only to the regular one, not to another site.
                        && (site is null || SiteOf(t.Title) is null))
            .GroupBy(t => SiteOf(t.Title))
            .Where(g => g.Count() == 1)
            .Select(g =>
            {
                var twin = g.Single();
                // Named as JDWriter names its class when it has one, else from the payroll title.
                var known = classByCode.GetValueOrDefault(TitleCodeIndex.Pad(twin.Code));
                return new SiteTwin
                {
                    UcJobCode = twin.Code,
                    Title = known?.Title ?? Uppercase(Profiles.ProfileAggregator.Titleize(twin.Title)),
                    Slug = known?.Slug,
                    Site = g.Key,
                };
            })
            .OrderBy(t => t.Site, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Titleize lowercases the markers ("Hc"); they read as acronyms.</summary>
    private static string Uppercase(string title) => SiteTokens().Replace(title, m => m.Value.ToUpperInvariant());

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

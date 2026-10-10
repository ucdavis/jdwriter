using System.Text.RegularExpressions;
using Server.Core.Domain;

namespace Server.Core.Titles;

/// <summary>
/// Which classes may supervise. Only a supervisory class may — a Supervisor or a Manager, as its
/// title says ("ACAD ACHIEVEMENT SUPV 2", "Financial Analyst Manager 1") — and never a
/// union-represented one. Every other position may lead, but not supervise.
///
/// Decided here, once, from the class: the build screen shows it and the server enforces it on
/// assembly, so no client can file a JD that breaks it.
/// </summary>
public static partial class Supervision
{
    /// <summary>
    /// Supervisor and manager titles, as payroll abbreviates them (SUPV, MGR) and as the standards
    /// spell them. "DIR" is not here: on payroll titles it is a producer-director, not a supervisor.
    /// </summary>
    [GeneratedRegex(@"\b(SUPV|SUPERVISOR|MGR|MANAGER)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SupervisoryTitle();

    public static bool IsSupervisory(string title) => SupervisoryTitle().IsMatch(title ?? "");

    /// <summary>Why the class's positions can't supervise, or null when they can.</summary>
    public static string? WhyNot(ClassProfile profile)
    {
        if (!IsSupervisory(profile.Title))
        {
            return "Only supervisor and manager classes can supervise. This position may lead.";
        }

        return BargainingUnits.IsRepresented(profile)
            ? $"This is a union-represented class ({BargainingUnits.For(profile)}), so it can't supervise. It may lead."
            : null;
    }

    /// <summary>Why a build's supervision can't stand, or null when it can.</summary>
    public static string? Problem(ClassProfile profile, bool? supervises, int? count)
    {
        if (supervises != true)
        {
            return null;
        }

        var whyNot = WhyNot(profile);
        if (whyNot is not null)
        {
            return $"{profile.Title}: {whyNot}";
        }

        return count is >= 1 and <= 10_000 ? null : "Enter how many people this position supervises.";
    }
}

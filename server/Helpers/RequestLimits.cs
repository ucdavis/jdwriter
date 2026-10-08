using Server.Controllers;
using Server.Core.Intake;

namespace Server.Helpers;

/// <summary>
/// Bounds on what a signed-in author can push into one request. Every author can reach the
/// model-backed endpoints, and without bounds a single request could carry a context window's worth
/// of text — a cost and storage problem rather than a feature. The limits sit well above real use:
/// the largest of 1,367 real HRTMS exports is about 140 KB.
/// </summary>
public static class RequestLimits
{
    /// <summary>Pasted or extracted description text; matches the upload extractor's cap.</summary>
    public const int DescriptionChars = 400_000;

    public const int IntakeRequestChars = 20_000;

    private const int TitleChars = 300;
    private const int NotesChars = 4_000;
    private const int ItemChars = 2_000;
    private const int ItemsPerList = 200;
    private const int Functions = 40;
    private const int DutiesPerFunction = 60;
    private const int TotalChars = 100_000;

    private const string TooLarge = "That is larger than a job description can be — shorten it and try again.";

    public static string? Description(string text) =>
        text.Length > DescriptionChars ? $"Descriptions are limited to {DescriptionChars:N0} characters." : null;

    public static string? IntakeRequest(string text) =>
        text.Length > IntakeRequestChars ? $"Keep the description under {IntakeRequestChars:N0} characters." : null;

    public static string? Build(BuildRequest b)
    {
        var lists = new[] { b.KeptCerts, b.KeptEducation, b.KeptWorkExperience, b.KeptMinKSA, b.KeptPrefKSA, b.KeptWorkEnvironment, b.AddedItems };
        if (b.WorkingTitle.Length > TitleChars || b.Department.Length > TitleChars || b.Notes.Length > NotesChars
            || b.KeptResponsibilities.Count > Functions
            || b.KeptResponsibilities.Any(r => r.Duties.Count > DutiesPerFunction || r.FunctionName.Length > TitleChars)
            || lists.Any(l => l.Count > ItemsPerList))
        {
            return TooLarge;
        }

        var texts = lists.SelectMany(l => l).Concat(b.KeptResponsibilities.SelectMany(r => r.Duties)).ToList();
        if (texts.Any(t => (t ?? "").Length > ItemChars))
        {
            return TooLarge;
        }

        return texts.Sum(t => (t ?? "").Length) + b.Notes.Length > TotalChars ? TooLarge : null;
    }

    public static string? Distilled(DistilledJd? d)
    {
        if (d is null || d.Functions.Count == 0)
        {
            return "Classify a description first — there is nothing to start a JD from.";
        }

        if (d.Functions.Count > Functions || d.Functions.Any(f => f.Duties.Count > DutiesPerFunction))
        {
            return TooLarge;
        }

        var texts = d.Functions.SelectMany(f => f.Duties.Prepend(f.Name))
            .Concat(d.Education).Concat(d.Experience).Concat(d.Ksas)
            .Append(d.WorkingTitle).Append(d.Summary)
            .Select(t => t ?? "")
            .ToList();
        return texts.Any(t => t.Length > ItemChars * 2) || texts.Sum(t => t.Length) > TotalChars ? TooLarge : null;
    }
}

using System.Text.RegularExpressions;
using Server.Core.Domain;

namespace Server.Core.Ingest;

/// <summary>
/// Replaces the identifying fields of real JDs for the Azure <c>test</c> environment, which gets a
/// scrubbed copy of the corpus; <c>prod</c> alone holds the real one.
///
/// What identifies a real position: its UCPath position number, the position it reports to, its JD
/// number, and its export file name (HRTMS embeds the position number in it). Department fields are
/// replaced too, so a unit plus a working title cannot single a position out. Everything the
/// analysis reads — functions, duties, percentages, qualifications, class — is kept, so the test
/// environment behaves like production.
///
/// Mappings are deterministic and shared across a run: a position maps to the same synthetic
/// number wherever it appears, so "reports to" still points at the right scrubbed JD, and a file
/// name maps to the same synthetic name in the JD, the profile's source list and its coverage rows.
/// </summary>
public sealed partial class TestDataScrubber
{
    /// <summary>A standalone eight-digit number: the shape of a UCPath position number.</summary>
    [GeneratedRegex(@"(?<!\d)\d{8}(?!\d)")]
    private static partial Regex PositionShaped();

    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _positions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _jdNumbers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _departments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _departmentCodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _divisions = new(StringComparer.Ordinal);

    /// <summary>
    /// "LAB AST 1/LAB AST 1 (40012345).HTML" → "LAB AST 1/JD-0001.HTML". The folder is the class
    /// title, which identifies no one, and is kept so the corpus stays browsable by class.
    /// </summary>
    public string SourceFile(string real)
    {
        if (_files.TryGetValue(real, out var known))
        {
            return known;
        }

        var slash = real.LastIndexOf('/');
        var folder = slash >= 0 ? real[..(slash + 1)] : "";
        var synthetic = $"{folder}JD-{_files.Count + 1:D4}.HTML";
        _files[real] = synthetic;
        return synthetic;
    }

    /// <summary>A UCPath position number → "T" + seven digits. Blank stays blank.</summary>
    public string Position(string real) =>
        Map(_positions, real, n => $"T{n:D7}");

    public void Apply(JobDescription jd)
    {
        jd.SourceFile = SourceFile(jd.SourceFile);
        jd.UcPathPositionNumber = Position(jd.UcPathPositionNumber);
        jd.ReportsToPositionNumber = Position(jd.ReportsToPositionNumber);
        jd.JdNumber = Map(_jdNumbers, jd.JdNumber, n => $"TEST-{n:D5}");
        jd.DepartmentName = Map(_departments, jd.DepartmentName, n => $"Department {n}");
        jd.DepartmentCode = Map(_departmentCodes, jd.DepartmentCode, n => $"D{n:D5}");
        jd.Division = Map(_divisions, jd.Division, n => $"Division {n}");

        // Position numbers also turn up in prose: measured over the real corpus, in nine working
        // titles and in 49 places in the parsed Education text. Field-level replacement cannot see
        // those, so every text field is swept for position-shaped numbers, mapped through the same
        // table — a number in a summary becomes the same synthetic value as the field it names.
        jd.WorkingTitle = ScrubText(jd.WorkingTitle);
        jd.UcJobTitle = ScrubText(jd.UcJobTitle);
        jd.JobSummary = ScrubText(jd.JobSummary);
        foreach (var r in jd.Responsibilities)
        {
            r.FunctionName = ScrubText(r.FunctionName);
            foreach (var d in r.Duties)
            {
                d.Text = ScrubText(d.Text);
            }
        }

        foreach (var q in jd.Qualifications)
        {
            q.Text = ScrubText(q.Text);
        }
    }

    /// <summary>Every position-shaped number in a piece of text, replaced through the position map.</summary>
    public string ScrubText(string text) =>
        string.IsNullOrEmpty(text) ? text : PositionShaped().Replace(text, m => Position(m.Value));

    private static string Map(Dictionary<string, string> map, string real, Func<int, string> make)
    {
        if (string.IsNullOrWhiteSpace(real))
        {
            return real;
        }

        if (!map.TryGetValue(real, out var synthetic))
        {
            synthetic = make(map.Count + 1);
            map[real] = synthetic;
        }

        return synthetic;
    }
}

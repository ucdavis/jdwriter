using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Server.Core.Domain;

namespace Server.Core.Package;

/// <summary>
/// Scrubs a package for the <c>test</c> environment, which never holds real JDs: what identifies a
/// position — its UCPath number, the position it reports to, its JD number, its export file name —
/// and the department fields are replaced, position-shaped numbers in any text are swept, and the
/// originals are left out. What the app reads — functions, duties, percentages, qualifications,
/// classes, envelopes — is kept, so test behaves like production. The same fields as the CLI's
/// <see cref="Ingest.TestDataScrubber"/>.
///
/// Unlike the CLI, a package is exported again and again, and the test environment merges each one
/// by name. So a stand-in is derived from the real value with a secret key the exporting environment
/// keeps: the same position or file gets the same stand-in in every export, and without the key a
/// stand-in can't be traced back by trying every position number.
/// </summary>
public sealed partial class PackageScrubber(byte[] key)
{
    /// <summary>A standalone eight-digit number: the shape of a UCPath position number.</summary>
    [GeneratedRegex(@"(?<!\d)\d{8}(?!\d)")]
    private static partial Regex PositionShaped();

    /// <summary>
    /// The keyed hash of a value, in the letters a–p rather than hex: a stand-in with digits could
    /// hold a run of eight, which reads as a position number to anyone — or anything — checking.
    /// </summary>
    private string Mac(string kind, string value) =>
        string.Concat(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{kind}\n{value}"))
            .SelectMany(b => new[] { (char)('a' + (b >> 4)), (char)('a' + (b & 0xF)) }));

    private string Map(string kind, string real, Func<string, string> make) =>
        string.IsNullOrWhiteSpace(real) ? real : make(Mac(kind, real));

    /// <summary>A position number → "T" and ten letters: never eight digits, so a second sweep leaves it alone.</summary>
    public string Position(string real) => Map("position", real, h => $"T{h[..10]}");

    /// <summary>
    /// A JD's name, everywhere it's recorded. An HRTMS export's name embeds the position number:
    /// "LAB AST 1/LAB AST 1 (40012345).HTML" → "LAB AST 1/JD-abcdefghijkl.HTML", keeping the class
    /// folder. A name the app gave ("import/1a2b…", a hash) identifies no one but can hold a run of
    /// eight digits, so it gets a letter stand-in too, keeping its kind: "import/abcdefghijklmnop".
    /// Stable from export to export, as the test environment merges by these names.
    /// </summary>
    public string SourceFile(string real)
    {
        foreach (var kind in new[] { "import/", "classify/", "authored/" })
        {
            if (real.StartsWith(kind, StringComparison.Ordinal))
            {
                return $"{kind}{Mac("file", real)[..16]}";
            }
        }

        var slash = real.LastIndexOf('/');
        var folder = slash >= 0 ? real[..(slash + 1)] : "";
        return $"{folder}JD-{Mac("file", real)[..12]}.HTML";
    }

    public string Text(string text) =>
        string.IsNullOrEmpty(text) ? text : PositionShaped().Replace(text, m => Position(m.Value));

    public void Apply(EnvironmentPackage package)
    {
        package.Scrubbed = true;
        // The originals are the real documents, names and all.
        package.Originals = [];

        foreach (var jd in package.JobDescriptions)
        {
            jd.SourceFile = SourceFile(jd.SourceFile);
            jd.UcPathPositionNumber = Position(jd.UcPathPositionNumber);
            jd.ReportsToPositionNumber = Position(jd.ReportsToPositionNumber);
            jd.JdNumber = Map("jd", jd.JdNumber, h => $"TEST-{h[..8]}");
            jd.DepartmentName = Map("department", jd.DepartmentName, h => $"Department {h[..6]}");
            jd.DepartmentCode = Map("department-code", jd.DepartmentCode, h => $"D{h[..6]}");
            jd.Division = Map("division", jd.Division, h => $"Division {h[..6]}");
            jd.WorkingTitle = Text(jd.WorkingTitle);
            jd.UcJobTitle = Text(jd.UcJobTitle);
            jd.JobSummary = Text(jd.JobSummary);
            foreach (var r in jd.Responsibilities)
            {
                r.FunctionName = Text(r.FunctionName);
                foreach (var d in r.Duties)
                {
                    d.Text = Text(d.Text);
                }
            }

            foreach (var q in jd.Qualifications)
            {
                q.Text = Text(q.Text);
            }

            foreach (var p in jd.PemEntries)
            {
                SweepStrings(p);
            }
        }

        foreach (var p in package.Classes)
        {
            foreach (var f in p.SourceFiles)
            {
                f.SourceFile = SourceFile(f.SourceFile);
            }

            foreach (var c in p.Coverage?.PerJd ?? [])
            {
                c.SourceFile = SourceFile(c.SourceFile);
            }

            // Sample duties and consolidated members are JD text: swept like it.
            SweepGraph(p);
        }
    }

    /// <summary>Every string in a class's graph, swept for position-shaped numbers.</summary>
    private void SweepGraph(object entity)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>([entity]);
        while (pending.TryPop(out var e))
        {
            if (!seen.Add(e))
            {
                continue;
            }

            SweepStrings(e);
            foreach (var prop in e.GetType().GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
            {
                var value = prop.GetValue(e);
                if (value is System.Collections.IEnumerable items and not string)
                {
                    foreach (var item in items)
                    {
                        if (item is not null && EnvironmentPackage.Types.Contains(item.GetType()))
                        {
                            pending.Push(item);
                        }
                    }
                }
                else if (value is not null && EnvironmentPackage.Types.Contains(value.GetType()))
                {
                    pending.Push(value);
                }
            }
        }
    }

    /// <summary>
    /// Every string but a JD name, which <see cref="SourceFile"/> has already mapped: swept again, a
    /// name could change in one place and not another, and a class would lose its JD.
    /// </summary>
    private void SweepStrings(object entity)
    {
        foreach (var prop in entity.GetType().GetProperties()
                     .Where(p => p.PropertyType == typeof(string) && p.CanRead && p.CanWrite && p.Name != "SourceFile"))
        {
            if (prop.GetValue(entity) is string s)
            {
                prop.SetValue(entity, Text(s));
            }
        }
    }

    /// <summary>A new random key, as stored.</summary>
    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}

/// <summary>What this environment will import: real JDs belong in prod only, and scrubbed data never mixes into prod.</summary>
public sealed class PackageOptions
{
    public bool AcceptsRealData { get; init; }
    public bool AcceptsScrubbed { get; init; }

    /// <summary>
    /// Development takes both. Elsewhere, <c>Package:AcceptsRealData</c> (default false) decides: prod
    /// sets it and takes only real packages; test leaves it unset and takes only scrubbed ones. A
    /// missing setting fails safe — no real JDs land where they shouldn't.
    /// </summary>
    public static PackageOptions For(bool isDevelopment, bool? acceptsRealData) =>
        isDevelopment
            ? new PackageOptions { AcceptsRealData = true, AcceptsScrubbed = true }
            : new PackageOptions { AcceptsRealData = acceptsRealData == true, AcceptsScrubbed = acceptsRealData != true };
}

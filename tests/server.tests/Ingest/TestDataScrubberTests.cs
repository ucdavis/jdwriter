using System.Reflection;
using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Ingest;

namespace Server.Tests.Ingest;

/// <summary>
/// The scrubbed corpus for the Azure test environment: nothing that identifies a real position
/// survives, and every link between scrubbed records still points at the right one.
/// </summary>
public class TestDataScrubberTests
{
    private static JobDescription Jd(string file, string position, string reportsTo) => new()
    {
        SourceFile = file,
        UcPathPositionNumber = position,
        ReportsToPositionNumber = reportsTo,
        JdNumber = $"JD{position}",
        DepartmentName = "Plant Sciences",
        DepartmentCode = "030100",
        Division = "CA&ES",
        WorkingTitle = "Greenhouse Technician",
        UcJobCode = "009605",
        JobSummary = "Kept as is.",
    };

    [Fact]
    public void Identifying_fields_are_replaced_and_the_analysis_fields_are_not()
    {
        var scrub = new TestDataScrubber();
        var jd = Jd("LAB AST 1/LAB AST 1 (40012345).HTML", "40012345", "40099999");

        scrub.Apply(jd);

        jd.SourceFile.Should().Be("LAB AST 1/JD-0001.HTML");
        jd.SourceFile.Should().NotContain("40012345");
        jd.UcPathPositionNumber.Should().Be("T0000001");
        jd.ReportsToPositionNumber.Should().Be("T0000002");
        jd.JdNumber.Should().NotContain("40012345");
        jd.DepartmentName.Should().Be("Department 1");
        jd.DepartmentCode.Should().NotBe("030100");
        jd.Division.Should().Be("Division 1");

        jd.WorkingTitle.Should().Be("Greenhouse Technician");
        jd.UcJobCode.Should().Be("009605");
        jd.JobSummary.Should().Be("Kept as is.");
    }

    [Fact]
    public void A_position_maps_the_same_way_wherever_it_appears()
    {
        // B reports to A. After scrubbing, B must still report to A's scrubbed number.
        var scrub = new TestDataScrubber();
        var a = Jd("X/A (40000001).HTML", "40000001", "40000999");
        var b = Jd("X/B (40000002).HTML", "40000002", "40000001");

        scrub.Apply(a);
        scrub.Apply(b);

        b.ReportsToPositionNumber.Should().Be(a.UcPathPositionNumber);
        a.DepartmentName.Should().Be(b.DepartmentName, "the same department stays one department");
    }

    [Fact]
    public void A_file_name_maps_the_same_way_for_jds_profiles_and_coverage()
    {
        var scrub = new TestDataScrubber();
        var jd = Jd("X/A (40000001).HTML", "40000001", "");

        scrub.Apply(jd);

        scrub.SourceFile("X/A (40000001).HTML").Should().Be(jd.SourceFile);
    }

    [Fact]
    public void A_position_number_in_prose_is_replaced_with_the_same_synthetic_value()
    {
        var scrub = new TestDataScrubber();
        var jd = Jd("X/A (40000001).HTML", "40000001", "");
        jd.WorkingTitle = "Lab Assistant 40000001";
        jd.Qualifications.Add(new JdQualificationItem { Kind = JdQualificationKind.Education, Text = "See position 40000777 for details" });

        scrub.Apply(jd);

        jd.WorkingTitle.Should().Be($"Lab Assistant {jd.UcPathPositionNumber}");
        jd.Qualifications[0].Text.Should().NotContain("40000777").And.Contain(scrub.Position("40000777"));
    }

    [Fact]
    public void Numbers_that_are_not_position_shaped_are_left_alone()
    {
        var scrub = new TestDataScrubber();
        var jd = Jd("X/A.HTML", "", "");
        jd.JobSummary = "Supports 1200 researchers across 3 campuses; budget 1234567890.";

        scrub.Apply(jd);

        jd.JobSummary.Should().Be("Supports 1200 researchers across 3 campuses; budget 1234567890.");
    }

    [Fact]
    public void Blank_fields_stay_blank()
    {
        var scrub = new TestDataScrubber();
        var jd = Jd("X/A.HTML", "", "");

        scrub.Apply(jd);

        jd.UcPathPositionNumber.Should().BeEmpty();
        jd.ReportsToPositionNumber.Should().BeEmpty();
    }

    /// <summary>
    /// Over the whole real corpus: after scrubbing, no original position number survives in ANY
    /// text field — including free text such as summaries and duties, which field-level scrubbing
    /// does not touch. If an export mentioned a position number in prose, this is where it shows.
    /// </summary>
    [Trait("Category", "CorpusParity")]
    [Fact]
    public void No_real_position_number_survives_anywhere_in_the_scrubbed_corpus()
    {
        var root = Environment.GetEnvironmentVariable("JDW_CORPUS_DIR");
        if (string.IsNullOrWhiteSpace(root))
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "..", "JDWriter", "Sample JDs")))
            {
                dir = dir.Parent;
            }

            root = dir is null
                ? throw new DirectoryNotFoundException(
                    "HRTMS corpus not found. Set JDW_CORPUS_DIR, or exclude: --filter \"Category!=CorpusParity\".")
                : Path.GetFullPath(Path.Combine(dir.FullName, "..", "JDWriter", "Sample JDs"));
        }

        var scrub = new TestDataScrubber();
        var originals = new HashSet<string>(StringComparer.Ordinal);
        var scrubbed = new List<JobDescription>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(p => p.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
        {
            var rel = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            var jd = HrtmsParser.Parse(File.ReadAllText(path), rel).ToEntity("000000");
            // Numeric values only: in 172 exports the parser picks up a neighbouring cell's TEXT
            // for these fields when they are blank, and words like that are not identifiers. The
            // scrubber replaces those field values wholesale anyway.
            foreach (var n in new[] { jd.UcPathPositionNumber, jd.ReportsToPositionNumber })
            {
                if (n.Length >= 6 && n.All(char.IsDigit))
                {
                    originals.Add(n);
                }
            }

            scrub.Apply(jd);
            scrubbed.Add(jd);
        }

        scrubbed.Should().HaveCountGreaterThan(1000, "the whole corpus must have been read");
        originals.Should().NotBeEmpty();

        static IEnumerable<string> Texts(object o) =>
            o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
                .Select(p => (string?)p.GetValue(o) ?? "");

        var leaks = 0;
        foreach (var jd in scrubbed)
        {
            var text = string.Join("\n", Texts(jd)
                .Concat(jd.Responsibilities.SelectMany(r => Texts(r).Concat(r.Duties.SelectMany(Texts))))
                .Concat(jd.Qualifications.SelectMany(Texts)));
            // Counted, never printed: a failure message must not itself publish position numbers.
            leaks += originals.Count(n => text.Contains(n, StringComparison.Ordinal));
        }

        leaks.Should().Be(0, "no real position number may survive scrubbing");
    }
}

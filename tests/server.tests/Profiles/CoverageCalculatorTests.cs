using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Profiles;

namespace Server.Tests.Profiles;

/// <summary>
/// Backwards coverage is pure arithmetic and drives the review-and-nudge work queue, so the numbers
/// are checked against hand-computed expectations.
/// </summary>
public class CoverageCalculatorTests
{
    private static HrtmsRecord Jd(string file, params (string Name, int? Pct)[] fns) => new()
    {
        SourceFile = file,
        Responsibilities = [.. fns.Select(f => new HrtmsResponsibility { FunctionName = f.Name, Pct = f.Pct })],
    };

    private static HashSet<string> Covered(params string[] names) =>
        new(names.Select(n => n.ToLowerInvariant()), StringComparer.Ordinal);

    [Fact]
    public void Coverage_is_a_share_of_stated_time_not_of_function_count()
    {
        // One covered function worth 80% beats three uncovered ones worth 20% between them. Counting
        // functions instead of time would call this JD a poor fit when it is a good one.
        var report = CoverageCalculator.FromCoveredSet(
            [Jd("a.HTML", ("Field Work", 80), ("Odd Job", 10), ("Other", 10))],
            Covered("field work"));

        report.PerJd.Should().ContainSingle();
        report.PerJd[0].CoveredPct.Should().Be(80);
        report.PerJd[0].Uncovered.Should().HaveCount(2);
    }

    [Fact]
    public void Matching_ignores_case_and_spacing()
    {
        var report = CoverageCalculator.FromCoveredSet(
            [Jd("a", ("  CULTURAL   OPERATIONS ", 100))],
            Covered("cultural operations"));

        report.PerJd[0].CoveredPct.Should().Be(100);
    }

    [Fact]
    public void A_jd_with_no_stated_percentages_counts_as_fully_covered()
    {
        // Absence of data is not evidence of a mismatch. Scoring it zero would flood the misfit
        // queue with JDs that simply never filled in the time column.
        var report = CoverageCalculator.FromCoveredSet(
            [Jd("a", ("Unquantified", null))], Covered());

        report.PerJd[0].CoveredPct.Should().Be(100);
        report.PerJd[0].Uncovered.Should().BeEmpty("a zero-percent function is not idiosyncratic time");
    }

    [Fact]
    public void Uncovered_functions_are_listed_worst_first()
    {
        var report = CoverageCalculator.FromCoveredSet(
            [Jd("a", ("Small Extra", 10), ("Big Extra", 30), ("Known", 60))],
            Covered("known"));

        report.PerJd[0].Uncovered.OrderBy(u => u.Ordinal).Select(u => u.Name)
            .Should().Equal("Big Extra", "Small Extra");
    }

    [Fact]
    public void The_report_is_ordered_worst_fit_first()
    {
        // This list is a work queue, so the JDs needing attention belong at the top.
        var report = CoverageCalculator.FromCoveredSet(
        [
            Jd("good.HTML", ("Known", 100)),
            Jd("bad.HTML", ("Strange", 100)),
            Jd("mixed.HTML", ("Known", 50), ("Strange", 50)),
        ], Covered("known"));

        report.PerJd.OrderBy(j => j.Ordinal).Select(j => j.SourceFile)
            .Should().Equal("bad.HTML", "mixed.HTML", "good.HTML");
        report.PerJd.Select(j => j.Ordinal).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void Mean_and_well_covered_share_are_computed_across_the_corpus()
    {
        var report = CoverageCalculator.FromCoveredSet(
        [
            Jd("a", ("Known", 100)),
            Jd("b", ("Known", 90), ("Strange", 10)),
            Jd("c", ("Strange", 100)),
        ], Covered("known"));

        report.N.Should().Be(3);
        report.MeanCoverage.Should().Be(63, "(100 + 90 + 0) / 3 = 63.3, rounded half-up");
        // 90% is the documented threshold for "matches the template".
        report.WellCoveredPct.Should().BeApproximately(2.0 / 3, 1e-9);
    }

    [Fact]
    public void An_empty_corpus_produces_an_empty_report_rather_than_dividing_by_zero()
    {
        var report = CoverageCalculator.FromCoveredSet([], Covered("known"));

        report.N.Should().Be(0);
        report.MeanCoverage.Should().Be(0);
        report.WellCoveredPct.Should().Be(0);
        report.PerJd.Should().BeEmpty();
    }

    [Fact]
    public void Profile_coverage_uses_consolidated_members_not_envelope_wording()
    {
        // This is the distinction between the two coverage checks. Stored coverage is computed from
        // the raw function names absorbed into consolidated groups, so rewording the envelope cannot
        // change it — that is what EnvelopeCoverageChecker is for.
        var profile = new ClassProfile { Slug = "s" };
        var group = new ConsolidatedFunction { Name = "Completely Different Wording" };
        group.Members.Add(new ConsolidatedFunctionMember { Ordinal = 0, Text = "Cultural Operations" });
        profile.ConsolidatedFunctions.Add(group);

        var report = CoverageCalculator.ComputeCoverage(profile, [Jd("a", ("CULTURAL OPERATIONS", 100))]);

        report.PerJd[0].CoveredPct.Should().Be(100,
            "coverage follows the MEMBERS, not the category name");
    }

    [Fact]
    public void A_profile_with_no_consolidation_covers_nothing()
    {
        var report = CoverageCalculator.ComputeCoverage(
            new ClassProfile { Slug = "s" }, [Jd("a", ("Anything", 100))]);

        report.PerJd[0].CoveredPct.Should().Be(0);
    }
}

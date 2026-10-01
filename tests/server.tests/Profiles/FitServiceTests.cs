using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Profiles;

namespace Server.Tests.Profiles;

/// <summary>
/// Fit is a browsing surface over work already done at ingest. These tests pin the two things that
/// make it useful: it reads stored coverage rather than recomputing (so opening the page is cheap),
/// and the queue is ordered worst-first.
/// </summary>
public class FitServiceTests
{
    private sealed class FakeRepo : IClassProfileRepository
    {
        public List<ClassProfile> Profiles { get; init; } = [];
        public int GetAllCalls { get; private set; }

        public Task<List<ClassProfile>> GetAllAsync(CancellationToken ct = default)
        {
            GetAllCalls++;
            return Task.FromResult(Profiles);
        }

        public Task<ClassProfile?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
            Task.FromResult(Profiles.FirstOrDefault(p => p.Slug == slug));

        public Task<ClassProfile?> GetByCodeAsync(string ucJobCode, CancellationToken ct = default) =>
            Task.FromResult(Profiles.FirstOrDefault(p => p.UcJobCode == ucJobCode));

        public Task<List<ClassDescriptor>> GetDescriptorsAsync(
            string? excludeSlug = null, CancellationToken ct = default) =>
            Task.FromResult(new List<ClassDescriptor>());

        public Task<List<ClassListItem>> GetClassListAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<ClassListItem>());

        public Task<List<ClassSummary>> GetSummariesAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<ClassSummary>());

        public Task<List<JobDescription>> GetJobDescriptionsAsync(
            string? ucJobCode = null, CancellationToken ct = default) =>
            Task.FromResult(new List<JobDescription>());
    }

    private static ClassProfile Profile(
        string slug, string title, string code, params (string File, double Pct, (string, double)[] Uncovered)[] jds)
    {
        var p = new ClassProfile { Slug = slug, Title = title, UcJobCode = code };

        if (jds.Length == 0)
        {
            return p;
        }

        var report = new CoverageReport { N = jds.Length };
        var i = 0;
        foreach (var (file, pct, uncovered) in jds)
        {
            var jd = new JdCoverage { Ordinal = i++, SourceFile = file, CoveredPct = pct };
            var j = 0;
            foreach (var (name, upct) in uncovered)
            {
                jd.Uncovered.Add(new JdCoverageUncovered { Ordinal = j++, Name = name, Pct = upct });
            }

            report.PerJd.Add(jd);
        }

        p.Coverage = report;
        return p;
    }

    [Fact]
    public async Task Only_jds_below_the_threshold_are_reported()
    {
        var repo = new FakeRepo
        {
            Profiles =
            [
                Profile("s1", "Farm Laborer", "008543",
                    ("good.HTML", 100, []),
                    ("bad.HTML", 40, [("Grant Administration", 60)])),
            ],
        };

        var summary = await new FitService(repo).GetMisfitsAsync();

        summary.Threshold.Should().Be(90);
        summary.TotalJds.Should().Be(2, "every JD is counted, not just the misfits");
        summary.Misfits.Should().ContainSingle()
            .Which.SourceFile.Should().Be("bad.HTML");
    }

    [Fact]
    public async Task Exactly_at_the_threshold_is_a_fit()
    {
        // 90% coverage is the documented target — under 10% idiosyncratic — so it must not be
        // flagged as a misfit.
        var repo = new FakeRepo { Profiles = [Profile("s", "T", "1", ("edge.HTML", 90, []))] };

        var summary = await new FitService(repo).GetMisfitsAsync();

        summary.Misfits.Should().BeEmpty();
    }

    [Fact]
    public async Task The_queue_is_ordered_worst_fit_first_across_all_classes()
    {
        var repo = new FakeRepo
        {
            Profiles =
            [
                Profile("s1", "Class One", "1", ("middling.HTML", 70, [])),
                Profile("s2", "Class Two", "2", ("worst.HTML", 15, []), ("nearly.HTML", 88, [])),
            ],
        };

        var summary = await new FitService(repo).GetMisfitsAsync();

        summary.Misfits.Select(m => m.SourceFile)
            .Should().Equal("worst.HTML", "middling.HTML", "nearly.HTML");
    }

    [Fact]
    public async Task The_idiosyncratic_work_travels_with_each_misfit()
    {
        // This list is what the review-and-nudge workflow proposes pulling back toward the standard,
        // so it has to come through in order.
        var repo = new FakeRepo
        {
            Profiles =
            [
                Profile("s", "Class", "1",
                    ("bad.HTML", 30, [("Grant Administration", 50), ("Event Planning", 20)])),
            ],
        };

        var summary = await new FitService(repo).GetMisfitsAsync();

        var misfit = summary.Misfits.Should().ContainSingle().Subject;
        misfit.ClassTitle.Should().Be("Class");
        misfit.UcJobCode.Should().Be("1");
        misfit.Idiosyncratic.Select(x => x.Name)
            .Should().Equal("Grant Administration", "Event Planning");
    }

    [Fact]
    public async Task A_profile_without_a_coverage_report_is_skipped_not_counted_as_perfect()
    {
        // No report means it never went through ingest's comparison step. Treating that as a perfect
        // fit would quietly inflate the corpus-wide picture.
        var repo = new FakeRepo
        {
            Profiles =
            [
                Profile("no-coverage", "Bootstrapped Class", "1"),
                Profile("s", "Real Class", "2", ("bad.HTML", 20, [])),
            ],
        };

        var summary = await new FitService(repo).GetMisfitsAsync();

        summary.TotalJds.Should().Be(1, "only the class with a report contributes");
        summary.Misfits.Should().ContainSingle();
    }

    [Fact]
    public async Task The_threshold_is_adjustable()
    {
        var repo = new FakeRepo { Profiles = [Profile("s", "T", "1", ("a.HTML", 60, []))] };

        (await new FitService(repo).GetMisfitsAsync(50)).Misfits.Should().BeEmpty();
        (await new FitService(repo).GetMisfitsAsync(70)).Misfits.Should().ContainSingle();
    }

    [Fact]
    public async Task Stored_coverage_is_read_once_and_never_recomputed()
    {
        var repo = new FakeRepo { Profiles = [Profile("s", "T", "1", ("a.HTML", 50, []))] };

        await new FitService(repo).GetMisfitsAsync();

        repo.GetAllCalls.Should().Be(1, "one pass over the profiles, no corpus scan");
    }
}

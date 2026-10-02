using FluentAssertions;
using Server.Core.Analytics;
using Server.Core.Domain;

namespace Server.Tests.Analytics;

/// <summary>Admin analytics are counts; these pin the windows and buckets on a fixed clock.</summary>
public class AdminAnalyticsTests
{
    // A Thursday, so the week boundary (Monday) is exercised.
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<AnalyticsReport> Report(Action<Server.Core.Data.AppDbContext> seed)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        seed(db);
        await db.SaveChangesAsync();
        return await new AdminAnalytics(db).BuildAsync(Now);
    }

    private static ClassProfile Class(string slug, double? well, int corpus = 10) => new()
    {
        Slug = slug, Title = slug.ToUpperInvariant(), UcJobCode = slug, CorpusSize = corpus,
        Coverage = well == null ? null : new CoverageReport { N = corpus, WellCoveredPct = well.Value, MeanCoverage = well.Value * 100 },
    };

    [Fact]
    public async Task Active_users_count_by_last_seen_within_7_and_30_days()
    {
        var r = await Report(db => db.AppUsers.AddRange(
            new AppUser { NameIdentifier = "a", LastSeenAt = Now.AddDays(-1) },
            new AppUser { NameIdentifier = "b", LastSeenAt = Now.AddDays(-20) },
            new AppUser { NameIdentifier = "c", LastSeenAt = Now.AddDays(-60) },
            new AppUser { NameIdentifier = "d" }));

        r.Usage.Users.Should().Be(4);
        r.Usage.Active7Days.Should().Be(1);
        r.Usage.Active30Days.Should().Be(2);
    }

    [Fact]
    public async Task Jds_per_week_are_twelve_monday_weeks_ending_this_week()
    {
        var r = await Report(db =>
        {
            var p = Class("x", 1);
            db.ClassProfiles.Add(p);
            db.AuthoredJds.AddRange(
                new AuthoredJd { ClassProfile = p, CreatedAt = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero), Status = AuthoredJdStatus.Ready }, // this Monday
                new AuthoredJd { ClassProfile = p, CreatedAt = new DateTimeOffset(2026, 9, 27, 23, 0, 0, TimeSpan.Zero), Status = AuthoredJdStatus.Draft }, // last Sunday
                new AuthoredJd { ClassProfile = p, CreatedAt = Now.AddDays(-200), Status = AuthoredJdStatus.Ready }); // outside the window
        });

        r.Usage.PerWeek.Should().HaveCount(12);
        r.Usage.PerWeek[^1].WeekOf.Should().Be(new DateOnly(2026, 9, 28));
        r.Usage.PerWeek[^1].Count.Should().Be(1);
        r.Usage.PerWeek[^2].Count.Should().Be(1);
        r.Usage.PerWeek.Sum(w => w.Count).Should().Be(2);
        (r.Usage.Ready, r.Usage.Draft, r.Usage.JdsSaved).Should().Be((2, 1, 3));
    }

    [Fact]
    public async Task Classes_split_into_most_authored_and_never_used()
    {
        var r = await Report(db =>
        {
            var busy = Class("busy", 1);
            var quiet = Class("quiet", 1);
            var unused = Class("unused", 1, corpus: 50);
            db.ClassProfiles.AddRange(busy, quiet, unused);
            db.AuthoredJds.AddRange(
                new AuthoredJd { ClassProfile = busy, CreatedAt = Now },
                new AuthoredJd { ClassProfile = busy, CreatedAt = Now },
                new AuthoredJd { ClassProfile = quiet, CreatedAt = Now });
        });

        r.Classes.MostAuthored.Select(c => (c.Slug, c.Count)).Should().Equal(("busy", 2), ("quiet", 1));
        r.Classes.NeverUsed.Select(c => c.Slug).Should().Equal("unused");
    }

    [Fact]
    public async Task Envelope_match_buckets_and_the_lowest_fits()
    {
        var r = await Report(db => db.ClassProfiles.AddRange(
            Class("great", 0.95), Class("good", 0.75), Class("fair", 0.55), Class("poor", 0.2), Class("none", null)));

        r.Classes.EnvelopeMatch.Select(b => b.Classes).Should().Equal(1, 1, 1, 1, 1);
        r.Classes.LowestMatch.First().Slug.Should().Be("poor");
        r.Classes.LowestMatch.Select(c => c.Slug).Should().NotContain("none", "a class with no coverage data has no fit to rank");
    }
}

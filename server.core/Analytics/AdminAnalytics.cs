using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Core.Analytics;

public sealed class AnalyticsReport
{
    public UsageStats Usage { get; set; } = new();
    public ClassStats Classes { get; set; } = new();
}

public sealed class UsageStats
{
    public int Users { get; set; }
    public int Active7Days { get; set; }
    public int Active30Days { get; set; }
    public int JdsSaved { get; set; }
    public int Ready { get; set; }
    public int Draft { get; set; }

    /// <summary>JDs created per week, oldest first, the last <see cref="AdminAnalytics.Weeks"/> weeks.</summary>
    public List<WeekCount> PerWeek { get; set; } = [];
}

public sealed class WeekCount
{
    /// <summary>The Monday the week starts on (UTC).</summary>
    public DateOnly WeekOf { get; set; }

    public int Count { get; set; }
}

public sealed class ClassStats
{
    public int TotalClasses { get; set; }

    /// <summary>The classes authors write against most, most first.</summary>
    public List<ClassCount> MostAuthored { get; set; } = [];

    /// <summary>Classes no one has written a JD against yet, largest corpus first.</summary>
    public List<ClassCount> NeverUsed { get; set; } = [];

    /// <summary>How well each class's envelope covers its real JDs, as share-of-JDs-≥90%-covered buckets.</summary>
    public List<BucketCount> EnvelopeMatch { get; set; } = [];

    /// <summary>The classes whose envelopes fit their JDs least — where curation pays off first.</summary>
    public List<ClassFit> LowestMatch { get; set; } = [];
}

public sealed class ClassCount
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public int Count { get; set; }
}

public sealed class BucketCount
{
    public string Label { get; set; } = "";
    public int Classes { get; set; }
}

public sealed class ClassFit
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public double WellCoveredPct { get; set; }
    public double MeanCoverage { get; set; }
    public int Jds { get; set; }
}

/// <summary>
/// Basic admin analytics: who uses the tool and how much, and how the classes are used and fit.
/// Everything is a count over stored rows, so the page is cheap to open.
/// </summary>
public sealed class AdminAnalytics
{
    public const int Weeks = 12;

    private readonly AppDbContext _db;

    public AdminAnalytics(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AnalyticsReport> BuildAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var users = await _db.AppUsers.AsNoTracking().Select(u => u.LastSeenAt).ToListAsync(ct);
        var jds = await _db.AuthoredJds.AsNoTracking()
            .Select(a => new { a.ClassProfileId, a.Status, a.CreatedAt })
            .ToListAsync(ct);
        var profiles = await _db.ClassProfiles.AsNoTracking()
            .Select(p => new
            {
                p.Id,
                p.Slug,
                p.Title,
                p.UcJobCode,
                p.CorpusSize,
                Well = p.Coverage != null ? p.Coverage.WellCoveredPct : (double?)null,
                Mean = p.Coverage != null ? p.Coverage.MeanCoverage : (double?)null,
                N = p.Coverage != null ? p.Coverage.N : 0,
            })
            .ToListAsync(ct);

        // Weeks start on Monday, in UTC, so a week means the same thing to every viewer.
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var perWeek = Enumerable.Range(0, Weeks)
            .Select(i => thisMonday.AddDays(-7 * (Weeks - 1 - i)))
            .Select(monday => new WeekCount
            {
                WeekOf = monday,
                Count = jds.Count(j =>
                {
                    var d = DateOnly.FromDateTime(j.CreatedAt.UtcDateTime);
                    return d >= monday && d < monday.AddDays(7);
                }),
            })
            .ToList();

        var authoredByClass = jds.GroupBy(j => j.ClassProfileId).ToDictionary(g => g.Key, g => g.Count());

        static string Bucket(double? well) => well switch
        {
            null => "No coverage data",
            >= 0.9 => "90–100% of JDs well covered",
            >= 0.7 => "70–89%",
            >= 0.5 => "50–69%",
            _ => "Under 50%",
        };
        string[] order = ["90–100% of JDs well covered", "70–89%", "50–69%", "Under 50%", "No coverage data"];

        return new AnalyticsReport
        {
            Usage = new UsageStats
            {
                Users = users.Count,
                Active7Days = users.Count(s => s >= now.AddDays(-7)),
                Active30Days = users.Count(s => s >= now.AddDays(-30)),
                JdsSaved = jds.Count,
                Ready = jds.Count(j => j.Status == AuthoredJdStatus.Ready),
                Draft = jds.Count(j => j.Status == AuthoredJdStatus.Draft),
                PerWeek = perWeek,
            },
            Classes = new ClassStats
            {
                TotalClasses = profiles.Count,
                MostAuthored =
                [
                    .. profiles
                        .Where(p => authoredByClass.ContainsKey(p.Id))
                        .Select(p => new ClassCount { Slug = p.Slug, Title = p.Title, UcJobCode = p.UcJobCode, Count = authoredByClass[p.Id] })
                        .OrderByDescending(c => c.Count).ThenBy(c => c.Title, StringComparer.Ordinal)
                        .Take(10),
                ],
                NeverUsed =
                [
                    .. profiles
                        .Where(p => !authoredByClass.ContainsKey(p.Id))
                        .OrderByDescending(p => p.CorpusSize).ThenBy(p => p.Title, StringComparer.Ordinal)
                        .Select(p => new ClassCount { Slug = p.Slug, Title = p.Title, UcJobCode = p.UcJobCode, Count = p.CorpusSize }),
                ],
                EnvelopeMatch =
                [
                    .. order.Select(label => new BucketCount
                    {
                        Label = label,
                        Classes = profiles.Count(p => Bucket(p.Well) == label),
                    }),
                ],
                LowestMatch =
                [
                    .. profiles
                        .Where(p => p.Well != null && p.N > 0)
                        .OrderBy(p => p.Well).ThenBy(p => p.Mean)
                        .Take(10)
                        .Select(p => new ClassFit
                        {
                            Slug = p.Slug,
                            Title = p.Title,
                            WellCoveredPct = p.Well!.Value,
                            MeanCoverage = p.Mean ?? 0,
                            Jds = p.N,
                        }),
                ],
            },
        };
    }
}

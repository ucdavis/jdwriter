using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Titles;

namespace Server.Core.Ingest;

/// <summary>One job description in the corpus, as the list of them shows it.</summary>
public sealed class CorpusJdRow
{
    public int Id { get; set; }
    public string SourceFile { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public string UcJobTitle { get; set; } = "";
    public string WorkingTitle { get; set; } = "";
    public string DepartmentName { get; set; } = "";

    /// <summary>"export", "authored", "classify" or "imported" (from import: a Word copy of a JD, parsed).</summary>
    public string Origin { get; set; } = "";

    public DateTimeOffset? AddedAt { get; set; }
    public int Functions { get; set; }
    public int Duties { get; set; }
    public int PctTotal { get; set; }

    /// <summary>The class it's filed under, when that class has been built.</summary>
    public string? ClassSlug { get; set; }

    public string? ClassTitle { get; set; }

    /// <summary>Added since its class was last built, so not yet in the envelope.</summary>
    public bool Pending { get; set; }
}

/// <summary>One job description, whole, with the class it's filed under.</summary>
public sealed class CorpusJdDetail
{
    public int Id { get; set; }
    public string Origin { get; set; } = "";
    public DateTimeOffset? AddedAt { get; set; }
    public string? ClassSlug { get; set; }
    public string? ClassTitle { get; set; }
    public bool Pending { get; set; }
    public HrtmsRecord Record { get; set; } = new();
}

/// <summary>Every job description in the corpus, whatever brought it in — and any one of them, whole.</summary>
public sealed class CorpusDirectory
{
    private readonly AppDbContext _db;

    public CorpusDirectory(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<CorpusJdRow>> ListAsync(CancellationToken ct = default)
    {
        var classes = await ClassesByCodeAsync(ct);
        var rows = await _db.JobDescriptions.AsNoTracking()
            .Select(j => new
            {
                j.Id, j.SourceFile, j.UcJobCode, j.UcJobTitle, j.WorkingTitle, j.DepartmentName, j.Origin, j.AddedAt,
                Functions = j.Responsibilities.Count,
                Duties = j.Responsibilities.SelectMany(r => r.Duties).Count(),
                PctTotal = j.Responsibilities.Sum(r => r.Pct ?? 0),
            })
            .ToListAsync(ct);

        return rows
            .Select(j =>
            {
                var cls = classes.GetValueOrDefault(TitleCodeIndex.Pad(j.UcJobCode));
                return new CorpusJdRow
                {
                    Id = j.Id,
                    SourceFile = j.SourceFile,
                    UcJobCode = j.UcJobCode,
                    UcJobTitle = j.UcJobTitle,
                    WorkingTitle = j.WorkingTitle,
                    DepartmentName = j.DepartmentName,
                    Origin = OriginName(j.Origin),
                    AddedAt = j.AddedAt,
                    Functions = j.Functions,
                    Duties = j.Duties,
                    PctTotal = j.PctTotal,
                    ClassSlug = cls?.Slug,
                    ClassTitle = cls?.Title,
                    Pending = IsPending(j.Origin, j.AddedAt, cls),
                };
            })
            .OrderByDescending(r => r.AddedAt ?? DateTimeOffset.MinValue)
            .ThenBy(r => r.UcJobTitle, StringComparer.Ordinal)
            .ThenBy(r => r.Id)
            .ToList();
    }

    public async Task<CorpusJdDetail?> GetAsync(int id, CancellationToken ct = default)
    {
        var jd = await _db.JobDescriptions.AsNoTracking()
            .Where(j => j.Id == id)
            .Include(j => j.Responsibilities).ThenInclude(r => r.Duties)
            .Include(j => j.Qualifications)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct);
        if (jd is null)
        {
            return null;
        }

        var cls = (await ClassesByCodeAsync(ct)).GetValueOrDefault(TitleCodeIndex.Pad(jd.UcJobCode));
        return new CorpusJdDetail
        {
            Id = jd.Id,
            Origin = OriginName(jd.Origin),
            AddedAt = jd.AddedAt,
            ClassSlug = cls?.Slug,
            ClassTitle = cls?.Title,
            Pending = IsPending(jd.Origin, jd.AddedAt, cls),
            Record = jd.ToHrtmsRecord(),
        };
    }

    private sealed record ClassRow(string Slug, string Title, DateTimeOffset? LastIngestedAt);

    private async Task<Dictionary<string, ClassRow>> ClassesByCodeAsync(CancellationToken ct) =>
        (await _db.ClassProfiles.AsNoTracking()
            .Select(p => new { p.UcJobCode, p.Slug, p.Title, p.LastIngestedAt })
            .ToListAsync(ct))
        .GroupBy(p => TitleCodeIndex.Pad(p.UcJobCode))
        .ToDictionary(g => g.Key, g => new ClassRow(g.First().Slug, g.First().Title, g.First().LastIngestedAt), StringComparer.Ordinal);

    /// <summary>
    /// The test the rebuild queue uses (<see cref="CorpusUploads.PendingAsync"/>): added in the app
    /// since its class was last built, or with no class yet. An export is filed by the rebuild itself.
    /// </summary>
    private static bool IsPending(CorpusOrigin origin, DateTimeOffset? addedAt, ClassRow? cls) =>
        origin != CorpusOrigin.Export && addedAt != null && (cls?.LastIngestedAt == null || addedAt > cls.LastIngestedAt);

    private static string OriginName(CorpusOrigin origin) => origin.ToString().ToLowerInvariant();
}

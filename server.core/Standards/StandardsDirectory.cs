using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Profiles;
using Server.Core.Titles;

namespace Server.Core.Standards;

/// <summary>One job standard, and the job and class it is attached to.</summary>
public sealed class StandardRow
{
    public string Title { get; set; } = "";
    public string Grade { get; set; } = "";
    public string Flsa { get; set; } = "";
    public string Union { get; set; } = "";
    public int KeyResponsibilities { get; set; }

    /// <summary>No key responsibilities: matched, but can't be bootstrapped from.</summary>
    public bool Blank { get; set; }

    /// <summary>The UC job code it is attached to, when one is.</summary>
    public string? Code { get; set; }

    public string? JobTitle { get; set; }

    /// <summary>"title" — its title matches the job's; "hand" — an admin matched or entered it.</summary>
    public string? How { get; set; }

    public bool OnPayroll { get; set; }
    public string? ClassSlug { get; set; }
    public string? ClassTitle { get; set; }
}

/// <summary>Every job standard JDWriter has, and what each is attached to.</summary>
public sealed class StandardsDirectory
{
    private readonly AppDbContext _db;
    private readonly IStandardsStore _standards;
    private readonly ITitleCodeService _titleCodes;

    public StandardsDirectory(AppDbContext db, IStandardsStore standards, ITitleCodeService titleCodes)
    {
        _db = db;
        _standards = standards;
        _titleCodes = titleCodes;
    }

    public async Task<List<StandardRow>> ListAsync(CancellationToken ct = default)
    {
        var index = await _titleCodes.GetAsync(ct);
        var standards = await _standards.GetIndexAsync(ct);
        var classes = (await _db.ClassProfiles.AsNoTracking()
                .Select(p => new { p.UcJobCode, p.Slug, p.Title })
                .ToListAsync(ct))
            .GroupBy(p => TitleCodeIndex.Pad(p.UcJobCode))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var byHand = (await _db.StandardMatchDecisions.AsNoTracking().Where(d => d.IsMatch)
                .Select(d => d.StandardTitle).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        return standards.All
            .Select(s =>
            {
                var tc = StandardCodes.For(s, index);
                var cls = tc is null ? null : classes.GetValueOrDefault(TitleCodeIndex.Pad(tc.Code));
                return new StandardRow
                {
                    Title = s.LongTitle,
                    Grade = s.Grade,
                    Flsa = s.Flsa,
                    Union = s.Union,
                    KeyResponsibilities = s.KeyResponsibilities.Count,
                    Blank = s.IsBlank,
                    Code = tc?.Code,
                    JobTitle = tc is null ? null : ProfileAggregator.Titleize(tc.Title),
                    How = tc is null ? null : byHand.Contains(s.LongTitle) ? "hand" : "title",
                    OnPayroll = tc?.IsInUse ?? false,
                    ClassSlug = cls?.Slug,
                    ClassTitle = cls?.Title,
                };
            })
            .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>One standard, whole, by its title.</summary>
    public async Task<ClassStandardRecord?> GetAsync(string title, CancellationToken ct = default) =>
        (await _standards.GetIndexAsync(ct)).All.FirstOrDefault(s => s.LongTitle == title);
}

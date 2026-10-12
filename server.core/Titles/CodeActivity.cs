using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Core.Titles;

/// <summary>What setting a code active or not changed.</summary>
public sealed class CodeActivityResult
{
    public string Code { get; set; } = "";
    public bool Active { get; set; }

    /// <summary>The payroll reference's answer, which an override departs from.</summary>
    public bool OnPayroll { get; set; }

    /// <summary>
    /// Union supersessions that began or ended. They are derived from which codes are active, so
    /// marking a suffixed code active can retire its base code, and marking it inactive can revive it.
    /// </summary>
    public List<string> SupersessionChanges { get; set; } = [];
}

/// <summary>
/// An admin's call on whether a job code is active at UC Davis, over what the payroll reference says.
/// Everything that asks — the Codes page, bootstrapping, imports, standard matching — reads it through
/// the title index.
/// </summary>
public sealed class CodeActivity
{
    private readonly AppDbContext _db;
    private readonly ITitleCodeService _titleCodes;

    public CodeActivity(AppDbContext db, ITitleCodeService titleCodes)
    {
        _db = db;
        _titleCodes = titleCodes;
    }

    public async Task<CodeActivityResult> SetAsync(string code, bool active, int? userId, CancellationToken ct = default)
    {
        var before = await _titleCodes.GetAsync(ct);
        var padded = TitleCodeIndex.Pad(code);
        var rows = before.All.Where(t => TitleCodeIndex.Pad(t.Code) == padded).ToList();
        if (rows.Count == 0)
        {
            throw new ArgumentException($"No UC job code {code}.");
        }

        var onPayroll = rows.Any(t => t.OnPayroll);
        var existing = (await _db.CodeActiveOverrides.ToListAsync(ct))
            .Where(o => TitleCodeIndex.Pad(o.UcJobCode) == padded)
            .ToList();
        _db.CodeActiveOverrides.RemoveRange(existing);
        // An override that agrees with the reference is no override: dropped, so a later
        // reference import that changes the code's status is followed.
        if (active != onPayroll)
        {
            _db.CodeActiveOverrides.Add(new CodeActiveOverride
            {
                UcJobCode = rows[0].Code,
                IsActive = active,
                DecidedByUserId = userId,
                DecidedAt = DateTimeOffset.UtcNow,
            });
        }

        await _db.SaveChangesAsync(ct);
        _titleCodes.Invalidate();
        var after = await _titleCodes.GetAsync(ct);

        static HashSet<(string, string)> Pairs(TitleCodeIndex i) =>
            i.AllSupersessions().Select(s => (s.FromCode, s.ToCode)).ToHashSet();
        var was = Pairs(before);
        var now = Pairs(after);
        string Name(string c) => $"{after.FindByCode(c)?.Title} ({c})";

        return new CodeActivityResult
        {
            Code = rows[0].Code,
            Active = active,
            OnPayroll = onPayroll,
            SupersessionChanges =
            [
                .. now.Except(was).Select(p => $"{Name(p.Item1)} is now superseded by {Name(p.Item2)}."),
                .. was.Except(now).Select(p => $"{Name(p.Item1)} is no longer superseded by {Name(p.Item2)}."),
            ],
        };
    }
}

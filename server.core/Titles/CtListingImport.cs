using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Profiles;
using Server.Core.Standards;

namespace Server.Core.Titles;

/// <summary>A standard the listing puts on a different code than it has: moved there on import.</summary>
public sealed class CtStandardMove
{
    public string Standard { get; set; } = "";
    public string FromCode { get; set; } = "";
    public string FromTitle { get; set; } = "";
    public string ToCode { get; set; } = "";
    public string ToTitle { get; set; } = "";
}

/// <summary>A class an import touched, to rebuild or rematch.</summary>
public sealed class CtAffectedClass
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Why { get; set; } = "";
}

/// <summary>What importing a Career Tracks listing does, or did.</summary>
public sealed class CtListingPlan
{
    public bool Applied { get; set; }
    public int Rows { get; set; }

    /// <summary>Codes new to the stored listing, changed in it, and unchanged.</summary>
    public int NewToListing { get; set; }
    public int Changed { get; set; }
    public int Unchanged { get; set; }

    /// <summary>Codes the title reference doesn't have: added, not active (they aren't on payroll).</summary>
    public List<string> AddedCodes { get; set; } = [];

    public int AddedCodeCount { get; set; }

    /// <summary>Grades and families the listing fills in or changes, the listing being newer.</summary>
    public int GradesFilled { get; set; }
    public List<string> GradesChanged { get; set; } = [];
    public int FamiliesFilled { get; set; }
    public int FamiliesChanged { get; set; }

    /// <summary>Codes the Office of the President alone uses: kept inactive here.</summary>
    public int OpOnly { get; set; }

    /// <summary>Active codes whose level the listing gives differently from what their title says.</summary>
    public List<string> SupervisionChanges { get; set; } = [];

    /// <summary>Standards moved to the code the listing gives their title.</summary>
    public List<CtStandardMove> StandardMoves { get; set; } = [];

    /// <summary>Standards matched to no code whose title the listing gives a code: suggested in the match review.</summary>
    public int StandardSuggestions { get; set; }

    /// <summary>Classes on either side of a moved standard, to rebuild or rematch.</summary>
    public List<CtAffectedClass> AffectedClasses { get; set; } = [];

    public const int ListCap = 200;
}

public sealed class CtListingRefusedException(string message) : Exception(message);

/// <summary>
/// Imports the UC Career Tracks title code listing. It is stored as it comes and laid over the
/// title reference (<see cref="CtOverlay"/>), so the reference's own import can't erase it. The
/// listing is trusted over what the app has: its grades and families win, its levels decide who may
/// supervise, and a standard whose exact title it lists under another code is moved to that code —
/// the classes on both sides are listed, to rebuild or rematch. A standard matched to no code whose
/// title it lists is only suggested, in the match review.
/// </summary>
public sealed class CtListingImport
{
    private readonly AppDbContext _db;
    private readonly ITitleCodeService _titleCodes;
    private readonly IStandardsStore _standards;
    private readonly StandardMatching _matching;

    public CtListingImport(AppDbContext db, ITitleCodeService titleCodes, IStandardsStore standards, StandardMatching matching)
    {
        _db = db;
        _titleCodes = titleCodes;
        _standards = standards;
        _matching = matching;
    }

    public static List<CtListingRow> Read(byte[] pdf)
    {
        if (!CtListing.IsListing(pdf))
        {
            throw new CtListingRefusedException("This isn't a Career Tracks title code listing report (a PDF with Job Family, JOB_FUNCTION, LvlCd and JobCode columns).");
        }

        var rows = CtListing.Parse(pdf);
        return rows.Count == 0 ? throw new CtListingRefusedException("No job codes could be read from this report.") : rows;
    }

    /// <summary>What importing the listing would do. Writes nothing.</summary>
    public Task<CtListingPlan> PreviewAsync(List<CtListingRow> rows, CancellationToken ct = default) =>
        PlanAsync(rows, ct);

    public async Task<CtListingPlan> ApplyAsync(List<CtListingRow> rows, int? userId, CancellationToken ct = default)
    {
        var plan = await PlanAsync(rows, ct);

        var now = DateTimeOffset.UtcNow;
        var stored = (await _db.CtTitles.ToListAsync(ct)).ToDictionary(c => TitleCodeIndex.Pad(c.UcJobCode), StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var code = TitleCodeIndex.Pad(r.UcJobCode);
            if (!stored.TryGetValue(code, out var t))
            {
                t = new CtTitle { UcJobCode = code };
                _db.CtTitles.Add(t);
            }

            t.Title = r.Title;
            t.JobFamily = r.JobFamily;
            t.JobFunction = r.JobFunction;
            t.Origin = r.Origin;
            t.LevelCode = r.LevelCode;
            t.LastUpdated = r.LastUpdated;
            t.OpOnly = r.OpOnly;
            t.Flsa = r.Flsa;
            t.PersonnelProgram = r.PersonnelProgram;
            t.JobStatus = r.JobStatus;
            t.Grade = r.Grade;
            t.ImportedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        _titleCodes.Invalidate();

        // A move is a confirmed match, like one made by hand: it replaces the standard's match and
        // travels with the environment package.
        foreach (var move in plan.StandardMoves)
        {
            await _matching.DecideAsync(move.Standard, move.ToCode, isMatch: true, userId, ct);
        }

        _standards.Invalidate();
        plan.Applied = true;
        return plan;
    }

    private async Task<CtListingPlan> PlanAsync(List<CtListingRow> rows, CancellationToken ct)
    {
        var plan = new CtListingPlan { Rows = rows.Count };
        var stored = (await _db.CtTitles.AsNoTracking().ToListAsync(ct))
            .ToDictionary(c => TitleCodeIndex.Pad(c.UcJobCode), StringComparer.Ordinal);
        // The reference as it is without the listing: what the listing changes is measured against it.
        var reference = await _db.TitleCodes.AsNoTracking().ToListAsync(ct);
        var refByCode = reference.GroupBy(t => TitleCodeIndex.Pad(t.Code)).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var index = await _titleCodes.GetAsync(ct);

        static string Num(string? s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).Replace("Grade", "", StringComparison.OrdinalIgnoreCase).TrimStart('0');

        foreach (var r in rows)
        {
            var code = TitleCodeIndex.Pad(r.UcJobCode);
            if (!stored.TryGetValue(code, out var before))
            {
                plan.NewToListing++;
            }
            else if (before.Title != r.Title || before.Grade != r.Grade || before.LevelCode != r.LevelCode || before.JobFamily != r.JobFamily
                     || before.JobFunction != r.JobFunction || before.OpOnly != r.OpOnly || before.Origin != r.Origin)
            {
                plan.Changed++;
            }
            else
            {
                plan.Unchanged++;
            }

            if (r.OpOnly)
            {
                plan.OpOnly++;
            }

            if (!refByCode.TryGetValue(code, out var refRows))
            {
                plan.AddedCodeCount++;
                if (plan.AddedCodes.Count < CtListingPlan.ListCap)
                {
                    plan.AddedCodes.Add($"{code} {r.Title}");
                }

                continue;
            }

            var refGrade = refRows.Select(x => x.Grade).FirstOrDefault(g => !string.IsNullOrWhiteSpace(g));
            if (r.Grade.Length > 0)
            {
                if (refGrade is null)
                {
                    plan.GradesFilled++;
                }
                else if (Num(refGrade) != Num(r.Grade) && plan.GradesChanged.Count < CtListingPlan.ListCap)
                {
                    plan.GradesChanged.Add($"{code} {Profiles.ProfileAggregator.Titleize(refRows[0].Title)}: {refGrade} → Grade {r.Grade}");
                }
            }

            var refFamily = refRows.Select(x => x.Family).FirstOrDefault(f => !string.IsNullOrWhiteSpace(f));
            if (r.JobFamily.Length > 0)
            {
                if (refFamily is null)
                {
                    plan.FamiliesFilled++;
                }
                else if (!string.Equals(refFamily, r.JobFamily, StringComparison.OrdinalIgnoreCase))
                {
                    plan.FamiliesChanged++;
                }
            }

            // Who may supervise: the listing's level against the title, for codes in use here.
            var title = refRows[0].Title;
            var ctRow = new CtTitle { LevelCode = r.LevelCode };
            if (refRows.Any(x => x.IsInUse) && r.LevelCode.Length > 0
                && Supervision.IsSupervisory(title) != ctRow.IsSupervisory
                && plan.SupervisionChanges.Count < CtListingPlan.ListCap)
            {
                plan.SupervisionChanges.Add($"{code} {ProfileAggregator.Titleize(title)} — level {r.LevelCode}: {(ctRow.IsSupervisory ? "can now supervise" : "can no longer supervise")}");
            }
        }

        await PlanStandardsAsync(plan, rows, index, ct);
        return plan;
    }

    /// <summary>
    /// Standards whose exact title the listing gives one code: moved there when they're on another,
    /// suggested when they're on none. A title the listing gives more than one code is left alone —
    /// a wrong code is worse than none. So is a move between a code and its union successor: the
    /// listing knows base codes, and a standard on the successor is where it belongs.
    /// </summary>
    private async Task PlanStandardsAsync(CtListingPlan plan, List<CtListingRow> rows, TitleCodeIndex index, CancellationToken ct)
    {
        var byTitle = rows.GroupBy(r => TitleNormalizer.TitleCodeKey(r.Title))
            .Where(g => g.Select(r => TitleCodeIndex.Pad(r.UcJobCode)).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var standards = await _standards.GetIndexAsync(ct);
        var affected = new Dictionary<string, CtAffectedClass>(StringComparer.Ordinal);
        var classes = (await _db.ClassProfiles.AsNoTracking().Select(p => new { p.UcJobCode, p.Slug, p.Title }).ToListAsync(ct))
            .GroupBy(p => TitleCodeIndex.Pad(p.UcJobCode))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var s in standards.All)
        {
            if (!byTitle.TryGetValue(TitleNormalizer.TitleCodeKey(s.LongTitle), out var listed))
            {
                continue;
            }

            var to = TitleCodeIndex.Pad(listed.UcJobCode);
            var current = StandardCodes.For(s, index);
            if (current is null)
            {
                plan.StandardSuggestions++;
                continue;
            }

            var from = TitleCodeIndex.Pad(current.Code);
            if (from == to
                || TitleCodeIndex.Pad(index.SupersededBy(to)?.ToCode ?? "") == from
                || TitleCodeIndex.Pad(index.SupersededBy(from)?.ToCode ?? "") == to)
            {
                continue;
            }

            plan.StandardMoves.Add(new CtStandardMove
            {
                Standard = s.LongTitle,
                FromCode = from,
                FromTitle = ProfileAggregator.Titleize(current.Title),
                ToCode = to,
                ToTitle = listed.Title,
            });
            foreach (var (code, why) in new[] { (from, $"“{s.LongTitle}” moved away to {to}"), (to, $"“{s.LongTitle}” moved here from {from}") })
            {
                if (classes.TryGetValue(code, out var cls) && !affected.ContainsKey(cls.Slug))
                {
                    affected[cls.Slug] = new CtAffectedClass { Slug = cls.Slug, Title = cls.Title, Why = why };
                }
            }
        }

        plan.AffectedClasses = [.. affected.Values.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase)];
    }
}

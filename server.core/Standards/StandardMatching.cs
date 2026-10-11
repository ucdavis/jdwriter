using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Titles;

namespace Server.Core.Standards;

/// <summary>A job code that may be a standard's.</summary>
public sealed class CodeSuggestion
{
    public string UcJobCode { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>On UC Davis payroll.</summary>
    public bool Active { get; set; }

    /// <summary>0–1: how alike the two titles are, once abbreviations are expanded.</summary>
    public double Score { get; set; }
}

/// <summary>A standard that matches no job code exactly, and the codes it might be.</summary>
public sealed class UnmatchedStandard
{
    public string StandardTitle { get; set; } = "";
    public List<CodeSuggestion> Suggestions { get; set; } = [];
}

/// <summary>A standard matched to a code by hand.</summary>
public sealed class ConfirmedMatch
{
    public string StandardTitle { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset DecidedAt { get; set; }
}

public sealed class StandardMatchReview
{
    public List<UnmatchedStandard> Unmatched { get; set; } = [];
    public List<ConfirmedMatch> Confirmed { get; set; } = [];

    /// <summary>Standards with no exact match and no suggestion worth showing.</summary>
    public int WithoutSuggestion { get; set; }
}

/// <summary>Which job code a standard belongs to.</summary>
public static class StandardCodes
{
    /// <summary>
    /// The standard's own code first — set from its title at import, from the workbook (layout B),
    /// or by an admin's confirmed match — then its title. The one rule every reader of standards uses.
    /// </summary>
    public static TitleCode? For(ClassStandardRecord standard, TitleCodeIndex index) =>
        (string.IsNullOrEmpty(standard.Code) ? null : index.FindByCode(standard.Code))
        ?? index.FindTitleCode(standard.LongTitle);
}

/// <summary>
/// Standards whose title matches no job code exactly — a spelling the normalizer doesn't know, a
/// typo, a reworded title — with the codes they might be, for an admin to confirm or reject.
///
/// Suggestions are deliberately strict: the code must share the standard's level number, union
/// suffix (and grandfathered GF), site marker, per-diem and CEH variant, and supervisor/manager
/// role, because "Coordinator 1" is never "CRD 4", "Supervisor 1" never "Manager 1", and
/// "Analyst 3 CX" is never "Analyst 3"; and its exempt status must not contradict the standard's
/// ("Exempt" is never a NEX code). A standard that states no status may be either. Within that, titles are ranked by shared words and by spelling. Nothing is matched
/// without a person: near titles can be different jobs ("Systems Administration Supervisor" is not
/// "Administrative Supervisor").
/// </summary>
public sealed class StandardMatching
{
    /// <summary>Below this a suggestion is noise; the review shows none rather than a bad one.</summary>
    private const double MinScore = 0.55;

    private const int MaxSuggestions = 3;

    private static readonly HashSet<string> Units = ["rp", "cx", "tx", "rx", "hx", "sv", "gf"];

    /// <summary>Exempt-status words, spelled out on standards ("Non Exempt") and abbreviated on codes ("NEX").</summary>
    private static readonly HashSet<string> FlsaWords = ["ex", "nex", "exempt", "non"];
    private static readonly HashSet<string> SiteMarkers = ["hc", "shs", "vmth"];

    private readonly AppDbContext _db;
    private readonly IStandardsStore _standards;
    private readonly ITitleCodeService _titleCodes;

    public StandardMatching(AppDbContext db, IStandardsStore standards, ITitleCodeService titleCodes)
    {
        _db = db;
        _standards = standards;
        _titleCodes = titleCodes;
    }

    public async Task<StandardMatchReview> ReviewAsync(bool includeNotActive, CancellationToken ct = default)
    {
        var standards = await _standards.GetIndexAsync(ct);
        var index = await _titleCodes.GetAsync(ct);
        var decisions = await _db.StandardMatchDecisions.AsNoTracking().ToListAsync(ct);
        var rejected = decisions.Where(d => !d.IsMatch)
            .Select(d => (d.StandardTitle, TitleCodeIndex.Pad(d.UcJobCode)))
            .ToHashSet();

        var pool = index.All
            .Where(t => !index.IsSuperseded(t.Code) && (includeNotActive || t.IsInUse))
            .Select(t => (Code: t, Key: TitleNormalizer.TitleCodeKey(t.Title)))
            .GroupBy(t => Shape(t.Key))
            .ToDictionary(g => g.Key, g => g.ToList());

        var review = new StandardMatchReview
        {
            Confirmed = decisions.Where(d => d.IsMatch)
                .OrderBy(d => d.StandardTitle, StringComparer.Ordinal)
                .Select(d => new ConfirmedMatch
                {
                    StandardTitle = d.StandardTitle,
                    UcJobCode = d.UcJobCode,
                    Title = index.FindByCode(d.UcJobCode)?.Title ?? "",
                    DecidedAt = d.DecidedAt,
                })
                .ToList(),
        };

        foreach (var s in standards.All.OrderBy(s => s.LongTitle, StringComparer.Ordinal))
        {
            if (StandardCodes.For(s, index) is not null)
            {
                continue;
            }

            var key = TitleNormalizer.TitleCodeKey(s.LongTitle);
            var flsa = Flsa(key);
            var suggestions = (pool.GetValueOrDefault(Shape(key)) ?? [])
                .Where(c => flsa is null || Flsa(c.Key) is not { } other || other == flsa)
                .Where(c => !rejected.Contains((s.LongTitle, TitleCodeIndex.Pad(c.Code.Code))))
                .Select(c => new CodeSuggestion
                {
                    UcJobCode = c.Code.Code,
                    Title = c.Code.Title,
                    Active = c.Code.IsInUse,
                    Score = Math.Round(Similarity(key, c.Key), 2),
                })
                .Where(c => c.Score >= MinScore)
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.UcJobCode, StringComparer.Ordinal)
                .Take(MaxSuggestions)
                .ToList();

            if (suggestions.Count == 0)
            {
                review.WithoutSuggestion++;
                continue;
            }

            review.Unmatched.Add(new UnmatchedStandard { StandardTitle = s.LongTitle, Suggestions = suggestions });
        }

        review.Unmatched = review.Unmatched.OrderByDescending(u => u.Suggestions[0].Score).ToList();
        return review;
    }

    /// <summary>
    /// Record a decision. A match replaces any earlier match for the standard and takes effect at
    /// once; a rejection hides that pairing. The code must exist in the title reference.
    /// </summary>
    public async Task DecideAsync(string standardTitle, string code, bool isMatch, int? userId, CancellationToken ct = default)
    {
        var index = await _titleCodes.GetAsync(ct);
        var tc = index.FindByCode(code) ?? throw new ArgumentException($"No UC job code {code}.");
        var standards = await _standards.GetIndexAsync(ct);
        if (!standards.All.Any(s => s.LongTitle == standardTitle))
        {
            throw new ArgumentException($"No standard titled “{standardTitle}”.");
        }

        var existing = await _db.StandardMatchDecisions.Where(d => d.StandardTitle == standardTitle).ToListAsync(ct);
        _db.StandardMatchDecisions.RemoveRange(existing.Where(d =>
            TitleCodeIndex.Pad(d.UcJobCode) == TitleCodeIndex.Pad(tc.Code) || (isMatch && d.IsMatch)));

        _db.StandardMatchDecisions.Add(new StandardMatchDecision
        {
            StandardTitle = standardTitle,
            UcJobCode = tc.Code,
            IsMatch = isMatch,
            DecidedByUserId = userId,
            DecidedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        _standards.Invalidate();
    }

    /// <summary>Undo a confirmed match: the standard goes back to the review.</summary>
    public async Task UnmatchAsync(string standardTitle, CancellationToken ct = default)
    {
        _db.StandardMatchDecisions.RemoveRange(
            await _db.StandardMatchDecisions.Where(d => d.StandardTitle == standardTitle && d.IsMatch).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
        _standards.Invalidate();
    }

    /// <summary>
    /// What a title must share with a code to be suggested: level numbers, union suffix, site marker,
    /// and its variant (per diem, CEH) and role (supervisor, manager) — each a separate classification.
    /// </summary>
    internal static string Shape(string key)
    {
        var words = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var variants = new List<string>();
        if (key.Contains("per diem", StringComparison.Ordinal) || words.Contains("pd"))
        {
            variants.Add("pd");
        }

        variants.AddRange(words.Where(w => w is "ceh" or "supervisor" or "manager").Distinct().Order(StringComparer.Ordinal));
        return string.Join(' ', words.Where(w => w.All(char.IsDigit)))
               + "|" + string.Join(' ', words.Where(Units.Contains).Order(StringComparer.Ordinal))
               + "|" + string.Join(' ', words.Where(SiteMarkers.Contains).Order(StringComparer.Ordinal))
               + "|" + string.Join(' ', variants);
    }

    /// <summary>"nex", "ex", or null when the title states no exempt status.</summary>
    internal static string? Flsa(string key)
    {
        var words = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Contains("nex") || key.Contains("non exempt", StringComparison.Ordinal))
        {
            return "nex";
        }

        return words.Contains("ex") || words.Contains("exempt") ? "ex" : null;
    }

    /// <summary>Half shared words (Jaccard), half spelling (character-bigram Dice), on expanded keys.</summary>
    internal static double Similarity(string a, string b)
    {
        static HashSet<string> Words(string k) =>
            k.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !w.All(char.IsDigit) && !Units.Contains(w) && !SiteMarkers.Contains(w) && !FlsaWords.Contains(w))
                .ToHashSet(StringComparer.Ordinal);

        static List<string> Bigrams(string k)
        {
            var s = k.Replace(" ", "", StringComparison.Ordinal);
            return Enumerable.Range(0, Math.Max(0, s.Length - 1)).Select(i => s.Substring(i, 2)).ToList();
        }

        var wa = Words(a);
        var wb = Words(b);
        var jaccard = wa.Count + wb.Count == 0 ? 0 : (double)wa.Intersect(wb).Count() / wa.Union(wb).Count();

        var ba = Bigrams(a);
        var bb = Bigrams(b);
        var pool = bb.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var shared = 0;
        foreach (var g in ba)
        {
            if (pool.TryGetValue(g, out var n) && n > 0)
            {
                shared++;
                pool[g] = n - 1;
            }
        }

        var dice = ba.Count + bb.Count == 0 ? 0 : 2.0 * shared / (ba.Count + bb.Count);
        return (jaccard + dice) / 2;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Titles;

namespace Server.Core.Standards;

/// <summary>
/// An immutable lookup over the official job standards.
///
/// Two keys point at the same records, exactly as the reference does: the STRICT key
/// (<see cref="TitleNormalizer.TitleCodeKey"/>, variant suffixes kept) wins, and the LOOSE key is a
/// fallback so a caller asking with a slightly different spelling still resolves to a reasonable
/// neighbour rather than to nothing.
///
/// Mirrors <see cref="TitleCodeIndex"/> deliberately: all the logic worth testing lives here with
/// no EF dependency, and <see cref="StandardsStore"/> is the thin thing that loads rows.
/// </summary>
public sealed class StandardsIndex
{
    private readonly List<ClassStandardRecord> _all;
    private readonly Dictionary<string, ClassStandardRecord> _byKey;

    public StandardsIndex(IEnumerable<ClassStandardRecord> standards)
    {
        _all = [.. standards];
        _byKey = new Dictionary<string, ClassStandardRecord>(StringComparer.Ordinal);

        // Loose keys first, first-wins — a loose key can cover several real classifications, and
        // the earliest record is the reference's choice.
        foreach (var s in _all)
        {
            // HC-aware, so a Health Center class never falls back to the regular class's standard.
            var loose = HealthCenter.ClassKey(s.LongTitle);
            _byKey.TryAdd(loose, s);
        }

        // Then strict keys, last-wins, overwriting any loose entry that collides. A strict key
        // identifies exactly one classification, so it must never lose to a loose one.
        foreach (var s in _all)
        {
            _byKey[TitleNormalizer.TitleCodeKey(s.LongTitle)] = s;
        }
    }

    /// <summary>
    /// Every standard, DISTINCT.
    ///
    /// Deliberate deviation from the reference, which returns the lookup map's values and so
    /// yields 228 entries for 214 standards — 14 records are reachable under both a loose and a
    /// strict key and appear twice. That inflates `standardsCount()` and would emit duplicate
    /// bootstrap candidates. The map is an index, not the canonical list; here the list comes from
    /// the rows.
    /// </summary>
    public IReadOnlyList<ClassStandardRecord> All => _all;

    public int Count => _all.Count;

    /// <summary>Find the standard for a class title, abbreviation-aware. Strict key first.</summary>
    public ClassStandardRecord? ForTitle(string title)
    {
        if (_byKey.TryGetValue(TitleNormalizer.TitleCodeKey(title), out var strict))
        {
            return strict;
        }

        return _byKey.TryGetValue(HealthCenter.ClassKey(title), out var loose) ? loose : null;
    }

    /// <summary>Find by job code, padded so unpadded form entries still resolve.</summary>
    public ClassStandardRecord? ForCode(string code)
    {
        var padded = TitleCodeIndex.Pad(code);
        return _all.FirstOrDefault(s => !string.IsNullOrEmpty(s.Code) && TitleCodeIndex.Pad(s.Code!) == padded);
    }
}

public interface IStandardsStore
{
    Task<StandardsIndex> GetIndexAsync(CancellationToken ct = default);

    /// <summary>Drop the cached index. Call after importing or editing standards.</summary>
    void Invalidate();
}

/// <summary>
/// Loads <see cref="StandardsIndex"/> from the database and caches it. Ported from the POC's
/// src/lib/standards/store.ts, where it read a JSON file and cached on its mtime — a workaround for
/// Next.js not sharing module state, which this stack does not need.
/// </summary>
public sealed class StandardsStore : IStandardsStore
{
    private const string CacheKey = "standards-index";

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public StandardsStore(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<StandardsIndex> GetIndexAsync(CancellationToken ct = default)
    {
        // Same cheap stamp as the title reference: COUNT and MAX(Id) catch every insert and delete,
        // which is what an import does. An in-place edit is not caught, hence Invalidate().
        var count = await _db.JobStandards.CountAsync(ct);
        var maxId = count == 0 ? 0 : await _db.JobStandards.MaxAsync(s => s.Id, ct);
        var stamp = $"{count}:{maxId}";

        if (_cache.TryGetValue(CacheKey, out Cached? cached) && cached is not null && cached.Stamp == stamp)
        {
            return cached.Index;
        }

        var rows = await _db.JobStandards.AsNoTracking()
            .Include(s => s.Items)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var index = new StandardsIndex(rows.Select(ToRecord));
        _cache.Set(CacheKey, new Cached(stamp, index));
        return index;
    }

    public void Invalidate() => _cache.Remove(CacheKey);

    /// <summary>
    /// Entity to the shape the parser produces, so every consumer works with one type whether a
    /// standard came from a workbook or from the database.
    /// </summary>
    public static ClassStandardRecord ToRecord(JobStandard s)
    {
        List<string> Items(StandardItemKind kind) => s.Items
            .Where(i => i.Kind == kind)
            .OrderBy(i => i.Ordinal)
            .Select(i => i.Text)
            .ToList();

        return new ClassStandardRecord
        {
            LongTitle = s.LongTitle,
            Code = s.Code,
            PersProg = s.PersProg,
            Grade = s.Grade,
            Flsa = s.Flsa,
            Union = s.Union,
            GenericScope = s.GenericScope,
            CustomScope = s.CustomScope,
            KeyResponsibilities = Items(StandardItemKind.KeyResponsibility),
            Ksa = Items(StandardItemKind.Ksa),
            Education = Items(StandardItemKind.Education),
            Licenses = Items(StandardItemKind.License),
            SpecialConditions = Items(StandardItemKind.SpecialCondition),
        };
    }

    /// <summary>The reverse of <see cref="ToRecord"/>, with both title keys derived.</summary>
    public static JobStandard ToEntity(ClassStandardRecord r)
    {
        var s = new JobStandard
        {
            LongTitle = r.LongTitle,
            Code = string.IsNullOrWhiteSpace(r.Code) ? null : r.Code,
            PersProg = r.PersProg,
            Grade = r.Grade,
            Flsa = r.Flsa,
            Union = r.Union,
            GenericScope = r.GenericScope,
            CustomScope = r.CustomScope,
            TitleKey = TitleNormalizer.TitleKey(r.LongTitle),
            TitleCodeKey = TitleNormalizer.TitleCodeKey(r.LongTitle),
        };

        void Add(StandardItemKind kind, List<string> texts)
        {
            for (var i = 0; i < texts.Count; i++)
            {
                s.Items.Add(new JobStandardItem { Kind = kind, Ordinal = i, Text = texts[i] });
            }
        }

        Add(StandardItemKind.KeyResponsibility, r.KeyResponsibilities);
        Add(StandardItemKind.Ksa, r.Ksa);
        Add(StandardItemKind.Education, r.Education);
        Add(StandardItemKind.License, r.Licenses);
        Add(StandardItemKind.SpecialCondition, r.SpecialConditions);
        return s;
    }

    private sealed record Cached(string Stamp, StandardsIndex Index);
}

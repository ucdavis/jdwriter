using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Server.Core.Data;

namespace Server.Core.Titles;

public interface ITitleCodeService
{
    /// <summary>The current index, loading and caching it if needed.</summary>
    Task<TitleCodeIndex> GetAsync(CancellationToken ct = default);

    /// <summary>Drop the cached index. Call after importing or editing the title reference.</summary>
    void Invalidate();
}

/// <summary>
/// Loads <see cref="TitleCodeIndex"/> from the database and caches it.
///
/// Deliberately thin: every decision worth testing lives in the index, which has no EF dependency
/// and is verified against fixtures. This class only answers "where do the rows come from and how
/// long may we keep them".
///
/// The POC cached on the JSON file's mtime, because Next.js does not reliably share module state
/// between its page and route-handler module instances, so a plain singleton went stale after an
/// ingest and no refresh fixed it. That whole problem is absent here — but the underlying need is
/// not: the reference is read on nearly every corpus path and rebuilt rarely, and a stale index
/// silently resolves job codes against data that no longer exists.
/// </summary>
public class TitleCodeService : ITitleCodeService
{
    private const string CacheKey = "titlecode-index";

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public TitleCodeService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<TitleCodeIndex> GetAsync(CancellationToken ct = default)
    {
        // A cheap stamp over the table stands in for the POC's file mtime. COUNT and MAX(Id)
        // catch every insert and delete, which is what a reference import does — it replaces
        // rows rather than editing them in place. An in-place edit is NOT caught, which is why
        // Invalidate() exists and why the importer must call it.
        var count = await _db.TitleCodes.CountAsync(ct);
        var maxId = count == 0 ? 0 : await _db.TitleCodes.MaxAsync(t => t.Id, ct);
        var stamp = $"{count}:{maxId}";

        if (_cache.TryGetValue(CacheKey, out CachedIndex? cached)
            && cached is not null
            && cached.Stamp == stamp)
        {
            return cached.Index;
        }

        var rows = await _db.TitleCodes.AsNoTracking().OrderBy(t => t.Id).ToListAsync(ct);
        var index = new TitleCodeIndex(rows);

        // No expiry: the reference changes only on an explicit import, and the stamp above
        // already forces a reload when it does. Size is bounded — roughly 3,500 rows.
        _cache.Set(CacheKey, new CachedIndex(stamp, index));
        return index;
    }

    public void Invalidate() => _cache.Remove(CacheKey);

    private sealed record CachedIndex(string Stamp, TitleCodeIndex Index);
}

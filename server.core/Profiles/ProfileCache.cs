using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Core.Profiles;

/// <summary>
/// Every class profile's full graph, held once in memory. Classify, intake and fit each need all
/// of them, and loading them is ~800 profiles and tens of thousands of rows (about 90 MB) per
/// request: on a small database, a few people classifying at once would saturate it, and each
/// concurrent request held its own copy.
///
/// It is never served stale by the app itself: <see cref="ProfileCacheInvalidator"/> drops it on
/// any save that touches a profile table, and a load that overlapped a save is not kept. Writes
/// from outside the app (the jdw-cli migrator) aren't seen, so a copy also expires after
/// <see cref="MaxAge"/> — which also frees the memory when nobody is using it.
///
/// The profiles are shared between requests and must not be modified. Every caller reads them.
/// </summary>
public sealed class ProfileCache
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private long _version;
    private (List<ClassProfile> Profiles, long Version, DateTimeOffset LoadedAt)? _held;
    private (Task<List<ClassProfile>> Load, long Version)? _loading;

    public ProfileCache(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    /// <summary>A profile changed: the next read loads afresh.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _version++;
            _held = null;
            _loading = null;
        }
    }

    /// <summary>
    /// The held profiles, or one shared load of them: requests arriving together wait for the
    /// same load rather than each running it. The load isn't cancelled by one caller leaving.
    /// </summary>
    public async Task<List<ClassProfile>> GetAsync(Func<Task<List<ClassProfile>>> load, CancellationToken ct = default)
    {
        Task<List<ClassProfile>> pending;
        long version;
        lock (_gate)
        {
            if (_held is { } held && _clock.GetUtcNow() - held.LoadedAt < MaxAge)
            {
                return held.Profiles;
            }

            if (_loading is { } running && running.Version == _version)
            {
                pending = running.Load;
                version = running.Version;
            }
            else
            {
                version = _version;
                pending = load();
                _loading = (pending, version);
            }
        }

        var profiles = await pending.WaitAsync(ct);
        lock (_gate)
        {
            // Kept only when nothing changed while it loaded: otherwise it may predate the change.
            if (_version == version && _loading?.Load == pending)
            {
                _held = (profiles, version, _clock.GetUtcNow());
                _loading = null;
            }
        }

        return profiles;
    }
}

/// <summary>
/// Drops the <see cref="ProfileCache"/> whenever a save touches any table in a profile's graph —
/// after the save, or after the transaction it ran in commits. One place, so no write path can
/// forget to. Saves to other tables (the corpus, drafts, users) leave it alone.
/// </summary>
public sealed class ProfileCacheInvalidator : SaveChangesInterceptor, IDbTransactionInterceptor
{
    /// <summary>The types <c>ClassProfileRepository.FullGraph</c> loads. Keep the two in step.</summary>
    public static readonly IReadOnlySet<Type> GraphTypes = new HashSet<Type>
    {
        typeof(ClassProfile),
        typeof(JobEnvelope), typeof(EnvelopeResponsibility), typeof(EnvelopeDuty), typeof(EnvelopeListItem),
        typeof(ProfileDistribution), typeof(ProfileDistributionValue),
        typeof(ProfileFunction), typeof(ProfileFunctionSampleDuty), typeof(ProfileQualItem),
        typeof(ConsolidatedFunction), typeof(ConsolidatedFunctionMember), typeof(ConsolidatedFunctionSampleDuty),
        typeof(ConsolidatedQual), typeof(ConsolidatedQualMember),
        typeof(ProfileDroppedItem), typeof(ProfileSourceFile),
        typeof(CoverageReport), typeof(JdCoverage), typeof(JdCoverageUncovered),
    };

    private readonly ProfileCache _cache;

    // Contexts whose open transaction has saved profile changes: dropped again when it commits.
    private readonly ConditionalWeakTable<DbContext, object> _pending = new();

    public ProfileCacheInvalidator(ProfileCache cache) => _cache = cache;

    private static bool TouchesProfiles(DbContext? db) =>
        db is not null && db.ChangeTracker.Entries().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
            && GraphTypes.Contains(e.Metadata.ClrType));

    private void Saving(DbContext? db)
    {
        if (TouchesProfiles(db))
        {
            _pending.AddOrUpdate(db!, new object());
        }
    }

    private void Saved(DbContext? db)
    {
        if (db is null || !_pending.TryGetValue(db, out _))
        {
            return;
        }

        _cache.Invalidate();
        // Inside a transaction, readers can't see the change until it commits: drop it again then.
        if (db.Database.CurrentTransaction is null)
        {
            _pending.Remove(db);
        }
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Saving(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Saving(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Saved(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void TransactionCommitted(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is not null && _pending.TryGetValue(eventData.Context, out _))
        {
            _pending.Remove(eventData.Context);
            _cache.Invalidate();
        }
    }

    public Task TransactionCommittedAsync(
        System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        TransactionCommitted(transaction, eventData);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is not null)
        {
            _pending.Remove(eventData.Context);
        }
    }

    public Task TransactionRolledBackAsync(
        System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        TransactionRolledBack(transaction, eventData);
        return Task.CompletedTask;
    }
}

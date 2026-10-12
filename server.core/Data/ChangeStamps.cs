using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Server.Core.Domain;

namespace Server.Core.Data;

/// <summary>
/// Stamps when a class, a standard or a compliance rule last changed, whatever changed it: every
/// write path — ingest, bootstrap, envelope edits, coverage, the qualification rules pass — saves
/// through here, so none can forget. A change to any row in a class's graph (its envelope, a duty,
/// a coverage row) stamps the class. An environment package keeps the newer of two versions by it.
/// </summary>
public partial class AppDbContext
{
    /// <summary>
    /// The parent of each row that belongs to a stamped root, by the foreign key that points to it.
    /// A type missing here is not part of a stamped graph.
    /// </summary>
    private static readonly Dictionary<Type, (string ForeignKey, Type Parent)> Owners = new()
    {
        [typeof(JobEnvelope)] = (nameof(JobEnvelope.ClassProfileId), typeof(ClassProfile)),
        [typeof(EnvelopeResponsibility)] = (nameof(EnvelopeResponsibility.JobEnvelopeId), typeof(JobEnvelope)),
        [typeof(EnvelopeDuty)] = (nameof(EnvelopeDuty.EnvelopeResponsibilityId), typeof(EnvelopeResponsibility)),
        [typeof(EnvelopeListItem)] = (nameof(EnvelopeListItem.JobEnvelopeId), typeof(JobEnvelope)),
        [typeof(ProfileSourceFile)] = (nameof(ProfileSourceFile.ClassProfileId), typeof(ClassProfile)),
        [typeof(ProfileDistribution)] = (nameof(ProfileDistribution.ClassProfileId), typeof(ClassProfile)),
        [typeof(ProfileDistributionValue)] = (nameof(ProfileDistributionValue.ProfileDistributionId), typeof(ProfileDistribution)),
        [typeof(ProfileFunction)] = (nameof(ProfileFunction.ClassProfileId), typeof(ClassProfile)),
        [typeof(ProfileFunctionSampleDuty)] = (nameof(ProfileFunctionSampleDuty.ProfileFunctionId), typeof(ProfileFunction)),
        [typeof(ProfileQualItem)] = (nameof(ProfileQualItem.ClassProfileId), typeof(ClassProfile)),
        [typeof(ConsolidatedFunction)] = (nameof(ConsolidatedFunction.ClassProfileId), typeof(ClassProfile)),
        [typeof(ConsolidatedFunctionMember)] = (nameof(ConsolidatedFunctionMember.ConsolidatedFunctionId), typeof(ConsolidatedFunction)),
        [typeof(ConsolidatedFunctionSampleDuty)] = (nameof(ConsolidatedFunctionSampleDuty.ConsolidatedFunctionId), typeof(ConsolidatedFunction)),
        [typeof(ConsolidatedQual)] = (nameof(ConsolidatedQual.ClassProfileId), typeof(ClassProfile)),
        [typeof(ConsolidatedQualMember)] = (nameof(ConsolidatedQualMember.ConsolidatedQualId), typeof(ConsolidatedQual)),
        [typeof(ProfileDroppedItem)] = (nameof(ProfileDroppedItem.ClassProfileId), typeof(ClassProfile)),
        [typeof(CoverageReport)] = (nameof(CoverageReport.ClassProfileId), typeof(ClassProfile)),
        [typeof(JdCoverage)] = (nameof(JdCoverage.CoverageReportId), typeof(CoverageReport)),
        [typeof(JdCoverageUncovered)] = (nameof(JdCoverageUncovered.JdCoverageId), typeof(JdCoverage)),
        [typeof(JobStandardItem)] = (nameof(JobStandardItem.JobStandardId), typeof(JobStandard)),
    };

    /// <summary>
    /// True while an environment package is applied: its rows carry the stamps they had where they
    /// came from, and a save must not replace them with now.
    /// </summary>
    public bool KeepChangeStamps { get; private set; }

    /// <summary>
    /// Keep the stamps rows already carry until the scope is disposed. A scope, never a setting: the
    /// context is pooled, so a flag left on by a failed import would stop stamping later requests.
    /// </summary>
    public IDisposable KeepingChangeStamps()
    {
        KeepChangeStamps = true;
        return new Scope(this);
    }

    private sealed class Scope(AppDbContext db) : IDisposable
    {
        public void Dispose() => db.KeepChangeStamps = false;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await StampAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task StampAsync(CancellationToken ct)
    {
        if (KeepChangeStamps)
        {
            return;
        }

        ChangeTracker.DetectChanges();
        var now = DateTimeOffset.UtcNow;
        var changed = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var roots = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var entry in changed)
        {
            if (await RootOfAsync(entry, ct) is { } root)
            {
                roots.Add(root);
            }
        }

        foreach (var root in roots)
        {
            var entry = Entry(root);
            if (entry.State == EntityState.Deleted)
            {
                continue;
            }

            switch (root)
            {
                case ClassProfile p:
                    p.UpdatedAt = now;
                    break;
                case JobStandard s:
                    s.UpdatedAt = now;
                    break;
                case ComplianceRule r:
                    r.UpdatedAt = now;
                    break;
            }
        }
    }

    /// <summary>The stamped root a changed row belongs to, walking up its foreign keys; null for none.</summary>
    private async Task<object?> RootOfAsync(EntityEntry entry, CancellationToken ct)
    {
        var entity = entry.Entity;
        for (var depth = 0; depth < 5; depth++)
        {
            if (entity is ClassProfile or JobStandard or ComplianceRule)
            {
                return entity;
            }

            if (!Owners.TryGetValue(entity.GetType(), out var owner))
            {
                return null;
            }

            var e = Entry(entity);
            var property = e.Property(owner.ForeignKey);
            // A deleted row's key is its original value; an added row's may be a temporary one that
            // points at a parent added in the same save, which the tracker resolves.
            var key = e.State == EntityState.Deleted ? property.OriginalValue : property.CurrentValue;
            var parent = key is null ? null : await FindAsync(owner.Parent, [key], ct);
            if (parent is null)
            {
                // Added with its parent through navigation, before keys are fixed up.
                parent = e.Navigations
                    .Where(n => n.Metadata.TargetEntityType.ClrType == owner.Parent)
                    .Select(n => n.CurrentValue)
                    .FirstOrDefault();
            }

            if (parent is null)
            {
                return null;
            }

            entity = parent;
        }

        return null;
    }
}

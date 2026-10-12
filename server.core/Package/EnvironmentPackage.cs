using System.Collections;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Core.Package;

/// <summary>
/// Everything an environment has built, in one file, to carry to another: the codes and what's
/// active, the standards and match decisions, every JD in the corpus with the original it was read
/// from, every class in full — envelope, consolidation, coverage, distributions — and the compliance
/// rules. A class arrives as it is, with no model calls and nothing rebuilt.
///
/// Never in it: user accounts, admin grants, AI keys and their audit, and authors' saved JDs. Those
/// belong to each environment.
/// </summary>
public sealed class EnvironmentPackage
{
    public const string FormatName = "jdwriter.environment";

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = 1;
    public DateTimeOffset ExportedAt { get; set; }

    /// <summary>Real JDs (prod only), or scrubbed of what identifies a position.</summary>
    public bool Scrubbed { get; set; }

    public List<TitleCode> TitleCodes { get; set; } = [];
    public List<CodeActiveOverride> ActiveCodes { get; set; } = [];
    public List<JobStandard> Standards { get; set; } = [];
    public List<StandardMatchDecision> StandardMatches { get; set; } = [];
    public List<ComplianceRule> ComplianceRules { get; set; } = [];

    /// <summary>The originals JDs were read from — uploads, Word documents, Classify text.</summary>
    public List<CorpusUpload> Originals { get; set; } = [];

    public List<JobDescription> JobDescriptions { get; set; } = [];
    public List<ClassProfile> Classes { get; set; } = [];

    /// <summary>The record types a package holds, and so the only ones its JSON may reach.</summary>
    public static readonly IReadOnlySet<Type> Types = new HashSet<Type>
    {
        typeof(TitleCode), typeof(CodeActiveOverride),
        typeof(JobStandard), typeof(JobStandardItem), typeof(StandardMatchDecision),
        typeof(ComplianceRule), typeof(CorpusUpload),
        typeof(JobDescription), typeof(JdResponsibility), typeof(JdDuty), typeof(JdQualificationItem), typeof(JdPemEntry),
        typeof(ClassProfile), typeof(ProfileSourceFile), typeof(ProfileDistribution), typeof(ProfileDistributionValue),
        typeof(ProfileFunction), typeof(ProfileFunctionSampleDuty), typeof(ProfileQualItem),
        typeof(JobEnvelope), typeof(EnvelopeResponsibility), typeof(EnvelopeDuty), typeof(EnvelopeListItem),
        typeof(ConsolidatedFunction), typeof(ConsolidatedFunctionMember), typeof(ConsolidatedFunctionSampleDuty),
        typeof(ConsolidatedQual), typeof(ConsolidatedQualMember), typeof(ProfileDroppedItem),
        typeof(CoverageReport), typeof(JdCoverage), typeof(JdCoverageUncovered),
    };
}

/// <summary>How many of each a package holds, as the admin sees it before downloading or applying it.</summary>
public sealed class PackageCounts
{
    public int Codes { get; set; }
    public int ActiveOverrides { get; set; }
    public int Standards { get; set; }
    public int StandardMatches { get; set; }
    public int ComplianceRules { get; set; }
    public int JobDescriptions { get; set; }
    public int Originals { get; set; }
    public int Classes { get; set; }

    public static PackageCounts Of(EnvironmentPackage p) => new()
    {
        Codes = p.TitleCodes.Count,
        ActiveOverrides = p.ActiveCodes.Count,
        Standards = p.Standards.Count,
        StandardMatches = p.StandardMatches.Count,
        ComplianceRules = p.ComplianceRules.Count,
        JobDescriptions = p.JobDescriptions.Count,
        Originals = p.Originals.Count,
        Classes = p.Classes.Count,
    };
}

public sealed class EnvironmentPackages
{
    private readonly AppDbContext _db;

    public EnvironmentPackages(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>The package as gzip-compressed JSON.</summary>
    public async Task<byte[]> ExportAsync(CancellationToken ct = default)
    {
        var package = await BuildAsync(ct);
        try
        {
            using var buffer = new MemoryStream();
            await using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                await JsonSerializer.SerializeAsync(gzip, package, PackageJson.Options(_db.Model, EnvironmentPackage.Types), ct);
            }

            return buffer.ToArray();
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Everything packaged, as tracked entities: each table is loaded whole and the context assembles
    /// the graphs, rather than one Include chain per child table. Callers clear the tracker after.
    /// </summary>
    public async Task<EnvironmentPackage> BuildAsync(CancellationToken ct = default)
    {
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var type in EnvironmentPackage.Types.Where(t => t != typeof(CorpusUpload)))
            {
                await LoadAsync(type, ct);
            }

            // Collections in the order they were written, so a class's lists read the same after the trip.
            foreach (var entry in _db.ChangeTracker.Entries().ToList())
            {
                foreach (var collection in entry.Collections)
                {
                    if (collection.CurrentValue is IList list && list.Count > 1)
                    {
                        var ordered = list.Cast<object>().OrderBy(Key).ToList();
                        list.Clear();
                        foreach (var item in ordered)
                        {
                            list.Add(item);
                        }
                    }
                }
            }

            static int Key(object o) => (int)(o.GetType().GetProperty("Id")?.GetValue(o) ?? 0);
            List<T> All<T>() where T : class => [.. _db.ChangeTracker.Entries<T>().Select(e => e.Entity)];

            return new EnvironmentPackage
            {
                ExportedAt = DateTimeOffset.UtcNow,
                TitleCodes = [.. All<TitleCode>().OrderBy(t => t.Id)],
                ActiveCodes = [.. All<CodeActiveOverride>().OrderBy(o => o.UcJobCode, StringComparer.Ordinal)],
                Standards = [.. All<JobStandard>().OrderBy(s => s.LongTitle, StringComparer.Ordinal)],
                StandardMatches = [.. All<StandardMatchDecision>().OrderBy(d => d.StandardTitle, StringComparer.Ordinal).ThenBy(d => d.UcJobCode, StringComparer.Ordinal)],
                ComplianceRules = [.. All<ComplianceRule>().OrderBy(r => r.Key, StringComparer.Ordinal)],
                // Only originals behind JDs that are, or are waiting to be, in the corpus: a failed
                // upload is a record of a refusal, not evidence.
                Originals = await _db.CorpusUploads.AsNoTracking()
                    .Where(u => u.Status != CorpusUploadStatus.Failed)
                    .OrderBy(u => u.Id)
                    .ToListAsync(ct),
                JobDescriptions = [.. All<JobDescription>().OrderBy(j => j.SourceFile, StringComparer.Ordinal)],
                Classes = [.. All<ClassProfile>().OrderBy(p => p.Slug, StringComparer.Ordinal)],
            };
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    private Task LoadAsync(Type type, CancellationToken ct) =>
        (Task)typeof(EnvironmentPackages).GetMethod(nameof(LoadTypeAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(type)
            .Invoke(this, [ct])!;

    private Task LoadTypeAsync<T>(CancellationToken ct) where T : class => _db.Set<T>().LoadAsync(ct);
}

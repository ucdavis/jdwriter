using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Core.Profiles;

/// <summary>What retiring a superseded profile does to it.</summary>
public enum RetireAction
{
    /// <summary>The successor already has a profile: move saved JDs onto it and drop this one.</summary>
    Merge,

    /// <summary>
    /// No successor profile, but this one carries corpus evidence or saved JDs: keep the row and
    /// re-identify it as the successor, so neither is lost or orphaned.
    /// </summary>
    Reidentify,

    /// <summary>
    /// A standard-derived profile with nothing attached. Dropped, so the successor can be built
    /// from ITS OWN standard rather than the retired class's.
    /// </summary>
    Remove,
}

/// <summary>A profile filed under a superseded job code, and what retiring it will do.</summary>
public sealed class SupersededProfile
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Code { get; set; } = "";
    public string SuccessorCode { get; set; } = "";
    public string SuccessorTitle { get; set; } = "";

    /// <summary>The successor's profile after retirement — null when the profile was removed.</summary>
    public string? SuccessorSlug { get; set; }

    /// <summary>Corpus JDs filed under the retired code, which are refiled to the successor.</summary>
    public int CorpusJds { get; set; }

    /// <summary>Saved JDs written against this profile, which follow it to the successor.</summary>
    public int AuthoredJds { get; set; }

    public RetireAction Action { get; set; }

    /// <summary>
    /// The successor profile was built without this class's corpus JDs, so it should be rebuilt
    /// from the corpus to learn from them.
    /// </summary>
    public bool NeedsRebuild { get; set; }
}

public sealed class RetirementResult
{
    public List<SupersededProfile> Profiles { get; set; } = [];

    /// <summary>Corpus JDs moved from a retired code to its successor, with or without a profile.</summary>
    public int RefiledJds { get; set; }
}

public interface ISupersessionReconciler
{
    /// <summary>What <see cref="RetireAsync"/> would do, without changing anything.</summary>
    Task<RetirementResult> PreviewAsync(CancellationToken ct = default);

    Task<RetirementResult> RetireAsync(CancellationToken ct = default);
}

/// <summary>
/// Brings stored data into line with the supersession map.
///
/// Superseded codes are remapped at parse time and every create path refuses them, so new data
/// never lands under a dead class. But supersession is DERIVED from the title reference, and
/// widening the derivation (RP alone, then CX, TX, RX and HX) retires codes that already have
/// profiles and corpus JDs. Without this, those classes stay in browse and the classifier, and
/// their loose-key title hides the successor's standard from bootstrapping.
///
/// Idempotent: once nothing is filed under a superseded code, a second run changes nothing.
/// </summary>
public sealed class SupersessionReconciler : ISupersessionReconciler
{
    private readonly AppDbContext _db;
    private readonly ITitleCodeService _titleCodes;
    private readonly IStandardsStore _standards;

    public SupersessionReconciler(AppDbContext db, ITitleCodeService titleCodes, IStandardsStore standards)
    {
        _db = db;
        _titleCodes = titleCodes;
        _standards = standards;
    }

    public async Task<RetirementResult> PreviewAsync(CancellationToken ct = default)
    {
        var index = await _titleCodes.GetAsync(ct);
        var (plan, refiled) = await PlanAsync(index, ct);
        return new RetirementResult { Profiles = plan, RefiledJds = refiled };
    }

    public async Task<RetirementResult> RetireAsync(CancellationToken ct = default)
    {
        var index = await _titleCodes.GetAsync(ct);
        var (plan, _) = await PlanAsync(index, ct);

        // Refile first: a JD exported under a retired code belongs to the successor, exactly as the
        // load-time remap would have filed it, and the original code is kept so the move is auditable.
        var fromCodes = index.AllSupersessions().Select(s => s.FromCode).ToList();
        var jds = await _db.JobDescriptions.Where(j => fromCodes.Contains(j.UcJobCode)).ToListAsync(ct);
        foreach (var jd in jds)
        {
            var sup = index.SupersededBy(jd.UcJobCode)!;
            jd.OriginalUcJobCode ??= jd.UcJobCode;
            jd.UcJobCode = sup.ToCode;
            jd.UcJobTitle = sup.ToTitle;
        }

        var slugs = plan.Select(p => p.Slug).ToList();
        var profiles = await _db.ClassProfiles.Where(p => slugs.Contains(p.Slug)).ToDictionaryAsync(p => p.Slug, ct);
        var successorIds = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in plan)
        {
            var profile = profiles[item.Slug];
            var authored = await _db.AuthoredJds.Where(a => a.ClassProfileId == profile.Id).ToListAsync(ct);

            switch (item.Action)
            {
                case RetireAction.Merge:
                    var successorId = await SuccessorIdAsync(item.SuccessorSlug!, successorIds, ct);
                    foreach (var a in authored)
                    {
                        a.ClassProfileId = successorId;
                        a.UcJobCode = item.SuccessorCode;
                    }

                    _db.ClassProfiles.Remove(profile);
                    break;

                case RetireAction.Reidentify:
                    var wasStandard = profile.EnvelopeSource == EnvelopeSource.Standard;
                    profile.Slug = item.SuccessorSlug!;
                    profile.Title = item.SuccessorTitle;
                    profile.UcJobCode = item.SuccessorCode;
                    if (wasStandard)
                    {
                        // Kept only because saved JDs hang off it; its envelope came from the
                        // retired class's standard, and saying so is better than passing it off as
                        // the successor's.
                        profile.GeneratedNote =
                            $"Built from the standard for {item.Title} ({item.Code}) before it was superseded by "
                            + $"{item.SuccessorCode}. Ingest JDs for this class to learn the real envelope.";
                    }

                    foreach (var a in authored)
                    {
                        a.UcJobCode = item.SuccessorCode;
                    }

                    successorIds[profile.Slug] = profile.Id;
                    break;

                case RetireAction.Remove:
                    _db.ClassProfiles.Remove(profile);
                    break;
            }
        }

        // The stored map is a snapshot of the derivation. Rewritten here because widening the
        // derivation changes it without any title row changing, which is the only time the CLI
        // would otherwise refresh it.
        _db.Supersessions.RemoveRange(await _db.Supersessions.ToListAsync(ct));
        _db.Supersessions.AddRange(index.AllSupersessions().Select(s => new Supersession
        {
            FromCode = s.FromCode,
            FromTitle = s.FromTitle,
            ToCode = s.ToCode,
            ToTitle = s.ToTitle,
        }));

        // One save, so a failure part-way leaves nothing half-retired.
        await _db.SaveChangesAsync(ct);
        return new RetirementResult { Profiles = plan, RefiledJds = jds.Count };
    }

    private async Task<int> SuccessorIdAsync(string slug, Dictionary<string, int> known, CancellationToken ct)
    {
        if (known.TryGetValue(slug, out var id))
        {
            return id;
        }

        return await _db.ClassProfiles.Where(p => p.Slug == slug).Select(p => p.Id).SingleAsync(ct);
    }

    private async Task<(List<SupersededProfile> Plan, int Refiled)> PlanAsync(
        TitleCodeIndex index, CancellationToken ct)
    {
        var profiles = await _db.ClassProfiles.AsNoTracking()
            .Select(p => new { p.Id, p.Slug, p.Title, p.UcJobCode, p.CorpusSize, p.EnvelopeSource })
            .OrderBy(p => p.Slug)
            .ToListAsync(ct);

        var fromCodes = index.AllSupersessions().Select(s => s.FromCode).ToList();
        var jdCounts = await _db.JobDescriptions.AsNoTracking()
            .Where(j => fromCodes.Contains(j.UcJobCode))
            .GroupBy(j => j.UcJobCode)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => TitleCodeIndex.Pad(x.Code), x => x.Count, ct);

        var standards = await _standards.GetIndexAsync(ct);

        // The live profile for each code. Updated as the plan re-identifies profiles, so two retired
        // profiles sharing a successor end as one class rather than colliding on its slug.
        var liveByCode = profiles
            .Where(p => !index.IsSuperseded(p.UcJobCode))
            .GroupBy(p => TitleCodeIndex.Pad(p.UcJobCode))
            .ToDictionary(g => g.Key, g => g.First().Slug, StringComparer.Ordinal);

        var plan = new List<SupersededProfile>();
        foreach (var p in profiles.Where(p => index.IsSuperseded(p.UcJobCode)))
        {
            var sup = index.SupersededBy(p.UcJobCode)!;
            var to = TitleCodeIndex.Pad(sup.ToCode);
            jdCounts.TryGetValue(TitleCodeIndex.Pad(p.UcJobCode), out var corpusJds);
            var authored = await _db.AuthoredJds.CountAsync(a => a.ClassProfileId == p.Id, ct);

            var item = new SupersededProfile
            {
                Slug = p.Slug,
                Title = p.Title,
                Code = p.UcJobCode,
                SuccessorCode = sup.ToCode,
                SuccessorTitle = SuccessorTitle(sup, p.EnvelopeSource, standards, index),
                CorpusJds = corpusJds,
                AuthoredJds = authored,
            };

            if (liveByCode.TryGetValue(to, out var successorSlug))
            {
                item.Action = RetireAction.Merge;
                item.SuccessorSlug = successorSlug;
                item.NeedsRebuild = corpusJds > 0;
            }
            else if (p.CorpusSize > 0 || corpusJds > 0 || authored > 0)
            {
                item.Action = RetireAction.Reidentify;
                item.SuccessorSlug = p.EnvelopeSource == EnvelopeSource.Standard && p.CorpusSize == 0
                    ? Bootstrapper.SlugFor(sup.ToCode, item.SuccessorTitle)
                    // The slug a corpus ingest of the successor derives, so a later rebuild of the
                    // class lands on this row instead of creating a second one.
                    : IngestPipeline.SlugForClass(sup.ToCode, sup.ToTitle);
                liveByCode[to] = item.SuccessorSlug;
            }
            else
            {
                item.Action = RetireAction.Remove;
            }

            plan.Add(item);
        }

        return (plan, jdCounts.Values.Sum());
    }

    /// <summary>
    /// The successor's display title, in the same spelling the class would have had if it were
    /// built fresh: the official standard's title for a standard-derived class, the titleized
    /// reference title (as corpus ingest writes it) otherwise.
    /// </summary>
    private static string SuccessorTitle(
        Supersession sup, EnvelopeSource? source, StandardsIndex standards, TitleCodeIndex index)
    {
        if (source == EnvelopeSource.Standard)
        {
            var std = standards.All.FirstOrDefault(s =>
            {
                var code = index.FindTitleCode(s.LongTitle)?.Code;
                return code != null && TitleCodeIndex.Pad(code) == TitleCodeIndex.Pad(sup.ToCode);
            });
            if (std is not null)
            {
                return std.LongTitle;
            }
        }

        return ProfileAggregator.Titleize(sup.ToTitle);
    }
}

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Core.Package;

/// <summary>What an import does with one kind of record.</summary>
public sealed class PlanSection
{
    public string Name { get; set; } = "";

    /// <summary>In the package, not here.</summary>
    public int Added { get; set; }

    /// <summary>Changed more recently in the package than here: the package's version replaces this one.</summary>
    public int Updated { get; set; }

    /// <summary>The same in both.</summary>
    public int Same { get; set; }

    /// <summary>Changed more recently here (or no newer in the package): this one is kept.</summary>
    public int KeptHere { get; set; }

    /// <summary>Can't be brought in here, with why.</summary>
    public int Skipped { get; set; }

    /// <summary>Here, not in the package. Never removed by an import: listed, to deal with by hand.</summary>
    public int OnlyHere { get; set; }

    public List<string> UpdatedItems { get; set; } = [];
    public List<string> KeptHereItems { get; set; } = [];
    public List<string> SkippedItems { get; set; } = [];
    public List<string> OnlyHereItems { get; set; } = [];

    /// <summary>Names listed per kind; the counts are always whole.</summary>
    public const int ListCap = 200;

    internal void Note(List<string> list, string item)
    {
        if (list.Count < ListCap)
        {
            list.Add(item);
        }
    }
}

/// <summary>What an import of a package would do, or did.</summary>
public sealed class ImportPlan
{
    public DateTimeOffset ExportedAt { get; set; }
    public bool Scrubbed { get; set; }
    public PackageCounts Counts { get; set; } = new();
    public List<PlanSection> Sections { get; set; } = [];

    /// <summary>False for a preview; true once written.</summary>
    public bool Applied { get; set; }
}

/// <summary>A package that can't be imported, and why.</summary>
public sealed class PackageRefusedException(string message) : Exception(message);

/// <summary>
/// Brings an environment package in. Records are matched by keys that mean the same thing in every
/// environment — a code, a standard's title, a JD's name, a class's slug — and:
///
/// - what's missing here is added;
/// - what both have, the newer change wins, by the stamps each record carries (a class's last
///   change, a decision's date); the same content in both is left alone;
/// - nothing here is ever removed for being absent from the package — it is listed instead.
///   The one exception is the rule the app already follows everywhere: a newer copy of the same
///   position's JD replaces the older one.
///
/// A class that is replaced keeps its row, because authors' saved JDs reference it; everything under
/// it is swapped. All of it is one transaction: a failure part-way writes nothing.
/// </summary>
public sealed class PackageImporter
{
    /// <summary>Generous bounds on a real package, checked before anything is stored.</summary>
    private const long MaxJsonBytes = 1_000_000_000;

    private readonly AppDbContext _db;
    private readonly EnvironmentPackages _packages;
    private readonly ITitleCodeService _titleCodes;
    private readonly IStandardsStore _standards;
    private readonly PackageOptions _options;

    public PackageImporter(
        AppDbContext db, EnvironmentPackages packages, ITitleCodeService titleCodes, IStandardsStore standards, PackageOptions options)
    {
        _db = db;
        _packages = packages;
        _titleCodes = titleCodes;
        _standards = standards;
        _options = options;
    }

    /// <summary>Read a package file: gzip-compressed JSON, bounded, and checked for format.</summary>
    public async Task<EnvironmentPackage> ReadAsync(Stream file, CancellationToken ct = default)
    {
        EnvironmentPackage? package;
        try
        {
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            await using var bounded = new BoundedStream(gzip, MaxJsonBytes);
            package = await JsonSerializer.DeserializeAsync<EnvironmentPackage>(
                bounded, PackageJson.Options(_db.Model, EnvironmentPackage.Types), ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or NotSupportedException)
        {
            throw new PackageRefusedException("This isn't a JDWriter package file (an exported .jdwpkg.json.gz).");
        }
        catch (PackageTooLargeException)
        {
            throw new PackageRefusedException("This file is larger than any real package; it was not read.");
        }

        if (package is null || package.Format != EnvironmentPackage.FormatName)
        {
            throw new PackageRefusedException("This isn't a JDWriter package file (an exported .jdwpkg.json.gz).");
        }

        if (package.Version != 1)
        {
            throw new PackageRefusedException($"This package is version {package.Version}; this JDWriter reads version 1. Update it first.");
        }

        // Real JDs go to prod only; scrubbed stand-ins never mix into the real corpus.
        if (!package.Scrubbed && !_options.AcceptsRealData)
        {
            throw new PackageRefusedException(
                "This package holds real job descriptions, and this environment takes only scrubbed packages. Export it with “Export scrubbed (for test)”.");
        }

        if (package.Scrubbed && !_options.AcceptsScrubbed)
        {
            throw new PackageRefusedException(
                "This package is scrubbed for testing, and this environment holds real job descriptions. Export it without scrubbing.");
        }

        return package;
    }

    /// <summary>What importing the package would do. Writes nothing.</summary>
    public async Task<ImportPlan> PreviewAsync(EnvironmentPackage incoming, CancellationToken ct = default)
    {
        try
        {
            var work = await PlanAsync(incoming, ct);
            return work.Plan;
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>Import the package, as <see cref="PreviewAsync"/> described, in one transaction.</summary>
    public async Task<ImportPlan> ApplyAsync(EnvironmentPackage incoming, CancellationToken ct = default)
    {
        try
        {
            var work = await PlanAsync(incoming, ct);
            // One transaction, never retried: the changes are not idempotent, and a failure part-way
            // must leave nothing written.
            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
            using (_db.KeepingChangeStamps())
            {
                _db.ChangeTracker.AutoDetectChangesEnabled = true;
                // Removals first, in their own save: a class's envelope and coverage are one-to-one on
                // the class, and a replacement inserted alongside the row it replaces would collide on
                // that key.
                work.RemoveReplaced();
                await _db.SaveChangesAsync(ct);
                work.AddAndUpdate();
                await _db.SaveChangesAsync(ct);
                await RefreshSupersessionsAsync(ct);
            }

            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }

            work.Plan.Applied = true;
            return work.Plan;
        }
        finally
        {
            _db.ChangeTracker.Clear();
            _db.ChangeTracker.AutoDetectChangesEnabled = true;
            _titleCodes.Invalidate();
            _standards.Invalidate();
        }
    }

    /// <summary>The supersession table is a snapshot of what the title index derives; rewritten after codes change.</summary>
    private async Task RefreshSupersessionsAsync(CancellationToken ct)
    {
        _titleCodes.Invalidate();
        var index = await _titleCodes.GetAsync(ct);
        _db.Supersessions.RemoveRange(await _db.Supersessions.ToListAsync(ct));
        _db.Supersessions.AddRange(index.AllSupersessions().Select(s => new Supersession
        {
            FromCode = s.FromCode,
            FromTitle = s.FromTitle,
            ToCode = s.ToCode,
            ToTitle = s.ToTitle,
        }));
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Work> PlanAsync(EnvironmentPackage incoming, CancellationToken ct)
    {
        // Everything here, tracked, so the plan can compare whole records and the apply can change them.
        // From a clean tracker: a record already tracked would keep its stale values over what's stored.
        _db.ChangeTracker.Clear();
        var here = await _packages.BuildAsync(ct);
        var work = new Work(_db, incoming, here);
        work.Plan.ExportedAt = incoming.ExportedAt;
        work.Plan.Scrubbed = incoming.Scrubbed;
        work.Plan.Counts = PackageCounts.Of(incoming);

        work.PlanCodes();
        work.PlanActiveCodes();
        work.PlanStandards();
        work.PlanMatches();
        work.PlanRules();
        work.PlanOriginals();
        work.PlanJobDescriptions(await _db.AuthoredJds.AsNoTracking().Select(a => new { a.Id, a.CorpusKey }).ToDictionaryAsync(a => a.CorpusKey, a => a.Id, ct));
        work.PlanClasses();
        work.PlanFitReviews();
        work.PlanCtListing();
        return work;
    }

    /// <summary>One import: the plan, and the changes it stands for.</summary>
    private sealed class Work(AppDbContext db, EnvironmentPackage incoming, EnvironmentPackage here)
    {
        public ImportPlan Plan { get; } = new();

        private readonly List<object> _remove = [];
        private readonly List<Action> _apply = [];
        private readonly JsonSerializerOptions _json = PackageJson.Options(db.Model, EnvironmentPackage.Types);

        private PlanSection Section(string name)
        {
            var s = new PlanSection { Name = name };
            Plan.Sections.Add(s);
            return s;
        }

        /// <summary>A record's content, without the stamps that say when it changed.</summary>
        private string Content(object entity)
        {
            var node = JsonSerializer.SerializeToNode(entity, entity.GetType(), _json)!;
            Strip(node);
            return node.ToJsonString();

            static void Strip(JsonNode? n)
            {
                switch (n)
                {
                    case JsonObject o:
                        o.Remove("updatedAt");
                        o.Remove("decidedAt");
                        o.Remove("reviewedAt");
                        o.Remove("importedAt");
                        foreach (var kv in o.ToList())
                        {
                            Strip(kv.Value);
                        }

                        break;
                    case JsonArray a:
                        foreach (var item in a)
                        {
                            Strip(item);
                        }

                        break;
                }
            }
        }

        private enum Verdict { Add, Update, Same, KeepHere }

        /// <summary>Newer wins; no stamp counts as oldest; the same content is left alone.</summary>
        private Verdict Decide(object? mine, DateTimeOffset? mineAt, object theirs, DateTimeOffset? theirsAt)
        {
            if (mine is null)
            {
                return Verdict.Add;
            }

            if (Content(mine) == Content(theirs))
            {
                return Verdict.Same;
            }

            return theirsAt is not null && (mineAt is null || theirsAt > mineAt) ? Verdict.Update : Verdict.KeepHere;
        }

        private static void Count(PlanSection s, Verdict v, string name)
        {
            switch (v)
            {
                case Verdict.Add: s.Added++; break;
                case Verdict.Same: s.Same++; break;
                case Verdict.Update: s.Updated++; s.Note(s.UpdatedItems, name); break;
                case Verdict.KeepHere: s.KeptHere++; s.Note(s.KeptHereItems, name); break;
            }
        }

        private static void OnlyHere<T>(PlanSection s, IEnumerable<T> mine, IEnumerable<T> theirs, Func<T, string> key, Func<T, string> name)
        {
            var keys = theirs.Select(key).ToHashSet(StringComparer.Ordinal);
            foreach (var m in mine.Where(m => !keys.Contains(key(m))))
            {
                s.OnlyHere++;
                s.Note(s.OnlyHereItems, name(m));
            }
        }

        // ------------------------------------------------------------ codes

        public void PlanCodes()
        {
            // The title reference has no stamps and is replaced wholesale by its own load: rows missing
            // here are added, and existing rows are never changed.
            var s = Section("Job codes");
            static string Key(TitleCode t) => $"{TitleCodeIndex.Pad(t.Code)}|{t.Title}|{t.Source}";
            var mine = here.TitleCodes.Select(Key).ToHashSet(StringComparer.Ordinal);
            foreach (var t in incoming.TitleCodes.DistinctBy(Key))
            {
                if (mine.Contains(Key(t)))
                {
                    s.Same++;
                    continue;
                }

                s.Added++;
                _apply.Add(() => db.TitleCodes.Add(t));
            }
        }

        public void PlanActiveCodes()
        {
            var s = Section("Active at UC Davis (set by hand)");
            var mine = here.ActiveCodes.ToDictionary(o => TitleCodeIndex.Pad(o.UcJobCode), StringComparer.Ordinal);
            foreach (var o in incoming.ActiveCodes.DistinctBy(o => TitleCodeIndex.Pad(o.UcJobCode)))
            {
                var m = mine.GetValueOrDefault(TitleCodeIndex.Pad(o.UcJobCode));
                var v = Decide(m, m?.DecidedAt, o, o.DecidedAt);
                Count(s, v, $"{o.UcJobCode}: {(o.IsActive ? "active" : "not active")}");
                if (v == Verdict.Add)
                {
                    _apply.Add(() => db.CodeActiveOverrides.Add(o));
                }
                else if (v == Verdict.Update)
                {
                    _apply.Add(() => { m!.IsActive = o.IsActive; m.DecidedAt = o.DecidedAt; });
                }
            }

            OnlyHere(s, here.ActiveCodes, incoming.ActiveCodes, o => TitleCodeIndex.Pad(o.UcJobCode), o => o.UcJobCode);
        }

        // ------------------------------------------------------------ standards

        public void PlanStandards()
        {
            var s = Section("Standards");
            var mine = here.Standards.ToDictionary(x => x.LongTitle, StringComparer.Ordinal);
            foreach (var t in incoming.Standards.DistinctBy(x => x.LongTitle))
            {
                var m = mine.GetValueOrDefault(t.LongTitle);
                var v = Decide(m, m?.UpdatedAt, t, t.UpdatedAt);
                Count(s, v, t.LongTitle);
                if (v == Verdict.Update)
                {
                    _remove.Add(m!);
                }

                if (v is Verdict.Add or Verdict.Update)
                {
                    _apply.Add(() => db.JobStandards.Add(t));
                }
            }

            OnlyHere(s, here.Standards, incoming.Standards, x => x.LongTitle, x => x.LongTitle);
        }

        public void PlanMatches()
        {
            var s = Section("Standard–code matches");
            static string Key(StandardMatchDecision d) => $"{d.StandardTitle}|{TitleCodeIndex.Pad(d.UcJobCode)}";
            var mine = here.StandardMatches.ToDictionary(Key, StringComparer.Ordinal);
            // One match per standard: the newest match for a title, wherever it was made.
            var newestMatch = here.StandardMatches.Where(d => d.IsMatch)
                .GroupBy(d => d.StandardTitle, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.MaxBy(d => d.DecidedAt)!, StringComparer.Ordinal);

            foreach (var d in incoming.StandardMatches.DistinctBy(Key))
            {
                var m = mine.GetValueOrDefault(Key(d));
                var v = Decide(m, m?.DecidedAt, d, d.DecidedAt);
                var name = $"{d.StandardTitle} → {d.UcJobCode} ({(d.IsMatch ? "match" : "not a match")})";
                if (d.IsMatch && v is Verdict.Add or Verdict.Update
                    && newestMatch.GetValueOrDefault(d.StandardTitle) is { } other
                    && TitleCodeIndex.Pad(other.UcJobCode) != TitleCodeIndex.Pad(d.UcJobCode))
                {
                    if (other.DecidedAt >= d.DecidedAt)
                    {
                        v = Verdict.KeepHere;
                        name += $" — matched to {other.UcJobCode} more recently here";
                    }
                    else
                    {
                        // The package's match is newer: the standard's match here becomes a rejection.
                        _apply.Add(() => { other.IsMatch = false; other.DecidedAt = d.DecidedAt; });
                    }
                }

                Count(s, v, name);
                if (v == Verdict.Add)
                {
                    _apply.Add(() => db.StandardMatchDecisions.Add(d));
                }
                else if (v == Verdict.Update)
                {
                    _apply.Add(() => { m!.IsMatch = d.IsMatch; m.DecidedAt = d.DecidedAt; });
                }
            }

            OnlyHere(s, here.StandardMatches, incoming.StandardMatches, Key, d => $"{d.StandardTitle} → {d.UcJobCode}");
        }

        public void PlanRules()
        {
            var s = Section("Envelope rules");
            var mine = here.ComplianceRules.ToDictionary(r => r.Key, StringComparer.Ordinal);
            foreach (var r in incoming.ComplianceRules.DistinctBy(r => r.Key))
            {
                var m = mine.GetValueOrDefault(r.Key);
                var v = Decide(m, m?.UpdatedAt, r, r.UpdatedAt);
                Count(s, v, r.Key);
                if (v == Verdict.Add)
                {
                    _apply.Add(() => db.ComplianceRules.Add(r));
                }
                else if (v == Verdict.Update)
                {
                    _apply.Add(() => { r.Id = m!.Id; db.Entry(m).CurrentValues.SetValues(r); });
                }
            }

            OnlyHere(s, here.ComplianceRules, incoming.ComplianceRules, r => r.Key, r => r.Key);
        }

        // ------------------------------------------------------------ the corpus

        public void PlanOriginals()
        {
            // Content-addressed: the same hash is the same original. Never changed.
            var s = Section("Originals");
            var mine = here.Originals.Select(u => u.Sha256).ToHashSet(StringComparer.Ordinal);
            foreach (var u in incoming.Originals.DistinctBy(u => u.Sha256))
            {
                if (mine.Contains(u.Sha256))
                {
                    s.Same++;
                    continue;
                }

                s.Added++;
                _apply.Add(() => db.CorpusUploads.Add(u));
            }
        }

        public void PlanJobDescriptions(Dictionary<Guid, int> authoredByKey)
        {
            var s = Section("Job descriptions");
            var mine = here.JobDescriptions.ToDictionary(j => j.SourceFile, StringComparer.Ordinal);
            static string Position(JobDescription j) =>
                j.UcPathPositionNumber.Length == 0 ? "" : $"{j.Origin}|{TitleCodeIndex.Pad(j.UcJobCode)}|{j.UcPathPositionNumber}";
            var byPosition = here.JobDescriptions.Where(j => Position(j).Length > 0)
                .GroupBy(Position, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var removed = new HashSet<JobDescription>(ReferenceEqualityComparer.Instance);

            foreach (var j in incoming.JobDescriptions.DistinctBy(j => j.SourceFile))
            {
                var m = mine.GetValueOrDefault(j.SourceFile);
                var v = Decide(m, m?.AddedAt, j, j.AddedAt);
                var name = $"{j.SourceFile} ({j.UcJobTitle})";

                // A newer JD of the same position replaces the older one, as in the app.
                var samePosition = Position(j).Length == 0 || m is not null
                    ? []
                    : byPosition.GetValueOrDefault(Position(j), []).Where(x => !removed.Contains(x)).ToList();
                if (v == Verdict.Add && samePosition.Count > 0)
                {
                    if (samePosition.Any(x => x.AddedAt is null || (j.AddedAt is not null && x.AddedAt >= j.AddedAt)))
                    {
                        v = Verdict.KeepHere;
                        name += " — a newer JD for the same position is here";
                    }
                    else
                    {
                        foreach (var x in samePosition)
                        {
                            removed.Add(x);
                            _remove.Add(x);
                            s.Note(s.UpdatedItems, $"{x.SourceFile} replaced by a newer JD for the same position");
                        }
                    }
                }

                Count(s, v, name);
                if (v == Verdict.Update)
                {
                    removed.Add(m!);
                    _remove.Add(m!);
                }

                if (v is Verdict.Add or Verdict.Update)
                {
                    // An authored JD stays linked to its draft where the draft lives, so re-finishing
                    // it there replaces this copy rather than adding a second.
                    j.AuthoredJdId = m?.AuthoredJdId
                                     ?? (j.SourceFile.StartsWith("authored/", StringComparison.Ordinal)
                                         && Guid.TryParse(j.SourceFile["authored/".Length..], out var key)
                                         && authoredByKey.TryGetValue(key, out var draft)
                                             ? draft
                                             : null);
                    _apply.Add(() => db.JobDescriptions.Add(j));
                }
            }

            OnlyHere(s, here.JobDescriptions.Where(j => !removed.Contains(j)), incoming.JobDescriptions, j => j.SourceFile,
                j => $"{j.SourceFile} ({j.UcJobTitle})");
        }

        // ------------------------------------------------------------ classes

        public void PlanClasses()
        {
            var s = Section("Classes");
            var bySlug = here.Classes.ToDictionary(p => p.Slug, StringComparer.Ordinal);
            var byCode = here.Classes.GroupBy(p => TitleCodeIndex.Pad(p.UcJobCode))
                .Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
            var index = ReceivingIndex();
            var matched = new HashSet<ClassProfile>(ReferenceEqualityComparer.Instance);

            foreach (var p in incoming.Classes.DistinctBy(p => p.Slug))
            {
                // The same class here: by slug, or else the one class filed under its code.
                var m = bySlug.GetValueOrDefault(p.Slug) ?? byCode.GetValueOrDefault(TitleCodeIndex.Pad(p.UcJobCode));
                if (m is not null)
                {
                    matched.Add(m);
                }

                // Every path that creates a class guards against a superseded code.
                if (m is null && index.SupersededBy(p.UcJobCode) is { } sup)
                {
                    s.Skipped++;
                    s.Note(s.SkippedItems, $"{p.Title} ({p.UcJobCode}) — superseded here by {sup.ToTitle} ({sup.ToCode})");
                    continue;
                }

                var v = Decide(m, m?.UpdatedAt, p, p.UpdatedAt);
                Count(s, v, $"{p.Title} ({p.UcJobCode})");
                if (v == Verdict.Add)
                {
                    _apply.Add(() => db.ClassProfiles.Add(p));
                }
                else if (v == Verdict.Update)
                {
                    ReplaceClass(m!, p);
                }
            }

            OnlyHere(s, here.Classes.Where(c => !matched.Contains(c)), incoming.Classes, c => c.Slug, c => $"{c.Title} ({c.UcJobCode})");
        }

        public void PlanCtListing()
        {
            var s = Section("CT title code listing");
            var mine = here.CtListing.ToDictionary(t => TitleCodeIndex.Pad(t.UcJobCode), StringComparer.Ordinal);
            foreach (var t in incoming.CtListing.DistinctBy(t => TitleCodeIndex.Pad(t.UcJobCode)))
            {
                var m = mine.GetValueOrDefault(TitleCodeIndex.Pad(t.UcJobCode));
                var v = Decide(m, m?.ImportedAt, t, t.ImportedAt);
                Count(s, v, $"{t.UcJobCode} {t.Title}");
                if (v == Verdict.Add)
                {
                    _apply.Add(() => db.CtTitles.Add(t));
                }
                else if (v == Verdict.Update)
                {
                    _apply.Add(() => { t.Id = m!.Id; db.Entry(m).CurrentValues.SetValues(t); });
                }
            }

            OnlyHere(s, here.CtListing, incoming.CtListing, t => TitleCodeIndex.Pad(t.UcJobCode), t => $"{t.UcJobCode} {t.Title}");
        }

        public void PlanFitReviews()
        {
            var s = Section("Fit reviews");
            var mine = here.FitReviews.ToDictionary(r => r.SourceFile, StringComparer.Ordinal);
            foreach (var r in incoming.FitReviews.DistinctBy(r => r.SourceFile))
            {
                var m = mine.GetValueOrDefault(r.SourceFile);
                var v = Decide(m, m?.ReviewedAt, r, r.ReviewedAt);
                Count(s, v, $"{r.SourceFile} ({r.UcJobCode})");
                if (v == Verdict.Add)
                {
                    _apply.Add(() => db.JdFitReviews.Add(r));
                }
                else if (v == Verdict.Update)
                {
                    _apply.Add(() =>
                    {
                        m!.UcJobCode = r.UcJobCode;
                        m.Note = r.Note;
                        m.ReviewedByName = r.ReviewedByName;
                        m.ReviewedByUserId = null;
                        m.ReviewedAt = r.ReviewedAt;
                    });
                }
            }

            OnlyHere(s, here.FitReviews, incoming.FitReviews, r => r.SourceFile, r => r.SourceFile);
        }

        /// <summary>
        /// The class row stays — authors' saved JDs reference it — and its slug with it; everything
        /// under it is swapped for the package's, and its fields take the package's values.
        /// </summary>
        private void ReplaceClass(ClassProfile mine, ClassProfile theirs)
        {
            var entry = db.Entry(mine);
            var children = entry.Navigations
                .Where(n => n.Metadata is Microsoft.EntityFrameworkCore.Metadata.INavigation { IsOnDependent: false } nav
                            && EnvironmentPackage.Types.Contains(nav.TargetEntityType.ClrType))
                .ToList();
            foreach (var nav in children)
            {
                switch (nav.CurrentValue)
                {
                    case System.Collections.IEnumerable items when nav.Metadata.IsCollection:
                        _remove.AddRange(items.Cast<object>());
                        break;
                    case { } single:
                        _remove.Add(single);
                        break;
                }
            }

            _apply.Add(() =>
            {
                theirs.Id = mine.Id;
                theirs.Slug = mine.Slug;
                entry.CurrentValues.SetValues(theirs);
                foreach (var nav in children)
                {
                    nav.CurrentValue = nav.Metadata.PropertyInfo!.GetValue(theirs);
                }
            });
        }

        /// <summary>The title index as it will be once the package's codes and Active calls are in.</summary>
        private TitleCodeIndex ReceivingIndex()
        {
            static string Key(TitleCode t) => $"{TitleCodeIndex.Pad(t.Code)}|{t.Title}|{t.Source}";
            var mineKeys = here.TitleCodes.Select(Key).ToHashSet(StringComparer.Ordinal);
            var rows = here.TitleCodes
                .Concat(incoming.TitleCodes.Where(t => !mineKeys.Contains(Key(t))))
                .Select(t => new TitleCode { Code = t.Code, Title = t.Title, Source = t.Source, TitleKey = t.TitleKey, TitleCodeKey = t.TitleCodeKey })
                .ToList();
            var active = here.ActiveCodes.ToDictionary(o => TitleCodeIndex.Pad(o.UcJobCode), StringComparer.Ordinal);
            var calls = active.ToDictionary(kv => kv.Key, kv => (kv.Value.IsActive, kv.Value.DecidedAt), StringComparer.Ordinal);
            foreach (var o in incoming.ActiveCodes)
            {
                var key = TitleCodeIndex.Pad(o.UcJobCode);
                if (!calls.TryGetValue(key, out var c) || o.DecidedAt > c.DecidedAt)
                {
                    calls[key] = (o.IsActive, o.DecidedAt);
                }
            }

            foreach (var r in rows)
            {
                r.ActiveOverride = calls.TryGetValue(TitleCodeIndex.Pad(r.Code), out var c) ? c.IsActive : null;
            }

            return new TitleCodeIndex(rows);
        }

        // ------------------------------------------------------------ apply

        public void RemoveReplaced()
        {
            foreach (var entity in _remove)
            {
                db.Remove(entity);
            }
        }

        public void AddAndUpdate()
        {
            foreach (var change in _apply)
            {
                change();
            }
        }
    }

    private sealed class PackageTooLargeException : Exception;

    /// <summary>Reads at most a set number of bytes: a small compressed file can't expand without limit.</summary>
    private sealed class BoundedStream(Stream inner, long max) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            Count(await inner.ReadAsync(buffer, ct));

        private int Count(int n)
        {
            _read += n;
            return _read > max ? throw new PackageTooLargeException() : n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

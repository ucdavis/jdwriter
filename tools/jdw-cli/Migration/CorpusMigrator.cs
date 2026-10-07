using System.Text.Json;
using Jdw.Cli.PocSource;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Microsoft.Extensions.Caching.Memory;
using Server.Core.Ingest;
using Server.Core.Profiles;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Jdw.Cli.Migration;

/// <summary>
/// Loads the POC's on-disk data into SQL.
///
/// Load order is dictated by foreign keys: title codes, then the supersessions derived from them,
/// then standards, then the JD corpus, then the class profiles computed from it.
///
/// Two things this does NOT do, both deliberate:
///
///   It does not import data/parsed/records.json. That cache is stale — 820 records across 11 job
///   codes, against 65 profiles — so the corpus is re-parsed from the HRTMS exports themselves.
///
///   It does not recompute anything on a class profile. The envelope, consolidation and coverage
///   blocks were produced by model calls that cost real money; they are migrated as they stand.
/// </summary>
public sealed class CorpusMigrator
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AppDbContext _db;
    private readonly string _pocRoot;
    private readonly bool _write;
    private readonly Action<string> _log;
    private readonly TestDataScrubber? _scrub;

    /// <param name="scrub">
    /// Replace identifying fields as the data is loaded — for the Azure test environment, never
    /// prod. Null loads the corpus exactly as exported.
    /// </param>
    public CorpusMigrator(
        AppDbContext db, string pocRoot, bool write, Action<string> log, TestDataScrubber? scrub = null)
    {
        _db = db;
        _pocRoot = pocRoot;
        _write = write;
        _log = log;
        _scrub = scrub;
    }

    private string DataPath(params string[] parts) =>
        Path.Combine(new[] { _pocRoot, "data" }.Concat(parts).ToArray());

    private string CorpusRoot => Path.Combine(_pocRoot, "Sample JDs");

    private static T LoadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Expected POC data file not found: {path}", path);
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
               ?? throw new InvalidOperationException($"{path} deserialized to null.");
    }

    public async Task<MigrationReport> RunAsync(CancellationToken ct = default)
    {
        var report = new MigrationReport { DryRun = !_write };

        // Bulk loading through the change tracker is pathologically slow once tens of thousands of
        // entities are attached: DetectChanges is quadratic in tracked entities. Off for the whole
        // run; every insert below is an explicit Add, so nothing relies on detection.
        _db.ChangeTracker.AutoDetectChangesEnabled = false;

        var index = await MigrateTitleCodesAsync(report, ct);
        await MigrateSupersessionsAsync(index, report, ct);
        await MigrateStandardsAsync(report, ct);
        await MigrateCorpusAsync(index, report, ct);
        await MigrateProfilesAsync(report, ct);
        await RetireSupersededProfilesAsync(index, report, ct);
        await MigrateComplianceRulesAsync(report, ct);

        return report;
    }

    // ---------------------------------------------------------------- title codes

    private async Task<TitleCodeIndex> MigrateTitleCodesAsync(MigrationReport report, CancellationToken ct)
    {
        var rows = LoadJson<List<PocTitleCode>>(DataPath("title-codes.json"));
        _log($"title-codes.json: {rows.Count} rows");

        var entities = rows.Select(r => new TitleCode
        {
            Code = r.Code,
            Title = r.Title,
            TitleKey = TitleNormalizer.TitleKey(r.Title),
            TitleCodeKey = TitleNormalizer.TitleCodeKey(r.Title),
            Grade = r.Grade,
            Function = r.Function,
            Family = r.Family,
            Source = r.Source,
        }).ToList();

        report.TitleCodes = entities.Count;

        if (_write)
        {
            // The reference is replace-wholesale by nature: it is regenerated from a workbook plus
            // payroll OCR, and rows carry no stable identity of their own (Code is deliberately
            // non-unique). Clearing first is what makes a re-run idempotent.
            await _db.TitleCodes.ExecuteDeleteAsync(ct);
            await AddInBatchesAsync(_db.TitleCodes, entities, ct);
        }

        // Build the index from the in-memory rows so a dry run derives supersessions too.
        return new TitleCodeIndex(entities);
    }

    private async Task MigrateSupersessionsAsync(
        TitleCodeIndex index, MigrationReport report, CancellationToken ct)
    {
        var derived = index.AllSupersessions();
        var ambiguous = index.AmbiguousSupersessions();

        report.Supersessions = derived.Count;
        report.AmbiguousSupersessions = ambiguous.Count;

        foreach (var a in ambiguous)
        {
            report.Problems.Add($"ambiguous supersession, not derived: {a}");
        }

        _log($"supersessions: {derived.Count} derived, {ambiguous.Count} ambiguous");

        if (!_write)
        {
            return;
        }

        await _db.Supersessions.ExecuteDeleteAsync(ct);
        await AddInBatchesAsync(_db.Supersessions, derived.Select(s => new Supersession
        {
            FromCode = s.FromCode,
            FromTitle = s.FromTitle,
            ToCode = s.ToCode,
            ToTitle = s.ToTitle,
        }).ToList(), ct);
    }

    // ---------------------------------------------------------------- standards

    private async Task MigrateStandardsAsync(MigrationReport report, CancellationToken ct)
    {
        var rows = LoadJson<List<PocStandard>>(DataPath("standards.json"));
        _log($"standards.json: {rows.Count} rows");

        var entities = new List<JobStandard>(rows.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in rows)
        {
            var strictKey = TitleNormalizer.TitleCodeKey(r.LongTitle);

            // The store dedups on the STRICT key. The loose key merges "Analyst 3 RP" into
            // "Analyst 3 RP GF" and silently discards around 25 real standards.
            if (!seen.Add(strictKey))
            {
                report.Problems.Add($"duplicate standard strict key, skipped: {r.LongTitle}");
                continue;
            }

            var std = new JobStandard
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
                TitleCodeKey = strictKey,
            };

            AddItems(std.Items, StandardItemKind.KeyResponsibility, r.KeyResponsibilities);
            AddItems(std.Items, StandardItemKind.Ksa, r.Ksa);
            AddItems(std.Items, StandardItemKind.Education, r.Education);
            AddItems(std.Items, StandardItemKind.License, r.Licenses);
            AddItems(std.Items, StandardItemKind.SpecialCondition, r.SpecialConditions);

            entities.Add(std);
        }

        report.Standards = entities.Count;
        report.StandardsWithCode = entities.Count(s => !string.IsNullOrEmpty(s.Code));
        report.StandardItems = entities.Sum(s => s.Items.Count);

        if (!_write)
        {
            return;
        }

        await _db.JobStandards.ExecuteDeleteAsync(ct);
        await AddInBatchesAsync(_db.JobStandards, entities, ct);
    }

    private static void AddItems(List<JobStandardItem> into, StandardItemKind kind, List<string> texts)
    {
        for (var i = 0; i < texts.Count; i++)
        {
            into.Add(new JobStandardItem { Kind = kind, Ordinal = i, Text = texts[i] });
        }
    }

    // ---------------------------------------------------------------- JD corpus

    private async Task MigrateCorpusAsync(
        TitleCodeIndex index, MigrationReport report, CancellationToken ct)
    {
        if (!Directory.Exists(CorpusRoot))
        {
            report.Problems.Add($"corpus directory not found, JD load skipped: {CorpusRoot}");
            return;
        }

        var files = Directory.EnumerateFiles(CorpusRoot, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                        || p.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetRelativePath(CorpusRoot, p).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        _log($"corpus: {files.Count} exports to parse");

        if (_write)
        {
            await _db.JobDescriptions.ExecuteDeleteAsync(ct);
        }

        var codes = new HashSet<string>(StringComparer.Ordinal);
        var batch = new List<JobDescription>(BatchSize);

        foreach (var rel in files)
        {
            HrtmsRecord parsed;
            try
            {
                parsed = HrtmsParser.Parse(File.ReadAllText(Path.Combine(CorpusRoot, rel)), rel);
            }
            catch (Exception ex)
            {
                report.Problems.Add($"parse failed for an export in {Path.GetDirectoryName(rel)}: {ex.Message}");
                continue;
            }

            var jd = MapJobDescription(parsed, index, report);
            _scrub?.Apply(jd);
            codes.Add(jd.UcJobCode);

            report.JobDescriptions++;
            report.JdResponsibilities += jd.Responsibilities.Count;
            report.JdDuties += jd.Responsibilities.Sum(r => r.Duties.Count);
            report.JdQualificationItems += jd.Qualifications.Count;
            report.JdPemEntries += jd.PemEntries.Count;

            if (!_write)
            {
                continue;
            }

            batch.Add(jd);
            if (batch.Count >= BatchSize)
            {
                await FlushAsync(_db.JobDescriptions, batch, ct);
            }
        }

        if (_write && batch.Count > 0)
        {
            await FlushAsync(_db.JobDescriptions, batch, ct);
        }

        report.DistinctJobCodes = codes.Count;
    }

    private JobDescription MapJobDescription(
        HrtmsRecord r, TitleCodeIndex index, MigrationReport report)
    {
        // Supersession is applied HERE rather than in the parser: the parser reports what the
        // export said, and remapping is an ingest decision. Rewriting the code means grouping,
        // slug, aggregation and profile identity all follow the live class, so a re-ingest cannot
        // resurrect a dead one.
        var resolved = index.ResolveCode(r.UcJobCode);
        if (!string.Equals(r.UcJobCode, resolved, StringComparison.Ordinal))
        {
            report.RemappedJobCodes++;
        }

        var jd = r.ToEntity(resolved);

        // Percentages are SUPPOSED to sum to 100 and almost always do. Six real exports do not, so
        // this is recorded and loaded rather than rejected — the schema carries no constraint for
        // exactly this reason.
        var stated = jd.Responsibilities.Where(x => x.Pct.HasValue).ToList();
        if (stated.Count > 0)
        {
            var sum = stated.Sum(x => x.Pct!.Value);
            if (sum != 100)
            {
                // Identified by class folder, not filename: HRTMS embeds the position number in
                // the name.
                var folder = Path.GetDirectoryName(r.SourceFile) ?? "(root)";
                report.PercentAnomalies.Add($"{folder}: sums to {sum}%");
            }
        }

        return jd;
    }

    // ---------------------------------------------------------------- superseded profiles

    /// <summary>
    /// The POC computed its profiles under an RP-only supersession map, so one can be filed under a
    /// code the wider map retires (FINANCIAL ANL 3, now FINANCIAL ANL 3 CX). Loading it as-is would
    /// be a create path with no supersession guard, so it is retired exactly as the admin screen
    /// does it. Write-only: it needs the profiles in the database to act on.
    /// </summary>
    private async Task RetireSupersededProfilesAsync(
        TitleCodeIndex index, MigrationReport report, CancellationToken ct)
    {
        if (!_write)
        {
            return;
        }

        // The bulk load left tens of thousands of entities tracked with detection off; retirement
        // edits tracked rows and relies on detection to save them.
        _db.ChangeTracker.Clear();
        _db.ChangeTracker.AutoDetectChangesEnabled = true;

        var reconciler = new SupersessionReconciler(
            _db, new FixedTitleCodes(index), new StandardsStore(_db, new MemoryCache(new MemoryCacheOptions())));
        var result = await reconciler.RetireAsync(ct);

        report.RetiredProfiles = result.Profiles.Count;
        foreach (var p in result.Profiles)
        {
            _log($"retired {p.Slug} ({p.Code}) -> {p.Action} {p.SuccessorSlug ?? p.SuccessorCode}");
        }

        _db.ChangeTracker.AutoDetectChangesEnabled = false;
    }

    /// <summary>The index this run built, served without a database round trip or a cache.</summary>
    private sealed class FixedTitleCodes(TitleCodeIndex index) : ITitleCodeService
    {
        public Task<TitleCodeIndex> GetAsync(CancellationToken ct = default) => Task.FromResult(index);

        public void Invalidate()
        {
        }
    }

    // ---------------------------------------------------------------- class profiles

    private async Task MigrateProfilesAsync(MigrationReport report, CancellationToken ct)
    {
        var dir = DataPath("profiles");
        if (!Directory.Exists(dir))
        {
            report.Problems.Add($"profiles directory not found: {dir}");
            return;
        }

        var files = Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
        _log($"profiles: {files.Count} files");

        if (_write)
        {
            // Restrict, not cascade, protects authored JDs from a profile delete — so this throws
            // rather than destroying published work, which is the correct outcome.
            await _db.ClassProfiles.ExecuteDeleteAsync(ct);
        }

        var batch = new List<ClassProfile>(BatchSize);
        var seenSlugs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in files)
        {
            PocProfile src;
            try
            {
                src = LoadJson<PocProfile>(path);
            }
            catch (Exception ex)
            {
                report.Problems.Add($"profile load failed {Path.GetFileName(path)}: {ex.Message}");
                continue;
            }

            if (!seenSlugs.Add(src.Slug))
            {
                report.Problems.Add($"duplicate profile slug, skipped: {src.Slug}");
                continue;
            }

            var profile = MapProfile(src, report);
            report.Profiles++;

            if (profile.Envelope is not null)
            {
                report.ProfilesWithEnvelope++;
            }

            if (profile.Coverage is not null)
            {
                report.ProfilesWithCoverage++;
            }

            if (profile.ConsolidatedFunctions.Count > 0 || profile.ConsolidatedQuals.Count > 0)
            {
                report.ProfilesWithConsolidated++;
            }

            if (!_write)
            {
                continue;
            }

            batch.Add(profile);
            if (batch.Count >= BatchSize)
            {
                await FlushAsync(_db.ClassProfiles, batch, ct);
            }
        }

        if (_write && batch.Count > 0)
        {
            await FlushAsync(_db.ClassProfiles, batch, ct);
        }
    }

    private ClassProfile MapProfile(PocProfile src, MigrationReport report)
    {
        var profile = new ClassProfile
        {
            Slug = src.Slug,
            UcJobCode = src.UcJobCode,
            Title = src.Title,
            CtJobFamily = src.CtJobFamily,
            CtJobFunction = src.CtJobFunction,
            PersonnelProgram = src.PersonnelProgram,
            CorpusSize = src.CorpusSize,
            RepresentativeSummary = src.RepresentativeSummary,
            EnvelopeSource = ParseEnvelopeSource(src.EnvelopeSource, src.Slug, report),
            GeneratedNote = src.GeneratedNote,
        };

        for (var i = 0; i < src.SourceFiles.Count; i++)
        {
            profile.SourceFiles.Add(new ProfileSourceFile
            {
                Ordinal = i,
                SourceFile = _scrub?.SourceFile(src.SourceFiles[i]) ?? src.SourceFiles[i],
            });
        }

        AddDistribution(profile, DistributionField.SalaryGrade, src.SalaryGrade);
        AddDistribution(profile, DistributionField.FlsaStatus, src.FlsaStatus);
        AddDistribution(profile, DistributionField.UnionCode, src.UnionCode);
        AddDistribution(profile, DistributionField.Supervises, src.Supervises);
        AddDistribution(profile, DistributionField.Leads, src.Leads);
        AddDistribution(profile, DistributionField.WorksOutdoorsOver50pct, src.WorksOutdoorsOver50pct);

        for (var i = 0; i < src.Functions.Count; i++)
        {
            var f = src.Functions[i];
            var fn = new ProfileFunction
            {
                Ordinal = i,
                Name = f.Name,
                PctMin = f.Pct.Min,
                PctMax = f.Pct.Max,
                PctMean = f.Pct.Mean,
                PctN = f.Pct.N,
                Prevalence = f.Prevalence,
            };

            for (var d = 0; d < f.SampleDuties.Count; d++)
            {
                fn.SampleDuties.Add(new ProfileFunctionSampleDuty { Ordinal = d, Text = f.SampleDuties[d] });
            }

            profile.Functions.Add(fn);
        }

        AddQualItems(profile, ProfileQualKind.License, src.Licenses);
        AddQualItems(profile, ProfileQualKind.Education, src.Education);
        AddQualItems(profile, ProfileQualKind.MinExperience, src.MinExperience);
        AddQualItems(profile, ProfileQualKind.KsaMin, src.KsaMin);
        AddQualItems(profile, ProfileQualKind.KsaPref, src.KsaPref);
        AddQualItems(profile, ProfileQualKind.WorkEnvironment, src.WorkEnvironment);

        if (src.Envelope is not null)
        {
            profile.Envelope = MapEnvelope(src.Envelope);
        }

        if (src.Consolidated is not null)
        {
            MapConsolidated(profile, src.Consolidated);
        }

        if (src.Coverage is not null)
        {
            profile.Coverage = MapCoverage(src.Coverage);
        }

        return profile;
    }

    private static EnvelopeSource? ParseEnvelopeSource(string? raw, string slug, MigrationReport report)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (Enum.TryParse<EnvelopeSource>(raw, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        report.Problems.Add($"unknown envelopeSource '{raw}' on profile {slug}");
        return null;
    }

    private static void AddDistribution(ClassProfile profile, DistributionField field, PocDistribution src)
    {
        var dist = new ProfileDistribution
        {
            Field = field,
            Consensus = src.Consensus,
            Agreement = src.Agreement,
        };

        for (var i = 0; i < src.Values.Count; i++)
        {
            // Value stays nullable: "the export did not say" is a real observation for the
            // tri-state fields and dropping it would skew every consensus computed from this.
            dist.Values.Add(new ProfileDistributionValue
            {
                Ordinal = i,
                Value = src.Values[i].Value,
                Count = src.Values[i].Count,
            });
        }

        profile.Distributions.Add(dist);
    }

    private static void AddQualItems(ClassProfile profile, ProfileQualKind kind, List<PocFreqText> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            profile.Qualifications.Add(new ProfileQualItem
            {
                Kind = kind,
                Ordinal = i,
                Text = items[i].Text,
                Freq = items[i].Freq,
            });
        }
    }

    private static JobEnvelope MapEnvelope(PocEnvelope src)
    {
        var env = new JobEnvelope
        {
            Summary = src.Summary,
            ScopeStatement = src.ScopeStatement,
        };

        for (var i = 0; i < src.KeyResponsibilities.Count; i++)
        {
            var kr = src.KeyResponsibilities[i];
            var resp = new EnvelopeResponsibility
            {
                Ordinal = i,
                FunctionName = kr.FunctionName,
                PctTime = kr.PctTime,
            };

            for (var d = 0; d < kr.Duties.Count; d++)
            {
                resp.Duties.Add(new EnvelopeDuty { Ordinal = d, Text = kr.Duties[d] });
            }

            env.KeyResponsibilities.Add(resp);
        }

        AddEnvelopeItems(env, EnvelopeListKind.RequiredCertification, src.RequiredCertifications);
        AddEnvelopeItems(env, EnvelopeListKind.Education, src.Education);
        AddEnvelopeItems(env, EnvelopeListKind.WorkExperience, src.WorkExperience);
        AddEnvelopeItems(env, EnvelopeListKind.MinQualification, src.MinQualifications);
        AddEnvelopeItems(env, EnvelopeListKind.PrefQualification, src.PrefQualifications);
        AddEnvelopeItems(env, EnvelopeListKind.ConditionOfEmployment, src.ConditionsOfEmployment);
        AddEnvelopeItems(env, EnvelopeListKind.WorkEnvironment, src.WorkEnvironment);
        AddEnvelopeItems(env, EnvelopeListKind.PhysicalRequirement, src.PhysicalRequirements);
        AddEnvelopeItems(env, EnvelopeListKind.OutOfEnvelope, src.OutOfEnvelope);

        return env;
    }

    private static void AddEnvelopeItems(JobEnvelope env, EnvelopeListKind kind, List<string> texts)
    {
        for (var i = 0; i < texts.Count; i++)
        {
            env.Items.Add(new EnvelopeListItem { Kind = kind, Ordinal = i, Text = texts[i] });
        }
    }

    private static void MapConsolidated(ClassProfile profile, PocConsolidated src)
    {
        for (var i = 0; i < src.FunctionGroups.Count; i++)
        {
            var g = src.FunctionGroups[i];
            var fn = new ConsolidatedFunction
            {
                Ordinal = i,
                Name = g.Name,
                MeanPct = g.MeanPct,
                TemplatePct = g.TemplatePct,
                MinPct = g.MinPct,
                MaxPct = g.MaxPct,
                Prevalence = g.Prevalence,
            };

            for (var m = 0; m < g.Members.Count; m++)
            {
                fn.Members.Add(new ConsolidatedFunctionMember { Ordinal = m, Text = g.Members[m] });
            }

            for (var d = 0; d < g.SampleDuties.Count; d++)
            {
                fn.SampleDuties.Add(new ConsolidatedFunctionSampleDuty { Ordinal = d, Text = g.SampleDuties[d] });
            }

            profile.ConsolidatedFunctions.Add(fn);
        }

        AddConsolidatedQuals(profile, ConsolidatedQualKind.Certification, src.Certifications);
        AddConsolidatedQuals(profile, ConsolidatedQualKind.MinQualification, src.MinQualifications);

        for (var i = 0; i < src.DroppedFunctions.Count; i++)
        {
            profile.DroppedItems.Add(new ProfileDroppedItem
            {
                Kind = DroppedItemKind.Function,
                Ordinal = i,
                Text = src.DroppedFunctions[i],
            });
        }

        for (var i = 0; i < src.DroppedQualifications.Count; i++)
        {
            profile.DroppedItems.Add(new ProfileDroppedItem
            {
                Kind = DroppedItemKind.Qualification,
                Ordinal = i,
                Text = src.DroppedQualifications[i],
            });
        }
    }

    private static void AddConsolidatedQuals(
        ClassProfile profile, ConsolidatedQualKind kind, List<PocConsolidatedQual> quals)
    {
        for (var i = 0; i < quals.Count; i++)
        {
            var q = new ConsolidatedQual
            {
                Kind = kind,
                Ordinal = i,
                Name = quals[i].Name,
                Freq = quals[i].Freq,
            };

            for (var m = 0; m < quals[i].Members.Count; m++)
            {
                q.Members.Add(new ConsolidatedQualMember { Ordinal = m, Text = quals[i].Members[m] });
            }

            profile.ConsolidatedQuals.Add(q);
        }
    }

    private CoverageReport MapCoverage(PocCoverageReport src)
    {
        var report = new CoverageReport
        {
            N = src.N,
            MeanCoverage = src.MeanCoverage,
            WellCoveredPct = src.WellCoveredPct,
        };

        for (var i = 0; i < src.PerJd.Count; i++)
        {
            var j = src.PerJd[i];
            var cov = new JdCoverage
            {
                Ordinal = i,
                SourceFile = _scrub?.SourceFile(j.SourceFile) ?? j.SourceFile,
                CoveredPct = j.CoveredPct,
            };

            for (var u = 0; u < j.Uncovered.Count; u++)
            {
                cov.Uncovered.Add(new JdCoverageUncovered
                {
                    Ordinal = u,
                    Name = j.Uncovered[u].Name,
                    Pct = j.Uncovered[u].Pct,
                });
            }

            report.PerJd.Add(cov);
        }

        return report;
    }

    // ---------------------------------------------------------------- compliance rules

    private async Task MigrateComplianceRulesAsync(MigrationReport report, CancellationToken ct)
    {
        var path = DataPath("compliance-rules.json");
        if (!File.Exists(path))
        {
            report.Problems.Add($"compliance-rules.json not found: {path}");
            return;
        }

        var rows = LoadJson<List<PocComplianceRule>>(path);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entities = new List<ComplianceRule>(rows.Count);

        foreach (var r in rows)
        {
            // The POC file carries no identifier. The rule's own pattern is what makes it unique,
            // so the key is derived from it rather than from position — which would shift the
            // moment a rule is inserted, and these keys appear in the audit trail.
            var key = $"{r.RuleType}:{r.Pattern}";
            if (key.Length > 100)
            {
                key = key[..100];
            }

            if (!seen.Add(key))
            {
                report.Problems.Add($"duplicate compliance rule key, skipped: {key}");
                continue;
            }

            entities.Add(new ComplianceRule
            {
                Key = key,
                Pattern = r.Pattern,
                Replacement = r.Replacement,
                Reason = r.Description,
                Enabled = true,
            });
        }

        report.ComplianceRules = entities.Count;
        _log($"compliance rules: {entities.Count}");

        if (!_write)
        {
            return;
        }

        await _db.ComplianceRules.ExecuteDeleteAsync(ct);
        await AddInBatchesAsync(_db.ComplianceRules, entities, ct);
    }

    // ---------------------------------------------------------------- batching

    /// <summary>
    /// Rows per SaveChanges. Sized for the JD corpus, where one "row" drags in roughly 20 children
    /// — so a batch of 200 is about 5,000 entities, which EF handles without the change tracker
    /// becoming the bottleneck.
    /// </summary>
    private const int BatchSize = 200;

    private async Task AddInBatchesAsync<T>(DbSet<T> set, List<T> entities, CancellationToken ct)
        where T : class
    {
        for (var i = 0; i < entities.Count; i += BatchSize)
        {
            var slice = entities.Skip(i).Take(BatchSize).ToList();
            await set.AddRangeAsync(slice, ct);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }
    }

    private async Task FlushAsync<T>(DbSet<T> set, List<T> batch, CancellationToken ct)
        where T : class
    {
        await set.AddRangeAsync(batch, ct);
        await _db.SaveChangesAsync(ct);

        // Detach everything: holding 1,367 JDs and their 25,000 children in the tracker makes each
        // subsequent SaveChanges slower than the last.
        _db.ChangeTracker.Clear();
        batch.Clear();
    }
}

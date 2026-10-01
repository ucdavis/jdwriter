using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Tests.Migration;

/// <summary>
/// Verifies what the corpus migration actually landed in SQL.
///
/// These read a database that `jdw-cli migrate-poc --write` has populated, so they depend on both
/// the corpus and a live SQL instance. Tagged CorpusParity and — like the parser parity suite —
/// they FAIL rather than skip when either is missing: a migration suite that quietly passes is
/// worse than none, because the run goes green while nobody has checked that 21,000 duty rows
/// arrived.
///
/// Run with:
///   dotnet run --project tools/jdw-cli -- migrate-poc --write
///   dotnet test --filter "Category=CorpusParity"
///
/// Exclude where SQL is unavailable:
///   dotnet test --filter "Category!=CorpusParity"
/// </summary>
[Trait("Category", "CorpusParity")]
public class CorpusMigrationTests : IDisposable
{
    // Expected shape of the corpus. Every one of these is independently established: the parser
    // parity suite derives the JD numbers from the exports, and the POC's own files carry the
    // standards and profile counts.
    private const int ExpectedTitleCodes = 3471;
    private const int ExpectedSupersessions = 29;
    private const int ExpectedStandards = 214;
    private const int ExpectedStandardsWithCode = 187;
    private const int ExpectedJobDescriptions = 1367;
    private const int ExpectedResponsibilities = 4443;
    private const int ExpectedDuties = 21353;
    private const int ExpectedDistinctCodes = 60;
    private const int ExpectedProfiles = 65;
    private const int ExpectedProfilesWithCoverage = 60;
    private const int ExpectedComplianceRules = 11;

    private readonly AppDbContext _db;

    public CorpusMigrationTests()
    {
        var conn = Environment.GetEnvironmentVariable("DB_CONNECTION")
                   ?? "Server=localhost,14333;Database=AppDb;User ID=sa;Password=LocalDev123!;Encrypt=False;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(conn, o => o.CommandTimeout(120))
            .Options;

        _db = new AppDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_database_is_reachable_and_populated()
    {
        // Guards every other test here. Without it, an empty database would make several of the
        // "nothing is wrong" assertions below pass vacuously.
        _db.Database.CanConnect().Should().BeTrue(
            "these tests read a migrated database — start SQL and run `jdw-cli migrate-poc --write`, " +
            "or exclude them with --filter \"Category!=CorpusParity\"");

        _db.ClassProfiles.Count().Should().BeGreaterThan(0,
            "the corpus has not been migrated — run `jdw-cli migrate-poc --write`");
    }

    [Fact]
    public void The_title_reference_and_its_derived_supersessions_loaded()
    {
        _db.TitleCodes.Count().Should().Be(ExpectedTitleCodes);
        _db.Supersessions.Count().Should().Be(ExpectedSupersessions);

        // Both keys are populated, which is what keeps code resolution correct: the strict key
        // separates "PROJECT POLICY ANL 1" from "... 1 RP", and merging them once produced
        // confidently wrong codes rather than missing ones.
        _db.TitleCodes.Count(t => t.TitleKey == "").Should().Be(0);
        _db.TitleCodes.Count(t => t.TitleCodeKey == "").Should().Be(0);

        // A retired code has exactly one successor, or the remap is not a function.
        _db.Supersessions.Select(s => s.FromCode).Distinct().Count().Should().Be(ExpectedSupersessions);
    }

    [Fact]
    public void The_standards_loaded_with_their_items()
    {
        _db.JobStandards.Count().Should().Be(ExpectedStandards);
        _db.JobStandards.Count(s => s.Code != null && s.Code != "").Should().Be(ExpectedStandardsWithCode);

        // Dedup is on the strict key; the loose key would have silently discarded ~25 standards.
        _db.JobStandards.Select(s => s.TitleCodeKey).Distinct().Count().Should().Be(ExpectedStandards);

        _db.JobStandardItems.Count().Should().BeGreaterThan(3000);
        _db.JobStandardItems.Count(i => i.Kind == StandardItemKind.KeyResponsibility)
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public void The_whole_JD_corpus_loaded_with_its_children()
    {
        _db.JobDescriptions.Count().Should().Be(ExpectedJobDescriptions);
        _db.JdResponsibilities.Count().Should().Be(ExpectedResponsibilities);
        _db.JdDuties.Count().Should().Be(ExpectedDuties);

        // Every export is distinct; SourceFile is the identity of a JD.
        _db.JobDescriptions.Select(j => j.SourceFile).Distinct().Count().Should().Be(ExpectedJobDescriptions);

        _db.JobDescriptions.Select(j => j.UcJobCode).Distinct().Count().Should().Be(ExpectedDistinctCodes);
    }

    [Fact]
    public void No_superseded_job_code_survives_in_the_corpus()
    {
        // The point of remapping at load: grouping, slug, aggregation and profile identity all
        // follow the live class, so a re-ingest cannot resurrect a dead one.
        var retired = _db.Supersessions.Select(s => s.FromCode).ToHashSet(StringComparer.Ordinal);
        retired.Should().NotBeEmpty();

        var survivors = _db.JobDescriptions
            .Select(j => j.UcJobCode)
            .Distinct()
            .ToList()
            .Where(retired.Contains)
            .ToList();

        survivors.Should().BeEmpty("every JD must be filed under a live classification");

        // And the rewrite is auditable rather than invisible.
        var remapped = _db.JobDescriptions.Count(j => j.OriginalUcJobCode != null);
        remapped.Should().Be(97, "97 exports are filed under one of 8 superseded codes");

        _db.JobDescriptions
            .Where(j => j.OriginalUcJobCode != null)
            .Select(j => j.OriginalUcJobCode!)
            .Distinct()
            .ToList()
            .Should().OnlyContain(c => retired.Contains(c));
    }

    [Fact]
    public void Percentages_that_do_not_sum_to_100_were_loaded_not_rejected()
    {
        // Six real exports range from 0 to 200. The schema carries no constraint for exactly this
        // reason, and the loader reports rather than refuses.
        var sums = _db.JobDescriptions
            .Select(j => new
            {
                j.Id,
                Sum = j.Responsibilities.Where(r => r.Pct != null).Sum(r => r.Pct!.Value),
                Stated = j.Responsibilities.Count(r => r.Pct != null),
            })
            .Where(x => x.Stated > 0)
            .ToList();

        sums.Should().HaveCount(ExpectedJobDescriptions);
        sums.Count(x => x.Sum == 100).Should().Be(1361);
        sums.Count(x => x.Sum != 100).Should().Be(6, "these are genuine data, not load failures");
    }

    [Fact]
    public void Corpus_wide_absences_are_preserved()
    {
        // Facts the schema was designed around. If any changed, Phase 2's decisions need revisiting.
        _db.JdPemEntries.Count().Should().Be(0, "no export in this corpus marks the PEM grid");
        _db.JobDescriptions.Count(j => j.PemPopulated).Should().Be(0);

        _db.JdQualificationItems.Count(q => q.Kind == JdQualificationKind.ConditionOfEmployment)
            .Should().Be(0, "this export template never populates conditions of employment");

        // Tri-state scope fields keep null as a distinct value from an explicit no.
        _db.JobDescriptions.Count(j => j.Supervises == null).Should().Be(1027);
    }

    [Fact]
    public void Every_profile_arrived_with_its_envelope_intact()
    {
        _db.ClassProfiles.Count().Should().Be(ExpectedProfiles);
        _db.JobEnvelopes.Count().Should().Be(ExpectedProfiles, "all 65 profiles carry an envelope");
        _db.CoverageReports.Count().Should().Be(ExpectedProfilesWithCoverage);

        _db.ClassProfiles.Select(p => p.Slug).Distinct().Count().Should().Be(ExpectedProfiles);

        // 60 corpus-derived, 5 bootstrapped from an official standard.
        _db.ClassProfiles.Count(p => p.EnvelopeSource == EnvelopeSource.Claude).Should().Be(60);
        _db.ClassProfiles.Count(p => p.EnvelopeSource == EnvelopeSource.Standard).Should().Be(5);

        // An envelope with no responsibilities would be a silently empty template.
        _db.EnvelopeResponsibilities.Count().Should().BeGreaterThan(0);
        _db.EnvelopeDuties.Count().Should().BeGreaterThan(0);

        var envelopesWithoutResponsibilities = _db.JobEnvelopes
            .Count(e => !e.KeyResponsibilities.Any());
        envelopesWithoutResponsibilities.Should().Be(0);
    }

    [Fact]
    public void Consolidation_and_coverage_survived_the_move()
    {
        // These came from model calls that cost real money. They are migrated as they stand, never
        // recomputed, so their presence is the thing worth checking.
        _db.ClassProfiles.Count(p => p.ConsolidatedFunctions.Any()).Should().Be(60);
        _db.ConsolidatedFunctions.Count().Should().BeGreaterThan(0);
        _db.ConsolidatedFunctionMembers.Count().Should().BeGreaterThan(0);

        _db.JdCoverages.Count().Should().BeGreaterThan(0);

        // The uncovered items are what the review-and-nudge workflow proposes pulling back toward
        // the standard, so losing them would quietly remove the product's next feature.
        _db.JdCoverageUncovereds.Count().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Distribution_nulls_are_preserved_as_real_observations()
    {
        // "The export did not say" is an observation, not a missing value. Collapsing it into
        // "false" would skew every consensus computed from these.
        var nullValues = _db.ProfileDistributionValues.Count(v => v.Value == null);
        nullValues.Should().BeGreaterThan(0,
            "tri-state fields record nulls alongside true/false");

        // Booleans render as their lowercase names, matching the schema's stated convention.
        var booleanish = _db.ProfileDistributionValues
            .Where(v => v.Value == "true" || v.Value == "false")
            .Count();
        booleanish.Should().BeGreaterThan(0);

        // All six distribution fields are present on every profile.
        _db.ProfileDistributions.Count().Should().Be(ExpectedProfiles * 6);
    }

    [Fact]
    public void Ordered_lists_kept_their_order()
    {
        // SQL has no inherent row order, so a duty list read back unordered is a scrambled job
        // description. Ordinals must be dense and zero-based per parent.
        var sample = _db.JdResponsibilities
            .Include(r => r.Duties)
            .Where(r => r.Duties.Count > 2)
            .OrderBy(r => r.Id)
            .Take(50)
            .ToList();

        sample.Should().NotBeEmpty();

        foreach (var resp in sample)
        {
            resp.Duties
                .Select(d => d.Ordinal)
                .OrderBy(o => o)
                .Should().Equal(Enumerable.Range(0, resp.Duties.Count),
                    "duty ordinals must be dense and zero-based");
        }
    }

    [Fact]
    public void The_compliance_rules_loaded_with_stable_keys()
    {
        _db.ComplianceRules.Count().Should().Be(ExpectedComplianceRules);

        // Keys appear in the audit trail, so they must be unique and derived from the rule itself
        // rather than from its position in the file.
        _db.ComplianceRules.Select(r => r.Key).Distinct().Count().Should().Be(ExpectedComplianceRules);
        _db.ComplianceRules.Count(r => r.Pattern == "" || r.Replacement == "").Should().Be(0);
    }
}

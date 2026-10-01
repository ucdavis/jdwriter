using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Profiles;
using Server.Core.Titles;

namespace Server.Tests.Profiles;

/// <summary>
/// The analyst index is a page of counts. These pin the counts, the consolidated-else-raw fallback
/// carried over from the POC's admin view, and the two "not yet" states (no envelope, no coverage)
/// that must read as absent rather than as zero.
/// </summary>
public class ClassSummaryTests
{
    private static ClassProfileRepository Repo(AppDbContext db) =>
        new(db, new TitleCodeService(db, new MemoryCache(new MemoryCacheOptions())));

    private static ClassProfile Curated() => new()
    {
        Slug = "004724-farm-laborer",
        Title = "FARM LABORER",
        UcJobCode = "004724",
        CtJobFamily = "Agriculture",
        CtJobFunction = "Field Operations",
        PersonnelProgram = "PSS",
        CorpusSize = 12,
        EnvelopeSource = EnvelopeSource.Manual,
        Envelope = new JobEnvelope
        {
            KeyResponsibilities =
            [
                new EnvelopeResponsibility { Ordinal = 0, FunctionName = "Field Work", PctTime = 60 },
                new EnvelopeResponsibility { Ordinal = 1, FunctionName = "Equipment", PctTime = 30 },
            ],
        },
        Distributions =
        [
            new ProfileDistribution { Field = DistributionField.UnionCode, Consensus = "SX" },
            new ProfileDistribution { Field = DistributionField.SalaryGrade, Consensus = "Grade 3" },
        ],
        Functions =
        [
            new ProfileFunction { Ordinal = 0, Name = "FIELD WORK" },
            new ProfileFunction { Ordinal = 1, Name = "Field work" },
            new ProfileFunction { Ordinal = 2, Name = "EQUIPMENT" },
        ],
        ConsolidatedFunctions =
        [
            new ConsolidatedFunction { Ordinal = 0, Name = "Field Work" },
            new ConsolidatedFunction { Ordinal = 1, Name = "Equipment" },
        ],
        Qualifications =
        [
            new ProfileQualItem { Kind = ProfileQualKind.KsaMin, Ordinal = 0, Text = "a" },
            new ProfileQualItem { Kind = ProfileQualKind.KsaMin, Ordinal = 1, Text = "b" },
            new ProfileQualItem { Kind = ProfileQualKind.KsaMin, Ordinal = 2, Text = "c" },
            new ProfileQualItem { Kind = ProfileQualKind.Education, Ordinal = 0, Text = "d" },
        ],
        ConsolidatedQuals =
        [
            new ConsolidatedQual { Kind = ConsolidatedQualKind.MinQualification, Ordinal = 0, Name = "x" },
            new ConsolidatedQual { Kind = ConsolidatedQualKind.Certification, Ordinal = 0, Name = "y" },
        ],
        Coverage = new CoverageReport { N = 12, MeanCoverage = 81.5, WellCoveredPct = 0.5 },
    };

    private static ClassProfile Bare() => new()
    {
        Slug = "009999-bare-class",
        Title = "BARE CLASS",
        UcJobCode = "009999",
        CorpusSize = 4,
        Functions =
        [
            new ProfileFunction { Ordinal = 0, Name = "ONE" },
            new ProfileFunction { Ordinal = 1, Name = "TWO" },
        ],
        Qualifications =
        [
            new ProfileQualItem { Kind = ProfileQualKind.KsaMin, Ordinal = 0, Text = "a" },
        ],
    };

    private static async Task<List<ClassSummary>> Summaries(params ClassProfile[] profiles)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.ClassProfiles.AddRange(profiles);
        await db.SaveChangesAsync();
        return await Repo(db).GetSummariesAsync();
    }

    [Fact]
    public async Task A_curated_class_reports_its_counts_and_stored_coverage()
    {
        var s = (await Summaries(Curated())).Single();

        s.Grade.Should().Be("Grade 3", "grade is the SalaryGrade consensus, not the union code beside it");
        s.EnvelopeSource.Should().Be(EnvelopeSource.Manual);
        s.HasEnvelope.Should().BeTrue();
        s.EnvelopeResponsibilities.Should().Be(2);
        s.ConsolidatedFunctions.Should().Be(2);
        s.Responsibilities.Should().Be(2, "consolidated categories win over raw functions when present");
        s.Ksas.Should().Be(1, "only MinQualification counts, and consolidated wins over raw KsaMin");
        s.CoverageN.Should().Be(12);
        s.MeanCoverage.Should().Be(81.5);
        s.WellCoveredPct.Should().Be(0.5);
    }

    [Fact]
    public async Task An_envelope_that_does_not_sum_to_100_is_reported_as_it_is()
    {
        // 60 + 30. The index exists to surface this, so it must not be rescaled or clamped.
        var s = (await Summaries(Curated())).Single();

        s.EnvelopePctTotal.Should().Be(90);
    }

    [Fact]
    public async Task An_unconsolidated_class_falls_back_to_raw_counts()
    {
        var s = (await Summaries(Bare())).Single();

        s.ConsolidatedFunctions.Should().Be(0);
        s.Responsibilities.Should().Be(2);
        s.Ksas.Should().Be(1);
    }

    [Fact]
    public async Task Missing_envelope_and_coverage_read_as_absent_not_zero()
    {
        var s = (await Summaries(Bare())).Single();

        s.HasEnvelope.Should().BeFalse();
        s.EnvelopeSource.Should().BeNull();
        s.EnvelopeResponsibilities.Should().Be(0);
        s.EnvelopePctTotal.Should().Be(0);
        s.Grade.Should().BeNull();

        // Zero coverage is a real and alarming result; no report is a different state entirely.
        s.CoverageN.Should().BeNull();
        s.MeanCoverage.Should().BeNull();
        s.WellCoveredPct.Should().BeNull();
    }

    [Fact]
    public async Task Classes_are_ordered_by_title_ordinally()
    {
        var summaries = await Summaries(Curated(), Bare());

        summaries.Select(s => s.Title).Should().Equal("BARE CLASS", "FARM LABORER");
    }

    [Fact]
    public async Task The_repository_leaves_standard_linkage_to_the_caller()
    {
        // Resolved by title against the standards index in the controller, not stored on the row.
        var summaries = await Summaries(Curated(), Bare());

        summaries.Should().OnlyContain(s => !s.StandardLinked);
    }
}

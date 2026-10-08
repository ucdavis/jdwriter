using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Core.Domain;
using Server.Core.Jd;
using Server.Core.Profiles;
using Server.Core.Titles;
using Microsoft.Extensions.Caching.Memory;

namespace Server.Tests.Profiles;

/// <summary>
/// The wire shapes the client is written against. Before these existed the API serialized EF
/// entities directly, and four screens rendered empty against the real backend while every MSW
/// test stayed green — so the mapping is pinned here, in both directions.
/// </summary>
public class ProfileWireTests
{
    private static EnvelopeWire Wire() => new()
    {
        Summary = "s",
        ScopeStatement = "scope",
        KeyResponsibilities =
        [
            new JdKeyResponsibility { FunctionName = "Lab Support", PctTime = 70, Duties = ["prep", "clean"] },
            new JdKeyResponsibility { FunctionName = "Records", PctTime = 30, Duties = ["log"] },
        ],
        RequiredCertifications = ["cert"],
        Education = ["ed1", "ed2"],
        WorkExperience = ["exp"],
        MinQualifications = ["min1", "min2"],
        PrefQualifications = ["pref"],
        ConditionsOfEmployment = ["cond"],
        WorkEnvironment = ["env"],
        PhysicalRequirements = ["phys"],
        OutOfEnvelope = ["out"],
    };

    [Fact]
    public void An_envelope_survives_the_round_trip_through_the_entity()
    {
        var wire = Wire();

        EnvelopeWire.From(wire.ToEntity()).Should().BeEquivalentTo(wire, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Each_list_section_lands_under_its_own_kind()
    {
        // A swapped kind is invisible in a count and fatal on screen: minimum qualifications
        // rendered as out-of-envelope signals.
        var entity = Wire().ToEntity();

        entity.Items.Where(i => i.Kind == EnvelopeListKind.MinQualification).Select(i => i.Text)
            .Should().Equal("min1", "min2");
        entity.Items.Where(i => i.Kind == EnvelopeListKind.OutOfEnvelope).Select(i => i.Text)
            .Should().Equal("out");
        entity.Items.Where(i => i.Kind == EnvelopeListKind.ConditionOfEmployment).Select(i => i.Text)
            .Should().Equal("cond");
    }

    [Fact]
    public void Reading_an_entity_orders_by_ordinal_not_by_row_order()
    {
        var entity = new JobEnvelope
        {
            KeyResponsibilities =
            [
                new EnvelopeResponsibility
                {
                    Ordinal = 1, FunctionName = "second", PctTime = 40,
                    Duties = [new EnvelopeDuty { Ordinal = 1, Text = "b" }, new EnvelopeDuty { Ordinal = 0, Text = "a" }],
                },
                new EnvelopeResponsibility { Ordinal = 0, FunctionName = "first", PctTime = 60 },
            ],
            Items =
            [
                new EnvelopeListItem { Kind = EnvelopeListKind.Education, Ordinal = 1, Text = "later" },
                new EnvelopeListItem { Kind = EnvelopeListKind.Education, Ordinal = 0, Text = "earlier" },
            ],
        };

        var wire = EnvelopeWire.From(entity);

        wire.KeyResponsibilities.Select(r => r.FunctionName).Should().Equal("first", "second");
        wire.KeyResponsibilities[1].Duties.Should().Equal("a", "b");
        wire.Education.Should().Equal("earlier", "later");
    }

    [Fact]
    public void The_profile_view_names_each_distribution_and_defaults_the_missing_ones()
    {
        var profile = new ClassProfile
        {
            Slug = "s",
            Distributions =
            [
                new ProfileDistribution { Field = DistributionField.UnionCode, Consensus = "TX", Agreement = 0.9 },
                new ProfileDistribution { Field = DistributionField.SalaryGrade, Consensus = "Grade 19", Agreement = 0.75 },
            ],
            SourceFiles =
            [
                new ProfileSourceFile { Ordinal = 1, SourceFile = "b.HTML" },
                new ProfileSourceFile { Ordinal = 0, SourceFile = "a.HTML" },
            ],
        };

        var view = ClassProfileView.From(profile, standard: null);

        view.SalaryGrade.Consensus.Should().Be("Grade 19");
        view.SalaryGrade.Agreement.Should().Be(0.75);
        view.UnionCode.Consensus.Should().Be("TX", "not the grade sitting beside it");
        view.FlsaStatus.Consensus.Should().BeNull("an absent distribution reads as no consensus");
        view.FlsaStatus.Agreement.Should().Be(0);
        // HRTMS file names embed UCPath position numbers, and this view goes to every author.
        System.Text.Json.JsonSerializer.Serialize(view).Should().NotContain("a.HTML").And.NotContain("SourceFiles");
        view.Envelope.Should().BeNull();
    }

    // ------------------------------------------------------------------ envelope save

    private static (EnvelopeController Controller, Server.Core.Data.AppDbContext Db) Controller()
    {
        var db = TestDbContextFactory.CreateInMemory();
        var repo = new ClassProfileRepository(db, new TitleCodeService(db, new MemoryCache(new MemoryCacheOptions())));
        var llm = new FakeStructuredLlm();
        return (new EnvelopeController(db, repo, new EnvelopeCoverageChecker(llm), llm), db);
    }

    [Fact]
    public async Task Saving_replaces_the_envelope_and_marks_it_partner_edited()
    {
        var (controller, db) = Controller();
        db.ClassProfiles.Add(new ClassProfile
        {
            Slug = "c1",
            EnvelopeSource = EnvelopeSource.Claude,
            Envelope = new JobEnvelope { Summary = "old" },
        });
        await db.SaveChangesAsync();

        var result = await controller.Save(new EnvelopeSaveRequest("c1", Wire()), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var saved = await db.ClassProfiles
            .Include(p => p.Envelope!).ThenInclude(e => e.KeyResponsibilities).ThenInclude(r => r.Duties)
            .Include(p => p.Envelope!).ThenInclude(e => e.Items)
            .SingleAsync(p => p.Slug == "c1");
        saved.EnvelopeSource.Should().Be(EnvelopeSource.Manual);
        EnvelopeWire.From(saved.Envelope!).Should().BeEquivalentTo(Wire(), o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(70, 20)]
    [InlineData(70, 40)]
    public async Task An_envelope_off_100_percent_is_refused_and_nothing_is_written(int a, int b)
    {
        // Ported from the POC's save route: an envelope is never valid off 100%, in either direction.
        var (controller, db) = Controller();
        db.ClassProfiles.Add(new ClassProfile
        {
            Slug = "c1",
            EnvelopeSource = EnvelopeSource.Claude,
            Envelope = new JobEnvelope { Summary = "old" },
        });
        await db.SaveChangesAsync();

        var wire = Wire();
        wire.KeyResponsibilities[0].PctTime = a;
        wire.KeyResponsibilities[1].PctTime = b;

        var result = await controller.Save(new EnvelopeSaveRequest("c1", wire), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        var saved = await db.ClassProfiles.Include(p => p.Envelope).SingleAsync(p => p.Slug == "c1");
        saved.EnvelopeSource.Should().Be(EnvelopeSource.Claude);
        saved.Envelope!.Summary.Should().Be("old");
    }
}

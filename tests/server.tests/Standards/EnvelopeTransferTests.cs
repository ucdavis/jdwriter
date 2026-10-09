using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Profiles;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Tests.Standards;

/// <summary>
/// Moving standard-derived envelopes between environments: bootstrap locally, import into production.
///
/// The import must be a CREATE PATH WITH EVERY GUARD a bootstrap has — supersession, an existing
/// class, a missing standard — because it creates profiles in the environment that matters most. And
/// it must never call the model: the point is to pay for each envelope once.
/// </summary>
public class EnvelopeTransferTests
{
    private sealed class StubStandards : IStandardsStore
    {
        private readonly StandardsIndex _index;

        public StubStandards(params ClassStandardRecord[] rows) => _index = new StandardsIndex(rows);

        public Task<StandardsIndex> GetIndexAsync(CancellationToken ct = default) => Task.FromResult(_index);

        public void Invalidate()
        {
        }
    }

    private sealed class StubTitleCodes : ITitleCodeService
    {
        private readonly TitleCodeIndex _index;

        public StubTitleCodes(params TitleCode[] rows) => _index = new TitleCodeIndex(rows);

        public Task<TitleCodeIndex> GetAsync(CancellationToken ct = default) => Task.FromResult(_index);

        public void Invalidate()
        {
        }
    }

    /// <summary>The "local" model: a recognisable envelope per standard.</summary>
    private sealed class FakeModel : IStandardEnvelopeBuilder
    {
        public Task<JobEnvelope> BuildAsync(ClassStandardRecord standard, BootstrapMeta meta, CancellationToken ct = default)
        {
            var env = new JobEnvelope { Summary = $"Model summary for {standard.LongTitle}.", ScopeStatement = "Scope." };
            var r = new EnvelopeResponsibility { Ordinal = 0, FunctionName = "PRIMARY", PctTime = 60 };
            r.Duties.Add(new EnvelopeDuty { Ordinal = 0, Text = $"Does {standard.LongTitle} work." });
            env.KeyResponsibilities.Add(r);
            env.KeyResponsibilities.Add(new EnvelopeResponsibility { Ordinal = 1, FunctionName = "SECONDARY", PctTime = 40 });
            env.Items.Add(new EnvelopeListItem { Kind = EnvelopeListKind.Education, Ordinal = 0, Text = "Bachelor's degree." });
            return Task.FromResult(env);
        }
    }

    /// <summary>Production must never reach the model during an import.</summary>
    private sealed class NoModel : IStandardEnvelopeBuilder
    {
        public Task<JobEnvelope> BuildAsync(ClassStandardRecord standard, BootstrapMeta meta, CancellationToken ct = default) =>
            throw new InvalidOperationException("An import called the model.");
    }

    private static ClassStandardRecord S(string title, string grade = "Grade 21") => new()
    {
        LongTitle = title,
        PersProg = "PSS",
        Grade = grade,
        Flsa = "Exempt",
        Union = "CX",
        GenericScope = $"Scope for {title}.",
        Ksa = ["Knows the subject."],
        Education = ["Bachelor's degree."],
    };

    private static TitleCode Tc(string code, string title) => new()
    {
        Code = code,
        Title = title,
        Source = "both",
        Family = "Finance",
        Function = "Financial Analysis",
        TitleKey = TitleNormalizer.TitleKey(title),
        TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
    };

    private static readonly ClassStandardRecord[] Standards = [S("Financial Analyst 2 CX"), S("Widget Analyst 2")];

    private static readonly TitleCode[] Reference =
    [
        Tc("004767", "FINANCIAL ANL 2 CX"),
        Tc("111111", "WIDGET ANL 2"),
        Tc("007709", "FINANCIAL ANL 3"),
        Tc("005183", "FINANCIAL ANL 3 CX"),
    ];

    private static (EnvelopeTransfer Transfer, Bootstrapper Boot, AppDbContext Db) Env(
        IStandardEnvelopeBuilder model, ClassStandardRecord[]? standards = null, TitleCode[]? reference = null)
    {
        var db = TestDbContextFactory.CreateInMemory();
        var titleCodes = new StubTitleCodes(reference ?? Reference);
        var boot = new Bootstrapper(db, new StubStandards(standards ?? Standards), titleCodes, model);
        return (new EnvelopeTransfer(db, boot, titleCodes, NullLogger<EnvelopeTransfer>.Instance), boot, db);
    }

    private static async Task<EnvelopeBundle> BootstrapLocallyAndExport(params string[] titles)
    {
        var (transfer, boot, db) = Env(new FakeModel());
        using var _db = db;
        foreach (var t in titles)
        {
            await boot.BootstrapAsync(t);
        }

        return await transfer.ExportAsync();
    }

    [Fact]
    public async Task An_envelope_bootstrapped_locally_lands_in_production_without_a_model_call()
    {
        var bundle = await BootstrapLocallyAndExport("Financial Analyst 2 CX");

        var (prod, _, db) = Env(new NoModel());
        using var _db = db;
        var result = await prod.ImportAsync(bundle);

        result.Skipped.Should().BeEmpty();
        result.Created.Should().ContainSingle().Which.UcJobCode.Should().Be("004767");

        var profile = db.ClassProfiles.Single();
        profile.EnvelopeSource.Should().Be(EnvelopeSource.Standard);
        profile.GeneratedNote.Should().Contain("imported");

        var envelope = EnvelopeWire.From(db.JobEnvelopes
            .Include(e => e.KeyResponsibilities).ThenInclude(r => r.Duties)
            .Include(e => e.Items)
            .Single());
        envelope.Summary.Should().Be("Model summary for Financial Analyst 2 CX.");
        envelope.KeyResponsibilities.Select(r => (r.FunctionName, r.PctTime)).Should().Equal(("PRIMARY", 60), ("SECONDARY", 40));
        envelope.KeyResponsibilities[0].Duties.Should().Equal("Does Financial Analyst 2 CX work.");
        // Arriving envelopes get the education house rules too: a degree reads "or equivalent".
        envelope.Education.Should().Equal("Bachelor's degree or equivalent experience.");

        // Everything else is rebuilt from production's OWN standard, as a bootstrap there would.
        db.ProfileDistributions.Should().Contain(d => d.Field == DistributionField.UnionCode && d.Consensus == "CX");
        db.ProfileQualItems.Should().Contain(q => q.Kind == ProfileQualKind.KsaMin && q.Text == "Knows the subject.");
    }

    [Fact]
    public async Task Export_carries_only_classes_still_built_from_their_standard()
    {
        var (transfer, boot, db) = Env(new FakeModel());
        using var _db = db;
        await boot.BootstrapAsync("Financial Analyst 2 CX");
        var edited = await boot.BootstrapAsync("Widget Analyst 2");
        edited.EnvelopeSource = EnvelopeSource.Manual;
        db.ClassProfiles.Add(new ClassProfile
        {
            Slug = "222222-learned", UcJobCode = "222222", Title = "Learned Class", CorpusSize = 9,
            EnvelopeSource = EnvelopeSource.Claude, Envelope = new JobEnvelope { Summary = "From JDs." },
        });
        // A dead class awaiting retirement: 007709 is superseded by the CX code in this reference.
        db.ClassProfiles.Add(new ClassProfile
        {
            Slug = "007709-financial-analyst-3", UcJobCode = "007709", Title = "Financial Analyst 3",
            EnvelopeSource = EnvelopeSource.Standard, Envelope = new JobEnvelope { Summary = "Dead class." },
        });
        await db.SaveChangesAsync();

        var bundle = await transfer.ExportAsync();

        bundle.Format.Should().Be(EnvelopeBundle.FormatName);
        bundle.Envelopes.Select(e => (e.Title, e.UcJobCode)).Should().Equal(("Financial Analyst 2 CX", "004767"));
    }

    [Fact]
    public async Task Each_refusal_is_reported_with_its_reason_and_the_rest_still_import()
    {
        var bundle = await BootstrapLocallyAndExport("Financial Analyst 2 CX", "Widget Analyst 2");
        bundle.Envelopes.Add(new BundledEnvelope
        {
            Title = "Nowhere Planner 1",
            UcJobCode = "999999",
            Envelope = bundle.Envelopes[0].Envelope,
        });

        // Production already has a learned class for 004767, under a corpus-spelled title.
        var (prod, _, db) = Env(new NoModel());
        using var _db = db;
        db.ClassProfiles.Add(new ClassProfile { Slug = "004767-financial-anl-2-cx", UcJobCode = "004767", Title = "Financial Anl 2 Cx", CorpusSize = 12 });
        await db.SaveChangesAsync();

        var result = await prod.ImportAsync(bundle);

        result.Created.Select(c => c.Title).Should().Equal("Widget Analyst 2");
        result.Skipped.Select(s => (s.Title, s.Reason)).Should().BeEquivalentTo(
        [
            ("Financial Analyst 2 CX", BootstrapRefusal.Exists),
            ("Nowhere Planner 1", BootstrapRefusal.NoStandard),
        ]);
        db.ClassProfiles.Single(p => p.UcJobCode == "004767").CorpusSize.Should().Be(12, "an existing class is never overwritten");
    }

    [Fact]
    public async Task A_class_superseded_in_production_is_refused()
    {
        var bundle = await BootstrapLocallyAndExport("Widget Analyst 2");

        // Production's reference has since gained the union successor that retires 111111.
        var (prod, _, db) = Env(new NoModel(), reference: [.. Reference, Tc("111112", "WIDGET ANL 2 CX")]);
        using var _db = db;

        var result = await prod.ImportAsync(bundle);

        result.Created.Should().BeEmpty();
        result.Skipped.Should().ContainSingle().Which.Reason.Should().Be(BootstrapRefusal.Superseded);
    }

    [Fact]
    public async Task A_standard_that_resolves_to_another_code_in_production_is_refused()
    {
        var bundle = await BootstrapLocallyAndExport("Widget Analyst 2");

        var (prod, _, db) = Env(new NoModel(), reference: [Tc("004767", "FINANCIAL ANL 2 CX"), Tc("333333", "WIDGET ANL 2")]);
        using var _db = db;

        var result = await prod.ImportAsync(bundle);

        var skipped = result.Skipped.Should().ContainSingle().Subject;
        skipped.Reason.Should().Be(BootstrapRefusal.CodeMismatch);
        skipped.Message.Should().Contain("333333").And.Contain("111111");
    }

    [Fact]
    public async Task A_file_that_is_not_an_envelope_export_is_rejected_whole()
    {
        var (prod, _, db) = Env(new NoModel());
        using var _db = db;

        var act = () => prod.ImportAsync(new EnvelopeBundle { Format = "something-else" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Not a JDWriter envelope export*");
    }

    [Fact]
    public async Task Bootstrapping_refuses_a_class_that_already_has_a_corpus_profile_under_another_title()
    {
        // Slug-only checking let a standard-derived duplicate be created beside a learned class.
        var (_, boot, db) = Env(new FakeModel());
        using var _db = db;
        db.ClassProfiles.Add(new ClassProfile { Slug = "004767-financial-anl-2-cx", UcJobCode = "004767", Title = "Financial Anl 2 Cx", CorpusSize = 12 });
        await db.SaveChangesAsync();

        var act = () => boot.BootstrapAsync("Financial Analyst 2 CX");

        (await act.Should().ThrowAsync<BootstrapRefusedException>()).Which.Reason.Should().Be(BootstrapRefusal.Exists);
    }
}

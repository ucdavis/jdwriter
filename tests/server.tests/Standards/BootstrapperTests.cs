using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Tests.Standards;

/// <summary>
/// Bootstrapping a class from an official standard alone.
///
/// The superseded-code guard is the point of this file. That bug shipped once: browse and the
/// classifier both hid deprecated classes, but `bootstrapCandidates` resolved titles with a matcher
/// that happily returned a dead code, so the UI kept offering a deprecated class and recreated one.
/// The lesson was that hiding a class from the READ paths is not enough — every path that CREATES a
/// profile needs its own guard.
/// </summary>
public class BootstrapperTests
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

    /// <summary>Returns a minimal deterministic envelope; the real synthesis lives elsewhere.</summary>
    private sealed class StubEnvelopes : IStandardEnvelopeBuilder
    {
        public int Calls { get; private set; }

        public Task<JobEnvelope> BuildAsync(
            ClassStandardRecord standard, BootstrapMeta meta, CancellationToken ct = default)
        {
            Calls++;
            var env = new JobEnvelope
            {
                Summary = $"Envelope for {standard.LongTitle}.",
                ScopeStatement = standard.GenericScope,
            };

            env.KeyResponsibilities.Add(new EnvelopeResponsibility
            {
                Ordinal = 0,
                FunctionName = "PRIMARY",
                PctTime = 100,
            });

            return Task.FromResult(env);
        }
    }

    private static ClassStandardRecord S(string title, string grade = "Grade 21") => new()
    {
        LongTitle = title,
        PersProg = "PSS",
        Grade = grade,
        Flsa = "Exempt",
        Union = "99",
        GenericScope = $"Scope for {title}.",
        KeyResponsibilities = [$"Does {title} work."],
        Ksa = ["Knows the subject."],
        Education = ["Bachelor's degree."],
        Licenses = ["Driver's licence."],
    };

    private static TitleCode Tc(string code, string title, string source = "both") => new()
    {
        Code = code,
        Title = title,
        Source = source,
        Grade = "Grade 21",
        Family = "Research",
        Function = "Research Data Analysis",
        TitleKey = TitleNormalizer.TitleKey(title),
        TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
    };

    private static (Bootstrapper Boot, AppDbContext Db, StubEnvelopes Env) Build(
        ClassStandardRecord[] standards, TitleCode[] titleCodes)
    {
        var db = TestDbContextFactory.CreateInMemory();
        var env = new StubEnvelopes();
        var boot = new Bootstrapper(db, new StubStandards(standards), new StubTitleCodes(titleCodes), env);
        return (boot, db, env);
    }

    [Fact]
    public async Task A_standard_with_no_profile_is_a_candidate()
    {
        var (boot, db, _) = Build([S("Widget Analyst 2")], [Tc("111111", "Widget Analyst 2")]);
        using var _db = db;

        var candidates = (await boot.GetCandidatesAsync()).Candidates;

        candidates.Should().ContainSingle();
        candidates[0].Title.Should().Be("Widget Analyst 2");
        candidates[0].Code.Should().Be("111111");
        candidates[0].Family.Should().Be("Research");
    }

    [Fact]
    public async Task A_class_that_already_has_a_profile_is_not_a_candidate()
    {
        var (boot, db, _) = Build([S("Widget Analyst 2")], [Tc("111111", "Widget Analyst 2")]);
        using var _db = db;

        db.ClassProfiles.Add(new ClassProfile { Slug = "x", Title = "Widget Analyst 2", UcJobCode = "111111" });
        await db.SaveChangesAsync();

        (await boot.GetCandidatesAsync()).Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task An_existing_profile_is_matched_on_the_loose_key()
    {
        // "Does this class already have a profile?" is a same-class question, so a variant suffix or
        // an abbreviation must not make an existing profile invisible and let a duplicate be created.
        var (boot, db, _) = Build(
            [S("Research Data Analyst 2")],
            [Tc("006256", "Research Data Analyst 2")]);
        using var _db = db;

        db.ClassProfiles.Add(new ClassProfile { Slug = "x", Title = "Rsch Data Anl 2", UcJobCode = "006256" });
        await db.SaveChangesAsync();

        (await boot.GetCandidatesAsync()).Candidates.Should().BeEmpty("the abbreviated corpus spelling is the same class");
    }

    [Fact]
    public async Task Only_classes_on_UC_Davis_payroll_are_offered_and_the_rest_are_counted()
    {
        // UC Davis uses about 1,100 of UC's codes. An envelope for any other could never be used.
        var (boot, db, _) = Build(
            [S("Widget Analyst 2"), S("Widget Analyst 3"), S("Widget Analyst 4"), S("Gadget Planner 1")],
            [
                Tc("111111", "Widget Analyst 2"),
                Tc("111112", "Widget Analyst 3", source: "matrix"),
                Tc("111113", "Widget Analyst 4", source: "payroll_list"),
            ]);
        using var _db = db;

        var result = await boot.GetCandidatesAsync();

        result.Candidates.Select(c => c.Title).Should().Equal("Widget Analyst 2", "Widget Analyst 4");
        result.NotOnPayroll.Should().Be(1, "a matrix-only title is one UC Davis could use, not one it does");
        result.NoCodeMatch.Should().Be(1, "no UC job code at all");
    }

    [Theory]
    [InlineData("Widget Analyst 3", "isn't on UC Davis payroll")]
    [InlineData("Gadget Planner 1", "doesn't match a UC job code")]
    public async Task Creating_a_class_UC_Davis_cannot_use_is_refused(string title, string reason)
    {
        // The list hides these; creation refuses them too, so no other path (one class at a time,
        // an envelope moved in from another environment) can make one.
        var (boot, db, _) = Build(
            [S("Widget Analyst 3"), S("Gadget Planner 1")],
            [Tc("111112", "Widget Analyst 3", source: "matrix")]);
        using var _db = db;

        var refused = await FluentActions.Awaiting(() => boot.BootstrapAsync(title))
            .Should().ThrowAsync<BootstrapRefusedException>();

        refused.Which.Reason.Should().Be(BootstrapRefusal.NotOnPayroll);
        refused.Which.Message.Should().Contain(reason);
        (await db.ClassProfiles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_union_successor_is_built_from_its_own_standard_not_its_predecessors()
    {
        // The loose key drops the union suffix, so looking the standard up by it alone found the
        // retired "Financial Analyst 3" first and refused the live CX class as superseded — every
        // successor Create all exists to build.
        var (boot, db, _) = Build(
            [S("Financial Analyst 3"), S("Financial Analyst 3 CX", grade: "Grade 22")],
            [Tc("007709", "FINANCIAL ANL 3"), Tc("005183", "FINANCIAL ANL 3 CX")]);
        using var _db = db;

        var profile = await boot.BootstrapAsync("Financial Analyst 3 CX");

        profile.UcJobCode.Should().Be("005183");
        profile.Title.Should().Be("Financial Analyst 3 CX");
        profile.Distributions.Single(d => d.Field == DistributionField.SalaryGrade).Consensus.Should().Be("Grade 22");
    }

    [Fact]
    public async Task A_corpus_spelled_union_title_finds_its_standard_by_the_strict_key()
    {
        var (boot, db, _) = Build(
            [S("Project and Policy Analyst 4"), S("Project and Policy Analyst 4 RP")],
            [Tc("007399", "PROJECT POLICY ANL 4"), Tc("005258", "PROJECT POLICY ANL 4 RP")]);
        using var _db = db;

        var profile = await boot.BootstrapAsync("Project Policy Anl 4 Rp");

        profile.UcJobCode.Should().Be("005258");
        profile.Title.Should().Be("Project and Policy Analyst 4 RP");
    }

    [Fact]
    public async Task A_profile_under_a_superseded_code_does_not_hide_its_successors_standard()
    {
        // The loose key drops the union suffix, so a not-yet-retired "Financial Anl 3" profile would
        // otherwise count as having "Financial Analyst 3 CX" and the live class could never be built.
        var (boot, db, _) = Build(
            [S("Financial Analyst 3"), S("Financial Analyst 3 CX")],
            [Tc("007709", "FINANCIAL ANL 3"), Tc("005183", "FINANCIAL ANL 3 CX")]);
        using var _db = db;

        db.ClassProfiles.Add(new ClassProfile { Slug = "x", Title = "Financial Anl 3", UcJobCode = "007709" });
        await db.SaveChangesAsync();

        var candidates = (await boot.GetCandidatesAsync()).Candidates;

        candidates.Select(c => c.Code).Should().Equal("005183");
    }

    [Fact]
    public async Task A_standard_for_a_superseded_code_is_never_offered()
    {
        // THE guard. Offering it would recreate a class that browse and the classifier deliberately
        // hide. Nothing is lost: the RP successor has its own standard in this same list, carrying
        // the live code.
        var (boot, db, _) = Build(
            [S("PROJECT POLICY ANL 1"), S("PROJECT POLICY ANL 1 RP")],
            [Tc("007396", "PROJECT POLICY ANL 1"), Tc("005255", "PROJECT POLICY ANL 1 RP")]);
        using var _db = db;

        var candidates = (await boot.GetCandidatesAsync()).Candidates;

        candidates.Should().ContainSingle("only the live successor may be offered");
        candidates[0].Title.Should().Be("PROJECT POLICY ANL 1 RP");
        candidates[0].Code.Should().Be("005255");
    }

    [Fact]
    public async Task Bootstrapping_a_superseded_class_is_refused_with_a_message_naming_the_successor()
    {
        // Refuses rather than silently building under the successor's code: the two standards
        // differ, and the RP one is the right source to build from.
        var (boot, db, _) = Build(
            [S("PROJECT POLICY ANL 1"), S("PROJECT POLICY ANL 1 RP")],
            [Tc("007396", "PROJECT POLICY ANL 1"), Tc("005255", "PROJECT POLICY ANL 1 RP")]);
        using var _db = db;

        var act = () => boot.BootstrapAsync("PROJECT POLICY ANL 1");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*superseded by*")
            .WithMessage("*005255*")
            .WithMessage("*bootstrap that class instead*");
    }

    [Fact]
    public async Task Bootstrapping_persists_a_standard_derived_profile()
    {
        var (boot, db, env) = Build([S("Widget Analyst 2")], [Tc("111111", "Widget Analyst 2")]);
        using var _db = db;

        var profile = await boot.BootstrapAsync("Widget Analyst 2");

        env.Calls.Should().Be(1);
        profile.Slug.Should().Be("111111-widget-analyst-2");
        profile.UcJobCode.Should().Be("111111");
        profile.EnvelopeSource.Should().Be(EnvelopeSource.Standard);
        profile.CorpusSize.Should().Be(0, "there is no JD corpus — that is the whole point");
        profile.GeneratedNote.Should().Contain("no JD corpus yet");
        profile.Envelope.Should().NotBeNull();

        (await db.ClassProfiles.CountAsync()).Should().Be(1, "it must actually be persisted");
    }

    [Fact]
    public async Task Fixed_attributes_come_from_the_standard_as_single_observations()
    {
        var (boot, db, _) = Build([S("Widget Analyst 2", grade: "Grade 23")], [Tc("111111", "Widget Analyst 2")]);
        using var _db = db;

        var profile = await boot.BootstrapAsync("Widget Analyst 2");

        string? Consensus(DistributionField f) =>
            profile.Distributions.Single(d => d.Field == f).Consensus;

        Consensus(DistributionField.SalaryGrade).Should().Be("Grade 23", "the standard beats the reference");
        Consensus(DistributionField.FlsaStatus).Should().Be("Exempt");
        Consensus(DistributionField.UnionCode).Should().Be("99");

        // A standard says nothing about supervision, and "unknown" is a real observation rather than
        // an absent one — so it is stored as a null value WITH a count.
        var sup = profile.Distributions.Single(d => d.Field == DistributionField.Supervises);
        sup.Consensus.Should().BeNull();
        sup.Values.Should().ContainSingle();
        sup.Values[0].Value.Should().BeNull();
        sup.Values[0].Count.Should().Be(1);
    }

    [Fact]
    public async Task The_grade_falls_back_to_the_title_reference_when_the_standard_omits_it()
    {
        // Layout B never states a salary grade, so the reference is the authority for it.
        var (boot, db, _) = Build([S("Widget Analyst 2", grade: "")], [Tc("111111", "Widget Analyst 2")]);
        using var _db = db;

        var profile = await boot.BootstrapAsync("Widget Analyst 2");

        profile.Distributions.Single(d => d.Field == DistributionField.SalaryGrade)
            .Consensus.Should().Be("Grade 21", "from the title reference");
    }

    [Fact]
    public async Task Qualifications_are_recorded_at_frequency_one()
    {
        // A standard states each requirement once. Claiming anything else would fabricate corpus
        // evidence that does not exist.
        var (boot, db, _) = Build([S("Widget Analyst 2")], [Tc("111111", "Widget Analyst 2")]);
        using var _db = db;

        var profile = await boot.BootstrapAsync("Widget Analyst 2");

        profile.Qualifications.Should().NotBeEmpty();
        profile.Qualifications.Should().OnlyContain(q => q.Freq == 1);
        profile.Qualifications.Should().Contain(q => q.Kind == ProfileQualKind.License);
        profile.Qualifications.Should().Contain(q => q.Kind == ProfileQualKind.Education);
        profile.Qualifications.Should().Contain(q => q.Kind == ProfileQualKind.KsaMin);
    }

    [Fact]
    public async Task Bootstrapping_refuses_to_overwrite_an_existing_profile()
    {
        var (boot, db, _) = Build([S("Widget Analyst 2")], [Tc("111111", "Widget Analyst 2")]);
        using var _db = db;

        await boot.BootstrapAsync("Widget Analyst 2");
        var act = () => boot.BootstrapAsync("Widget Analyst 2");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task An_unknown_title_is_refused()
    {
        var (boot, db, _) = Build([S("Widget Analyst 2")], [Tc("111111", "Widget Analyst 2")]);
        using var _db = db;

        var act = () => boot.BootstrapAsync("Farm Laborer");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*No standard found*");
    }

    // A title with no resolvable code used to bootstrap under a "std-" slug, on the reasoning that
    // UC-systemwide titles were still worth having. Reversed (2026-10-09): UC Davis can't use a class
    // that isn't on its payroll, so creation now refuses it — see
    // Creating_a_class_UC_Davis_cannot_use_is_refused.

    // ------------------------------------------------------------------ coverage

    [Fact]
    public async Task Coverage_lists_active_jobs_with_no_class_and_whether_a_standard_covers_them()
    {
        var (boot, db, _) = Build(
            [S("Widget Analyst 2"), S("Widget Analyst 4")],
            [
                Tc("111111", "Widget Analyst 2"),
                Tc("111113", "Widget Analyst 3"),
                Tc("111114", "Widget Analyst 4", source: "matrix"),
                Tc("222222", "Gadget Planner 1"),
            ]);
        using var _db = db;
        db.ClassProfiles.Add(new ClassProfile { Slug = "222222-gadget-planner-1", UcJobCode = "222222", Title = "Gadget Planner 1" });
        await db.SaveChangesAsync();

        var active = await boot.GetCoverageAsync(active: true);

        active.Select(r => (r.Code, r.StandardTitle)).Should().Equal(("111111", "Widget Analyst 2"), ("111113", null));

        var notActive = await boot.GetCoverageAsync(active: false);
        notActive.Select(r => (r.Code, r.StandardTitle)).Should().Equal(("111114", "Widget Analyst 4"));
    }

    [Fact]
    public async Task A_job_not_active_at_UC_Davis_is_bootstrapped_only_when_asked_for_by_name()
    {
        var (boot, db, _) = Build([S("Widget Analyst 4"), S("Gadget Planner 1")], [Tc("111114", "Widget Analyst 4", source: "matrix")]);
        using var _db = db;

        await FluentActions.Awaiting(() => boot.BootstrapAsync("Widget Analyst 4"))
            .Should().ThrowAsync<BootstrapRefusedException>().Where(e => e.Reason == BootstrapRefusal.NotOnPayroll);

        var profile = await boot.BootstrapNotActiveAsync("Widget Analyst 4");
        profile.UcJobCode.Should().Be("111114");

        // A title matching no UC job code is still refused: there is no classification to build.
        await FluentActions.Awaiting(() => boot.BootstrapNotActiveAsync("Gadget Planner 1"))
            .Should().ThrowAsync<BootstrapRefusedException>().Where(e => e.Reason == BootstrapRefusal.NotOnPayroll);
    }
}

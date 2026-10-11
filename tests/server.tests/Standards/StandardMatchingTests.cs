using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Tests.Standards;

/// <summary>
/// Standards whose titles match no job code exactly get suggestions for a person to confirm. These
/// pin that suggestions never cross a level, union suffix or site; that a rejection hides the
/// pairing; and that a confirmed match gives the standard its code everywhere — surviving a
/// re-upload of the workbooks. Titles are real UC titles from the public reference.
/// </summary>
public class StandardMatchingTests
{
    private sealed class Titles(params TitleCode[] rows) : ITitleCodeService
    {
        private readonly TitleCodeIndex _index = new(rows);
        public Task<TitleCodeIndex> GetAsync(CancellationToken ct = default) => Task.FromResult(_index);
        public void Invalidate() { }
    }

    private static TitleCode Tc(string code, string title, string source = "both") => new()
    {
        Code = code,
        Title = title,
        Source = source,
        TitleKey = TitleNormalizer.TitleKey(title),
        TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
    };

    private static (AppDbContext Db, StandardMatching Matching, StandardsStore Store, Titles Titles) Harness(
        string[] standards, params TitleCode[] codes)
    {
        var db = TestDbContextFactory.CreateInMemory();
        var titles = new Titles(codes);
        var index = titles.GetAsync().Result;
        foreach (var s in standards)
        {
            // As the importer does: the code its title resolves to, if any.
            db.JobStandards.Add(StandardsStore.ToEntity(new ClassStandardRecord { LongTitle = s, Code = index.FindTitleCode(s)?.Code }));
        }

        db.SaveChanges();
        var store = new StandardsStore(db, new MemoryCache(new MemoryCacheOptions()));
        return (db, new StandardMatching(db, store, titles), store, titles);
    }

    [Fact]
    public async Task A_standard_that_matches_no_code_exactly_gets_suggestions_of_the_same_shape()
    {
        // A real typo in a real workbook: "Reqruitment".
        var (db, matching, _, _) = Harness(
            ["Admissions Reqruitment Supervisor 2"],
            Tc("004512", "ADMISSIONS RECRMT SUPV 2"),
            Tc("004511", "ADMISSIONS RECRMT SUPV 1"));
        using var _db = db;

        var review = await matching.ReviewAsync(includeNotActive: false);

        // Level 1 is a different classification: never suggested. (A union suffix is part of the
        // same shape rule — see The_shape_is_level_union_and_site — and a plain code beside its union
        // twin is superseded, so it is never offered at all.)
        review.Unmatched.Single().Suggestions.Select(s => s.UcJobCode).Should().Equal("004512");
    }

    [Fact]
    public async Task Codes_not_active_at_UC_Davis_are_suggested_only_on_request()
    {
        var (db, matching, _, _) = Harness(["Admissions Reqruitment Supervisor 1"], Tc("004511", "ADMISSIONS RECRMT SUPV 1", "matrix"));
        using var _db = db;

        (await matching.ReviewAsync(false)).Unmatched.Should().BeEmpty();
        (await matching.ReviewAsync(true)).Unmatched.Single().Suggestions.Single().Active.Should().BeFalse();
    }

    [Fact]
    public async Task A_confirmed_match_gives_the_standard_its_code_and_survives_a_reupload()
    {
        var (db, matching, store, titles) = Harness(["Admissions Reqruitment Supervisor 2"], Tc("004512", "ADMISSIONS RECRMT SUPV 2"));
        using var _db = db;
        var index = await titles.GetAsync();

        await matching.DecideAsync("Admissions Reqruitment Supervisor 2", "004512", isMatch: true, userId: null);

        var std = (await store.GetIndexAsync()).All.Single();
        StandardCodes.For(std, index)!.Code.Should().Be("004512");
        (await store.GetIndexAsync()).For("Admissions Recrmt Supv 2", "004512").Should().NotBeNull("the class finds its standard by code");
        (await matching.ReviewAsync(false)).Unmatched.Should().BeEmpty();
        (await matching.ReviewAsync(false)).Confirmed.Single().UcJobCode.Should().Be("004512");

        // Re-uploading replaces the standard row; the decision still applies.
        db.JobStandards.RemoveRange(db.JobStandards);
        db.JobStandards.Add(StandardsStore.ToEntity(new ClassStandardRecord { LongTitle = "Admissions Reqruitment Supervisor 2" }));
        await db.SaveChangesAsync();
        StandardCodes.For((await store.GetIndexAsync()).All.Single(), index)!.Code.Should().Be("004512");
    }

    [Fact]
    public async Task A_rejected_pairing_is_not_suggested_again_and_an_undo_returns_the_standard_to_review()
    {
        // Near titles, different jobs: the reason nothing is matched without a person.
        var (db, matching, _, _) = Harness(["Systems Administration Supervisor 1"], Tc("007374", "ADMIN SUPV 1"));
        using var _db = db;
        (await matching.ReviewAsync(false)).Unmatched.Single().Suggestions.Single().UcJobCode.Should().Be("007374");

        await matching.DecideAsync("Systems Administration Supervisor 1", "007374", isMatch: false, userId: null);
        var after = await matching.ReviewAsync(false);
        after.Unmatched.Should().BeEmpty();
        after.WithoutSuggestion.Should().Be(1);

        // Changing one's mind: a match replaces the rejection, and undoing it returns the suggestion.
        await matching.DecideAsync("Systems Administration Supervisor 1", "007374", isMatch: true, userId: null);
        await matching.UnmatchAsync("Systems Administration Supervisor 1");
        (await matching.ReviewAsync(false)).Unmatched.Should().ContainSingle();
    }

    [Fact]
    public async Task A_decision_must_name_a_real_code_and_a_real_standard()
    {
        var (db, matching, _, _) = Harness(["Admissions Reqruitment Supervisor 2"], Tc("004512", "ADMISSIONS RECRMT SUPV 2"));
        using var _db = db;

        await FluentActions.Awaiting(() => matching.DecideAsync("Admissions Reqruitment Supervisor 2", "999999", true, null))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => matching.DecideAsync("No Such Standard", "004512", true, null))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task An_exempt_standard_is_never_offered_a_non_exempt_code()
    {
        var (db, matching, _, _) = Harness(
            ["Clinical Research Coordinator Senior Exempt", "Clinical Research Coordinator Senior"],
            Tc("007889", "CLIN RSCH CRD SR NEX"),
            Tc("007888", "CLIN RSCH CRD SR EX"));
        using var _db = db;

        var review = await matching.ReviewAsync(includeNotActive: false);

        review.Unmatched.Single(u => u.StandardTitle.EndsWith("Exempt", StringComparison.Ordinal))
            .Suggestions.Select(s => s.UcJobCode).Should().Equal("007888");
        // States no status: either may be it.
        review.Unmatched.Single(u => u.StandardTitle.EndsWith("Senior", StringComparison.Ordinal))
            .Suggestions.Select(s => s.UcJobCode).Should().BeEquivalentTo(["007888", "007889"]);
    }

    [Fact]
    public async Task Per_diem_CEH_and_supervisor_manager_versions_are_never_offered_across()
    {
        var (db, matching, _, _) = Harness(
            ["Child Life Specialist 1 Exempt", "Patient Support Call Center Supervisor 1"],
            Tc("007873", "CHILD LIFE SPEC 1 EX"),
            Tc("005007", "CHILD LIFE SPEC 1 EX CEH"),
            Tc("009368", "CHILD LIFE SPEC 1 PD"),
            Tc("004643", "PAT SUPP CALL CTR SUPV 1 NEX"),
            Tc("005777", "PAT SUPP CALL CTR MGR 1"));
        using var _db = db;

        var review = await matching.ReviewAsync(includeNotActive: false);

        review.Unmatched.Single(u => u.StandardTitle.StartsWith("Child", StringComparison.Ordinal))
            .Suggestions.Select(s => s.UcJobCode).Should().Equal("007873");
        review.Unmatched.Single(u => u.StandardTitle.StartsWith("Patient", StringComparison.Ordinal))
            .Suggestions.Select(s => s.UcJobCode).Should().Equal("004643");
    }

    [Theory]
    [InlineData("clinical research coordinator senior exempt", "ex")]
    [InlineData("clinical research coordinator senior non exempt", "nex")]
    [InlineData("clinical research coordinator senior nex", "nex")]
    [InlineData("clinical research coordinator senior ex", "ex")]
    [InlineData("clinical research coordinator senior", null)]
    public void Exempt_status_reads_both_spellings(string key, string? flsa) =>
        StandardMatching.Flsa(key).Should().Be(flsa);

    [Theory]
    [InlineData("medical office service coordinator 3", "med ofc svc crd 3", "3|||")]
    [InlineData("analyst 3 cx", "anl 3 cx", "3|cx||")]
    [InlineData("social work hc supervisor 2", "x", "2||hc|supervisor")]
    [InlineData("communication network technical analyst 3 tx gf", "x", "3|gf tx||")]
    [InlineData("child life specialist 1 per diem", "x", "1|||pd")]
    [InlineData("child life specialist 1 ex ceh", "x", "1|||ceh")]
    [InlineData("patient support call center manager 1", "x", "1|||manager")]
    public void The_shape_is_level_union_and_site(string key, string _, string shape) =>
        StandardMatching.Shape(key).Should().Be(shape);

    [Fact]
    public async Task Payroll_abbreviations_are_suggested_but_never_matched_without_a_person()
    {
        // "AMBUL CARE ADMSTN CRD 3": ambul(atory), adm(ini)st(ratio)n — beyond the normalizer's list.
        var (db, matching, store, titles) = Harness(
            ["Ambulatory Care Administration Coordinator 3 CX"],
            Tc("004478", "AMBUL CARE ADMSTN CRD 3 CX"));
        using var _db = db;

        var suggestion = (await matching.ReviewAsync(false)).Unmatched.Single().Suggestions.Single();

        suggestion.UcJobCode.Should().Be("004478");
        suggestion.Score.Should().BeGreaterThan(0.75);
        StandardCodes.For((await store.GetIndexAsync()).All.Single(), await titles.GetAsync())
            .Should().BeNull("a suggestion is only a suggestion");
    }

    [Theory]
    [InlineData("ambul", "ambulatory", true)]
    [InlineData("admstn", "administration", true)]
    [InlineData("crd", "coordinator", true)]
    [InlineData("care", "care", true)]
    [InlineData("adm", "manager", false)]
    [InlineData("sr", "senior", false)]
    [InlineData("tech", "chemistry", false)]
    public void A_payroll_abbreviation_corresponds_to_its_word(string a, string b, bool corresponds) =>
        StandardMatching.Corresponds(a, b).Should().Be(corresponds);
}

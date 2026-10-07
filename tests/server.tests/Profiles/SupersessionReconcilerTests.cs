using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Profiles;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Tests.Profiles;

/// <summary>
/// Retiring profiles left under a code that a union successor has superseded.
///
/// Every create path already refuses superseded codes, so this only matters when the derivation
/// widens and retires a code that already has data. The risks are losing that data — a saved JD
/// orphaned, a corpus JD dropped — or leaving the dead class visible, so those are what is tested.
/// </summary>
public class SupersessionReconcilerTests
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

    private static TitleCode Tc(string code, string title) => new()
    {
        Code = code,
        Title = title,
        Source = "both",
        TitleKey = TitleNormalizer.TitleKey(title),
        TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
    };

    /// <summary>FINANCIAL ANL 3 (007709) is retired by FINANCIAL ANL 3 CX (005183).</summary>
    private static readonly TitleCode[] Reference =
    [
        Tc("007709", "FINANCIAL ANL 3"),
        Tc("005183", "FINANCIAL ANL 3 CX"),
        Tc("111111", "WIDGET ANL 2"),
    ];

    private static readonly ClassStandardRecord[] Standards =
    [
        new() { LongTitle = "Financial Analyst 3", PersProg = "PSS" },
        new() { LongTitle = "Financial Analyst 3 CX", PersProg = "PSS" },
    ];

    private static (SupersessionReconciler Sut, AppDbContext Db) Build()
    {
        var db = TestDbContextFactory.CreateInMemory();
        return (new SupersessionReconciler(db, new StubTitleCodes(Reference), new StubStandards(Standards)), db);
    }

    private static ClassProfile Profile(string slug, string code, string title, EnvelopeSource source, int corpus = 0) =>
        new() { Slug = slug, UcJobCode = code, Title = title, EnvelopeSource = source, CorpusSize = corpus };

    private static JobDescription Jd(string file, string code, string title) =>
        new() { SourceFile = file, UcJobCode = code, UcJobTitle = title };

    private static AuthoredJd Saved(int profileId, string code) =>
        new() { ClassProfileId = profileId, UcJobCode = code, Title = "Saved JD" };

    [Fact]
    public async Task A_standard_only_profile_with_nothing_attached_is_removed()
    {
        var (sut, db) = Build();
        using var _db = db;
        db.ClassProfiles.Add(Profile("007709-financial-analyst-3", "007709", "Financial Analyst 3", EnvelopeSource.Standard));
        await db.SaveChangesAsync();

        var result = await sut.RetireAsync();

        result.Profiles.Should().ContainSingle().Which.Action.Should().Be(RetireAction.Remove);
        db.ClassProfiles.Should().BeEmpty("the successor is built from its own standard, not this one's");
    }

    [Fact]
    public async Task A_corpus_profile_is_reidentified_as_its_successor_and_its_jds_refiled()
    {
        var (sut, db) = Build();
        using var _db = db;
        db.ClassProfiles.Add(Profile("007709-financial-anl-3", "007709", "Financial Anl 3", EnvelopeSource.Claude, corpus: 2));
        db.JobDescriptions.AddRange(Jd("a.html", "007709", "FINANCIAL ANL 3"), Jd("b.html", "007709", "FINANCIAL ANL 3"));
        await db.SaveChangesAsync();

        var result = await sut.RetireAsync();

        result.RefiledJds.Should().Be(2);
        var item = result.Profiles.Should().ContainSingle().Subject;
        item.Action.Should().Be(RetireAction.Reidentify);

        // The slug a corpus ingest of the successor would derive, so a rebuild lands on this row.
        var expectedSlug = IngestPipeline.SlugForClass("005183", "FINANCIAL ANL 3 CX");
        item.SuccessorSlug.Should().Be(expectedSlug);

        var profile = db.ClassProfiles.Single();
        profile.Slug.Should().Be(expectedSlug);
        profile.UcJobCode.Should().Be("005183");
        profile.Title.Should().Be("Financial Anl 3 Cx");
        profile.EnvelopeSource.Should().Be(EnvelopeSource.Claude, "the learned envelope came from these same JDs");

        db.JobDescriptions.Should().OnlyContain(j =>
            j.UcJobCode == "005183" && j.OriginalUcJobCode == "007709" && j.UcJobTitle == "FINANCIAL ANL 3 CX");
    }

    [Fact]
    public async Task Saved_jds_follow_the_class_into_an_existing_successor()
    {
        var (sut, db) = Build();
        using var _db = db;
        var dead = Profile("007709-financial-anl-3", "007709", "Financial Anl 3", EnvelopeSource.Claude, corpus: 1);
        var live = Profile("005183-financial-anl-3-cx", "005183", "Financial Anl 3 Cx", EnvelopeSource.Claude, corpus: 4);
        db.ClassProfiles.AddRange(dead, live);
        db.JobDescriptions.Add(Jd("a.html", "007709", "FINANCIAL ANL 3"));
        await db.SaveChangesAsync();
        db.AuthoredJds.Add(Saved(dead.Id, "007709"));
        await db.SaveChangesAsync();

        var result = await sut.RetireAsync();

        var item = result.Profiles.Should().ContainSingle().Subject;
        item.Action.Should().Be(RetireAction.Merge);
        item.SuccessorSlug.Should().Be(live.Slug);
        item.AuthoredJds.Should().Be(1);
        item.NeedsRebuild.Should().BeTrue("the successor was built without the refiled JD");

        db.ClassProfiles.Select(p => p.Slug).Should().Equal(live.Slug);
        var saved = db.AuthoredJds.Single();
        saved.ClassProfileId.Should().Be(live.Id);
        saved.UcJobCode.Should().Be("005183");
    }

    [Fact]
    public async Task A_standard_profile_with_a_saved_jd_is_kept_and_says_where_its_envelope_came_from()
    {
        var (sut, db) = Build();
        using var _db = db;
        var dead = Profile("007709-financial-analyst-3", "007709", "Financial Analyst 3", EnvelopeSource.Standard);
        db.ClassProfiles.Add(dead);
        await db.SaveChangesAsync();
        db.AuthoredJds.Add(Saved(dead.Id, "007709"));
        await db.SaveChangesAsync();

        var result = await sut.RetireAsync();

        result.Profiles.Single().Action.Should().Be(RetireAction.Reidentify);
        var profile = db.ClassProfiles.Single();
        profile.Title.Should().Be("Financial Analyst 3 CX", "the successor's own standard names it");
        profile.Slug.Should().Be(Bootstrapper.SlugFor("005183", "Financial Analyst 3 CX"));
        profile.GeneratedNote.Should().Contain("Financial Analyst 3 (007709)");
        db.AuthoredJds.Single().UcJobCode.Should().Be("005183");
    }

    [Fact]
    public async Task Preview_changes_nothing_and_a_second_run_finds_nothing()
    {
        var (sut, db) = Build();
        using var _db = db;
        db.ClassProfiles.AddRange(
            Profile("007709-financial-anl-3", "007709", "Financial Anl 3", EnvelopeSource.Claude, corpus: 1),
            Profile("111111-widget-anl-2", "111111", "Widget Anl 2", EnvelopeSource.Claude, corpus: 1));
        db.JobDescriptions.Add(Jd("a.html", "007709", "FINANCIAL ANL 3"));
        await db.SaveChangesAsync();

        var preview = await sut.PreviewAsync();
        preview.Profiles.Should().ContainSingle("a class under a live code is left alone");
        preview.RefiledJds.Should().Be(1);
        db.ChangeTracker.Clear();
        (await db.ClassProfiles.Select(p => p.UcJobCode).ToListAsync()).Should().BeEquivalentTo(["007709", "111111"]);

        await sut.RetireAsync();
        db.ChangeTracker.Clear();

        var again = await sut.PreviewAsync();
        again.Profiles.Should().BeEmpty();
        again.RefiledJds.Should().Be(0);
    }

    [Fact]
    public async Task The_stored_supersession_map_is_rewritten_from_the_derivation()
    {
        var (sut, db) = Build();
        using var _db = db;
        db.Supersessions.Add(new Supersession { FromCode = "999999", ToCode = "888888" });
        await db.SaveChangesAsync();

        await sut.RetireAsync();

        db.Supersessions.Select(s => s.FromCode + ">" + s.ToCode).Should().Equal("007709>005183");
    }
}

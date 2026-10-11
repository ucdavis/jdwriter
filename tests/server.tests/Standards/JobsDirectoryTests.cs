using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Server.Core.Ai;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Standards;
using Server.Core.Titles;
using Server.Tests.Profiles;

namespace Server.Tests.Standards;

/// <summary>
/// The jobs directory — every UC job code and what it is attached to — and standards entered by
/// hand for a job, which link to it whatever their title. Real UC titles; synthetic standard text.
/// </summary>
public class JobsDirectoryTests
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

    private static (AppDbContext Db, JobsDirectory Jobs, ManualStandards Manual) Harness(params TitleCode[] codes)
    {
        var db = TestDbContextFactory.CreateInMemory();
        var titles = new Titles(codes);
        var store = new StandardsStore(db, new MemoryCache(new MemoryCacheOptions()));
        return (db, new JobsDirectory(db, store, titles), new ManualStandards(db, store, titles, new FakeStructuredLlm()));
    }

    [Fact]
    public async Task Each_job_shows_its_class_and_its_standard()
    {
        var (db, jobs, _) = Harness(
            Tc("009605", "LAB AST 1"),
            Tc("004724", "FARM LABORER"),
            Tc("000685", "ACCOUNTING MGR 1", "matrix"));
        using var _db = db;
        db.ClassProfiles.Add(new ClassProfile { Slug = "009605-lab-ast-1", UcJobCode = "009605", Title = "Lab Ast 1", CorpusSize = 155 });
        db.JobStandards.Add(StandardsStore.ToEntity(new ClassStandardRecord { LongTitle = "Farm Laborer" }));
        await db.SaveChangesAsync();

        var active = await jobs.ListAsync("active");

        active.Select(j => j.Code).Should().Equal("004724", "009605");
        var lab = active.Single(j => j.Code == "009605");
        lab.Class!.CorpusSize.Should().Be(155);
        lab.Standard.Should().BeNull();
        var farm = active.Single(j => j.Code == "004724");
        farm.Class.Should().BeNull();
        farm.Standard!.How.Should().Be("title");

        (await jobs.ListAsync("inactive")).Single().Code.Should().Be("000685");
        (await jobs.ListAsync("all")).Should().HaveCount(3);
    }

    [Fact]
    public async Task A_superseded_job_shows_its_successors_class()
    {
        var (db, jobs, _) = Harness(Tc("007709", "FINANCIAL ANL 3"), Tc("005183", "FINANCIAL ANL 3 CX"));
        using var _db = db;
        db.ClassProfiles.Add(new ClassProfile { Slug = "005183-financial-analyst-3-cx", UcJobCode = "005183", Title = "Financial Analyst 3 CX" });
        db.JobStandards.Add(StandardsStore.ToEntity(new ClassStandardRecord { LongTitle = "Financial Analyst 3 CX", KeyResponsibilities = ["Analyzes."] }));
        await db.SaveChangesAsync();

        var old = (await jobs.ListAsync("all")).Single(j => j.Code == "007709");

        old.SupersededBy.Should().Be("005183");
        old.Class!.Slug.Should().Be("005183-financial-analyst-3-cx");
        old.Standard!.Title.Should().Be("Financial Analyst 3 CX", "tied to its successor's standard, as to its class");
        old.Standard.FromSuccessor.Should().BeTrue();
    }

    [Fact]
    public async Task A_standard_entered_by_hand_is_linked_to_its_job_whatever_its_title()
    {
        var (db, jobs, manual) = Harness(Tc("009536", "ANML HEALTH TCHN 2"));
        using var _db = db;

        await manual.SaveAsync("009536", new StandardDraft
        {
            Title = "Veterinary Technician II (synthetic)",
            Scope = "Provides technical animal care.",
            KeyResponsibilities = ["Monitors patients", "  ", "Records treatments"],
            Education = ["High school diploma or equivalent experience."],
        }, userId: null);

        var job = (await jobs.ListAsync("active")).Single();
        job.Standard!.Title.Should().Be("Veterinary Technician II (synthetic)");
        job.Standard.How.Should().Be("hand");
        var saved = db.JobStandards.Single();
        saved.Code.Should().Be("009536");
        saved.Items.Count(i => i.Kind == StandardItemKind.KeyResponsibility).Should().Be(2, "blank lines are dropped");

        // Saving again replaces the standard rather than adding a second.
        await manual.SaveAsync("009536", new StandardDraft { Title = "Veterinary Technician II (synthetic)", Scope = "Revised." }, null);
        db.JobStandards.Should().ContainSingle();
    }

    [Fact]
    public async Task A_hand_entered_standard_needs_a_real_code_and_a_title()
    {
        var (db, _, manual) = Harness(Tc("009536", "ANML HEALTH TCHN 2"));
        using var _db = db;

        await FluentActions.Awaiting(() => manual.SaveAsync("999999", new StandardDraft { Title = "X" }, null))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => manual.SaveAsync("009536", new StandardDraft { Title = " " }, null))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task A_blank_standard_is_called_out_and_so_is_a_class_built_from_one()
    {
        var (db, jobs, _) = Harness(Tc("009605", "LAB AST 1"), Tc("004724", "FARM LABORER"));
        using var _db = db;
        db.JobStandards.Add(StandardsStore.ToEntity(new ClassStandardRecord { LongTitle = "Farm Laborer", GenericScope = "Scope only." }));
        db.JobStandards.Add(StandardsStore.ToEntity(new ClassStandardRecord { LongTitle = "Lab Ast 1", KeyResponsibilities = ["Does lab work."] }));
        db.ClassProfiles.Add(new ClassProfile { Slug = "004724-farm-laborer", UcJobCode = "004724", Title = "Farm Laborer", EnvelopeSource = EnvelopeSource.Standard });
        await db.SaveChangesAsync();

        var all = await jobs.ListAsync("active");

        var farm = all.Single(j => j.Code == "004724");
        farm.Standard!.Blank.Should().BeTrue("matched, but with no responsibilities");
        farm.Class!.FromBlankStandard.Should().BeTrue();
        var lab = all.Single(j => j.Code == "009605");
        lab.Standard!.Blank.Should().BeFalse();
    }

    [Fact]
    public async Task Classes_built_from_blank_standards_are_removed_unless_saved_JDs_hang_off_them()
    {
        var db = TestDbContextFactory.CreateInMemory();
        using var _db = db;
        var store = new StandardsStore(db, new MemoryCache(new MemoryCacheOptions()));
        db.JobStandards.Add(StandardsStore.ToEntity(new ClassStandardRecord { LongTitle = "Farm Laborer", GenericScope = "Scope only." }));
        db.JobStandards.Add(StandardsStore.ToEntity(new ClassStandardRecord { LongTitle = "Garden Worker", GenericScope = "Scope only." }));
        db.JobStandards.Add(StandardsStore.ToEntity(new ClassStandardRecord { LongTitle = "Lab Ast 1", KeyResponsibilities = ["Does lab work."] }));
        var blank = new ClassProfile { Slug = "004724-farm-laborer", UcJobCode = "004724", Title = "Farm Laborer", EnvelopeSource = EnvelopeSource.Standard };
        var withJd = new ClassProfile { Slug = "004725-garden-worker", UcJobCode = "004725", Title = "Garden Worker", EnvelopeSource = EnvelopeSource.Standard };
        var full = new ClassProfile { Slug = "009605-lab-ast-1", UcJobCode = "009605", Title = "Lab Ast 1", EnvelopeSource = EnvelopeSource.Standard };
        var learned = new ClassProfile { Slug = "x-farm", UcJobCode = "004726", Title = "Farm Laborer", EnvelopeSource = EnvelopeSource.Claude, CorpusSize = 3 };
        db.ClassProfiles.AddRange(blank, withJd, full, learned);
        await db.SaveChangesAsync();
        db.AuthoredJds.Add(new AuthoredJd { ClassProfileId = withJd.Id, Title = "Garden Worker", UcJobCode = "004725" });
        await db.SaveChangesAsync();
        var cleanup = new BlankStandardClasses(db, store);

        (await cleanup.PreviewAsync()).Select(c => c.Slug).Should().Equal("004724-farm-laborer", "004725-garden-worker");

        var result = await cleanup.RemoveAsync();

        result.Removed.Select(c => c.Slug).Should().Equal("004724-farm-laborer");
        result.Kept.Single().SavedJds.Should().Be(1);
        db.ClassProfiles.Select(p => p.Slug).Should().BeEquivalentTo(["004725-garden-worker", "009605-lab-ast-1", "x-farm"]);
        (await cleanup.RemoveAsync()).Removed.Should().BeEmpty("a second run finds nothing");
    }
}

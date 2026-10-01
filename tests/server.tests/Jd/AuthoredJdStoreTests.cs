using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Jd;

namespace Server.Tests.Jd;

/// <summary>
/// Saved JDs: every assembly is kept, re-assembling in one session revises one record, and an
/// author can never overwrite — or list — someone else's.
/// </summary>
public class AuthoredJdStoreTests
{
    private static AssembledJd Assembled(string workingTitle, params (string Name, int Pct)[] fns) => new()
    {
        Slug = "009605-lab-ast-1",
        Title = "Lab Ast 1",
        WorkingTitle = workingTitle,
        Department = "Plant Sciences",
        UcJobCode = "009605",
        Jd = new Server.Core.Jd.Jd
        {
            JobSummary = $"Summary for {workingTitle}",
            KeyResponsibilities = [.. fns.Select(f => new JdKeyResponsibility { FunctionName = f.Name, PctTime = f.Pct, Duties = ["d1"] })],
            Education = ["High school"],
        },
        UnallocatedPct = 100 - fns.Sum(f => f.Pct),
        ComplianceEdits = [new ComplianceEditRecord { Section = "summary", Source = ComplianceEditSource.Rule, Before = "a", After = "b", Reason = "r" }],
    };

    private static async Task<(AppDbContext Db, ClassProfile Profile, int Alice, int Bob)> Seed()
    {
        var db = TestDbContextFactory.CreateInMemory();
        var profile = new ClassProfile { Slug = "009605-lab-ast-1", UcJobCode = "009605", EnvelopeSource = EnvelopeSource.Claude };
        var alice = new AppUser { NameIdentifier = "a", DisplayName = "Alice" };
        var bob = new AppUser { NameIdentifier = "b", DisplayName = "Bob" };
        db.AddRange(profile, alice, bob);
        await db.SaveChangesAsync();
        return (db, profile, alice.Id, bob.Id);
    }

    [Fact]
    public async Task An_assembly_is_saved_with_its_status_and_provenance()
    {
        var (db, profile, alice, _) = await Seed();
        var store = new AuthoredJdStore(db);

        var id = await store.SaveAsync(Assembled("Lab Tech", ("Bench work", 70)), new BuildInputs(), profile, alice, null);

        var saved = (await store.GetAsync(id))!.Value;
        saved.OwnerId.Should().Be(alice);
        saved.Jd.Status.Should().Be(AuthoredJdStatus.Draft);
        saved.Jd.UnallocatedPct.Should().Be(30);
        saved.Jd.CreatedBy.Should().Be("Alice");
        saved.Jd.Slug.Should().Be("009605-lab-ast-1");
        saved.Jd.ComplianceEdits.Should().ContainSingle();
        (await db.AuthoredJds.SingleAsync()).EnvelopeSource.Should().Be(EnvelopeSource.Claude);
    }

    [Fact]
    public async Task Reassembling_your_own_jd_revises_it_in_place()
    {
        var (db, profile, alice, _) = await Seed();
        var store = new AuthoredJdStore(db);
        var id = await store.SaveAsync(Assembled("Lab Tech", ("Bench work", 70)), new BuildInputs(), profile, alice, null);

        var again = await store.SaveAsync(
            Assembled("Lab Tech II", ("Bench work", 70), ("Records", 30)), new BuildInputs(), profile, alice, id);

        again.Should().Be(id);
        (await db.AuthoredJds.CountAsync()).Should().Be(1);
        var saved = (await store.GetAsync(id))!.Value.Jd;
        saved.WorkingTitle.Should().Be("Lab Tech II");
        saved.Status.Should().Be(AuthoredJdStatus.Ready);
        saved.Jd.KeyResponsibilities.Select(r => r.FunctionName).Should().Equal("Bench work", "Records");
        (await db.AuthoredJdResponsibilities.CountAsync()).Should().Be(2, "the old content is replaced, not appended to");
    }

    [Fact]
    public async Task Someone_elses_id_creates_a_new_record_instead_of_overwriting_theirs()
    {
        var (db, profile, alice, bob) = await Seed();
        var store = new AuthoredJdStore(db);
        var alicesId = await store.SaveAsync(Assembled("Alice's JD", ("A", 100)), new BuildInputs(), profile, alice, null);

        var bobsId = await store.SaveAsync(Assembled("Bob's JD", ("B", 100)), new BuildInputs(), profile, bob, alicesId);

        bobsId.Should().NotBe(alicesId);
        (await store.GetAsync(alicesId))!.Value.Jd.WorkingTitle.Should().Be("Alice's JD");
    }

    [Fact]
    public async Task An_author_lists_only_their_own_and_null_lists_everyone()
    {
        var (db, profile, alice, bob) = await Seed();
        var store = new AuthoredJdStore(db);
        await store.SaveAsync(Assembled("Alice 1", ("A", 100)), new BuildInputs(), profile, alice, null);
        await store.SaveAsync(Assembled("Bob 1", ("B", 100)), new BuildInputs(), profile, bob, null);

        (await store.ListAsync(alice)).Select(j => j.WorkingTitle).Should().Equal("Alice 1");
        (await store.ListAsync(null)).Should().HaveCount(2);
    }
}

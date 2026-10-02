using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Jd;
using Server.Core.Profiles;
using Server.Core.Standards;
using Server.Core.Titles;
using Server.Tests.Profiles;

namespace Server.Tests.Jd;

/// <summary>
/// A finished JD joins its class's corpus only when it is Ready, carries a change from the
/// envelope, and was not judged out of envelope — and leaves it again when deleted.
/// </summary>
public class CorpusContributionTests
{
    private static JobEnvelope Envelope() => new EnvelopeWire
    {
        Summary = "s",
        KeyResponsibilities =
        [
            new JdKeyResponsibility { FunctionName = "Plant care", PctTime = 70, Duties = ["Waters plants", "Prunes"] },
            new JdKeyResponsibility { FunctionName = "Records", PctTime = 30, Duties = ["Logs data"] },
        ],
        Education = ["High school"],
        MinQualifications = ["Reliable"],
    }.ToEntity();

    /// <summary>Exactly what the envelope offers — the "unchanged" build.</summary>
    private static BuildInputs Unchanged() => new()
    {
        KeptResponsibilities =
        [
            new JdKeyResponsibility { FunctionName = "Plant care", PctTime = 70, Duties = ["Waters plants", "Prunes"] },
            new JdKeyResponsibility { FunctionName = "Records", PctTime = 30, Duties = ["Logs data"] },
        ],
        KeptEducation = ["High school"],
        KeptMinKSA = ["Reliable"],
    };

    private static AssembledJd Assembled(int unallocated = 0) => new()
    {
        Title = "Lab Ast 1",
        UcJobCode = "009605",
        WorkingTitle = "Greenhouse Tech",
        Jd = new Server.Core.Jd.Jd
        {
            JobSummary = "Supports greenhouse research.",
            KeyResponsibilities = [new JdKeyResponsibility { FunctionName = "Plant care", PctTime = 100 - unallocated, Duties = ["Waters plants"] }],
            Education = ["High school", "Or equivalent"],
            MinKSA = ["Reliable"],
        },
        UnallocatedPct = unallocated,
    };

    // ------------------------------------------------------------------ what counts as a change

    [Fact]
    public void A_build_that_keeps_the_envelope_exactly_is_unchanged() =>
        CorpusContribution.Differs(Unchanged(), Envelope()).Should().BeFalse();

    [Theory]
    [InlineData("added item")]
    [InlineData("pct moved")]
    [InlineData("duty dropped")]
    [InlineData("function reworded")]
    [InlineData("responsibility dropped")]
    [InlineData("qualification dropped")]
    public void Any_edit_is_a_change(string edit)
    {
        var b = Unchanged();
        switch (edit)
        {
            case "added item": b.AddedItems = ["Runs the plant sale"]; break;
            case "pct moved": b.KeptResponsibilities[0].PctTime = 60; b.KeptResponsibilities[1].PctTime = 40; break;
            case "duty dropped": b.KeptResponsibilities[0].Duties = ["Waters plants"]; break;
            case "function reworded": b.KeptResponsibilities[1].FunctionName = "Record keeping"; break;
            case "responsibility dropped": b.KeptResponsibilities.RemoveAt(1); break;
            case "qualification dropped": b.KeptMinKSA = []; break;
        }

        CorpusContribution.Differs(b, Envelope()).Should().BeTrue();
    }

    [Fact]
    public void Whitespace_alone_is_not_a_change()
    {
        var b = Unchanged();
        b.KeptResponsibilities[0].FunctionName = "  Plant care ";

        CorpusContribution.Differs(b, Envelope()).Should().BeFalse();
    }

    // ------------------------------------------------------------------ the gate

    [Fact]
    public void A_changed_ready_in_envelope_jd_is_added()
    {
        var b = Unchanged();
        b.AddedItems = ["Runs the plant sale"];

        CorpusContribution.Exclusion(Assembled(), b, Envelope(), EnvelopeVerdict.InEnvelope).Should().BeNull();
        CorpusContribution.Exclusion(Assembled(), b, Envelope(), EnvelopeVerdict.Borderline)
            .Should().BeNull("only an out-of-envelope verdict keeps it out");
    }

    [Fact]
    public void It_is_kept_out_when_not_ready_out_of_envelope_unchecked_or_unchanged()
    {
        var changed = Unchanged();
        changed.AddedItems = ["Runs the plant sale"];

        CorpusContribution.Exclusion(Assembled(unallocated: 10), changed, Envelope(), EnvelopeVerdict.InEnvelope)
            .Should().Be(CorpusContribution.NotPublishable);
        CorpusContribution.Exclusion(Assembled(), changed, Envelope(), EnvelopeVerdict.OutOfEnvelope)
            .Should().Be(CorpusContribution.OutOfEnvelope);
        CorpusContribution.Exclusion(Assembled(), changed, Envelope(), verdict: null)
            .Should().Be(CorpusContribution.Unchecked);
        CorpusContribution.Exclusion(Assembled(), Unchanged(), Envelope(), EnvelopeVerdict.InEnvelope)
            .Should().Be(CorpusContribution.Unchanged);
    }

    [Fact]
    public void The_corpus_record_is_the_finished_text_and_carries_no_position()
    {
        var record = CorpusContribution.ToCorpusRecord(Assembled(), authoredJdId: 7);

        record.Origin.Should().Be(CorpusOrigin.Authored);
        record.AuthoredJdId.Should().Be(7);
        record.UcJobCode.Should().Be("009605");
        record.UcPathPositionNumber.Should().BeEmpty();
        record.Responsibilities.Single().Pct.Should().Be(100);
        record.Qualifications.Single(q => q.Kind == JdQualificationKind.Education).Text
            .Should().Be("High school; Or equivalent");
    }

    // ------------------------------------------------------------------ store and queue

    private sealed class Titles : ITitleCodeService
    {
        private readonly TitleCodeIndex _index = new([]);
        public Task<TitleCodeIndex> GetAsync(CancellationToken ct = default) => Task.FromResult(_index);
        public void Invalidate() { }
    }

    private sealed class NoStandards : IStandardLookup
    {
        public Task<ClassStandardRecord?> ForTitleAsync(string title, CancellationToken ct = default) =>
            Task.FromResult<ClassStandardRecord?>(null);
    }

    [Fact]
    public async Task Sync_adds_the_copy_a_revision_can_remove_it_and_deleting_the_jd_removes_it()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"contrib_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var profile = new ClassProfile
        {
            Slug = "009605-lab-ast-1", UcJobCode = "009605", Title = "Lab Ast 1",
            Envelope = Envelope(), LastIngestedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        var author = new AppUser { NameIdentifier = "a" };
        db.AddRange(profile, author);
        await db.SaveChangesAsync();
        var store = new AuthoredJdStore(db);

        var changed = Unchanged();
        changed.AddedItems = ["Runs the plant sale"];
        var assembled = Assembled();
        var id = await store.SaveAsync(assembled, changed, profile, author.Id, null);
        await store.SyncCorpusAsync(id, assembled, changed, profile, EnvelopeVerdict.InEnvelope);

        assembled.InCorpus.Should().BeTrue();
        (await db.JobDescriptions.SingleAsync()).AuthoredJdId.Should().Be(id);

        // It now shows as new evidence waiting for the class.
        var llm = new Server.Tests.Profiles.FakeStructuredLlm { HasApiKey = false };
        var uploads = new CorpusUploads(db, new Titles(), new IngestPipeline(db, new Titles(), llm,
            new Consolidator(llm), new EnvelopeSynthesizer(llm, new NoStandards()), NullLogger<IngestPipeline>.Instance));
        (await uploads.PendingAsync()).Single().NewAuthored.Should().Be(1);

        // A revision back to the plain envelope no longer qualifies; the copy is withdrawn.
        var again = Assembled();
        await store.SaveAsync(again, Unchanged(), profile, author.Id, id);
        await store.SyncCorpusAsync(id, again, Unchanged(), profile, EnvelopeVerdict.InEnvelope);
        again.InCorpus.Should().BeFalse();
        again.CorpusNote.Should().Be(CorpusContribution.Unchanged);
        (await db.JobDescriptions.CountAsync()).Should().Be(0);

        // Qualifies again, then the author deletes the JD: the copy goes with it.
        await store.SyncCorpusAsync(id, assembled, changed, profile, EnvelopeVerdict.InEnvelope);
        (await db.JobDescriptions.CountAsync()).Should().Be(1);
        (await store.DeleteAsync(id, author.Id, isAdmin: false)).Should().BeTrue();
        (await db.JobDescriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Rebuilding_the_class_takes_the_new_jd_in_and_clears_it_from_the_waiting_list()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"contrib_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var profile = new ClassProfile
        {
            Slug = "009605-lab-ast-1", UcJobCode = "009605", Title = "Lab Ast 1",
            Envelope = Envelope(), LastIngestedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        var author = new AppUser { NameIdentifier = "a" };
        db.AddRange(profile, author);
        await db.SaveChangesAsync();
        var store = new AuthoredJdStore(db);
        var changed = Unchanged();
        changed.AddedItems = ["Runs the plant sale"];
        var assembled = Assembled();
        var id = await store.SaveAsync(assembled, changed, profile, author.Id, null);
        await store.SyncCorpusAsync(id, assembled, changed, profile, EnvelopeVerdict.InEnvelope);

        var llm = new Server.Tests.Profiles.FakeStructuredLlm { HasApiKey = false };
        var uploads = new CorpusUploads(db, new Titles(), new IngestPipeline(db, new Titles(), llm,
            new Consolidator(llm), new EnvelopeSynthesizer(llm, new NoStandards()), NullLogger<IngestPipeline>.Instance));
        (await uploads.PendingAsync()).Should().ContainSingle();

        var rebuilt = await uploads.IngestAsync("009605");

        rebuilt.CorpusSize.Should().Be(1);
        rebuilt.Id.Should().Be(profile.Id);
        (await uploads.PendingAsync()).Should().BeEmpty();
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Tests.Data;

/// <summary>
/// A class's last-changed stamp follows a change anywhere in its graph, however deep, so an
/// environment package can keep the newer version. Each test uses a fresh context for the edit, as
/// a request would.
/// </summary>
public class ChangeStampsTests
{
    private readonly string _name = $"stamps_{Guid.NewGuid():N}";

    private AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_name).Options);

    private static readonly DateTimeOffset Long = DateTimeOffset.UtcNow.AddYears(-1);

    private async Task<int> SeedAsync()
    {
        await using var db = Db();
        var profile = new ClassProfile
        {
            Slug = "009605-lab-ast-1", UcJobCode = "009605", Title = "Lab Ast 1",
            Envelope = new JobEnvelope
            {
                Summary = "Supports research.",
                KeyResponsibilities = [new EnvelopeResponsibility { FunctionName = "PLANT CARE", PctTime = 100, Duties = [new EnvelopeDuty { Text = "Waters plants." }] }],
                Items = [new EnvelopeListItem { Kind = EnvelopeListKind.Education, Text = "High school." }],
            },
        };
        db.ClassProfiles.Add(profile);
        await db.SaveChangesAsync();
        profile.UpdatedAt.Should().NotBeNull("a new class is stamped, children and all, in one save");

        // Back-date it, as an older change, with stamping off.
        using (db.KeepingChangeStamps())
        {
            profile.UpdatedAt = Long;
            await db.SaveChangesAsync();
        }
        return profile.Id;
    }

    private async Task<DateTimeOffset?> StampAsync(int id)
    {
        await using var db = Db();
        return (await db.ClassProfiles.SingleAsync(p => p.Id == id)).UpdatedAt;
    }

    [Fact]
    public async Task Editing_one_duty_deep_in_the_envelope_stamps_its_class()
    {
        var id = await SeedAsync();
        await using (var db = Db())
        {
            var duty = await db.Set<EnvelopeDuty>().SingleAsync();
            duty.Text = "Waters and feeds plants.";
            await db.SaveChangesAsync();
        }

        (await StampAsync(id)).Should().BeAfter(Long);
    }

    [Fact]
    public async Task Removing_or_adding_an_envelope_item_stamps_its_class()
    {
        var id = await SeedAsync();
        await using (var db = Db())
        {
            db.Set<EnvelopeListItem>().Remove(await db.Set<EnvelopeListItem>().SingleAsync());
            await db.SaveChangesAsync();
        }

        var afterRemove = await StampAsync(id);
        afterRemove.Should().BeAfter(Long);

        await using (var db = Db())
        {
            var envelope = await db.Set<JobEnvelope>().SingleAsync();
            db.Set<EnvelopeListItem>().Add(new EnvelopeListItem { JobEnvelopeId = envelope.Id, Kind = EnvelopeListKind.Education, Text = "Degree." });
            await db.SaveChangesAsync();
        }

        (await StampAsync(id)).Should().BeOnOrAfter(afterRemove!.Value);
    }

    [Fact]
    public async Task A_package_import_keeps_the_stamps_it_brings()
    {
        var id = await SeedAsync();
        await using (var db = Db())
        {
            using (db.KeepingChangeStamps())
            {
                (await db.Set<EnvelopeDuty>().SingleAsync()).Text = "Imported wording.";
                await db.SaveChangesAsync();
            }

            db.KeepChangeStamps.Should().BeFalse("the scope ends with the import");
        }

        (await StampAsync(id)).Should().Be(Long);
    }

    [Fact]
    public async Task A_change_to_a_standard_item_stamps_the_standard()
    {
        await using (var db = Db())
        {
            db.JobStandards.Add(new JobStandard { LongTitle = "Lab Assistant 1", Items = [new JobStandardItem { Text = "Duty." }] });
            await db.SaveChangesAsync();
            using (db.KeepingChangeStamps())
            {
                (await db.JobStandards.SingleAsync()).UpdatedAt = Long;
                await db.SaveChangesAsync();
            }
        }

        await using (var db = Db())
        {
            (await db.JobStandardItems.SingleAsync()).Text = "Revised duty.";
            await db.SaveChangesAsync();
        }

        await using (var check = Db())
        {
            (await check.JobStandards.SingleAsync()).UpdatedAt.Should().BeAfter(Long);
        }
    }
}

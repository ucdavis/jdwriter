using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Ingest;

namespace Server.Tests.Ingest;

/// <summary>The list of every JD in the corpus: what brought each in, its class, and whether it's in the envelope yet.</summary>
public class CorpusDirectoryTests
{
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase($"corpusdir_{Guid.NewGuid():N}").Options);

    [Fact]
    public async Task Lists_every_JD_newest_first_marking_those_added_since_their_class_was_built()
    {
        await using var db = Db();
        var built = DateTimeOffset.UtcNow.AddDays(-1);
        db.ClassProfiles.Add(new ClassProfile { UcJobCode = "009605", Slug = "009605-lab-ast-1", Title = "Lab Ast 1", LastIngestedAt = built });
        db.JobDescriptions.AddRange(
            new JobDescription
            {
                SourceFile = "a.html", UcJobCode = "009605", UcJobTitle = "LAB AST 1", Origin = CorpusOrigin.Export,
                Responsibilities = [new JdResponsibility { FunctionName = "A", Pct = 100, Duties = [new JdDuty { Text = "One." }, new JdDuty { Text = "Two." }] }],
            },
            new JobDescription
            {
                SourceFile = "import/document-1", UcJobCode = "009605", UcJobTitle = "LAB AST 1", Origin = CorpusOrigin.Imported,
                AddedAt = built.AddHours(2),
                Responsibilities = [new JdResponsibility { FunctionName = "A", Pct = 60 }, new JdResponsibility { FunctionName = "B", Pct = 40 }],
            },
            new JobDescription { SourceFile = "import/document-2", UcJobCode = "004724", UcJobTitle = "FARM LABORER", Origin = CorpusOrigin.Imported, AddedAt = built.AddHours(1) });
        await db.SaveChangesAsync();

        var rows = await new CorpusDirectory(db).ListAsync();

        rows.Select(r => r.SourceFile).Should().Equal("import/document-1", "import/document-2", "a.html");
        rows[0].Should().BeEquivalentTo(new { Origin = "imported", Pending = true, ClassSlug = "009605-lab-ast-1", Functions = 2, PctTotal = 100 });
        rows[1].Should().BeEquivalentTo(new { Pending = true, ClassSlug = (string?)null });
        rows[2].Should().BeEquivalentTo(new { Origin = "export", Pending = false, Duties = 2 });
    }

    [Fact]
    public async Task Gets_one_JD_whole()
    {
        await using var db = Db();
        db.JobDescriptions.Add(new JobDescription
        {
            SourceFile = "import/document-1", UcJobCode = "009605", WorkingTitle = "Greenhouse Technician", Origin = CorpusOrigin.Imported,
            Responsibilities = [new JdResponsibility { FunctionName = "PLANT CARE", Pct = 100, Duties = [new JdDuty { Text = "Waters plants." }] }],
        });
        await db.SaveChangesAsync();
        var id = (await db.JobDescriptions.SingleAsync()).Id;

        var jd = await new CorpusDirectory(db).GetAsync(id);

        jd!.Record.WorkingTitle.Should().Be("Greenhouse Technician");
        jd.Record.Responsibilities.Single().Duties.Should().Equal("Waters plants.");
        (await new CorpusDirectory(db).GetAsync(id + 1)).Should().BeNull();
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Server.Core.Domain;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Tests.Standards;

/// <summary>
/// Uploading standards workbooks MERGES: their standards are added or replace the same exact
/// title, and everything else in the store survives — uploading one family never wipes the rest.
/// Uses the real committed Job Builder workbooks.
/// </summary>
public class StandardsMergeTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "inputs", "standards-real");

    private sealed class Titles : ITitleCodeService
    {
        private readonly TitleCodeIndex _index = new([]);
        public Task<TitleCodeIndex> GetAsync(CancellationToken ct = default) => Task.FromResult(_index);
        public void Invalidate() { }
    }

    private static (Server.Core.Data.AppDbContext Db, StandardsImporter Importer) Harness()
    {
        var db = TestDbContextFactory.CreateInMemory();
        return (db, new StandardsImporter(db, new Titles(), new StandardsStore(db, new MemoryCache(new MemoryCacheOptions()))));
    }

    private static (string, byte[]) Workbook(string name) => (name, File.ReadAllBytes(Path.Combine(Dir, name)));

    [Trait("Category", "StandardsWorkbooks")]
    [Fact]
    public async Task Uploading_adds_standards_and_leaves_the_rest_of_the_store_alone()
    {
        var (db, importer) = Harness();
        db.JobStandards.Add(StandardsStore.ToEntity(new ClassStandardRecord { LongTitle = "Unrelated Standard 1", Grade = "Grade 1" }));
        await db.SaveChangesAsync();

        var report = await importer.MergeAsync([Workbook("Job Standard_SideBySide (3).xlsx")], userId: null);

        report.Files.Single().Result.Should().Be("added");
        report.Added.Should().Be(report.Files.Single().Standards).And.BeGreaterThan(0);
        report.Updated.Should().Be(0);
        (await db.JobStandards.AnyAsync(s => s.LongTitle == "Unrelated Standard 1")).Should().BeTrue();
        report.Total.Should().Be(report.Added + 1);
        (await db.StandardsWorkbooks.SingleAsync()).StandardsFound.Should().Be(report.Added);
    }

    [Trait("Category", "StandardsWorkbooks")]
    [Fact]
    public async Task A_standard_with_the_same_title_is_replaced_not_duplicated()
    {
        var (db, importer) = Harness();
        var first = await importer.MergeAsync([Workbook("Job Standard_SideBySide (3).xlsx")], null);
        var title = (await db.JobStandards.FirstAsync()).LongTitle;
        var stale = await db.JobStandards.Include(s => s.Items).FirstAsync(s => s.LongTitle == title);
        stale.GenericScope = "stale wording";
        await db.SaveChangesAsync();
        db.StandardsWorkbooks.RemoveRange(db.StandardsWorkbooks); // allow the same bytes again
        await db.SaveChangesAsync();

        var again = await importer.MergeAsync([Workbook("Job Standard_SideBySide (3).xlsx")], null);

        again.Added.Should().Be(0);
        again.Updated.Should().Be(first.Added);
        (await db.JobStandards.CountAsync(s => s.LongTitle == title)).Should().Be(1);
        (await db.JobStandards.SingleAsync(s => s.LongTitle == title)).GenericScope.Should().NotBe("stale wording");
    }

    [Trait("Category", "StandardsWorkbooks")]
    [Fact]
    public async Task Duplicates_and_unusable_files_get_their_own_verdicts()
    {
        var (_, importer) = Harness();
        var wb = Workbook("Job Standard_SideBySide (3).xlsx");

        var report = await importer.MergeAsync(
        [
            wb,
            ("copy.xlsx", wb.Item2),
            ("notes.txt", "x"u8.ToArray()),
            ("broken.xlsx", "not a zip"u8.ToArray()),
        ], null);

        report.Files.Select(f => f.Result).Should().Equal("added", "duplicate", "failed", "failed");
        report.Files[2].Error.Should().Contain(".xlsx");
        report.Files[3].Error.Should().Contain("Excel");
    }
}

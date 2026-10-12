using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Titles;

namespace Server.Tests.Titles;

/// <summary>An admin's call on whether a code is active at UC Davis, over the payroll reference.</summary>
public class CodeActivityTests
{
    private static TitleCode Tc(string code, string title, string source) => new()
    {
        Code = code, Title = title, Source = source,
        TitleKey = TitleNormalizer.TitleKey(title), TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
    };

    private static (AppDbContext Db, TitleCodeService Titles, CodeActivity Activity) Harness()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"activity_{Guid.NewGuid():N}").Options);
        db.TitleCodes.AddRange(
            Tc("009605", "LAB AST 1", "both"),
            Tc("004501", "ACAD ACHIEVEMENT CNSLR 3", "matrix"),
            Tc("004502", "ACAD ACHIEVEMENT CNSLR 3 SV", "matrix"));
        db.SaveChanges();
        var titles = new TitleCodeService(db, new MemoryCache(new MemoryCacheOptions()));
        return (db, titles, new CodeActivity(db, titles));
    }

    [Fact]
    public async Task Marking_a_code_overrides_payroll_everywhere_the_index_is_read_and_agreeing_again_drops_the_override()
    {
        var (db, titles, activity) = Harness();
        (await titles.GetAsync()).FindByCode("009605")!.IsInUse.Should().BeTrue();

        var off = await activity.SetAsync("9605", active: false, userId: null);

        off.OnPayroll.Should().BeTrue();
        (await titles.GetAsync()).FindByCode("009605")!.IsInUse.Should().BeFalse();
        (await db.CodeActiveOverrides.SingleAsync()).IsActive.Should().BeFalse();

        await activity.SetAsync("009605", active: true, userId: null);

        (await titles.GetAsync()).FindByCode("009605")!.IsInUse.Should().BeTrue();
        (await db.CodeActiveOverrides.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Reports_a_supersession_it_creates_or_ends()
    {
        var (_, titles, activity) = Harness();
        await activity.SetAsync("004501", active: true, userId: null);
        (await titles.GetAsync()).IsSuperseded("004501").Should().BeFalse();

        var on = await activity.SetAsync("004502", active: true, userId: null);

        on.SupersessionChanges.Should().ContainSingle().Which.Should().Contain("is now superseded by");
        (await titles.GetAsync()).IsSuperseded("004501").Should().BeTrue();

        var off = await activity.SetAsync("004502", active: false, userId: null);
        off.SupersessionChanges.Should().ContainSingle().Which.Should().Contain("no longer superseded");
    }

    [Fact]
    public async Task An_unknown_code_is_refused()
    {
        var (_, _, activity) = Harness();
        await activity.Invoking(a => a.SetAsync("123456", true, null)).Should().ThrowAsync<ArgumentException>();
    }
}

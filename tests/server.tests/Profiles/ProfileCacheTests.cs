using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Profiles;

namespace Server.Tests.Profiles;

public class ProfileCacheTests
{
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static List<ClassProfile> Profiles(string title) => [new ClassProfile { Slug = title.ToLowerInvariant(), Title = title }];

    [Fact]
    public async Task Requests_together_share_one_load_and_later_ones_are_served_from_memory()
    {
        var cache = new ProfileCache();
        var loads = 0;
        var gate = new TaskCompletionSource<List<ClassProfile>>();
        Task<List<ClassProfile>> Load()
        {
            loads++;
            return gate.Task;
        }

        var first = cache.GetAsync(Load);
        var second = cache.GetAsync(Load);
        gate.SetResult(Profiles("Analyst 2"));

        (await first).Should().BeSameAs(await second);
        (await cache.GetAsync(Load))[0].Title.Should().Be("Analyst 2");
        loads.Should().Be(1);
    }

    [Fact]
    public async Task A_change_drops_it_and_a_load_that_overlapped_the_change_is_not_kept()
    {
        var cache = new ProfileCache();
        var gate = new TaskCompletionSource<List<ClassProfile>>();
        var during = cache.GetAsync(() => gate.Task);

        cache.Invalidate();
        gate.SetResult(Profiles("Before the change"));
        (await during)[0].Title.Should().Be("Before the change");

        var after = await cache.GetAsync(() => Task.FromResult(Profiles("After the change")));
        after[0].Title.Should().Be("After the change");
    }

    [Fact]
    public async Task It_expires_so_outside_writes_are_seen_and_idle_memory_is_freed()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = new ProfileCache(clock);
        await cache.GetAsync(() => Task.FromResult(Profiles("Old")));

        clock.Advance(ProfileCache.MaxAge - TimeSpan.FromMinutes(1));
        (await cache.GetAsync(() => Task.FromResult(Profiles("New"))))[0].Title.Should().Be("Old");
        clock.Advance(TimeSpan.FromMinutes(2));
        (await cache.GetAsync(() => Task.FromResult(Profiles("New"))))[0].Title.Should().Be("New");
    }

    [Fact]
    public async Task A_save_that_touches_a_profile_table_drops_it_and_other_saves_do_not()
    {
        var cache = new ProfileCache();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid():N}")
            .AddInterceptors(new ProfileCacheInvalidator(cache))
            .Options;
        await using var db = new AppDbContext(options);
        var loads = 0;
        Task<List<ClassProfile>> Load()
        {
            loads++;
            return Task.FromResult(Profiles("Analyst 2"));
        }

        await cache.GetAsync(Load);
        db.AppUsers.Add(new AppUser { NameIdentifier = "someone" });
        await db.SaveChangesAsync();
        await cache.GetAsync(Load);
        loads.Should().Be(1, "a save to another table leaves the profiles held");

        db.ClassProfiles.Add(new ClassProfile { Slug = "analyst-2", Title = "Analyst 2", UcJobCode = "000001" });
        await db.SaveChangesAsync();
        await cache.GetAsync(Load);
        loads.Should().Be(2, "a save to a profile table drops them");

        var fn = new ProfileFunction { Name = "Reporting" };
        (await db.ClassProfiles.Include(p => p.Functions).SingleAsync()).Functions.Add(fn);
        await db.SaveChangesAsync();
        await cache.GetAsync(Load);
        loads.Should().Be(3, "so does a change deep in a profile's graph");
    }

    /// <summary>
    /// The invalidator's list is hand-written. Every table in a profile's graph — everything that
    /// belongs to a ClassProfile, however deep — must be on it, or a change there would go unseen.
    /// </summary>
    [Fact]
    public void The_invalidator_knows_every_table_in_a_profiles_graph()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var found = new HashSet<Type>();
        var queue = new Queue<Microsoft.EntityFrameworkCore.Metadata.IEntityType>([db.Model.FindEntityType(typeof(ClassProfile))!]);
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (!found.Add(type.ClrType))
            {
                continue;
            }

            // Down to what this type owns: navigations whose foreign key is on the other side.
            foreach (var nav in type.GetNavigations().Where(n => !n.IsOnDependent))
            {
                queue.Enqueue(nav.TargetEntityType);
            }
        }

        found.Should().BeEquivalentTo(ProfileCacheInvalidator.GraphTypes);
    }
}

using System.Security.Claims;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core.Access;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Services;

namespace Server.Tests.Access;

/// <summary>
/// Two roles: Author for everyone, Admin for a whitelisted campus login. These pin who becomes an
/// admin and who cannot, and that the configured bootstrap admin cannot be removed from the app.
/// </summary>
public class AdminAccessTests
{
    private static IConfiguration Config(string? bootstrap = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Admin:BootstrapLoginIds"] = bootstrap,
        }).Build();

    private static ClaimsPrincipal Principal(string id, string username, string scheme = "AuthenticationTypes.Federation") =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim(ClaimTypes.Name, "Someone"),
            new Claim("preferred_username", username),
        ], scheme));

    private static async Task<string[]> RolesFor(AppDbContext db, IConfiguration config, ClaimsPrincipal p)
    {
        var service = new UserService(NullLogger<UserService>.Instance, db, new AdminAccess(db, config));
        var updated = await service.UpdateUserPrincipalIfNeeded(p) ?? p;
        return [.. updated.FindAll(ClaimTypes.Role).Select(c => c.Value).Order()];
    }

    // ------------------------------------------------------------------ login ids

    [Theory]
    [InlineData("ndlewis", "ndlewis")]
    [InlineData("  NDLewis ", "ndlewis")]
    [InlineData("ndlewis@ucdavis.edu", "ndlewis")]
    [InlineData("NDLEWIS@UCDAVIS.EDU", "ndlewis")]
    [InlineData("j.doe-2", "j.doe-2")]
    public void A_login_id_is_normalized(string input, string expected) =>
        CampusLogin.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ndlewis@gmail.com")]
    [InlineData("nd lewis")]
    [InlineData("-leading")]
    [InlineData("ndlewis@ucdavis.edu.evil.com")]
    public void Anything_else_is_refused(string input) =>
        CampusLogin.Normalize(input).Should().BeNull();

    [Fact]
    public void The_login_id_comes_only_from_a_name_on_an_allowed_domain()
    {
        CampusLogin.FromPrincipal(Principal("1", "ndlewis@ucdavis.edu"), [CampusLogin.Domain])
            .Should().Be("ndlewis");
        CampusLogin.FromPrincipal(Principal("1", "ndlewis@elsewhere.edu"), [CampusLogin.Domain])
            .Should().BeNull("only the campus domain makes the local part a campus login");
    }

    // ------------------------------------------------------------------ roles

    [Fact]
    public async Task Everyone_is_an_author_and_only_the_whitelist_is_admin()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var config = Config();
        await new AdminAccess(db, config).GrantAsync("ndlewis", null);

        (await RolesFor(db, config, Principal("a", "ndlewis@ucdavis.edu")))
            .Should().Equal(AppRoles.Admin, AppRoles.Author);
        (await RolesFor(db, config, Principal("b", "someone@ucdavis.edu")))
            .Should().Equal(AppRoles.Author);
    }

    [Fact]
    public async Task A_matching_name_on_another_domain_is_not_an_admin()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var config = Config("ndlewis");

        (await RolesFor(db, config, Principal("x", "ndlewis@elsewhere.edu")))
            .Should().Equal(AppRoles.Author);
    }

    [Fact]
    public async Task The_sandbox_domain_counts_only_for_the_local_sandbox_scheme()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var config = Config("sample");

        (await RolesFor(db, config, Principal("s1", "sample@example.test", LocalAuthentication.Scheme)))
            .Should().Contain(AppRoles.Admin);
        (await RolesFor(db, config, Principal("s2", "sample@example.test")))
            .Should().Equal(AppRoles.Author);
    }

    [Fact]
    public async Task Revoking_takes_effect_on_the_next_request()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var config = Config();
        var access = new AdminAccess(db, config);
        await access.GrantAsync("jdoe", null);
        var p = Principal("j", "jdoe@ucdavis.edu");
        (await RolesFor(db, config, p)).Should().Contain(AppRoles.Admin);

        await access.RevokeAsync("jdoe");

        (await RolesFor(db, config, p)).Should().Equal(AppRoles.Author);
    }

    [Fact]
    public async Task Signing_in_records_the_login_id()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await RolesFor(db, Config(), Principal("u1", "jdoe@ucdavis.edu"));

        (await db.AppUsers.SingleAsync()).LoginId.Should().Be("jdoe");
    }

    // ------------------------------------------------------------------ whitelist management

    [Fact]
    public async Task A_configured_admin_is_listed_and_cannot_be_removed_in_the_app()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var access = new AdminAccess(db, Config("ndlewis, jdoe"));

        var list = await access.ListAsync();
        list.Select(e => (e.LoginId, e.FromConfiguration)).Should().Equal(("jdoe", true), ("ndlewis", true));

        var revoke = () => access.RevokeAsync("ndlewis");
        await revoke.Should().ThrowAsync<InvalidOperationException>().WithMessage("*configuration*");
        (await access.IsAdminAsync("ndlewis")).Should().BeTrue();
    }

    [Fact]
    public async Task An_admin_cannot_remove_themselves()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var access = new AdminAccess(db, Config());
        await access.GrantAsync("jdoe", null);

        var revoke = () => access.RevokeAsync("JDoe", actingLoginId: "jdoe");

        await revoke.Should().ThrowAsync<InvalidOperationException>().WithMessage("*remove yourself*");
        (await access.IsAdminAsync("jdoe")).Should().BeTrue();
    }

    [Fact]
    public async Task Granting_normalizes_and_is_idempotent()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var access = new AdminAccess(db, Config());

        (await access.GrantAsync(" JDoe@UCDavis.edu ", null)).Should().Be("jdoe");
        await access.GrantAsync("jdoe", null);

        (await db.AdminGrants.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Granting_something_that_is_not_a_campus_login_is_refused_with_a_message()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var grant = () => new AdminAccess(db, Config()).GrantAsync("jdoe@gmail.com", null);

        await grant.Should().ThrowAsync<ArgumentException>().WithMessage("*not a UC Davis login ID*");
    }

    [Fact]
    public async Task Someone_can_be_made_admin_before_they_ever_sign_in()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var config = Config();
        await new AdminAccess(db, config).GrantAsync("newperson", null);

        var listed = (await new AdminAccess(db, config).ListAsync()).Single();
        listed.DisplayName.Should().BeNull("they have not signed in yet");

        (await RolesFor(db, config, Principal("np", "newperson@ucdavis.edu"))).Should().Contain(AppRoles.Admin);
    }
}

using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Server.Controllers;
using Server.Core.Access;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Tests.Access;

public class AppAccessTests
{
    private static AdminAccess Admins(AppDbContext db, string? bootstrap = null) =>
        new(db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:BootstrapLoginIds"] = bootstrap }).Build());

    private static UserController Me(AppDbContext db, string oid, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, oid) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new UserController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) } },
        };
    }

    private static JsonElement Json(IActionResult result) =>
        JsonSerializer.SerializeToElement(((ObjectResult)result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public async Task Access_is_the_admins_plus_the_approved_logins()
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        var access = new AppAccess(db, Admins(db, "boss"));

        (await access.HasAccessAsync("boss")).Should().BeTrue();
        (await access.HasAccessAsync("jdoe")).Should().BeFalse();
        (await access.HasAccessAsync(null)).Should().BeFalse();

        (await access.GrantAsync(" JDoe@UCDavis.edu ", null)).Should().Be("jdoe");
        (await access.HasAccessAsync("jdoe")).Should().BeTrue();

        await access.RevokeAsync("jdoe");
        (await access.HasAccessAsync("jdoe")).Should().BeFalse();

        var notALogin = () => access.GrantAsync("someone@gmail.com", null);
        await notALogin.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Someone_without_access_asks_once_sees_that_they_asked_and_approval_resolves_it()
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        db.AppUsers.Add(new AppUser { NameIdentifier = "oid-j", LoginId = "jdoe", DisplayName = "J Doe", LastSeenAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var access = new AppAccess(db, Admins(db));

        Json(await Me(db, "oid-j").Me(default)).GetProperty("accessRequestedAt").ValueKind.Should().Be(JsonValueKind.Null);

        await Me(db, "oid-j").RequestAccess(new UserController.AccessRequestBody("Plant Sciences, writing two JDs."), default);
        await Me(db, "oid-j").RequestAccess(new UserController.AccessRequestBody("Plant Sciences, writing three JDs."), default);

        var request = await db.ProblemReports.SingleAsync();
        request.Should().BeEquivalentTo(new { Kind = ProblemReport.Access, Message = "Plant Sciences, writing three JDs.", Resolved = false });
        Json(await Me(db, "oid-j").Me(default)).GetProperty("accessRequestedAt").ValueKind.Should().NotBe(JsonValueKind.Null);

        var (_, waiting) = await access.ListAsync();
        waiting.Should().ContainSingle().Which.Should().BeEquivalentTo(new { LoginId = "jdoe", DisplayName = "J Doe" });
        waiting[0].RequestedAt.Should().NotBeNull();

        await access.GrantAsync("jdoe", null);
        (await db.ProblemReports.SingleAsync()).Should().BeEquivalentTo(new { Resolved = true, Note = "Access granted." });
        var (approved, stillWaiting) = await access.ListAsync();
        approved.Should().ContainSingle().Which.DisplayName.Should().Be("J Doe");
        stillWaiting.Should().BeEmpty();
    }

    [Fact]
    public async Task Someone_with_access_is_told_so_rather_than_filing_a_request()
    {
        await using var db = TestDbContextFactory.CreateInMemory();

        var result = Json(await Me(db, "oid-a", AppRoles.Author).RequestAccess(new UserController.AccessRequestBody(null), default));

        result.GetProperty("hasAccess").GetBoolean().Should().BeTrue();
        (await db.ProblemReports.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_access_request_is_approved_from_the_reports_inbox()
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        db.AppUsers.Add(new AppUser { NameIdentifier = "oid-j", LoginId = "jdoe" });
        await db.SaveChangesAsync();
        await Me(db, "oid-j").RequestAccess(new UserController.AccessRequestBody("Please."), default);
        var id = (await db.ProblemReports.SingleAsync()).Id;
        var access = new AppAccess(db, Admins(db));
        var reports = new ReportsController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) } },
        };

        (await reports.ApproveAccess(id, access, default)).Should().BeOfType<OkObjectResult>();

        (await access.HasAccessAsync("jdoe")).Should().BeTrue();
        (await db.ProblemReports.SingleAsync()).Resolved.Should().BeTrue();
    }

    /// <summary>
    /// The allow-list works because every endpoint requires a role, and only approved people have
    /// one. A controller that asked only for a signed-in user would let anyone at UC Davis in. Only
    /// the account's own endpoints (to see your access and ask for it) and sign-in may do that.
    /// </summary>
    [Fact]
    public void Every_endpoint_but_the_users_own_account_and_sign_in_requires_a_role()
    {
        var open = new List<string>();
        foreach (var c in typeof(Program).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            if (c == typeof(UserController) || c.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            {
                continue;
            }

            var classRoles = c.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Any(a => !string.IsNullOrEmpty(a.Roles));
            foreach (var m in c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any()))
            {
                var methodRoles = m.GetCustomAttributes<AuthorizeAttribute>().Any(a => !string.IsNullOrEmpty(a.Roles));
                if (m.GetCustomAttribute<AllowAnonymousAttribute>() != null || !(classRoles || methodRoles))
                {
                    open.Add($"{c.Name}.{m.Name}");
                }
            }
        }

        open.Should().BeEmpty();
    }
}

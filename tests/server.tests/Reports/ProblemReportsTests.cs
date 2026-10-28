using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Tests.Reports;

public class ProblemReportsTests
{
    private static ReportsController Controller(AppDbContext db, string userAgent = "Mozilla/5.0 (Macintosh) Chrome/141")
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "oid-1")], "test")),
        };
        http.Request.Headers.UserAgent = userAgent;
        return new ReportsController(db) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static async Task<AppDbContext> DbWithUser()
    {
        var db = TestDbContextFactory.CreateInMemory();
        db.AppUsers.Add(new AppUser { NameIdentifier = "oid-1", DisplayName = "Sam Lee", Email = "slee@example.edu" });
        await db.SaveChangesAsync();
        return db;
    }

    private static JsonElement Json(IActionResult result) =>
        JsonSerializer.SerializeToElement(((ObjectResult)result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public async Task A_report_is_kept_with_who_sent_it_from_where_and_in_what_browser()
    {
        await using var db = await DbWithUser();

        var sent = await Controller(db).Send(new ReportsController.NewReport("request", "  Please add a print button.  ", "/class/abc?q=secret#top"), default);

        sent.Should().BeOfType<OkObjectResult>();
        var report = await db.ProblemReports.Include(r => r.CreatedBy).SingleAsync();
        report.Should().BeEquivalentTo(new
        {
            Kind = "Request",
            Message = "Please add a print button.",
            Page = "/class/abc",
            Browser = "Mozilla/5.0 (Macintosh) Chrome/141",
            Resolved = false,
        });
        report.CreatedBy!.DisplayName.Should().Be("Sam Lee");
    }

    [Fact]
    public async Task An_empty_or_overlong_report_is_refused_and_an_unknown_kind_is_a_bug()
    {
        await using var db = await DbWithUser();
        var controller = Controller(db);

        (await controller.Send(new ReportsController.NewReport("Bug", "   ", "/"), default)).Should().BeOfType<BadRequestObjectResult>();
        (await controller.Send(new ReportsController.NewReport("Bug", new string('x', ProblemReport.MaxMessage + 1), "/"), default))
            .Should().BeOfType<BadRequestObjectResult>();
        await controller.Send(new ReportsController.NewReport("<script>", "Something broke.", "/"), default);

        (await db.ProblemReports.SingleAsync()).Kind.Should().Be("Bug");
    }

    [Fact]
    public async Task Admins_list_open_and_resolved_reports_newest_first_and_resolve_them_with_a_note()
    {
        await using var db = await DbWithUser();
        var controller = Controller(db);
        await controller.Send(new ReportsController.NewReport("Bug", "First.", "/a"), default);
        await controller.Send(new ReportsController.NewReport("Question", "Second.", "/b"), default);

        var all = Json(await controller.List(null, default));
        all.GetProperty("open").GetInt32().Should().Be(2);
        all.GetProperty("reports")[0].GetProperty("message").GetString().Should().Be("Second.");
        all.GetProperty("reports")[0].GetProperty("from").GetString().Should().Be("Sam Lee");

        var first = (await db.ProblemReports.SingleAsync(r => r.Message == "First.")).Id;
        await controller.SetStatus(first, new ReportsController.ReportStatus(true, "Fixed in #90."), default);

        Json(await controller.List("open", default)).GetProperty("reports").GetArrayLength().Should().Be(1);
        var resolved = Json(await controller.List("resolved", default)).GetProperty("reports")[0];
        resolved.GetProperty("note").GetString().Should().Be("Fixed in #90.");
        resolved.GetProperty("resolvedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        Json(await controller.OpenCount(default)).GetProperty("open").GetInt32().Should().Be(1);

        await controller.SetStatus(first, new ReportsController.ReportStatus(false, null), default);
        var reopened = await db.ProblemReports.SingleAsync(r => r.Id == first);
        reopened.Should().BeEquivalentTo(new { Resolved = false, ResolvedAt = (DateTimeOffset?)null, Note = "Fixed in #90." });
    }

    [Fact]
    public void Anyone_signed_in_can_send_but_only_admins_can_read_or_resolve()
    {
        typeof(ReportsController).GetCustomAttribute<AuthorizeAttribute>(inherit: false)!.Roles.Should().Be(AppRoles.Author);
        foreach (var name in new[] { nameof(ReportsController.List), nameof(ReportsController.OpenCount), nameof(ReportsController.SetStatus) })
        {
            typeof(ReportsController).GetMethod(name)!.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be(AppRoles.Admin, name);
        }

        typeof(ReportsController).GetMethod(nameof(ReportsController.Send))!.GetCustomAttribute<AuthorizeAttribute>().Should().BeNull();
    }
}

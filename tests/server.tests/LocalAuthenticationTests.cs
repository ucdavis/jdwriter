using FluentAssertions;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Server.Controllers;
using Server.Helpers;
using Server.Services;

namespace Server.Tests;

public class LocalAuthenticationTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("test")]
    [InlineData("Staging")]
    public void Local_auth_is_rejected_outside_development(string environment)
    {
        var configure = () => new ServiceCollection().AddAuthenticationServices(
            Configuration(local: "true"), new TestEnvironment(environment));

        configure.Should().Throw<InvalidOperationException>().WithMessage("*only allowed*Development*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task Entra_remains_the_default_when_local_auth_is_not_enabled(string? local)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthenticationServices(Configuration(local, "11111111-1111-1111-1111-111111111111"),
            new TestEnvironment("Development"));
        await using var provider = services.BuildServiceProvider();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        (await schemes.GetDefaultAuthenticateSchemeAsync())!.Name.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
        (await schemes.GetDefaultChallengeSchemeAsync())!.Name.Should().Be(OpenIdConnectDefaults.AuthenticationScheme);
        (await schemes.GetSchemeAsync(LocalAuthentication.Scheme)).Should().BeNull();
    }

    [Fact]
    public void Entra_still_requires_a_real_client_id()
    {
        var configure = () => new ServiceCollection().AddAuthenticationServices(
            Configuration(), new TestEnvironment("Development"));

        configure.Should().Throw<InvalidOperationException>().WithMessage("Auth:ClientId is not configured.*");
    }

    [Fact]
    public async Task Local_auth_does_not_register_Entra_or_need_its_configuration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthenticationServices(Configuration("true"), new TestEnvironment("Development"));
        await using var provider = services.BuildServiceProvider();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        (await schemes.GetDefaultChallengeSchemeAsync())!.Name.Should().Be(LocalAuthentication.Scheme);
        (await schemes.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme)).Should().BeNull();
    }

    [Fact]
    public async Task Configured_Entra_and_local_login_share_the_development_cookie()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:UseLocal"] = "true",
            ["Auth:ClientId"] = "11111111-1111-1111-1111-111111111111",
            ["Auth:TenantId"] = "22222222-2222-2222-2222-222222222222",
            ["Auth:Instance"] = "https://login.microsoftonline.com/",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAuthenticationServices(configuration, new TestEnvironment("Development"));
        await using var provider = services.BuildServiceProvider();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        (await schemes.GetDefaultAuthenticateSchemeAsync())!.Name.Should().Be(LocalAuthentication.Scheme);
        (await schemes.GetDefaultChallengeSchemeAsync())!.Name.Should().Be(LocalAuthentication.Scheme);
        (await schemes.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme)).Should().NotBeNull();
        var oidc = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        oidc.SignInScheme.Should().Be(LocalAuthentication.Scheme);
        oidc.ResponseType.Should().Be("id_token");
        oidc.ClientSecret.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Production_uses_template_id_token_sign_in_without_a_client_secret()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:ClientId"] = "11111111-1111-1111-1111-111111111111",
            ["Auth:TenantId"] = "22222222-2222-2222-2222-222222222222",
            ["Auth:Instance"] = "https://login.microsoftonline.com/",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAuthenticationServices(configuration, new TestEnvironment("Production"));
        await using var provider = services.BuildServiceProvider();
        var oidc = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        oidc.ResponseType.Should().Be("id_token");
        oidc.ClientSecret.Should().BeNullOrEmpty();
        oidc.SignInScheme.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        (await schemes.GetSchemeAsync(LocalAuthentication.Scheme)).Should().BeNull();
    }

    [Theory]
    [InlineData("/jds", "/jds")]
    [InlineData("https://example.test", "/")]
    [InlineData("//example.test", "/")]
    [InlineData(null, "/")]
    public void Campus_login_challenges_Entra_even_with_a_local_session(string? returnUrl, string expected)
    {
        var controller = Controller("true", "11111111-1111-1111-1111-111111111111");
        controller.HttpContext.User = LocalAuthentication.CreatePrincipal("sample")!;

        var result = controller.UcDavisLogin(returnUrl).Should().BeOfType<ChallengeResult>().Subject;

        result.AuthenticationSchemes.Should().ContainSingle().Which.Should().Be(OpenIdConnectDefaults.AuthenticationScheme);
        result.Properties!.RedirectUri.Should().Be(expected);
    }

    [Fact]
    public void Campus_login_is_unavailable_without_Entra_configuration()
    {
        Controller("true").UcDavisLogin("/").Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void Personas_exercise_the_existing_role_boundary()
    {
        var sample = LocalAuthentication.CreatePrincipal("sample")!;
        var basic = LocalAuthentication.CreatePrincipal("basic")!;

        sample.Identity!.IsAuthenticated.Should().BeTrue();
        sample.IsInRole("User").Should().BeTrue();
        sample.IsInRole("SampleRole").Should().BeTrue();
        basic.IsInRole("User").Should().BeTrue();
        basic.IsInRole("SampleRole").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("admin")]
    [InlineData("sample,Admin")]
    public void Login_rejects_unknown_personas(string? persona)
    {
        LocalAuthentication.CreatePrincipal(persona).Should().BeNull();
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("//example.com/")]
    [InlineData("/\\example.com/")]
    public void Login_does_not_redirect_to_an_external_site(string returnUrl)
    {
        var controller = Controller(local: "true");

        var view = controller.Login(returnUrl).Should().BeOfType<ViewResult>().Subject;

        view.Model.Should().Be("/");
    }

    [Fact]
    public void Normal_login_challenges_Entra_with_the_local_return_url()
    {
        var controller = Controller();

        var challenge = controller.Login("/fetch?example=1").Should().BeOfType<ChallengeResult>().Subject;

        challenge.AuthenticationSchemes.Should().ContainSingle().Which.Should().Be(OpenIdConnectDefaults.AuthenticationScheme);
        challenge.Properties!.RedirectUri.Should().Be("/fetch?example=1");
    }

    [Fact]
    public async Task Local_login_and_logout_are_unavailable_by_default()
    {
        var controller = Controller();

        (await controller.LocalLogin("sample", "/")).Should().BeOfType<NotFoundResult>();
        (await controller.LocalLogout()).Should().BeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData(null, CookieAuthenticationDefaults.AuthenticationScheme)]
    [InlineData("true", LocalAuthentication.Scheme)]
    public void Campus_logout_clears_the_session_cookie_before_Entra(string? local, string cookieScheme)
    {
        var controller = Controller(local, "11111111-1111-1111-1111-111111111111");
        controller.HttpContext.Request.PathBase = "/jdwriter";
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "campus-user")], "AuthenticationTypes.Federation"));

        var result = controller.Logout().Should().BeOfType<SignOutResult>().Subject;

        result.AuthenticationSchemes.Should().Equal(cookieScheme, OpenIdConnectDefaults.AuthenticationScheme);
        result.Properties!.RedirectUri.Should().Be("/jdwriter/logout/complete");
    }

    [Fact]
    public void Fictional_persona_logout_does_not_sign_out_of_Entra()
    {
        var controller = Controller("true", "11111111-1111-1111-1111-111111111111");
        controller.HttpContext.User = LocalAuthentication.CreatePrincipal("sample")!;
        var result = controller.Logout().Should().BeOfType<SignOutResult>().Subject;
        result.AuthenticationSchemes.Should().Equal(LocalAuthentication.Scheme);
        result.Properties!.RedirectUri.Should().Be("/logout/complete");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/jdwriter")]
    public async Task Logout_returns_to_a_public_page_and_clears_the_cookie(string pathBase)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AccountController).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:UseLocal"] = "true",
            ["Auth:ClientId"] = "<client-guid>",
        });
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AccountController).Assembly);
        builder.Services.AddAuthenticationServices(builder.Configuration, builder.Environment);
        builder.Services.AddAuthorization();
        builder.Services.AddScoped(_ => TestDbContextFactory.CreateInMemory());
        builder.Services.AddScoped<Server.Core.Access.AdminAccess>();
        builder.Services.AddScoped<IUserService, UserService>();
        await using var app = builder.Build();
        if (pathBase.Length > 0)
        {
            app.UsePathBase(pathBase);
        }
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/api/session", () => Results.Ok()).RequireAuthorization();
        await app.StartAsync();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };

        var anonymousLanding = await client.GetAsync(pathBase + "/logout/complete");
        anonymousLanding.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginHtml = await client.GetStringAsync(pathBase + "/login");
        var login = await client.PostAsync(pathBase + "/login/local", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["persona"] = "sample",
            ["__RequestVerificationToken"] = AntiforgeryToken(loginHtml),
        }));
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await client.GetAsync(pathBase + "/api/session")).StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmation = await client.GetStringAsync(pathBase + "/logout");
        var rejected = await client.PostAsync(pathBase + "/logout", new FormUrlEncodedContent(new Dictionary<string, string>()));
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync(pathBase + "/api/session")).StatusCode.Should().Be(HttpStatusCode.OK);
        var logout = await client.PostAsync(pathBase + "/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryToken(confirmation),
        }));
        logout.StatusCode.Should().Be(HttpStatusCode.Redirect);
        logout.Headers.Location!.OriginalString.Should().Be(pathBase + "/logout/complete");
        var landing = await client.GetAsync(logout.Headers.Location);
        landing.StatusCode.Should().Be(HttpStatusCode.OK);
        landing.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var html = await landing.Content.ReadAsStringAsync();
        html.Should().Contain("You are signed out of JDWriter");
        html.Should().Contain($"href=\"{pathBase}/login\"");
        (await client.GetAsync(pathBase + "/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync(pathBase + "/login")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string AntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        match.Success.Should().BeTrue();
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static AccountController Controller(string? local = null, string clientId = "<client-guid>")
    {
        var controller = new AccountController(Configuration(local, clientId), new TestEnvironment("Development"))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
                RouteData = new RouteData(),
            },
        };
        controller.Url = new UrlHelper(controller.ControllerContext);
        return controller;
    }

    private static IConfiguration Configuration(string? local = null, string clientId = "<client-guid>") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:UseLocal"] = local,
            ["Auth:ClientId"] = clientId,
        }).Build();

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Server.Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

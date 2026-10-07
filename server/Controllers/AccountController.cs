using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Helpers;

namespace Server.Controllers;

[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AccountController(IConfiguration configuration, IHostEnvironment environment) : Controller
{
    /// <summary>
    /// The app's own root, mount point included ("/jdwriter/" under CAES People). A bare "/" would
    /// send the user to the portal's landing page instead of back into JDWriter. A return URL from
    /// the client already carries the mount point, because it is the browser's full path.
    /// </summary>
    private string AppRoot => Url.Content("~/");

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl)
    {
        var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : AppRoot;
        if (LocalAuthentication.IsEnabled(configuration, environment))
        {
            ViewData["EntraConfigured"] = AuthenticationHelper.IsEntraConfigured(configuration);
            return View("LocalLogin", safeReturnUrl);
        }

        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(safeReturnUrl);
        }

        return Challenge(new AuthenticationProperties { RedirectUri = safeReturnUrl },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("login/ucdavis")]
    public IActionResult UcDavisLogin(string? returnUrl)
    {
        if (!AuthenticationHelper.IsEntraConfigured(configuration))
        {
            return NotFound();
        }

        return Challenge(new AuthenticationProperties
        {
            RedirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl! : AppRoot,
        }, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpPost("login/local")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LocalLogin(string? persona, string? returnUrl)
    {
        if (!LocalAuthentication.IsEnabled(configuration, environment))
        {
            return NotFound();
        }

        var principal = LocalAuthentication.CreatePrincipal(persona);
        if (principal == null)
        {
            return BadRequest("Choose one of the listed sandbox users.");
        }

        await HttpContext.SignInAsync(LocalAuthentication.Scheme, principal);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : AppRoot);
    }

    [HttpPost("logout/local")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LocalLogout()
    {
        if (!LocalAuthentication.IsEnabled(configuration, environment))
        {
            return NotFound();
        }

        await HttpContext.SignOutAsync(LocalAuthentication.Scheme);
        return LocalRedirect(Url.Content("~/login"));
    }
}

using System.Security.Claims;

namespace Server.Helpers;

public static class LocalAuthentication
{
    public const string Scheme = "LocalSandbox";

    /// <summary>
    /// The fictional personas' sign-in domain. Accepted as a login domain only for identities
    /// created by this scheme, which cannot exist outside Development.
    /// </summary>
    public const string PersonaDomain = "example.test";

    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        var enabled = configuration.GetValue<bool>("Auth:UseLocal");
        if (enabled && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("Auth:UseLocal is only allowed in the Development environment.");
        }

        return enabled;
    }

    public static ClaimsPrincipal? CreatePrincipal(string? persona)
    {
        if (persona != "sample" && persona != "basic")
        {
            return null;
        }

        var isSample = persona == "sample";
        var name = isSample ? "Sample User" : "Basic User";
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, isSample ? "sandbox-sample" : "sandbox-basic"),
            new(ClaimTypes.Name, name),
            new("name", name),
            new("preferred_username", $"{persona}@{PersonaDomain}"),
            new("ucdPersonIAMID", isSample ? "sandbox-10001" : "sandbox-10002"),
            new(ClaimTypes.Role, "User"),
        };

        if (isSample)
        {
            claims.Add(new Claim(ClaimTypes.Role, "SampleRole"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
    }
}

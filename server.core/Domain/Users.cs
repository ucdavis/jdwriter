using System.Security.Claims;
using System.Text.RegularExpressions;

namespace Server.Core.Domain;

/// <summary>
/// An application user, recorded on first sign-in. Roles are NOT stored here: they are derived on
/// every request — Author for everyone, Admin for a whitelisted campus login — so revoking an admin
/// takes effect on the next request rather than at the next sign-in.
/// </summary>
public class AppUser
{
    public int Id { get; set; }

    /// <summary>
    /// The Entra object identifier (the NameIdentifier claim). Unique, and the key the sign-in
    /// pipeline looks up on every request.
    /// </summary>
    public string NameIdentifier { get; set; } = "";

    /// <summary>
    /// Campus login id ("ndlewis"), derived from a @ucdavis.edu sign-in name. Null for an account
    /// whose sign-in name is not on a campus domain — such an account can never be an admin.
    /// </summary>
    public string? LoginId { get; set; }

    /// <summary>UC Davis IAM id, when the claim is present.</summary>
    public string? IamId { get; set; }

    public string? Email { get; set; }
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
}

/// <summary>
/// The two roles, as constants rather than an enum so they can be used directly in
/// [Authorize(Roles = ...)] attributes, which take strings.
/// </summary>
public static class AppRoles
{
    /// <summary>Everyone who signs in: drafting and classifying job descriptions.</summary>
    public const string Author = "Author";

    /// <summary>
    /// The whole back end — envelopes, review, ingest, standards, settings. Granted only by the
    /// admin whitelist, because an admin edits the envelopes every author writes against.
    /// </summary>
    public const string Admin = "Admin";

    public static readonly string[] All = [Author, Admin];
}

/// <summary>
/// One whitelisted admin, by campus login id. Exists independently of <see cref="AppUser"/> so a
/// person can be made an admin before they have ever signed in.
/// </summary>
public class AdminGrant
{
    public int Id { get; set; }

    /// <summary>Normalized campus login id. Unique.</summary>
    public string LoginId { get; set; } = "";

    public int? GrantedByUserId { get; set; }
    public AppUser? GrantedBy { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}

/// <summary>
/// Campus login ids: normalizing what an admin types, and deriving one from a signed-in user.
/// </summary>
public static partial class CampusLogin
{
    /// <summary>The campus domain. A login id is only ever taken from a name on it.</summary>
    public const string Domain = "ucdavis.edu";

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex Shape();

    /// <summary>
    /// "ndlewis", " NDLewis ", or "ndlewis@ucdavis.edu" → "ndlewis". Null when the input is not a
    /// plausible login id, or names a domain other than the campus one.
    /// </summary>
    public static string? Normalize(string? input)
    {
        var s = (input ?? "").Trim().ToLowerInvariant();
        var at = s.IndexOf('@');
        if (at >= 0)
        {
            if (s[(at + 1)..] != Domain)
            {
                return null;
            }

            s = s[..at];
        }

        return Shape().IsMatch(s) ? s : null;
    }

    /// <summary>
    /// The login id of a signed-in user, from the first sign-in name on an allowed domain.
    ///
    /// Campus SSO only authenticates UC Davis accounts, so a non-campus name should never arrive.
    /// The domain is still checked rather than assumed: it is what makes "the part before the @" a
    /// campus login id at all, and a misconfigured tenant or app registration would otherwise let
    /// "ndlewis@elsewhere" match the whitelisted "ndlewis" silently.
    /// </summary>
    public static string? FromPrincipal(ClaimsPrincipal principal, IReadOnlyCollection<string> allowedDomains)
    {
        string[] claimTypes = ["preferred_username", ClaimTypes.Upn, "upn", ClaimTypes.Email, "email"];
        foreach (var type in claimTypes)
        {
            var value = principal.FindFirst(type)?.Value?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var at = value.LastIndexOf('@');
            if (at <= 0)
            {
                continue;
            }

            var local = value[..at];
            if (allowedDomains.Contains(value[(at + 1)..]) && Shape().IsMatch(local))
            {
                return local;
            }
        }

        return null;
    }
}

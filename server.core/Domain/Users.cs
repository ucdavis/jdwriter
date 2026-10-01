namespace Server.Core.Domain;

/// <summary>
/// An application user. The template ships with no user table and a stubbed role lookup; this
/// fills it, because JDWriter genuinely needs to separate a supervisor drafting a JD from the
/// HR analyst who curates envelopes and runs ingest.
/// </summary>
public class AppUser
{
    public int Id { get; set; }

    /// <summary>
    /// The Entra object identifier (the NameIdentifier claim). Unique, and the key the sign-in
    /// pipeline looks up on every request.
    /// </summary>
    public string NameIdentifier { get; set; } = "";

    /// <summary>UC Davis IAM id, when the claim is present.</summary>
    public string? IamId { get; set; }

    public string? Email { get; set; }
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }

    public List<AppUserRole> Roles { get; set; } = [];
}

/// <summary>
/// Role names, mirrored as constants rather than an enum so they can be used directly in
/// [Authorize(Roles = ...)] attributes, which take strings.
/// </summary>
public static class AppRoles
{
    /// <summary>A supervisor or department user drafting a job description.</summary>
    public const string Author = "Author";

    /// <summary>HR classification analyst — the backend surfaces: envelopes, fit, review.</summary>
    public const string Analyst = "Analyst";

    /// <summary>Corpus ingest, standards ingest, and migration operations.</summary>
    public const string Admin = "Admin";

    public static readonly string[] All = [Author, Analyst, Admin];
}

/// <summary>One granted role. Composite-unique on (user, role).</summary>
public class AppUserRole
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    public string Role { get; set; } = "";
    public DateTimeOffset GrantedAt { get; set; }
}

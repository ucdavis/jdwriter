namespace Server.Core.Domain;

/// <summary>
/// A secret an admin entered in the app, stored encrypted. <see cref="ProtectedValue"/> is ASP.NET
/// Core Data Protection ciphertext; the plaintext is never stored and never sent back to a browser.
/// </summary>
public class AppSecret
{
    public int Id { get; set; }

    /// <summary>Which secret this is. Unique. See <see cref="AppSecretNames"/>.</summary>
    public string Name { get; set; } = "";

    public string ProtectedValue { get; set; } = "";

    /// <summary>The last four characters, so an admin can tell which key is in use.</summary>
    public string LastFour { get; set; } = "";

    public int? UpdatedByUserId { get; set; }
    public AppUser? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class AppSecretNames
{
    public const string AnthropicApiKey = "AnthropicApiKey";

    /// <summary>One stored key per provider, so switching providers never reuses the wrong key.</summary>
    public static string ApiKeyFor(Server.Core.Ai.LlmProvider provider) => $"{provider}ApiKey";
}

/// <summary>
/// One change to a stored secret: who, when, what — never the value. Kept for compliance review of
/// who has handled the AI provider keys.
/// </summary>
public class AppSecretAudit
{
    public int Id { get; set; }
    public string SecretName { get; set; } = "";

    /// <summary>"set" or "cleared".</summary>
    public string Action { get; set; } = "";

    /// <summary>The last four characters of the key set, to tell rotations apart. Null when cleared.</summary>
    public string? LastFour { get; set; }

    public int? UserId { get; set; }
    public AppUser? User { get; set; }
    public DateTimeOffset At { get; set; }
}

namespace Server.Core.Domain;

/// <summary>
/// A bug report, request or question sent from the footer's "Report a problem". JDWriter's own
/// support queue: reports come to the app's admins, not to CRU's ticket system (decided 2026-10-28 —
/// CRU hosts the app but doesn't carry its support). Kept in the database only, never logged: a
/// message may quote a job description.
/// </summary>
public class ProblemReport
{
    public const int MaxMessage = 4000;
    public const int MaxPage = 300;
    public const int MaxBrowser = 400;
    public const int MaxNote = 2000;

    public static readonly string[] Kinds = ["Bug", "Request", "Question"];

    public int Id { get; set; }

    /// <summary>Bug, Request or Question.</summary>
    public string Kind { get; set; } = "Bug";

    public string Message { get; set; } = "";

    /// <summary>The page it was sent from (path only, no query), so the report says where.</summary>
    public string Page { get; set; } = "";

    /// <summary>The sender's browser, as it identifies itself.</summary>
    public string Browser { get; set; } = "";

    public int? CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public bool Resolved { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>The admin's own note: what was done, or why not.</summary>
    public string Note { get; set; } = "";
}

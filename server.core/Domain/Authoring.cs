namespace Server.Core.Domain;

/// <summary>
/// A finished job description produced by the authoring flow — the system's actual output.
/// Structure mirrors the HRTMS/JDXpert format so it can be handed back to HR directly.
/// </summary>
public class AuthoredJd
{
    public int Id { get; set; }

    /// <summary>The class this was authored against.</summary>
    public int ClassProfileId { get; set; }
    public ClassProfile? ClassProfile { get; set; }

    public string Title { get; set; } = "";
    public string WorkingTitle { get; set; } = "";
    public string Department { get; set; } = "";
    public string UcJobCode { get; set; } = "";

    /// <summary>
    /// Fixed attributes carried from the class standard. Nullable because a class bootstrapped
    /// without a standard has no authoritative value to copy, and inventing one would be worse
    /// than leaving it blank.
    /// </summary>
    public string? SalaryGrade { get; set; }
    public string? FlsaStatus { get; set; }
    public string? BargainingUnit { get; set; }

    public string JobSummary { get; set; } = "";

    // ---- provenance
    public int? CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<AuthoredJdResponsibility> KeyResponsibilities { get; set; } = [];
    public List<AuthoredJdListItem> Items { get; set; } = [];
    public List<ComplianceEdit> ComplianceEdits { get; set; } = [];
}

/// <summary>A key responsibility of an authored JD.</summary>
public class AuthoredJdResponsibility
{
    public int Id { get; set; }
    public int AuthoredJdId { get; set; }
    public AuthoredJd? AuthoredJd { get; set; }

    public int Ordinal { get; set; }
    public string FunctionName { get; set; } = "";

    /// <summary>
    /// Percent of time, exactly as the author allocated it — never rescaled.
    ///
    /// These must sum to 100 on a finished JD, and unlike the corpus that is enforced rather than
    /// observed. But it is enforced by REFUSING to persist an under- or over-allocated draft, not
    /// by recomputing: rescaling would silently move a number the author chose, publishing a kept
    /// 50% as 71% with nobody told. The shortfall is surfaced as
    /// <c>AssembledJd.UnallocatedPct</c> and the author reallocates it.
    ///
    /// Contrast the corpus, where six real JDs range from 0 to 200 and are stored exactly as the
    /// HRTMS export stated them.
    /// </summary>
    public int PctTime { get; set; }

    public List<AuthoredJdDuty> Duties { get; set; } = [];
}

/// <summary>A duty bullet on an authored JD.</summary>
public class AuthoredJdDuty : ITextItem
{
    public int Id { get; set; }
    public int AuthoredJdResponsibilityId { get; set; }
    public AuthoredJdResponsibility? AuthoredJdResponsibility { get; set; }

    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>One line of an authored JD's list-valued sections.</summary>
public class AuthoredJdListItem : ITextItem
{
    public int Id { get; set; }
    public int AuthoredJdId { get; set; }
    public AuthoredJd? AuthoredJd { get; set; }

    public AuthoredJdListKind Kind { get; set; }
    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// One entry in the compliance audit trail: what was changed, by what, and why. This exists so a
/// rewrite is reviewable rather than silent — an analyst has to be able to see that a duty was
/// reworded and on whose authority.
/// </summary>
public class ComplianceEdit
{
    public int Id { get; set; }
    public int AuthoredJdId { get; set; }
    public AuthoredJd? AuthoredJd { get; set; }

    public int Ordinal { get; set; }

    /// <summary>Where the edit landed, e.g. "keyResponsibilities[2].duties[0]".</summary>
    public string Section { get; set; } = "";

    public ComplianceEditSource Source { get; set; }
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public string Reason { get; set; } = "";
}

/// <summary>
/// A deterministic compliance rule applied during assembly, before any model call. Kept in the
/// database rather than in code so HR can adjust policy language without a deployment.
/// </summary>
public class ComplianceRule
{
    public int Id { get; set; }

    /// <summary>Stable identifier used in the audit trail. Unique.</summary>
    public string Key { get; set; } = "";

    public string Pattern { get; set; } = "";
    public string Replacement { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

using System.Text;

namespace Server.Core.Jd;

/// <summary>
/// A finished JD as JDWriter hands it to the workforce management (WFM) tool.
///
/// The process is JDWriter → WFM → HR: the author finishes a JD here, starts the workforce
/// management justification in WFM with the JD attached, and WFM submits the justification, the JD
/// and anything else required to HR as one package. This is the contract between the two apps,
/// documented for the WFM side in docs/WFM-HANDOFF.md. Change it only by adding fields, or by
/// raising <see cref="Version"/>.
/// </summary>
public sealed class JdHandoff
{
    public const string FormatName = "jdwriter.jd";

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = 1;

    /// <summary>The saved JD's id in JDWriter.</summary>
    public int Id { get; set; }

    /// <summary>"ready" — only a publishable JD is handed off.</summary>
    public string Status { get; set; } = "";

    public HandoffSource Source { get; set; } = new();
    public HandoffClassification Classification { get; set; } = new();
    public HandoffPosition Position { get; set; } = new();
    public Jd Jd { get; set; } = new();

    /// <summary>The same JD as files, for attaching to the HR package.</summary>
    public HandoffDocuments Documents { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class HandoffSource
{
    public string App { get; set; } = "JDWriter";

    /// <summary>The JD in JDWriter, for a person to open.</summary>
    public string Url { get; set; } = "";

    public DateTimeOffset ExportedAt { get; set; }
}

/// <summary>The UC job class the JD is written for.</summary>
public sealed class HandoffClassification
{
    public string Title { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public string? SalaryGrade { get; set; }
    public string? FlsaStatus { get; set; }
    public string? BargainingUnit { get; set; }
}

public sealed class HandoffPosition
{
    public string WorkingTitle { get; set; } = "";
    public string Department { get; set; } = "";

    /// <summary>Null when not stated. Added in v1 (additive).</summary>
    public bool? Supervises { get; set; }

    public int? SupervisesCount { get; set; }
    public bool? Leads { get; set; }
}

public sealed class HandoffDocuments
{
    public string Docx { get; set; } = "";
    public string Markdown { get; set; } = "";
}

public static class JdHandoffBuilder
{
    /// <param name="appBaseUrl">JDWriter's absolute root, mount point included ("https://host/jdwriter").</param>
    public static JdHandoff Build(SavedJd saved, string appBaseUrl, DateTimeOffset now)
    {
        var root = appBaseUrl.TrimEnd('/');
        return new JdHandoff
        {
            Id = saved.AuthoredJdId,
            Status = saved.Status.ToString().ToLowerInvariant(),
            Source = new HandoffSource { Url = $"{root}/jds/{saved.AuthoredJdId}", ExportedAt = now },
            Classification = new HandoffClassification
            {
                Title = saved.Title,
                UcJobCode = saved.UcJobCode,
                SalaryGrade = saved.SalaryGrade,
                FlsaStatus = saved.FlsaStatus,
                BargainingUnit = saved.BargainingUnit,
            },
            Position = new HandoffPosition
            {
                WorkingTitle = saved.WorkingTitle,
                Department = saved.Department,
                Supervises = saved.Supervises,
                SupervisesCount = saved.SupervisesCount,
                Leads = saved.Leads,
            },
            Jd = saved.Jd,
            Documents = new HandoffDocuments
            {
                Docx = $"{root}/api/jds/{saved.AuthoredJdId}/docx",
                Markdown = $"{root}/api/jds/{saved.AuthoredJdId}/markdown",
            },
            CreatedAt = saved.CreatedAt,
            UpdatedAt = saved.UpdatedAt,
        };
    }

    /// <summary>The JD as Markdown: the same sections, in the same order, as the page, the PDF and the Word file.</summary>
    public static string Markdown(SavedJd saved)
    {
        var jd = saved.Jd;
        var md = new StringBuilder();
        md.Append("# ").AppendLine(saved.WorkingTitle.Length > 0 ? saved.WorkingTitle : saved.Title).AppendLine();

        var identity = new List<string>();
        if (saved.Department.Length > 0)
        {
            identity.Add(saved.Department);
        }

        identity.Add($"UC Job Code {saved.UcJobCode}");
        if (saved.Title.Length > 0 && saved.Title != saved.WorkingTitle)
        {
            identity.Add(saved.Title);
        }

        md.AppendLine(string.Join(" · ", identity));
        var facts = new List<string>();
        if (!string.IsNullOrEmpty(saved.SalaryGrade))
        {
            facts.Add($"Salary Grade: {saved.SalaryGrade}");
        }

        if (!string.IsNullOrEmpty(saved.FlsaStatus))
        {
            facts.Add($"FLSA: {saved.FlsaStatus}");
        }

        if (!string.IsNullOrEmpty(saved.BargainingUnit))
        {
            facts.Add($"Bargaining Unit: {saved.BargainingUnit}");
        }

        facts.AddRange(SupervisionFacts(saved));

        if (facts.Count > 0)
        {
            md.AppendLine().AppendLine(string.Join(" · ", facts));
        }

        md.AppendLine().AppendLine("## Job Summary").AppendLine().AppendLine(jd.JobSummary);

        md.AppendLine().AppendLine($"## Key Responsibilities — Total {jd.KeyResponsibilities.Sum(r => r.PctTime)}%");
        foreach (var r in jd.KeyResponsibilities)
        {
            md.AppendLine().AppendLine($"### {r.PctTime}% {r.FunctionName}").AppendLine();
            foreach (var d in r.Duties)
            {
                md.Append("- ").AppendLine(d);
            }
        }

        md.AppendLine().AppendLine("## Qualifications");
        List(md, "### Licenses & Certifications", jd.LicensesCertifications);
        List(md, "### Education", jd.Education);
        List(md, "### Work Experience", jd.WorkExperience);
        List(md, "### Minimum Knowledge, Skills & Abilities", jd.MinKSA);
        List(md, "### Preferred Knowledge, Skills & Abilities", jd.PrefKSA);
        List(md, "## Conditions of Employment", jd.ConditionsOfEmployment);
        List(md, "## Work Environment", jd.WorkEnvironment);
        List(md, "## Physical Requirements", jd.PhysicalRequirements);

        // A file format, not console output: the same bytes whatever OS the server runs on.
        return md.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>"Supervises: Yes (4)", "Leads: No" — only what the author stated.</summary>
    public static IEnumerable<string> SupervisionFacts(SavedJd saved)
    {
        if (saved.Supervises is { } supervises)
        {
            yield return supervises && saved.SupervisesCount is { } n
                ? $"Supervises: Yes ({n} {(n == 1 ? "person" : "people")})"
                : $"Supervises: {(supervises ? "Yes" : "No")}";
        }

        if (saved.Leads is { } leads)
        {
            yield return $"Leads: {(leads ? "Yes" : "No")}";
        }
    }

    private static void List(StringBuilder md, string heading, List<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        md.AppendLine().AppendLine(heading).AppendLine();
        foreach (var item in items)
        {
            md.Append("- ").AppendLine(item);
        }
    }
}

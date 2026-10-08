using System.Text.Json;
using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Jd;
using JdDoc = Server.Core.Jd.Jd;

namespace Server.Tests.Jd;

/// <summary>
/// The hand-off to the workforce management tool. Its JSON is a contract another app is built
/// against (docs/WFM-HANDOFF.md), so field names and shape are pinned exactly: a rename here breaks
/// WFM silently.
/// </summary>
public class JdHandoffTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 17, 0, 0, TimeSpan.Zero);

    private static SavedJd Saved() => new()
    {
        AuthoredJdId = 12,
        Title = "Lab Ast 1",
        WorkingTitle = "Greenhouse Tech",
        Department = "Plant Sciences",
        UcJobCode = "009605",
        SalaryGrade = "Grade 3",
        FlsaStatus = "Non-Exempt",
        Status = AuthoredJdStatus.Ready,
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now,
        Jd = new JdDoc
        {
            JobSummary = "Runs greenhouse experiments.",
            KeyResponsibilities =
            [
                new JdKeyResponsibility { FunctionName = "Greenhouse Operations", PctTime = 70, Duties = ["Waters and records plants."] },
                new JdKeyResponsibility { FunctionName = "Data", PctTime = 30, Duties = ["Enters measurements."] },
            ],
            Education = ["High school diploma."],
            PhysicalRequirements = ["Lift 25 lbs."],
        },
    };

    [Fact]
    public void The_json_contract_has_exactly_the_documented_shape()
    {
        var handoff = JdHandoffBuilder.Build(Saved(), "https://people.caes.ucdavis.edu/jdwriter", Now);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(handoff, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = doc.RootElement;

        root.EnumerateObject().Select(p => p.Name).Should().Equal(
            "format", "version", "id", "status", "source", "classification", "position", "jd", "documents", "createdAt", "updatedAt");
        root.GetProperty("format").GetString().Should().Be("jdwriter.jd");
        root.GetProperty("version").GetInt32().Should().Be(1);
        root.GetProperty("status").GetString().Should().Be("ready");

        root.GetProperty("source").GetProperty("url").GetString().Should().Be("https://people.caes.ucdavis.edu/jdwriter/jds/12");
        root.GetProperty("documents").GetProperty("docx").GetString().Should().Be("https://people.caes.ucdavis.edu/jdwriter/api/jds/12/docx");
        root.GetProperty("documents").GetProperty("markdown").GetString().Should().Be("https://people.caes.ucdavis.edu/jdwriter/api/jds/12/markdown");

        var classification = root.GetProperty("classification");
        classification.GetProperty("ucJobCode").GetString().Should().Be("009605");
        classification.GetProperty("title").GetString().Should().Be("Lab Ast 1");
        root.GetProperty("position").GetProperty("workingTitle").GetString().Should().Be("Greenhouse Tech");

        var jd = root.GetProperty("jd");
        jd.GetProperty("keyResponsibilities")[0].GetProperty("pctTime").GetInt32().Should().Be(70);
        jd.GetProperty("keyResponsibilities")[0].GetProperty("duties")[0].GetString().Should().Be("Waters and records plants.");
        jd.GetProperty("minKSA").GetArrayLength().Should().Be(0, "empty sections are present, never omitted");
    }

    [Fact]
    public void Links_work_at_a_hosts_root_too()
    {
        var handoff = JdHandoffBuilder.Build(Saved(), "http://localhost:5165/", Now);

        handoff.Source.Url.Should().Be("http://localhost:5165/jds/12");
        handoff.Documents.Markdown.Should().Be("http://localhost:5165/api/jds/12/markdown");
    }

    [Fact]
    public void The_markdown_has_the_sections_of_the_printed_jd_in_order()
    {
        JdHandoffBuilder.Markdown(Saved()).Should().Be("""
            # Greenhouse Tech

            Plant Sciences · UC Job Code 009605 · Lab Ast 1

            Salary Grade: Grade 3 · FLSA: Non-Exempt

            ## Job Summary

            Runs greenhouse experiments.

            ## Key Responsibilities — Total 100%

            ### 70% Greenhouse Operations

            - Waters and records plants.

            ### 30% Data

            - Enters measurements.

            ## Qualifications

            ### Education

            - High school diploma.

            ## Physical Requirements

            - Lift 25 lbs.

            """.ReplaceLineEndings("\n"));
    }
}

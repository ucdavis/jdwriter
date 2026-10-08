using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Server.Controllers;
using Server.Core.Jd;
using JdDoc = Server.Core.Jd.Jd;

namespace Server.Tests.Jd;

/// <summary>
/// The Word export. It must carry the same sections, in the same order, as the page and the PDF —
/// two versions of one JD that disagree is worse than having only one.
/// </summary>
public class JdDocxTests
{
    private static SavedJd Saved() => new()
    {
        AuthoredJdId = 12,
        Title = "Lab Ast 1",
        WorkingTitle = "Greenhouse Tech",
        Department = "Plant Sciences",
        UcJobCode = "009605",
        SalaryGrade = "Grade 3",
        FlsaStatus = "Non-Exempt",
        Jd = new JdDoc
        {
            JobSummary = "Runs greenhouse experiments.",
            KeyResponsibilities =
            [
                new JdKeyResponsibility { FunctionName = "Greenhouse Operations", PctTime = 70, Duties = ["Waters and records plants."] },
                new JdKeyResponsibility { FunctionName = "Data", PctTime = 30, Duties = ["Enters measurements."] },
            ],
            Education = ["High school diploma."],
            MinKSA = ["Basic plant care."],
            PhysicalRequirements = ["Lift 25 lbs."],
        },
    };

    private static List<(string Style, string Text)> Read(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(stream, false);
        return doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>()
            .Select(p => (p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "", p.InnerText))
            .ToList();
    }

    [Fact]
    public void The_document_has_the_sections_of_the_printed_jd_in_order()
    {
        var paragraphs = Read(JdDocx.Build(Saved()));

        paragraphs.Should().Equal(
            ("Title", "Greenhouse Tech"),
            ("", "Plant Sciences · UC Job Code 009605 · Lab Ast 1"),
            ("", "Salary Grade: Grade 3 · FLSA: Non-Exempt"),
            ("Heading1", "Job Summary"),
            ("", "Runs greenhouse experiments."),
            ("Heading1", "Key Responsibilities — Total 100%"),
            ("Heading2", "70%  Greenhouse Operations"),
            ("ListBullet", "Waters and records plants."),
            ("Heading2", "30%  Data"),
            ("ListBullet", "Enters measurements."),
            ("Heading1", "Qualifications"),
            ("Heading2", "Education"),
            ("ListBullet", "High school diploma."),
            ("Heading2", "Minimum Knowledge, Skills & Abilities"),
            ("ListBullet", "Basic plant care."),
            ("Heading1", "Physical Requirements"),
            ("ListBullet", "Lift 25 lbs."));
    }

    [Fact]
    public void Every_style_it_uses_is_defined_and_bullets_are_real_numbering()
    {
        using var stream = new MemoryStream(JdDocx.Build(Saved()));
        using var doc = WordprocessingDocument.Open(stream, false);
        var main = doc.MainDocumentPart!;

        var defined = main.StyleDefinitionsPart!.Styles!.Elements<Style>().Select(s => s.StyleId!.Value).ToHashSet();
        defined.Should().Contain(["Normal", "Title", "Heading1", "Heading2", "ListBullet"]);
        main.NumberingDefinitionsPart!.Numbering!.Elements<NumberingInstance>().Should().ContainSingle();
    }

    [Fact]
    public void The_document_is_valid_against_the_office_schema()
    {
        // Word refuses, or "repairs", a file whose XML elements are out of schema order — and a
        // permissive reader would not notice. The SDK's validator is the check Word effectively makes.
        using var stream = new MemoryStream(JdDocx.Build(Saved()));
        using var doc = WordprocessingDocument.Open(stream, false);

        var errors = new OpenXmlValidator().Validate(doc)
            .Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}")
            .ToList();

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Relationship_targets_are_relative_as_Word_writes_them()
    {
        // Absolute targets are valid but unreadable to Apple's reader (Quick Look, Pages, Mail).
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(JdDocx.Build(Saved())));
        foreach (var rels in zip.Entries.Where(e => e.FullName.EndsWith(".rels", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(rels.Open());
            reader.ReadToEnd().Should().NotContain("Target=\"/", rels.FullName);
        }

        // ...and the package still opens, with every part resolved.
        using var doc = WordprocessingDocument.Open(new MemoryStream(JdDocx.Build(Saved())), false);
        doc.MainDocumentPart!.StyleDefinitionsPart.Should().NotBeNull();
        doc.MainDocumentPart.NumberingDefinitionsPart.Should().NotBeNull();
    }

    [Theory]
    [InlineData("Greenhouse Tech", "009605", "Greenhouse Tech - 009605.docx")]
    [InlineData("Lab / Field: Tech?", "009605", "Lab - Field- Tech- - 009605.docx")]
    public void The_file_name_is_the_title_and_code_and_safe_everywhere(string title, string code, string expected)
    {
        var saved = Saved();
        saved.WorkingTitle = title;
        saved.UcJobCode = code;

        JdDocx.FileName(saved).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://cthulhu.example.edu/", "https://cthulhu.example.edu/")]
    [InlineData("http://cthulhu.example.edu/", null)]
    [InlineData("not a url", null)]
    [InlineData(null, null)]
    public void The_workforce_tool_is_linked_only_at_an_https_address(string? configured, string? expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Wfm:Url"] = configured })
            .Build();

        var result = new LinksController(config).Get().Should().BeOfType<OkObjectResult>().Subject;

        result.Value!.GetType().GetProperty("wfmUrl")!.GetValue(result.Value).Should().Be(expected);
    }
}

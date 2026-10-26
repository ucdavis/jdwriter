using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Server.Controllers;
using Server.Core.Vrf;
using Server.Tests.Profiles;

namespace Server.Tests.Vrf;

public class VrfTests
{
    // Synthetic: no real position, person or department.
    private static VrfForm Form(Action<VrfForm>? change = null)
    {
        var f = new VrfForm
        {
            Department = "Plant Sciences", Division = "College of Agricultural and Environmental Sciences",
            ActionRequested = "New Career Position", FillMethod = "Open Competitive Recruitment", PositionNumber = "N/A",
            PayrollTitle = "LAB AST 1", JobCode = "009605", WorkingTitle = "Greenhouse Assistant", BargainingUnit = "TX",
            Vacancies = "1", Fte = "1.0", BudgetSource = "19900 - State", Reasons = ["Essential UC Davis Functions"],
            Rationale = "R.", CriticalRole = "C.", AdverseImpact = "A.", BudgetConsideration = "B.", AlternativeOptions = "O.",
            HiringManagerName = "Pat Example", HiringManagerEmail = "pexample@example.edu",
        };
        change?.Invoke(f);
        return f;
    }

    private static List<(string Label, string Value)> Rows(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx), false);
        return [.. doc.MainDocumentPart!.Document!.Body!.Descendants<TableRow>()
            .Select(r => r.Elements<TableCell>().Select(c => c.InnerText).ToList())
            .Select(c => (c[0], c[1]))];
    }

    [Fact]
    public void The_word_file_carries_every_field_in_the_campus_form_s_order_and_is_valid()
    {
        var docx = VrfDocx.Build(Form(), new DateOnly(2026, 10, 26));

        using (var doc = WordprocessingDocument.Open(new MemoryStream(docx), false))
        {
            new OpenXmlValidator().Validate(doc).Select(e => e.Description).Should().BeEmpty();
        }

        var labels = Rows(docx).Select(r => r.Label).ToList();
        labels.Should().Equal(VrfCatalog.Fields.Where(f => f.Label != "Other reason(s), explained").Select(f => f.Label),
            "the other-reason row appears only when Other is chosen");
        labels.IndexOf("Department Name").Should().BeLessThan(labels.IndexOf("Payroll Title"));
    }

    [Fact]
    public void Blank_and_inapplicable_fields_say_why()
    {
        var rows = Rows(VrfDocx.Build(Form(f =>
        {
            f.HrbpName = "";
            f.SalaryMin = "";
            f.PositionNumber = "";
        }), new DateOnly(2026, 10, 26))).ToDictionary(r => r.Label, r => r.Value);

        rows["HR Business Partner Name"].Should().Be(VrfDocx.NotProvided);
        rows["Salary Range Minimum (Annual)"].Should().Be(VrfDocx.ForHr);
        rows["Salary Grade/Steps"].Should().Be(VrfDocx.ForHr, "HR can fill in salary details");
        rows["Date Vacated or Expiration Date"].Should().Be("N/A (new position)");
        rows["Is the funding for this contract secured?"].Should().Be("N/A (not a contract)");
        rows["Job Opening ID"].Should().Be("—");
        rows["Position Number"].Should().Be("N/A", "no one should have to type N/A");
    }

    [Fact]
    public void A_backfill_asks_for_the_vacated_date_and_Other_brings_its_explanation()
    {
        var rows = Rows(VrfDocx.Build(Form(f =>
        {
            f.ActionRequested = "Backfill Position";
            f.DateVacated = "09/30/2026";
            f.Reasons = ["Backfilling a vacancy", "Other"];
            f.OtherReason = "Grant deliverables due in spring.";
        }), new DateOnly(2026, 10, 26))).ToDictionary(r => r.Label, r => r.Value);

        rows["Date Vacated or Expiration Date"].Should().Be("09/30/2026");
        rows["Reason(s) for Recruitment/Extension"].Should().Be("Backfilling a vacancy; Other");
        rows["Other reason(s), explained"].Should().Be("Grant deliverables due in spring.");
    }

    [Fact]
    public void A_budget_source_typed_under_Other_is_written_out()
    {
        var rows = Rows(VrfDocx.Build(Form(f =>
        {
            f.BudgetSource = "Other";
            f.BudgetSourceOther = "USDA cooperative agreement";
        }), new DateOnly(2026, 10, 26))).ToDictionary(r => r.Label, r => r.Value);

        rows["Budget Source"].Should().Be("Other: USDA cooperative agreement");
    }

    [Fact]
    public void The_file_is_named_by_its_title()
    {
        VrfDocx.FileName(Form()).Should().Be("VRF - Greenhouse Assistant.docx");
        VrfDocx.FileName(Form(f => { f.WorkingTitle = ""; f.PayrollTitle = "LAB/AST 1"; })).Should().Be("VRF - LAB-AST 1.docx");
    }

    [Fact]
    public void The_wand_writes_from_the_notes_and_the_position_and_is_told_not_to_invent()
    {
        var context = new VrfContext { PayrollTitle = "LAB AST 1", JobCode = "009605", WorkingTitle = "Greenhouse Assistant", ActionRequested = "Backfill Position", Reasons = ["Backfilling a vacancy"] };

        var prompt = VrfNarrative.UserPrompt("adverseImpact", "trials stall, two grad students lose data", context);

        prompt.Should().Contain("Field: \"Adverse Impact\"")
            .And.Contain("The form asks: Describe the adverse impact of not filling this position for up to 6 months.")
            .And.Contain("- Payroll title: LAB AST 1 (009605)")
            .And.Contain("- Action: Backfill Position")
            .And.Contain("Manager's notes: trials stall, two grad students lose data");
        VrfNarrative.System.Should().Contain("do not invent figures, dates, names, funding sources, or cost savings");
    }

    [Fact]
    public async Task The_narrative_endpoint_refuses_unknown_fields_empty_notes_and_a_missing_provider()
    {
        var controller = new VrfController(new VrfNarrative(new FakeStructuredLlm { HasApiKey = false }));

        (await controller.Narrative(new() { Field = "salary", Notes = "x" }, default)).Should().BeOfType<BadRequestObjectResult>();
        (await controller.Narrative(new() { Field = "rationale", Notes = "  " }, default)).Should().BeOfType<BadRequestObjectResult>();
        (await controller.Narrative(new() { Field = "rationale", Notes = "lab is growing" }, default))
            .Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task The_narrative_endpoint_returns_the_drafted_paragraph()
    {
        var llm = new FakeStructuredLlm().Returns(new { paragraph = "  The lab's trials depend on this role.  " });
        var controller = new VrfController(new VrfNarrative(llm));

        var ok = (await controller.Narrative(new() { Field = "criticalRole", Notes = "trials depend on it" }, default))
            .Should().BeOfType<OkObjectResult>().Subject;

        ok.Value.Should().BeEquivalentTo(new { paragraph = "The lab's trials depend on this role." });
        llm.Requests.Single().Label.Should().Be("vrf.narrative");
    }

    [Fact]
    public void The_word_endpoint_returns_the_file_and_refuses_an_oversized_form()
    {
        var controller = new VrfController(new VrfNarrative(new FakeStructuredLlm()));

        var file = controller.Docx(Form()).Should().BeOfType<FileContentResult>().Subject;
        file.FileDownloadName.Should().Be("VRF - Greenhouse Assistant.docx");
        controller.Docx(Form(f => f.Rationale = new string('x', 6_001))).Should().BeOfType<BadRequestObjectResult>();
    }
}

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Server.Core.Jd;

namespace Server.Tests.Jd;

/// <summary>
/// Prompt assembly, asserted exactly.
///
/// This is where a port of this layer realistically fails. Every field can map correctly, every
/// type can compile, and the system still behaves differently because a prompt was reflowed, a
/// separator changed, or an interpolated fallback differs. Prompts were ported byte-for-byte on
/// purpose, and these tests are what keeps them that way — they also cost nothing to run, unlike
/// asking the model.
///
/// The four system prompts are guarded by hash rather than duplicated in full. A change fails
/// loudly and the message says what to check; duplicating eighty lines of prompt text into the
/// test would just be two copies to drift apart.
///
/// The hashes are not this implementation's own output — they were computed from the POC's
/// TypeScript template literals in src/lib/jd/assemble.ts and the C# matched all four on the first
/// comparison. So they verify parity with the reference, not merely that the C# has not changed.
/// </summary>
public class JdAssemblerPromptTests
{
    private static string Sha256(string s) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    [Theory]
    [InlineData(nameof(JdAssembler.CheckSystem), JdAssembler.CheckSystem,
        "ade94fd9fb9709af5d3e8634467efd9d6fa885e3e98d8b3bc02dddc558abbee2")]
    [InlineData(nameof(JdAssembler.AltSystem), JdAssembler.AltSystem,
        "bfa76a936d8747a4b8b71a0d544ceefa0649dc53970394f6b4cc7a0c7158eaa0")]
    [InlineData(nameof(JdAssembler.GenSystem), JdAssembler.GenSystem,
        "74558e71948d091ddb208d75b27a8d9da0a2393f15b54524d500377370bb464d")]
    [InlineData(nameof(JdAssembler.ComplianceSystem), JdAssembler.ComplianceSystem,
        "9aa0946c0b21d2b76679d36106dac683e15cefeba56998326aa616183b3d2462")]
    public void System_prompts_are_unchanged(string name, string prompt, string expectedHash)
    {
        Sha256(prompt).Should().Be(expectedHash,
            "{0} changed. Prompts are ported byte-for-byte from the POC — if this edit was " +
            "deliberate, verify it against src/lib/jd/assemble.ts and update the hash. If it was " +
            "not, revert it: a behaviour change here is indistinguishable from a port bug.", name);
    }

    [Fact]
    public void System_prompts_have_no_leading_or_trailing_whitespace()
    {
        // A raw string literal makes a stray blank line easy to introduce, and the reference's
        // template literals have none.
        foreach (var prompt in new[]
                 {
                     JdAssembler.CheckSystem, JdAssembler.AltSystem,
                     JdAssembler.GenSystem, JdAssembler.ComplianceSystem,
                 })
        {
            prompt.Should().Be(prompt.Trim());
            prompt.Should().NotContain("\r", "line endings must be LF, as in the reference");
            prompt.Split('\n').Should().OnlyContain(l => l == l.TrimEnd(),
                "no line may carry trailing whitespace");
        }
    }

    [Fact]
    public void The_verdict_enum_reaches_the_wire_as_snake_case()
    {
        // The reference used a Zod string enum, so the model must return "out_of_envelope", not
        // "OutOfEnvelope" and not an integer. A PascalCase or numeric schema would bind to the
        // default member silently — every verdict would read as in_envelope.
        var schema = Server.Core.Ai.StructuredSchema.For<EnvelopeCheck>().ToJsonString();

        schema.Should().Contain("in_envelope");
        schema.Should().Contain("borderline");
        schema.Should().Contain("out_of_envelope");
        schema.Should().NotContain("OutOfEnvelope");
    }

    [Fact]
    public void The_envelope_context_is_assembled_exactly()
    {
        var expected =
            "CLASS: Rsch Data Anl 2 (UC job code 006256)\n" +
            "Career Tracks: Research / Research Data Analysis · PSS\n" +
            "Salary grade: Grade 21 · FLSA: Exempt · Unit: 99 - Non-Represented (PPSM)\n" +
            "Standard summary: Applies professional research data concepts to analyse study data.\n" +
            "Scope: Works under general supervision within an established program.\n" +
            "Standard key responsibilities:\n" +
            "- 60% DATA ANALYSIS: Cleans and validates study datasets.; Runs statistical analyses.\n" +
            "- 40% REPORTING: Prepares written summaries of findings.\n" +
            "Standard minimum KSAs:\n" +
            "- Working knowledge of statistical methods.\n" +
            "- Skill in written communication.\n" +
            "Typical certifications:\n" +
            "- None required.\n" +
            "Out-of-envelope signals (these indicate a DIFFERENT class):\n" +
            "- Supervising career staff.\n" +
            "- Managing a budget.";

        JdAssembler.EnvelopeContext(JdTestData.Profile()).Should().Be(expected);
    }

    [Fact]
    public void A_class_with_no_consensus_reads_varies_rather_than_blank()
    {
        // "varies" is a real statement about the class. An empty value would read as missing data.
        var profile = JdTestData.Profile();
        profile.Distributions.Clear();

        var context = JdAssembler.EnvelopeContext(profile);

        context.Should().Contain("Salary grade: varies · FLSA: varies · Unit: varies");
    }

    [Fact]
    public void A_class_with_no_envelope_falls_back_to_its_representative_summary()
    {
        // Bootstrapped classes have no synthesized envelope yet, and the prompt still has to say
        // something true about the class.
        var profile = JdTestData.Profile();
        profile.Envelope = null;

        var context = JdAssembler.EnvelopeContext(profile);

        context.Should().Contain("Standard summary: Analyzes research data for the department.");
        context.Should().Contain("Scope: \n");
    }

    [Fact]
    public void Additions_text_reports_no_additions_explicitly()
    {
        // Silence would read as an empty section; the model needs to know the standard is being
        // taken as-is.
        JdAssembler.AdditionsText(JdTestData.Inputs())
            .Should().Be("(no additions — using the standard as-is)");
    }

    [Fact]
    public void Additions_text_lists_items_then_notes()
    {
        var inputs = JdTestData.Inputs();
        inputs.AddedItems = ["Supervises two student assistants.", "Manages the survey budget."];
        inputs.Notes = "  This role is new.  ";

        JdAssembler.AdditionsText(inputs).Should().Be(
            "Added items:\n" +
            "- Supervises two student assistants.\n" +
            "- Manages the survey budget.\n" +
            "\n" +
            "Notes: This role is new.");
    }

    [Fact]
    public void Additions_text_omits_an_empty_half()
    {
        var itemsOnly = JdTestData.Inputs();
        itemsOnly.AddedItems = ["Runs the lab."];
        JdAssembler.AdditionsText(itemsOnly).Should().Be("Added items:\n- Runs the lab.");

        var notesOnly = JdTestData.Inputs();
        notesOnly.Notes = "Part time.";
        JdAssembler.AdditionsText(notesOnly).Should().Be("Notes: Part time.");
    }

    [Fact]
    public void Whitespace_only_notes_do_not_count_as_an_addition()
    {
        var inputs = JdTestData.Inputs();
        inputs.Notes = "   \t  ";

        var assembler = new JdAssembler(new FakeStructuredLlm());
        assembler.HasAdditions(inputs).Should().BeFalse();
        JdAssembler.AdditionsText(inputs).Should().Be("(no additions — using the standard as-is)");
    }

    [Fact]
    public void Functions_are_numbered_with_indented_duty_bullets()
    {
        JdAssembler.FunctionBlock(JdTestData.Inputs()).Should().Be(
            "0: 60% — DATA ANALYSIS\n" +
            "   - Cleans and validates study datasets.\n" +
            "   - Runs statistical analyses.\n" +
            "1: 40% — REPORTING\n" +
            "   - Prepares written summaries of findings.");
    }

    [Fact]
    public void Qualification_items_are_numbered_across_sections_in_a_fixed_order()
    {
        // One flat numbering across all six sections, built identically here and during assembly —
        // that is what lets the model remove item 3 and have it mean the same item on both sides.
        JdAssembler.QualBlock(JdTestData.Inputs()).Should().Be(
            "0: [education] Bachelor's degree or equivalent experience.\n" +
            "1: [workExperience] Two years of related experience.\n" +
            "2: [minKSA] Working knowledge of statistical methods.\n" +
            "3: [workEnvironment] Standard office environment.");
    }

    [Fact]
    public void The_generation_prompt_is_assembled_exactly()
    {
        var expected =
            JdAssembler.EnvelopeContext(JdTestData.Profile()) + "\n" +
            "\n" +
            "KEPT FUNCTIONS (numbered; % time is fixed):\n" +
            JdAssembler.FunctionBlock(JdTestData.Inputs()) + "\n" +
            "\n" +
            "KEPT QUALIFICATION ITEMS (numbered):\n" +
            JdAssembler.QualBlock(JdTestData.Inputs()) + "\n" +
            "\n" +
            "MANAGER'S ADDITIONS:\n" +
            "(no additions — using the standard as-is)\n" +
            "\n" +
            "Working title: Evaluation Analyst\n" +
            "Department: Office of Evaluation";

        JdAssembler.GenerateUserPrompt(JdTestData.Profile(), JdTestData.Inputs())
            .Should().Be(expected);
    }

    [Fact]
    public void An_empty_build_says_none_kept_rather_than_leaving_a_gap()
    {
        var inputs = JdTestData.Inputs();
        inputs.KeptResponsibilities = [];
        inputs.KeptEducation = [];
        inputs.KeptWorkExperience = [];
        inputs.KeptMinKSA = [];
        inputs.KeptWorkEnvironment = [];

        var prompt = JdAssembler.GenerateUserPrompt(JdTestData.Profile(), inputs);

        prompt.Should().Contain("KEPT FUNCTIONS (numbered; % time is fixed):\n(none kept)");
        prompt.Should().Contain("KEPT QUALIFICATION ITEMS (numbered):\n(none kept)");
    }

    [Fact]
    public void A_blank_working_title_falls_back_to_the_class_title()
    {
        var inputs = JdTestData.Inputs();
        inputs.WorkingTitle = "";
        inputs.Department = "";

        var prompt = JdAssembler.GenerateUserPrompt(JdTestData.Profile(), inputs);

        prompt.Should().Contain("Working title: Rsch Data Anl 2");
        prompt.Should().Contain("Department: (unspecified)");
    }
}

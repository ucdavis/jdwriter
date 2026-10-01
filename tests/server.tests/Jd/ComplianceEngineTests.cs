using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Jd;

namespace Server.Tests.Jd;

/// <summary>
/// The audit trail is the point of this layer, so these tests are mostly about what gets RECORDED
/// and what gets REFUSED — not about the text coming out right.
///
/// A rewrite that happens without an audit entry is worse than no rewrite: an analyst handing a job
/// description to a department has to be able to see that a duty was reworded and on whose
/// authority.
/// </summary>
public class ComplianceEngineTests
{
    // ------------------------------------------------------------------ addressable fields

    [Fact]
    public void Text_fields_are_numbered_in_a_stable_documented_order()
    {
        // The index IS the address the model returns, so this order is a wire contract. Reordering
        // it silently re-points every edit a model makes.
        var paths = ComplianceEngine.TextFields(JdTestData.Draft()).Select(f => f.Path).ToList();

        paths.Should().Equal(
            "jobSummary",
            "keyResponsibilities[0].functionName",
            "keyResponsibilities[0].duties[0]",
            "keyResponsibilities[0].duties[1]",
            "keyResponsibilities[1].functionName",
            "keyResponsibilities[1].duties[0]",
            "licensesCertifications[0]",
            "education[0]",
            "workExperience[0]",
            "minKSA[0]",
            "prefKSA[0]",
            "conditionsOfEmployment[0]",
            "workEnvironment[0]",
            "physicalRequirements[0]");
    }

    [Fact]
    public void Paths_use_the_reference_implementations_property_names()
    {
        // These strings land in the audit trail's Section column, so an analyst reading "minKSA[2]"
        // is reading the same address the POC produced. C# names would silently change them.
        var paths = ComplianceEngine.TextFields(JdTestData.Draft()).Select(f => f.Path).ToList();

        paths.Should().Contain("minKSA[0]");
        paths.Should().Contain("prefKSA[0]");
        paths.Should().NotContain(p => p.StartsWith("MinKSA", StringComparison.Ordinal));
    }

    [Fact]
    public void No_numeric_field_is_addressable()
    {
        // Percentages never appear in the addressable list, which is what makes it impossible for a
        // compliance pass to touch the arithmetic.
        var fields = ComplianceEngine.TextFields(JdTestData.Draft());

        fields.Should().NotContain(f => f.Path.Contains("pctTime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_setter_writes_back_to_the_object_it_came_from()
    {
        var jd = JdTestData.Draft();
        var fields = ComplianceEngine.TextFields(jd);

        fields.Single(f => f.Path == "keyResponsibilities[0].duties[1]").Set("Runs regressions.");
        fields.Single(f => f.Path == "minKSA[0]").Set("Advanced statistics.");

        jd.KeyResponsibilities[0].Duties[1].Should().Be("Runs regressions.");
        jd.MinKSA[0].Should().Be("Advanced statistics.");
    }

    // ------------------------------------------------------------------ applying edits

    [Fact]
    public void An_edit_records_before_after_section_and_reason()
    {
        var jd = JdTestData.Draft();

        var result = ComplianceEngine.ApplyTextEdits(jd, [
            new TextEdit { Index = 0, After = "Analyses study data and reports findings.", Reason = "Spelling" },
        ], ComplianceEditSource.Llm);

        result.Edits.Should().ContainSingle();
        var edit = result.Edits[0];
        edit.Section.Should().Be("jobSummary");
        edit.Source.Should().Be(ComplianceEditSource.Llm);
        edit.Before.Should().Be("Analyzes study data and reports findings.");
        edit.After.Should().Be("Analyses study data and reports findings.");
        edit.Reason.Should().Be("Spelling");
    }

    [Fact]
    public void Edits_apply_to_a_copy_and_leave_the_original_untouched()
    {
        // Otherwise applying an edit would also alter the "before" text recorded in the trail.
        var jd = JdTestData.Draft();

        var result = ComplianceEngine.ApplyTextEdits(jd, [
            new TextEdit { Index = 0, After = "Rewritten.", Reason = "x" },
        ], ComplianceEditSource.Llm);

        jd.JobSummary.Should().Be("Analyzes study data and reports findings.");
        result.Jd.JobSummary.Should().Be("Rewritten.");
    }

    [Theory]
    [InlineData(99, "An index past the end must not corrupt anything")]
    [InlineData(-1, "A negative index is nonsense, not a reverse index")]
    public void An_unaddressable_index_is_dropped(int index, string why)
    {
        var result = ComplianceEngine.ApplyTextEdits(JdTestData.Draft(), [
            new TextEdit { Index = index, After = "Whatever.", Reason = "x" },
        ], ComplianceEditSource.Llm);

        result.Edits.Should().BeEmpty(why);
        result.Dropped.Should().Be(1);
    }

    [Fact]
    public void A_duplicate_index_is_dropped_and_the_first_edit_wins()
    {
        var result = ComplianceEngine.ApplyTextEdits(JdTestData.Draft(), [
            new TextEdit { Index = 0, After = "First.", Reason = "a" },
            new TextEdit { Index = 0, After = "Second.", Reason = "b" },
        ], ComplianceEditSource.Llm);

        result.Edits.Should().ContainSingle();
        result.Edits[0].After.Should().Be("First.");
        result.Jd.JobSummary.Should().Be("First.");
        result.Dropped.Should().Be(1);
    }

    [Fact]
    public void A_blank_replacement_is_dropped_rather_than_deleting_the_line()
    {
        // A model returning "" for a line is a malfunction, not an instruction to empty a section.
        var result = ComplianceEngine.ApplyTextEdits(JdTestData.Draft(), [
            new TextEdit { Index = 0, After = "   ", Reason = "x" },
        ], ComplianceEditSource.Llm);

        result.Edits.Should().BeEmpty();
        result.Dropped.Should().Be(1);
        result.Jd.JobSummary.Should().Be("Analyzes study data and reports findings.");
    }

    [Fact]
    public void A_no_op_edit_is_dropped_so_the_trail_stays_meaningful()
    {
        // An audit trail full of "changed X to X" is an audit trail nobody reads.
        var jd = JdTestData.Draft();
        var result = ComplianceEngine.ApplyTextEdits(jd, [
            new TextEdit { Index = 0, After = jd.JobSummary, Reason = "x" },
        ], ComplianceEditSource.Llm);

        result.Edits.Should().BeEmpty();
        result.Dropped.Should().Be(1);
    }

    [Fact]
    public void Replacement_text_is_trimmed()
    {
        var result = ComplianceEngine.ApplyTextEdits(JdTestData.Draft(), [
            new TextEdit { Index = 0, After = "  Trimmed.  ", Reason = "x" },
        ], ComplianceEditSource.Llm);

        result.Edits[0].After.Should().Be("Trimmed.");
        result.Jd.JobSummary.Should().Be("Trimmed.");
    }

    // ------------------------------------------------------------------ deterministic rules

    [Fact]
    public void Rules_rewrite_every_addressable_section()
    {
        var jd = JdTestData.Draft();
        jd.JobSummary = "Seeking a rockstar analyst.";
        jd.KeyResponsibilities[0].Duties[0] = "Coordinates with the chairman.";
        jd.MinKSA[0] = "Comfortable as a digital native.";

        var result = ComplianceEngine.ApplyRulePass(jd, JdTestData.Rules());

        result.Jd.JobSummary.Should().Be("Seeking a expert analyst.");
        result.Jd.KeyResponsibilities[0].Duties[0].Should().Be("Coordinates with the chair.");
        result.Jd.MinKSA[0].Should().Be("Comfortable as a skilled with digital tools.");
        result.Edits.Should().HaveCount(3);
    }

    [Fact]
    public void A_rule_edit_names_the_rule_that_fired()
    {
        // "inclusive: Gender-neutral title" tells an analyst which policy drove the change. A bare
        // "rule" would not.
        var jd = JdTestData.Draft();
        jd.JobSummary = "Reports to the chairman.";

        var result = ComplianceEngine.ApplyRulePass(jd, JdTestData.Rules());

        result.Edits.Should().ContainSingle();
        result.Edits[0].Source.Should().Be(ComplianceEditSource.Rule);
        result.Edits[0].Reason.Should().Be("inclusive: Gender-neutral title");
        result.Edits[0].Section.Should().Be("jobSummary");
    }

    [Fact]
    public void Two_rules_on_one_line_chain_into_two_audit_entries()
    {
        // Each entry shows what THAT rule did, so the sequence is reconstructible rather than being
        // collapsed into one opaque before-and-after.
        var jd = JdTestData.Draft();
        jd.JobSummary = "A young rockstar analyst.";

        var result = ComplianceEngine.ApplyRulePass(jd, JdTestData.Rules());

        result.Edits.Should().HaveCount(2);
        result.Edits[0].Before.Should().Be("A young rockstar analyst.");
        result.Edits[0].After.Should().Be("A young expert analyst.");
        result.Edits[1].Before.Should().Be("A young expert analyst.");
        result.Edits[1].After.Should().Be("A early-career expert analyst.");
        result.Jd.JobSummary.Should().Be("A early-career expert analyst.");
    }

    [Fact]
    public void Rules_match_case_insensitively()
    {
        var jd = JdTestData.Draft();
        jd.JobSummary = "Seeking a ROCKSTAR.";

        ComplianceEngine.ApplyRulePass(jd, JdTestData.Rules())
            .Jd.JobSummary.Should().Be("Seeking a expert.");
    }

    [Fact]
    public void A_clean_draft_produces_no_edits()
    {
        ComplianceEngine.ApplyRulePass(JdTestData.Draft(), JdTestData.Rules())
            .Edits.Should().BeEmpty();
    }

    [Fact]
    public void A_disabled_rule_does_not_fire()
    {
        // The Enabled column exists so HR can retire a rule without deleting the audit history
        // that references it.
        var rules = JdTestData.Rules();
        foreach (var r in rules)
        {
            r.Enabled = false;
        }

        var jd = JdTestData.Draft();
        jd.JobSummary = "Seeking a rockstar.";

        var result = ComplianceEngine.ApplyRulePass(jd, rules);
        result.Edits.Should().BeEmpty();
        result.Jd.JobSummary.Should().Be("Seeking a rockstar.");
    }

    private static List<ComplianceRule> RulesWithOneBroken() =>
    [
        new() { Key = "broken-rule", Pattern = "([unclosed", Replacement = "x", Reason = "bad", Enabled = true },
        new() { Key = "inclusive", Pattern = @"\brockstar\b", Replacement = "expert", Reason = "Jargon", Enabled = true },
    ];

    [Fact]
    public void A_malformed_pattern_does_not_block_the_other_rules()
    {
        // Patterns come from the database so HR can change policy without a deployment, which means
        // a malformed one is possible. One typo must not stop every author from working.
        var jd = JdTestData.Draft();
        jd.JobSummary = "Seeking a rockstar.";

        var result = ComplianceEngine.ApplyRulePass(jd, RulesWithOneBroken());

        result.Jd.JobSummary.Should().Be("Seeking a expert.", "the good rule still ran");
    }

    [Fact]
    public void A_malformed_pattern_is_recorded_in_the_audit_trail_as_a_failure()
    {
        // THE dangerous failure mode in this system: a compliance rule that quietly stops applying,
        // on a document that goes to a department. Skipping it silently is what nobody would
        // notice, so the skip is itself auditable.
        var jd = JdTestData.Draft();
        jd.JobSummary = "Seeking a rockstar.";

        var result = ComplianceEngine.ApplyRulePass(jd, RulesWithOneBroken());

        var failure = result.Edits.Should().ContainSingle(e => e.IsRuleFailure).Subject;
        failure.Section.Should().Be("broken-rule", "the entry names the rule, not a document path");
        failure.Reason.Should().StartWith(ComplianceEditRecord.RuleFailedMarker);
        failure.Reason.Should().Contain("([unclosed");
    }

    [Fact]
    public void A_rule_failure_cannot_be_mistaken_for_an_applied_edit()
    {
        // Before and After are both empty, which no real edit ever is — a reviewer scanning the
        // trail cannot read this as a change that was made.
        var jd = JdTestData.Draft();
        jd.JobSummary = "Seeking a rockstar.";

        var result = ComplianceEngine.ApplyRulePass(jd, RulesWithOneBroken());

        var failure = result.Edits.Single(e => e.IsRuleFailure);
        failure.Before.Should().BeEmpty();
        failure.After.Should().BeEmpty();

        var applied = result.Edits.Single(e => !e.IsRuleFailure);
        applied.IsRuleFailure.Should().BeFalse();
        applied.Before.Should().NotBe(applied.After);
    }

    [Fact]
    public void A_broken_rule_is_reported_once_not_once_per_line()
    {
        // Repeating it for every addressable line would bury the one entry that matters.
        var jd = JdTestData.Draft();

        var result = ComplianceEngine.ApplyRulePass(jd, RulesWithOneBroken());

        result.Edits.Count(e => e.IsRuleFailure).Should().Be(1);
    }

    [Fact]
    public void A_broken_rule_is_logged_with_its_key_and_the_regex_error()
    {
        // The audit trail reaches whoever reviews that job description. The log reaches whoever
        // maintains the rules, who is the person who can actually fix it.
        var logger = new CapturingLogger();
        var jd = JdTestData.Draft();

        ComplianceEngine.ApplyRulePass(jd, RulesWithOneBroken(), logger);

        logger.Warnings.Should().ContainSingle();
        logger.Warnings[0].Should().Contain("broken-rule").And.Contain("([unclosed");
    }

    [Fact]
    public void A_disabled_broken_rule_is_not_reported()
    {
        // It is not being applied because nobody wants it applied. Reporting it would be noise.
        var rules = RulesWithOneBroken();
        rules[0].Enabled = false;

        var result = ComplianceEngine.ApplyRulePass(JdTestData.Draft(), rules);

        result.Edits.Should().NotContain(e => e.IsRuleFailure);
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    [Fact]
    public void The_rule_pass_never_touches_percentages()
    {
        var jd = JdTestData.Draft();
        jd.JobSummary = "A rockstar role.";

        var result = ComplianceEngine.ApplyRulePass(jd, JdTestData.Rules());

        result.Jd.KeyResponsibilities.Select(r => r.PctTime).Should().Equal(60, 40);
    }
}

using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Profiles;

namespace Server.Tests.Profiles;

/// <summary>
/// Consolidation is where the governing rule is most visible: the model groups, C# recomputes every
/// number. So these tests do two things — assert the exact prompt the model receives, and feed back
/// replies whose statistics are deliberately absent, proving the output numbers are derived from the
/// records rather than taken on trust.
/// </summary>
public class ConsolidatorTests
{
    private static HrtmsRecord Jd(
        string file,
        List<HrtmsResponsibility>? fns = null,
        List<string>? licenses = null,
        List<string>? ksaMin = null) => new()
    {
        SourceFile = file,
        Responsibilities = fns ?? [],
        Qualifications = new HrtmsQualifications { Licenses = licenses ?? [], KsaMin = ksaMin ?? [] },
    };

    private static HrtmsResponsibility Resp(string name, int? pct, params string[] duties) =>
        new() { FunctionName = name, Pct = pct, Duties = [.. duties] };

    private static object Group(string name, params string[] ids) => new { name, memberIds = ids };

    private static object Pass1(
        object[] functionGroups,
        object[]? certGroups = null,
        object[]? minQualGroups = null,
        string[]? droppedFunctionIds = null,
        string[]? droppedCertIds = null,
        string[]? droppedMinQualIds = null) => new
        {
            functionGroups,
            droppedFunctionIds = droppedFunctionIds ?? [],
            certGroups = certGroups ?? [],
            droppedCertIds = droppedCertIds ?? [],
            minQualGroups = minQualGroups ?? [],
            droppedMinQualIds = droppedMinQualIds ?? [],
        };

    // ---------------------------------------------------------------- prompt assembly

    [Fact]
    public async Task The_pass_one_prompt_is_assembled_exactly()
    {
        var llm = new FakeStructuredLlm().Returns(Pass1([Group("Field Work", "F0", "F1")]));

        var records = new List<HrtmsRecord>
        {
            Jd("a", [Resp("Cultural Operations", 60), Resp("Harvest", 40)],
                licenses: ["CA Driver's License"], ksaMin: ["Attention to detail"]),
            Jd("b", [Resp("Cultural Operations", 80)], ksaMin: ["Attention to detail"]),
        };

        await new Consolidator(llm).ConsolidateAsync(records);

        // Percentages in the prompt are the mean among the JDs that HAVE the function, and the
        // document counts are shares of the corpus — both computed here, not by the model.
        llm.RequestFor("consolidate.pass1").User.Should().Be(
            """
            RESPONSIBILITY FUNCTIONS:
            F0: Cultural Operations — ~70% time, 2/2 JDs
            F1: Harvest — ~40% time, 1/2 JDs

            LICENSES / CERTIFICATIONS:
            C0: CA Driver's License — 1/2 JDs

            MINIMUM QUALIFICATIONS (KSAs):
            M0: Attention to detail — 2/2 JDs

            Consolidate each list into broad categories per the rules.
            """);
    }

    [Fact]
    public async Task Empty_lists_render_as_none()
    {
        var llm = new FakeStructuredLlm().Returns(Pass1([Group("Work", "F0")]));

        await new Consolidator(llm).ConsolidateAsync([Jd("a", [Resp("Work", 100)])]);

        var user = llm.RequestFor("consolidate.pass1").User;
        user.Should().Contain("LICENSES / CERTIFICATIONS:\n(none)");
        user.Should().Contain("MINIMUM QUALIFICATIONS (KSAs):\n(none)");
    }

    [Fact]
    public async Task Pass_one_runs_at_medium_effort_with_room_for_a_long_reply()
    {
        var llm = new FakeStructuredLlm().Returns(Pass1([Group("Work", "F0")]));
        await new Consolidator(llm).ConsolidateAsync([Jd("a", [Resp("Work", 100)])]);

        var req = llm.RequestFor("consolidate.pass1");
        req.Effort.Should().Be(Server.Core.Ai.LlmEffort.Medium);
        req.MaxTokens.Should().Be(20000);
        req.System.Should().Be(Consolidator.Pass1System);
    }

    // ---------------------------------------------------------------- honest recomputation

    [Fact]
    public async Task Category_statistics_are_recomputed_from_the_records()
    {
        // The model said only which raw names belong together. Every number below is derived here.
        var llm = new FakeStructuredLlm().Returns(Pass1([Group("Field Work", "F0", "F1")]));

        var records = new List<HrtmsRecord>
        {
            Jd("a", [Resp("Irrigation", 60, "Water crops"), Resp("Weeding", 40, "Pull weeds")]),
            Jd("b", [Resp("Irrigation", 50, "Water crops")]),
            Jd("c", [Resp("Unrelated", 100)]),
        };

        var result = await new Consolidator(llm).ConsolidateAsync(records);

        var group = result.FunctionGroups.Should().ContainSingle().Subject;
        group.Name.Should().Be("Field Work");

        // Per-document totals are 100 (a), 50 (b), absent (c).
        group.MeanPct.Should().Be(75, "mean among the two JDs that HAVE the category");
        group.MinPct.Should().Be(50);
        group.MaxPct.Should().Be(100);
        group.Prevalence.Should().BeApproximately(2.0 / 3, 1e-9);
        group.Members.Select(m => m.Text).Should().Equal("Irrigation", "Weeding");
        group.SampleDuties.Select(d => d.Text).Should().BeEquivalentTo(["Water crops", "Pull weeds"]);
    }

    [Fact]
    public async Task Template_percent_is_normalized_to_exactly_one_hundred()
    {
        // TemplatePct is the share of a typical JD's 100% across ALL JDs, and the set is scaled so it
        // reads like a real job description rather than summing to 99 or 101.
        var llm = new FakeStructuredLlm().Returns(
            Pass1([Group("A", "F0"), Group("B", "F1"), Group("C", "F2")]));

        var result = await new Consolidator(llm).ConsolidateAsync(
        [
            Jd("a", [Resp("One", 33), Resp("Two", 33), Resp("Three", 34)]),
        ]);

        result.FunctionGroups.Sum(g => g.TemplatePct).Should().Be(100);
    }

    [Fact]
    public async Task The_rounding_remainder_is_given_to_the_largest_category()
    {
        // Three equal thirds round to 33 each and sum to 99. Without the remainder adjustment the
        // template would quietly read as 99% of a job — which is why a case that sums to 100 before
        // rounding cannot prove this works.
        var llm = new FakeStructuredLlm().Returns(
            Pass1([Group("A", "F0"), Group("B", "F1"), Group("C", "F2")]));

        var result = await new Consolidator(llm).ConsolidateAsync(
        [
            Jd("a", [Resp("One", 10)]),
            Jd("b", [Resp("Two", 10)]),
            Jd("c", [Resp("Three", 10)]),
        ]);

        result.FunctionGroups.Sum(g => g.TemplatePct).Should().Be(100);
        result.FunctionGroups.Select(g => g.TemplatePct).Should().BeEquivalentTo([34.0, 33.0, 33.0]);
    }

    [Fact]
    public async Task Mean_percent_and_template_percent_are_different_numbers()
    {
        // Only TemplatePct sums to ~100. Conflating them would make a function that appears in half
        // the JDs look like it fills half of every JD.
        var llm = new FakeStructuredLlm().Returns(Pass1([Group("Rare", "F0"), Group("Common", "F1")]));

        var result = await new Consolidator(llm).ConsolidateAsync(
        [
            Jd("a", [Resp("Rare Work", 100)]),
            Jd("b", [Resp("Common Work", 100)]),
            Jd("c", [Resp("Common Work", 100)]),
        ]);

        var rare = result.FunctionGroups.Single(g => g.Name == "Rare");
        rare.MeanPct.Should().Be(100, "it is the whole job where it appears");
        rare.TemplatePct.Should().BeLessThan(50, "but it only appears in one JD of three");
        result.FunctionGroups.Sum(g => g.TemplatePct).Should().Be(100);
    }

    [Fact]
    public async Task Function_groups_are_ordered_by_template_percent_descending()
    {
        var llm = new FakeStructuredLlm().Returns(Pass1([Group("Small", "F0"), Group("Large", "F1")]));

        var result = await new Consolidator(llm).ConsolidateAsync(
            [Jd("a", [Resp("Minor", 20), Resp("Major", 80)])]);

        result.FunctionGroups.Select(g => g.Name).Should().Equal("Large", "Small");
        result.FunctionGroups.Select(g => g.Ordinal).Should().Equal(0, 1);
    }

    [Fact]
    public async Task Qualification_frequency_is_recomputed_from_documents()
    {
        var llm = new FakeStructuredLlm().Returns(Pass1(
            [Group("Work", "F0")],
            certGroups: [Group("Driver's Licence", "C0", "C1")]));

        var result = await new Consolidator(llm).ConsolidateAsync(
        [
            Jd("a", [Resp("Work", 100)], licenses: ["CA Driver's License"]),
            Jd("b", [Resp("Work", 100)], licenses: ["Valid drivers license"]),
            Jd("c", [Resp("Work", 100)]),
            Jd("d", [Resp("Work", 100)]),
        ]);

        var cert = result.Certifications.Should().ContainSingle().Subject;
        cert.Kind.Should().Be(ConsolidatedQualKind.Certification);
        // Two distinct spellings across two of four documents.
        cert.Freq.Should().Be(0.5);
        cert.Members.Should().HaveCount(2);
    }

    [Fact]
    public async Task Minimum_qualifications_are_capped_at_seven()
    {
        // The envelope is meant to be usable, not exhaustive.
        var groups = Enumerable.Range(0, 10).Select(i => Group($"KSA {i}", $"M{i}")).ToArray();
        var ksas = Enumerable.Range(0, 10).Select(i => $"Requirement number {i}").ToList();

        var llm = new FakeStructuredLlm().Returns(Pass1([Group("Work", "F0")], minQualGroups: groups));

        var result = await new Consolidator(llm).ConsolidateAsync(
            [Jd("a", [Resp("Work", 100)], ksaMin: ksas)]);

        result.MinQualifications.Should().HaveCount(7);
    }

    [Fact]
    public async Task Dropped_items_are_resolved_back_to_their_names()
    {
        var llm = new FakeStructuredLlm().Returns(Pass1(
            [Group("Work", "F0")],
            droppedFunctionIds: ["F1"],
            droppedCertIds: ["C0"],
            droppedMinQualIds: ["M0"]));

        var result = await new Consolidator(llm).ConsolidateAsync(
        [
            Jd("a", [Resp("Work", 90), Resp("Odd One Out", 10)],
                licenses: ["Irrelevant certificate"], ksaMin: ["Boilerplate requirement"]),
        ]);

        result.DroppedFunctions.Should().Equal("Odd One Out");
        result.DroppedQualifications.Should().Equal("Irrelevant certificate", "Boilerplate requirement");
    }

    [Fact]
    public async Task Unparseable_or_out_of_range_ids_are_discarded_not_guessed()
    {
        var llm = new FakeStructuredLlm().Returns(Pass1([Group("Work", "F0", "F99", "nonsense")]));

        var result = await new Consolidator(llm).ConsolidateAsync([Jd("a", [Resp("Work", 100)])]);

        result.FunctionGroups.Should().ContainSingle()
            .Which.Members.Should().ContainSingle().Which.Text.Should().Be("Work");
    }

    [Fact]
    public async Task A_group_with_no_resolvable_members_is_dropped_entirely()
    {
        var llm = new FakeStructuredLlm().Returns(Pass1([Group("Real", "F0"), Group("Phantom", "F42")]));

        var result = await new Consolidator(llm).ConsolidateAsync([Jd("a", [Resp("Work", 100)])]);

        result.FunctionGroups.Select(g => g.Name).Should().Equal("Real");
    }

    // ---------------------------------------------------------------- two-pass behaviour

    [Fact]
    public async Task Pass_two_is_skipped_when_pass_one_did_not_overshoot()
    {
        // Merging four categories into four costs a call and changes nothing.
        var llm = new FakeStructuredLlm().Returns(Pass1(
            [Group("A", "F0"), Group("B", "F1"), Group("C", "F2"), Group("D", "F3")]));

        await new Consolidator(llm).ConsolidateAsync(
            [Jd("a", [Resp("W", 25), Resp("X", 25), Resp("Y", 25), Resp("Z", 25)])]);

        llm.Requests.Should().ContainSingle();
        llm.Requests[0].Label.Should().Be("consolidate.pass1");
    }

    [Fact]
    public async Task Pass_two_merges_when_pass_one_produced_more_than_four()
    {
        var llm = new FakeStructuredLlm()
            .Returns(Pass1([Group("A", "F0"), Group("B", "F1"), Group("C", "F2"), Group("D", "F3"), Group("E", "F4")]))
            .Returns(new { groups = new[] { new { name = "Everything", memberIndices = new[] { 0, 1, 2, 3, 4 } } } });

        var result = await new Consolidator(llm).ConsolidateAsync(
            [Jd("a", [Resp("V", 20), Resp("W", 20), Resp("X", 20), Resp("Y", 20), Resp("Z", 20)])]);

        llm.Requests.Select(r => r.Label).Should().Equal("consolidate.pass1", "consolidate.pass2");
        result.FunctionGroups.Should().ContainSingle().Which.Name.Should().Be("Everything");

        var pass2 = llm.RequestFor("consolidate.pass2");
        pass2.Effort.Should().Be(Server.Core.Ai.LlmEffort.Low);
        pass2.User.Should().Be(
            """
            FIRST-PASS CATEGORIES:
            0: A (1 member functions)
            1: B (1 member functions)
            2: C (1 member functions)
            3: D (1 member functions)
            4: E (1 member functions)

            Merge into the fewest broad categories.
            """);
    }

    [Fact]
    public async Task A_category_pass_two_failed_to_place_is_kept_rather_than_lost()
    {
        // Losing a whole category of work would be invisible in the output, so the safety net keeps
        // anything pass 2 did not absorb.
        var llm = new FakeStructuredLlm()
            .Returns(Pass1([Group("A", "F0"), Group("B", "F1"), Group("C", "F2"), Group("D", "F3"), Group("Orphan", "F4")]))
            .Returns(new { groups = new[] { new { name = "Merged", memberIndices = new[] { 0, 1, 2, 3 } } } });

        var result = await new Consolidator(llm).ConsolidateAsync(
            [Jd("a", [Resp("V", 20), Resp("W", 20), Resp("X", 20), Resp("Y", 20), Resp("Orphaned Work", 20)])]);

        result.FunctionGroups.Select(g => g.Name).Should().Contain("Orphan");
        result.FunctionGroups.Sum(g => g.TemplatePct).Should().Be(100);
    }
}

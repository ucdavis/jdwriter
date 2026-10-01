using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Profiles;

namespace Server.Tests.Profiles;

/// <summary>
/// The aggregator is pure arithmetic over parsed records, so it earns real tests rather than
/// fixtures: every number it produces is checked against a hand-computed expectation.
///
/// These are also the statistics the envelope is built on, so a quiet error here would surface as a
/// plausible-looking but wrong job description.
/// </summary>
public class ProfileAggregatorTests
{
    private static HrtmsRecord Record(
        string sourceFile,
        string code = "008543",
        string title = "FARM LABORER",
        string grade = "STEPS",
        string flsa = "Non-Exempt",
        string union = "SX-Service Workers",
        bool? supervises = null,
        bool? leads = null,
        bool? outdoors = null,
        string summary = "Performs general farm work.",
        List<HrtmsResponsibility>? responsibilities = null,
        List<string>? licenses = null,
        List<string>? ksaMin = null,
        string education = "",
        List<string>? workEnvironment = null) => new()
    {
        SourceFile = sourceFile,
        UcJobCode = code,
        UcJobTitle = title,
        CtJobFamily = "Agriculture",
        CtJobFunction = "Field Work",
        PersonnelProgram = "PSS",
        SalaryGrade = grade,
        FlsaStatus = flsa,
        UnionCode = union,
        Supervises = supervises,
        Leads = leads,
        WorksOutdoorsOver50pct = outdoors,
        JobSummary = summary,
        Responsibilities = responsibilities ?? [],
        WorkEnvironment = workEnvironment ?? [],
        Qualifications = new HrtmsQualifications
        {
            Licenses = licenses ?? [],
            KsaMin = ksaMin ?? [],
            Education = education,
        },
    };

    private static HrtmsResponsibility Resp(string name, int? pct, params string[] duties) => new()
    {
        FunctionName = name,
        Pct = pct,
        Duties = [.. duties],
    };

    private static ProfileDistribution Dist(ClassProfile p, DistributionField f) =>
        p.Distributions.Single(d => d.Field == f);

    [Fact]
    public void A_dominant_value_becomes_the_consensus()
    {
        var p = ProfileAggregator.Aggregate(
            [Record("a"), Record("b"), Record("c", grade: "Grade 21")], "008543-farm-laborer");

        var grade = Dist(p, DistributionField.SalaryGrade);
        grade.Consensus.Should().Be("STEPS");
        grade.Agreement.Should().BeApproximately(2.0 / 3, 1e-9);
    }

    [Fact]
    public void A_split_below_half_has_no_consensus()
    {
        // Half the corpus is the bar. Below it the class genuinely varies, and naming one value
        // would be a claim the data does not support.
        var p = ProfileAggregator.Aggregate(
            [Record("a", grade: "A"), Record("b", grade: "B"), Record("c", grade: "C")], "s");

        var grade = Dist(p, DistributionField.SalaryGrade);
        grade.Consensus.Should().BeNull();
        grade.Agreement.Should().BeApproximately(1.0 / 3, 1e-9);
    }

    [Fact]
    public void Exactly_half_is_enough_for_a_consensus()
    {
        var p = ProfileAggregator.Aggregate([Record("a", grade: "A"), Record("b", grade: "B")], "s");
        Dist(p, DistributionField.SalaryGrade).Consensus.Should().Be("A");
    }

    [Fact]
    public void Null_survives_as_a_real_observed_value()
    {
        // "The export did not say" is the MAJORITY answer for supervision across the real corpus —
        // 1,027 of 1,367 — and is not the same claim as an explicit no. Dropping it would skew
        // every consensus that depends on it.
        var p = ProfileAggregator.Aggregate(
            [Record("a", supervises: null), Record("b", supervises: null), Record("c", supervises: false)], "s");

        var sup = Dist(p, DistributionField.Supervises);
        sup.Values.Should().HaveCount(2);
        sup.Values.Should().Contain(v => v.Value == null && v.Count == 2);
        sup.Values.Should().Contain(v => v.Value == "false" && v.Count == 1);
        sup.Consensus.Should().BeNull("the modal value IS null here");
    }

    [Fact]
    public void Booleans_are_stored_as_tokens_not_dropped()
    {
        var p = ProfileAggregator.Aggregate(
            [Record("a", leads: true), Record("b", leads: true), Record("c", leads: false)], "s");

        var leads = Dist(p, DistributionField.Leads);
        leads.Consensus.Should().Be("true");
        leads.Values.Select(v => v.Value).Should().BeEquivalentTo(["true", "false"]);
    }

    [Fact]
    public void Distribution_values_are_ordered_by_count_descending()
    {
        var p = ProfileAggregator.Aggregate(
            [Record("a", union: "SX"), Record("b", union: "TX"), Record("c", union: "TX")], "s");

        Dist(p, DistributionField.UnionCode).Values
            .OrderBy(v => v.Ordinal).Select(v => v.Value)
            .Should().Equal("TX", "SX");
    }

    [Fact]
    public void Functions_cluster_on_a_canonical_name()
    {
        // "Cultural Operations" and "CULTURAL OPERATIONS" are the same function; case and spacing
        // must not split a category in two.
        var p = ProfileAggregator.Aggregate(
        [
            Record("a", responsibilities: [Resp("Cultural Operations", 60, "Irrigate")]),
            Record("b", responsibilities: [Resp("CULTURAL  OPERATIONS", 40, "Weed")]),
        ], "s");

        p.Functions.Should().ContainSingle();
        var f = p.Functions[0];
        f.Name.Should().Be("Cultural Operations", "the first spelling is titleized and wins");
        f.PctMin.Should().Be(40);
        f.PctMax.Should().Be(60);
        f.PctMean.Should().Be(50);
        f.PctN.Should().Be(2);
        f.Prevalence.Should().Be(1.0);
        f.SampleDuties.Select(d => d.Text).Should().BeEquivalentTo(["Irrigate", "Weed"]);
    }

    [Fact]
    public void Prevalence_counts_documents_not_occurrences()
    {
        // One JD listing a function three times is still one JD. Counting occurrences would let a
        // single verbose record dominate the class.
        var p = ProfileAggregator.Aggregate(
        [
            Record("a", responsibilities:
            [
                Resp("Harvest", 30), Resp("Harvest", 20), Resp("Harvest", 10),
            ]),
            Record("b", responsibilities: [Resp("Other", 100)]),
        ], "s");

        var harvest = p.Functions.Single(f => f.Name == "Harvest");
        harvest.Prevalence.Should().Be(0.5);
        harvest.PctN.Should().Be(3, "all three stated percentages still inform the range");
    }

    [Fact]
    public void Functions_are_ordered_by_mean_percent_descending()
    {
        var p = ProfileAggregator.Aggregate(
        [
            Record("a", responsibilities: [Resp("Small", 10), Resp("Big", 70), Resp("Middle", 20)]),
        ], "s");

        p.Functions.OrderBy(f => f.Ordinal).Select(f => f.Name).Should().Equal("Big", "Middle", "Small");
    }

    [Fact]
    public void A_function_with_no_stated_percentage_ranges_to_zero()
    {
        var p = ProfileAggregator.Aggregate(
            [Record("a", responsibilities: [Resp("Unquantified", null, "Does things")])], "s");

        var f = p.Functions[0];
        f.PctMin.Should().Be(0);
        f.PctMax.Should().Be(0);
        f.PctMean.Should().Be(0);
        f.PctN.Should().Be(0, "no observations, rather than one observation of zero");
    }

    [Fact]
    public void Percentages_that_do_not_sum_to_100_are_recorded_as_they_are()
    {
        // Six real JDs in the corpus range from 0 to 200. The aggregator reports; it never rejects.
        var p = ProfileAggregator.Aggregate(
            [Record("a", responsibilities: [Resp("A", 120), Resp("B", 80)])], "s");

        p.Functions.Sum(f => f.PctMean).Should().Be(200);
    }

    [Fact]
    public void Qualification_frequency_is_a_share_of_documents()
    {
        var p = ProfileAggregator.Aggregate(
        [
            Record("a", licenses: ["CA Driver's License"]),
            Record("b", licenses: ["CA Drivers License!!"]),
            Record("c", licenses: ["Forklift certificate"]),
            Record("d"),
        ], "s");

        var licences = p.Qualifications.Where(q => q.Kind == ProfileQualKind.License).ToList();

        // The first two differ only by punctuation, so they merge under one key and the first
        // spelling seen is the one displayed.
        licences.Should().HaveCount(2);
        var driver = licences.Single(l => l.Text == "CA Driver's License");
        driver.Freq.Should().Be(0.5);
        licences.Single(l => l.Text == "Forklift certificate").Freq.Should().Be(0.25);
    }

    [Fact]
    public void A_requirement_repeated_within_one_document_counts_once()
    {
        var p = ProfileAggregator.Aggregate(
        [
            Record("a", ksaMin: ["Attention to detail", "attention to detail", "ATTENTION TO DETAIL"]),
            Record("b"),
        ], "s");

        p.Qualifications.Where(q => q.Kind == ProfileQualKind.KsaMin)
            .Should().ContainSingle()
            .Which.Freq.Should().Be(0.5);
    }

    [Fact]
    public void Very_short_qualification_keys_are_ignored()
    {
        // Sub-three-character keys are split artifacts, not requirements.
        var p = ProfileAggregator.Aggregate([Record("a", ksaMin: ["ab", "!!", "Real requirement"])], "s");

        p.Qualifications.Where(q => q.Kind == ProfileQualKind.KsaMin)
            .Should().ContainSingle()
            .Which.Text.Should().Be("Real requirement");
    }

    [Fact]
    public void Education_arrives_as_a_single_field_and_an_empty_one_contributes_nothing()
    {
        var p = ProfileAggregator.Aggregate(
        [
            Record("a", education: "High school diploma"),
            Record("b", education: ""),
        ], "s");

        p.Qualifications.Where(q => q.Kind == ProfileQualKind.Education)
            .Should().ContainSingle()
            .Which.Freq.Should().Be(0.5);
    }

    [Fact]
    public void The_longest_job_summary_represents_the_class()
    {
        var p = ProfileAggregator.Aggregate(
        [
            Record("a", summary: "Short."),
            Record("b", summary: "A considerably longer and more descriptive job summary."),
            Record("c", summary: "Medium length one."),
        ], "s");

        p.RepresentativeSummary.Should().Be("A considerably longer and more descriptive job summary.");
    }

    [Fact]
    public void Identity_fields_come_from_consensus_with_the_first_record_as_fallback()
    {
        var p = ProfileAggregator.Aggregate(
            [Record("a", code: "008543"), Record("b", code: "008543")], "008543-farm-laborer");

        p.UcJobCode.Should().Be("008543");
        p.Title.Should().Be("Farm Laborer", "SHOUTING titles are titleized");
        p.CtJobFamily.Should().Be("Agriculture");
        p.CorpusSize.Should().Be(2);
        p.Slug.Should().Be("008543-farm-laborer");
    }

    [Fact]
    public void Source_files_are_recorded_in_order()
    {
        var p = ProfileAggregator.Aggregate([Record("one.HTML"), Record("two.HTML")], "s");

        p.SourceFiles.OrderBy(f => f.Ordinal).Select(f => f.SourceFile)
            .Should().Equal("one.HTML", "two.HTML");
    }

    [Fact]
    public void The_derived_layers_are_left_for_later_steps()
    {
        // Aggregation is deterministic and free; consolidation and synthesis cost a model call and
        // are separate steps. A profile straight out of here must not pretend to have an envelope.
        var p = ProfileAggregator.Aggregate([Record("a")], "s");

        p.Envelope.Should().BeNull();
        p.EnvelopeSource.Should().BeNull();
        p.Coverage.Should().BeNull();
        p.ConsolidatedFunctions.Should().BeEmpty();
        p.GeneratedNote.Should().Be(ProfileAggregator.GeneratedNoteText);
    }

    [Theory]
    [InlineData("FARM LABORER", "Farm Laborer")]
    [InlineData("acad prg mgt ofcr 4", "Acad Prg Mgt Ofcr 4")]
    [InlineData("BUSINESS/TECH SUPPORT", "Business/tech Support")]
    [InlineData("RSCH DATA ANL 3", "Rsch Data Anl 3")]
    public void Titleize_matches_the_reference_word_pattern(string input, string expected)
    {
        // The reference replaces /\w\S*/g, so a token like "BUSINESS/TECH" is ONE match and only
        // its first letter is capitalized. A naive per-word titleizer would render "Business/Tech".
        ProfileAggregator.Titleize(input).Should().Be(expected);
    }

    [Fact]
    public void An_empty_record_set_is_rejected()
    {
        var act = () => ProfileAggregator.Aggregate([], "s");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

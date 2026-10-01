using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Profiles;
using Server.Core.Standards;

namespace Server.Tests.Profiles;

/// <summary>
/// The division of labour in envelope synthesis is the thing worth protecting: function NAMES and
/// DUTIES come from the model; the % time comes from the deterministic consolidated statistics. So
/// the fake returns percentages a real model might plausibly offer, and the tests prove they are
/// ignored.
/// </summary>
public class EnvelopeSynthesizerTests
{
    private sealed class FakeStandards : IStandardLookup
    {
        public ClassStandardRecord? Standard { get; set; }
        public List<string> Asked { get; } = [];

        public Task<ClassStandardRecord?> ForTitleAsync(string title, CancellationToken ct = default)
        {
            Asked.Add(title);
            return Task.FromResult(Standard);
        }
    }

    private static ClassProfile Profile(bool consolidated = true)
    {
        var p = new ClassProfile
        {
            Slug = "008543-farm-laborer",
            UcJobCode = "008543",
            Title = "Farm Laborer",
            CtJobFamily = "Agriculture",
            CtJobFunction = "Field Work",
            PersonnelProgram = "PSS",
            CorpusSize = 44,
            RepresentativeSummary = "Performs a variety of farm and field tasks.",
        };

        void Dist(DistributionField f, string? consensus, params (string? Value, int Count)[] vals)
        {
            var d = new ProfileDistribution { Field = f, Consensus = consensus, Agreement = 1 };
            var i = 0;
            foreach (var (v, c) in vals)
            {
                d.Values.Add(new ProfileDistributionValue { Ordinal = i++, Value = v, Count = c });
            }

            p.Distributions.Add(d);
        }

        Dist(DistributionField.SalaryGrade, "STEPS", ("STEPS", 44));
        Dist(DistributionField.FlsaStatus, "Non-Exempt", ("Non-Exempt", 44));
        Dist(DistributionField.UnionCode, "SX-Service Workers", ("SX-Service Workers", 44));
        Dist(DistributionField.Supervises, null, ("false", 40), ((string?)null, 4));
        Dist(DistributionField.Leads, "false", ("false", 44));
        Dist(DistributionField.WorksOutdoorsOver50pct, "true", ("true", 44));

        p.Qualifications.Add(new ProfileQualItem
        { Kind = ProfileQualKind.Education, Ordinal = 0, Text = "High school diploma", Freq = 0.25 });
        p.Qualifications.Add(new ProfileQualItem
        { Kind = ProfileQualKind.MinExperience, Ordinal = 0, Text = "Six months of farm work", Freq = 0.5 });
        p.Qualifications.Add(new ProfileQualItem
        { Kind = ProfileQualKind.WorkEnvironment, Ordinal = 0, Text = "Outdoor work in all weather", Freq = 0.75 });
        p.Qualifications.Add(new ProfileQualItem
        { Kind = ProfileQualKind.License, Ordinal = 0, Text = "CA Driver's License", Freq = 0.3 });
        p.Qualifications.Add(new ProfileQualItem
        { Kind = ProfileQualKind.KsaMin, Ordinal = 0, Text = "Ability to follow directions", Freq = 0.9 });
        p.Qualifications.Add(new ProfileQualItem
        { Kind = ProfileQualKind.KsaPref, Ordinal = 0, Text = "Tractor experience", Freq = 0.2 });

        if (consolidated)
        {
            var a = new ConsolidatedFunction
            { Ordinal = 0, Name = "Field Operations", TemplatePct = 70, Prevalence = 1.0 };
            a.SampleDuties.Add(new ConsolidatedFunctionSampleDuty { Ordinal = 0, Text = "Irrigate crops" });
            a.SampleDuties.Add(new ConsolidatedFunctionSampleDuty { Ordinal = 1, Text = "Harvest produce" });
            a.Members.Add(new ConsolidatedFunctionMember { Ordinal = 0, Text = "Cultural Operations" });

            var b = new ConsolidatedFunction
            { Ordinal = 1, Name = "Equipment Use", TemplatePct = 30, Prevalence = 0.5 };
            b.SampleDuties.Add(new ConsolidatedFunctionSampleDuty { Ordinal = 0, Text = "Operate a tractor" });

            p.ConsolidatedFunctions.Add(a);
            p.ConsolidatedFunctions.Add(b);

            p.ConsolidatedQuals.Add(new ConsolidatedQual
            { Kind = ConsolidatedQualKind.Certification, Ordinal = 0, Name = "Driver's licence", Freq = 0.3 });
            p.ConsolidatedQuals.Add(new ConsolidatedQual
            { Kind = ConsolidatedQualKind.MinQualification, Ordinal = 0, Name = "Follows directions", Freq = 0.9 });
        }

        return p;
    }

    private static object Envelope(object[] responsibilities, string[]? minQuals = null) => new
    {
        summary = "Standard farm labourer role.",
        scopeStatement = "Entry-level field work under supervision.",
        responsibilities,
        requiredCertifications = new[] { "Driver's licence where required" },
        education = new[] { "No formal education required; equivalent experience accepted." },
        workExperience = new[] { "Some agricultural experience preferred." },
        minQualifications = minQuals ?? ["Follows directions"],
        prefQualifications = new[] { "Equipment operation experience" },
        workEnvironment = new[] { "Outdoor work in varying conditions" },
        physicalRequirements = new[] { "Able to perform essential functions with or without accommodation" },
        outOfEnvelope = new[] { "Supervising other employees" },
    };

    private static object Resp(string name, params string[] duties) => new { functionName = name, duties };

    // ---------------------------------------------------------------- the key invariant

    [Fact]
    public async Task Percent_time_comes_from_the_consolidated_statistics_not_the_model()
    {
        // The model is never asked what share of time a function takes, and anything it volunteers
        // is ignored. The envelope's arithmetic is ours.
        var llm = new FakeStructuredLlm().Returns(
            Envelope([Resp("Field Operations", "Irrigate"), Resp("Equipment Use", "Drive")]));

        var envelope = await new EnvelopeSynthesizer(llm, new FakeStandards()).SynthesizeAsync(Profile());

        envelope.KeyResponsibilities.OrderBy(r => r.Ordinal).Select(r => r.PctTime)
            .Should().Equal(new[] { 70, 30 }, "they come from ConsolidatedFunction.TemplatePct");
    }

    [Fact]
    public async Task Percent_time_always_sums_to_one_hundred()
    {
        // Consolidated percentages that do not sum to 100 are rescaled, so the envelope always reads
        // like a real job description.
        var p = Profile();
        p.ConsolidatedFunctions[0].TemplatePct = 50;
        p.ConsolidatedFunctions[1].TemplatePct = 20;

        var llm = new FakeStructuredLlm().Returns(
            Envelope([Resp("A", "x"), Resp("B", "y")]));

        var envelope = await new EnvelopeSynthesizer(llm, new FakeStandards()).SynthesizeAsync(p);

        envelope.KeyResponsibilities.Sum(r => r.PctTime).Should().Be(100);
    }

    [Fact]
    public async Task More_responsibilities_than_categories_fall_back_to_an_even_split()
    {
        // The prompt asks for one responsibility per category, but a model can overshoot. An even
        // split is a defensible guess; leaving it at zero would silently erase the function.
        var llm = new FakeStructuredLlm().Returns(
            Envelope([Resp("A", "x"), Resp("B", "y"), Resp("C", "z")]));

        var envelope = await new EnvelopeSynthesizer(llm, new FakeStandards()).SynthesizeAsync(Profile());

        envelope.KeyResponsibilities.Should().HaveCount(3);
        envelope.KeyResponsibilities.Sum(r => r.PctTime).Should().Be(100);
        envelope.KeyResponsibilities.Should().OnlyContain(r => r.PctTime > 0);
    }

    [Fact]
    public async Task Minimum_qualifications_are_capped_at_seven_even_if_the_model_returns_more()
    {
        var tooMany = Enumerable.Range(1, 12).Select(i => $"Requirement {i}").ToArray();
        var llm = new FakeStructuredLlm().Returns(Envelope([Resp("A", "x")], tooMany));

        var envelope = await new EnvelopeSynthesizer(llm, new FakeStandards()).SynthesizeAsync(Profile());

        envelope.Items.Count(i => i.Kind == EnvelopeListKind.MinQualification).Should().Be(7);
    }

    [Fact]
    public async Task Fixed_policy_conditions_are_attached_in_code()
    {
        // These are identical across every UC job description. Asking a model to reproduce fixed
        // policy text invites paraphrase.
        var llm = new FakeStructuredLlm().Returns(Envelope([Resp("A", "x")]));

        var envelope = await new EnvelopeSynthesizer(llm, new FakeStandards()).SynthesizeAsync(Profile());

        envelope.Items.Where(i => i.Kind == EnvelopeListKind.ConditionOfEmployment)
            .OrderBy(i => i.Ordinal).Select(i => i.Text)
            .Should().Equal(EnvelopeSynthesizer.StandardConditions);
    }

    // ---------------------------------------------------------------- prompt assembly

    [Fact]
    public async Task The_corpus_prompt_is_assembled_exactly()
    {
        var llm = new FakeStructuredLlm().Returns(Envelope([Resp("A", "x"), Resp("B", "y")]));

        await new EnvelopeSynthesizer(llm, new FakeStandards()).SynthesizeAsync(Profile());

        llm.Requests.Should().ContainSingle();
        llm.Requests[0].User.Should().Be(
            """
            CLASS: Farm Laborer (UC job code 008543)
            Career Tracks: Agriculture / Field Work · Program: PSS
            Corpus size: 44 job descriptions.

            CONSENSUS ATTRIBUTES:
            - Salary grade: STEPS · FLSA: Non-Exempt · Unit: SX-Service Workers
            - Supervises employees: 0 yes / 40 no / 4 unspecified · Leads: 0 yes / 44 no / 0 unspecified · Outdoors >50%: 44 yes / 0 no / 0 unspecified

            REPRESENTATIVE JOB SUMMARY (strip its specifics when generalizing):
            Performs a variety of farm and field tasks.

            FUNCTION CATEGORIES — write ONE responsibility per line item, in this order:
            1. Field Operations — 70% of time (in 100% of JDs). Example duties to generalize: Irrigate crops | Harvest produce
            2. Equipment Use — 30% of time (in 50% of JDs). Example duties to generalize: Operate a tractor

            CONSOLIDATED CERTIFICATIONS:
            - Driver's licence

            CONSOLIDATED MINIMUM QUALIFICATIONS (up to 7):
            - Follows directions

            EDUCATION (frequency across corpus):
            - (25%) High school diploma

            MINIMUM WORK EXPERIENCE (frequency):
            - (50%) Six months of farm work

            WORK ENVIRONMENT (frequency):
            - (75%) Outdoor work in all weather

            Produce the standardized, generic Job Envelope, generalizing every section.
            """);
    }

    [Fact]
    public async Task An_official_standard_is_injected_as_the_authoritative_baseline()
    {
        var standards = new FakeStandards
        {
            Standard = new ClassStandardRecord
            {
                LongTitle = "Farm Laborer",
                GenericScope = "Performs routine field tasks.",
                CustomScope = "Within an agricultural unit.",
                Ksa = ["Knowledge of safe practices"],
                Education = ["High school or equivalent"],
                Licenses = ["Valid driver's licence"],
                Grade = "STEPS",
                Flsa = "Non-Exempt",
                PersProg = "PSS",
                Union = "SX",
            },
        };

        var llm = new FakeStructuredLlm().Returns(Envelope([Resp("A", "x")]));
        await new EnvelopeSynthesizer(llm, standards).SynthesizeAsync(Profile());

        standards.Asked.Should().ContainSingle("the standard is looked up once, by class title")
            .Which.Should().Be("Farm Laborer");

        var user = llm.Requests[0].User;
        user.Should().Contain("OFFICIAL UC JOB STANDARD (authoritative baseline for quals & scope):");
        user.Should().Contain("Generic scope: Performs routine field tasks.");
        user.Should().Contain("- Knowledge of safe practices");
        user.Should().Contain("Grade: STEPS · FLSA: Non-Exempt · Pers Prog: PSS · Union: SX");
    }

    [Fact]
    public void No_standard_yields_no_standard_block()
    {
        EnvelopeSynthesizer.StandardBlock(null).Should().BeEmpty();
    }

    [Fact]
    public async Task Without_consolidation_the_prompt_falls_back_to_raw_functions()
    {
        // A profile that has been aggregated but not consolidated can still be described; the
        // fallback keeps functions that are either common or large.
        var p = Profile(consolidated: false);
        var common = new ProfileFunction { Ordinal = 0, Name = "Common Work", PctMean = 10, Prevalence = 0.5 };
        common.SampleDuties.Add(new ProfileFunctionSampleDuty { Ordinal = 0, Text = "Does the common thing" });
        var big = new ProfileFunction { Ordinal = 1, Name = "Big Work", PctMean = 40, Prevalence = 0.05 };
        var negligible = new ProfileFunction { Ordinal = 2, Name = "Negligible", PctMean = 2, Prevalence = 0.01 };
        p.Functions.AddRange([common, big, negligible]);

        var llm = new FakeStructuredLlm().Returns(Envelope([Resp("A", "x")]));
        await new EnvelopeSynthesizer(llm, new FakeStandards()).SynthesizeAsync(p);

        var user = llm.Requests[0].User;
        user.Should().Contain("1. Common Work — ~10% time.");
        user.Should().Contain("2. Big Work — ~40% time.");
        user.Should().NotContain("Negligible", "neither common enough nor large enough to matter");
    }

    // ---------------------------------------------------------------- standard-only path

    [Fact]
    public async Task A_standard_only_envelope_takes_its_percentages_from_the_model()
    {
        // There is no corpus here, so the model's estimate is all there is — but it is still
        // normalized to 100, and the prompt states plainly that these are estimates.
        var llm = new FakeStructuredLlm().ReturnsJson("""
            {
              "summary": "Analyst role.",
              "scopeStatement": "Professional individual contributor.",
              "responsibilities": [
                {"functionName": "Analysis", "pctTime": 60, "duties": ["Analyses data"]},
                {"functionName": "Reporting", "pctTime": 30, "duties": ["Writes reports"]}
              ],
              "requiredCertifications": [],
              "education": ["Bachelor's degree"],
              "workExperience": [],
              "minQualifications": ["Analytical skill"],
              "prefQualifications": [],
              "workEnvironment": [],
              "physicalRequirements": ["Able to perform essential functions"],
              "outOfEnvelope": ["Supervising staff"]
            }
            """);

        var standard = new ClassStandardRecord
        {
            LongTitle = "Analyst 3",
            GenericScope = "Applies advanced knowledge.",
            KeyResponsibilities = ["Analyses data", "Reports findings"],
            Ksa = ["Analytical skill"],
            Grade = "Grade 21",
        };

        var envelope = await new EnvelopeSynthesizer(llm, new FakeStandards())
            .SynthesizeFromStandardAsync(standard,
                new EnvelopeSynthesizer.StandardMeta("Analyst 3", "007399", "Research", "Analysis", "PSS"));

        // 60 and 30 sum to 90, so they are scaled up.
        envelope.KeyResponsibilities.Sum(r => r.PctTime).Should().Be(100);
        envelope.KeyResponsibilities.Should().HaveCount(2);

        llm.Requests[0].System.Should().Be(EnvelopeSynthesizer.StandardOnlySystem);
        llm.Requests[0].User.Should().Contain("CLASS: Analyst 3 (UC job code 007399)");
        llm.Requests[0].User.Should().Contain("OFFICIAL STANDARD — KEY RESPONSIBILITIES (group these into 2-4 functions):");
    }

    // ---------------------------------------------------------------- deterministic fallbacks

    [Fact]
    public void The_deterministic_corpus_envelope_needs_no_model()
    {
        var envelope = EnvelopeSynthesizer.Deterministic(Profile());

        envelope.KeyResponsibilities.Sum(r => r.PctTime).Should().Be(100);
        envelope.Summary.Should().Contain("learned from 44 job descriptions");
        envelope.Items.Count(i => i.Kind == EnvelopeListKind.ConditionOfEmployment).Should().Be(3);
        envelope.Items.Should().Contain(i =>
            i.Kind == EnvelopeListKind.OutOfEnvelope && i.Text == "Supervising or leading other employees");
    }

    [Fact]
    public void The_deterministic_standard_envelope_maps_the_standard_straight_through()
    {
        var envelope = EnvelopeSynthesizer.DeterministicFromStandard(new ClassStandardRecord
        {
            LongTitle = "Analyst 3",
            GenericScope = "Applies advanced knowledge.",
            KeyResponsibilities = ["Analyses data"],
            Ksa = ["Analytical skill"],
            Licenses = ["None"],
            Education = ["Bachelor's degree"],
            Grade = "Grade 21",
            Flsa = "Exempt",
            Union = "99",
        });

        envelope.Summary.Should().Be("Applies advanced knowledge.");
        envelope.KeyResponsibilities.Sum(r => r.PctTime).Should().Be(100);
        envelope.Items.Should().Contain(i =>
            i.Kind == EnvelopeListKind.MinQualification && i.Text == "Analytical skill");
    }

    [Fact]
    public void A_standard_with_no_responsibilities_still_produces_a_usable_envelope()
    {
        var envelope = EnvelopeSynthesizer.DeterministicFromStandard(new ClassStandardRecord
        {
            LongTitle = "Sparse Class",
            CustomScope = "Does things.",
        });

        envelope.KeyResponsibilities.Should().ContainSingle();
        envelope.KeyResponsibilities[0].PctTime.Should().Be(100);
    }
}

using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Server.Core.Domain;
using Server.Core.Intake;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Tests.Intake;

/// <summary>
/// Behaviour of the two matchers against a fake model: the branches that protect a search from
/// silently returning nothing, and the anti-anchoring guarantee.
///
/// Every one of these was a real defect or a deliberate design decision in the POC, so they are
/// asserted rather than assumed.
/// </summary>
public class IntakeBehaviourTests
{
    private static ClassProfile P(string slug, string title, string code, params string[] functions)
    {
        var p = new ClassProfile
        {
            Slug = slug,
            Title = title,
            UcJobCode = code,
            CtJobFunction = "Research Data Analysis",
            PersonnelProgram = "PSS",
            CorpusSize = 5,
            RepresentativeSummary = "Does research things across a range of studies and protocols.",
            Envelope = new JobEnvelope { Summary = $"Summary for {title}.", ScopeStatement = "Scope." },
        };

        for (var i = 0; i < functions.Length; i++)
        {
            p.Envelope!.KeyResponsibilities.Add(new EnvelopeResponsibility
            {
                Ordinal = i,
                FunctionName = functions[i],
                PctTime = 100 / functions.Length,
            });
        }

        p.Distributions.Add(new ProfileDistribution
        {
            Field = DistributionField.SalaryGrade, Consensus = "Grade 21", Agreement = 1,
        });
        p.Distributions.Add(new ProfileDistribution
        {
            Field = DistributionField.Supervises, Consensus = "false", Agreement = 1,
        });

        return p;
    }

    private static List<ClassProfile> Catalog(int n) =>
        Enumerable.Range(0, n).Select(i => P($"slug-{i}", $"Class {i}", $"00{i:D4}", "WORK")).ToList();

    private sealed class EmptyStandards : IStandardsStore
    {
        public Task<StandardsIndex> GetIndexAsync(CancellationToken ct = default) =>
            Task.FromResult(new StandardsIndex([]));

        public void Invalidate()
        {
        }
    }

    private sealed class StubStandards : IStandardsStore
    {
        private readonly StandardsIndex _index;

        public StubStandards(params ClassStandardRecord[] rows) => _index = new StandardsIndex(rows);

        public Task<StandardsIndex> GetIndexAsync(CancellationToken ct = default) => Task.FromResult(_index);

        public void Invalidate()
        {
        }
    }

    private static IntakeMatcher Matcher(FakeStructuredLlm llm) => new(llm, new EmptyStandards());

    // ---- shortlist

    [Fact]
    public async Task A_small_catalog_skips_the_shortlist_stage_entirely()
    {
        // Below the threshold the shortlist call is pure overhead — it would cost a round trip to
        // narrow a list that already fits.
        var llm = new FakeStructuredLlm();
        var profiles = Catalog(IntakeMatcher.SkipShortlistBelow - 1);

        var result = await Matcher(llm).ShortlistAsync("anything", profiles);

        result.Should().BeEquivalentTo(profiles);
        llm.WasCalled("intake.shortlist").Should().BeFalse("no call should be made at all");
    }

    [Fact]
    public async Task A_catalog_at_the_threshold_does_call_the_shortlist()
    {
        var llm = new FakeStructuredLlm();
        var profiles = Catalog(IntakeMatcher.SkipShortlistBelow);
        llm.Reply("intake.shortlist", new { candidates = new[] { 0, 2 } });

        var result = await Matcher(llm).ShortlistAsync("anything", profiles);

        llm.WasCalled("intake.shortlist").Should().BeTrue();
        result.Select(p => p.Slug).Should().Equal("slug-0", "slug-2");
    }

    [Fact]
    public async Task An_empty_shortlist_falls_back_to_the_whole_catalog()
    {
        // THE important failure mode. A shortlist that comes back empty must not silently zero out
        // the search — the user typed a real request and deserves the full comparison.
        var llm = new FakeStructuredLlm();
        var profiles = Catalog(20);
        llm.Reply("intake.shortlist", new { candidates = Array.Empty<int>() });

        var result = await Matcher(llm).ShortlistAsync("anything", profiles);

        result.Should().HaveCount(20, "an unusable shortlist falls back to everything");
    }

    [Fact]
    public async Task A_shortlist_of_only_invalid_indices_also_falls_back()
    {
        var llm = new FakeStructuredLlm();
        var profiles = Catalog(20);
        llm.Reply("intake.shortlist", new { candidates = new[] { 99, -3, 500 } });

        var result = await Matcher(llm).ShortlistAsync("anything", profiles);

        result.Should().HaveCount(20);
    }

    [Fact]
    public async Task The_shortlist_is_deduplicated_and_capped()
    {
        var llm = new FakeStructuredLlm();
        var profiles = Catalog(40);
        // Duplicates plus more than the cap allows.
        var candidates = Enumerable.Range(0, 30).Concat(Enumerable.Range(0, 30)).ToArray();
        llm.Reply("intake.shortlist", new { candidates });

        var result = await Matcher(llm).ShortlistAsync("anything", profiles);

        result.Should().HaveCount(IntakeMatcher.ShortlistTarget);
        result.Select(p => p.Slug).Should().OnlyHaveUniqueItems();
    }

    // ---- rank

    [Fact]
    public async Task Matches_are_addressed_by_index_and_out_of_range_ones_are_dropped()
    {
        // Indices, never reproduced strings: the single-call version asked the model to echo slugs
        // and silently dropped any that did not resolve, turning a fluffed string into a missing
        // result with no signal.
        var llm = new FakeStructuredLlm();
        var profiles = Catalog(5);
        llm.Reply("intake.rank", new
        {
            matches = new object[]
            {
                new { index = 1, confidence = 90.0, rationale = "fits" },
                new { index = 99, confidence = 95.0, rationale = "not a real class" },
                new { index = -1, confidence = 99.0, rationale = "also not" },
            },
        });

        var result = await Matcher(llm).MatchAsync("anything", profiles);

        result.Should().ContainSingle();
        result[0].Slug.Should().Be("slug-1");
        result[0].Confidence.Should().Be(90);
    }

    [Fact]
    public async Task Results_are_sorted_by_confidence_and_capped()
    {
        var llm = new FakeStructuredLlm();
        var profiles = Catalog(10);
        llm.Reply("intake.rank", new
        {
            matches = new object[]
            {
                new { index = 0, confidence = 50.0, rationale = "a" },
                new { index = 1, confidence = 90.0, rationale = "b" },
                new { index = 2, confidence = 70.0, rationale = "c" },
                new { index = 3, confidence = 95.0, rationale = "d" },
                new { index = 4, confidence = 60.0, rationale = "e" },
            },
        });

        var result = await Matcher(llm).MatchAsync("anything", profiles);

        result.Should().HaveCount(IntakeMatcher.MaxResults);
        result.Select(r => r.Confidence).Should().BeInDescendingOrder();
        result[0].Confidence.Should().Be(95);
    }

    [Fact]
    public async Task Confidence_rounds_half_up_like_the_reference()
    {
        // JS Math.round is half-up; .NET Math.Round is banker's rounding, which would turn 86.5
        // into 86 instead of 87.
        var llm = new FakeStructuredLlm();
        var profiles = Catalog(5);
        llm.Reply("intake.rank", new
        {
            matches = new object[]
            {
                new { index = 0, confidence = 86.5, rationale = "a" },
                new { index = 1, confidence = 85.5, rationale = "b" },
            },
        });

        var result = await Matcher(llm).MatchAsync("anything", profiles);

        result.Select(r => r.Confidence).Should().BeEquivalentTo(new[] { 87, 86 });
    }

    [Fact]
    public async Task An_empty_catalog_returns_nothing_without_calling_the_model()
    {
        var llm = new FakeStructuredLlm();

        var result = await Matcher(llm).MatchAsync("anything", []);

        result.Should().BeEmpty();
        llm.Requests.Should().BeEmpty("there is nothing to rank");
    }

    [Fact]
    public async Task Both_stages_use_the_documented_labels()
    {
        // Labels carry over verbatim so measured token costs stay comparable across the port.
        var llm = new FakeStructuredLlm();
        var profiles = Catalog(20);
        llm.Reply("intake.shortlist", new { candidates = new[] { 0, 1 } });
        llm.Reply("intake.rank", new { matches = new object[] { new { index = 0, confidence = 80.0, rationale = "x" } } });

        await Matcher(llm).MatchAsync("anything", profiles);

        llm.Requests.Select(r => r.Label).Should().Equal("intake.shortlist", "intake.rank");
    }

    // ---- classifier: anti-anchoring

    private static DescriptionClassifier Classifier(FakeStructuredLlm llm, ITitleCodeService titleCodes) =>
        new(llm, Matcher(llm), new EmptyStandards(), titleCodes);

    private sealed class StubTitleCodes : ITitleCodeService
    {
        private readonly TitleCodeIndex _index;

        public StubTitleCodes(params TitleCode[] rows) => _index = new TitleCodeIndex(rows);

        public Task<TitleCodeIndex> GetAsync(CancellationToken ct = default) => Task.FromResult(_index);

        public void Invalidate()
        {
        }
    }

    private static TitleCode Tc(string code, string title, string source = "both") => new()
    {
        Code = code,
        Title = title,
        Source = source,
        Grade = "Grade 21",
        Family = "Research",
        Function = "Research Data Analysis",
        TitleKey = TitleNormalizer.TitleKey(title),
        TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
    };

    [Fact]
    public async Task The_proposed_job_code_never_appears_in_any_prompt_before_the_ranking_is_settled()
    {
        // THE anti-anchoring guarantee, asserted rather than trusted. A model shown the answer the
        // unit wants tends to ratify it, which makes the confidence number meaningless. This is not
        // about secrecy or cost: envelopes and standards go to the model in full either way.
        //
        // The proposed class is backed by a STANDARD rather than a profile, so its code is not in
        // the candidate catalog and any occurrence in the first two prompts is a real leak. (A
        // candidate class legitimately carrying that code would be fine — it is one option among
        // several, with nothing marking it as the proposal.)
        var llm = new FakeStructuredLlm();
        var profiles = new List<ClassProfile> { P("a", "Rsch Data Anl 2", "006256", "ANALYSIS") };

        const string proposed = "007399";
        var standard = new ClassStandardRecord
        {
            LongTitle = "Project Policy Anl 4",
            Code = proposed,
            GenericScope = "Sets policy direction across the organisation.",
            CustomScope = "Institution-wide.",
            KeyResponsibilities = ["Sets policy.", "Advises leadership."],
            Ksa = ["Policy analysis."],
            Education = ["Advanced degree."],
        };

        llm.Reply("classify.distill", new
        {
            workingTitle = "Evaluation Analyst",
            summary = "Analyses data.",
            functions = new object[] { new { name = "ANALYSIS", pctTime = 100.0, duties = new[] { "Analyses." } } },
            supervises = "no",
            education = Array.Empty<string>(),
            experience = Array.Empty<string>(),
            ksas = Array.Empty<string>(),
        });
        llm.Reply("classify.rank", new
        {
            matches = new object[]
            {
                new
                {
                    index = 0, confidence = 86.0, levelFit = "at", levelNote = "n",
                    rationale = "r", matchedFunctions = new[] { 0 }, unmatchedFunctions = Array.Empty<int>(),
                },
            },
        });
        llm.Reply("classify.proposed", new
        {
            fits = "no",
            summary = "Different level.",
            contradicts = new object[] { new { point = "p", evidence = "e" } },
            missing = Array.Empty<string>(),
            supports = Array.Empty<string>(),
        });

        var standards = new StubStandards(standard);
        var matcher = new IntakeMatcher(llm, standards);
        var classifier = new DescriptionClassifier(
            llm, matcher, standards, new StubTitleCodes(Tc(proposed, "Project Policy Anl 4")));

        var result = await classifier.ClassifyAsync("A description of analytical work.", profiles, proposed);

        result.Matches.Should().NotBeEmpty("the ranking must settle before the proposal is argued");

        // Neither the code nor the proposed class's identity may reach the first two prompts.
        foreach (var label in new[] { "classify.distill", "classify.rank" })
        {
            var req = llm.RequestFor(label);
            req.System.Should().NotContain(proposed, "{0} system prompt", label);
            req.User.Should().NotContain(proposed, "{0} user prompt", label);
            req.User.Should().NotContain("Project Policy Anl 4", "{0} must not name the proposed class", label);
        }

        // It is argued separately, and only after the ranking is settled.
        llm.WasCalled("classify.proposed").Should().BeTrue();
        llm.Requests.Select(r => r.Label).Should()
            .ContainInOrder("classify.rank", "classify.proposed");

        result.Proposed.Should().NotBeNull();
        result.Proposed!.Code.Should().Be(proposed);
        result.Proposed.Basis.Should().Be("standard");

        // The compare call is told our independent pick, explicitly so it can disagree with it.
        llm.RequestFor("classify.proposed").User.Should().Contain("Rsch Data Anl 2");
        llm.RequestFor("classify.proposed").User.Should().Contain("Do not defer to");
    }

    [Fact]
    public async Task The_proposed_assessment_is_skipped_when_no_class_was_named()
    {
        var llm = new FakeStructuredLlm();
        var profiles = new List<ClassProfile> { P("a", "Rsch Data Anl 2", "006256", "ANALYSIS") };

        llm.Reply("classify.distill", new
        {
            workingTitle = "X", summary = "S",
            functions = new object[] { new { name = "F", pctTime = 100.0, duties = new[] { "d" } } },
            supervises = "no", education = Array.Empty<string>(),
            experience = Array.Empty<string>(), ksas = Array.Empty<string>(),
        });
        llm.Reply("classify.rank", new
        {
            matches = new object[]
            {
                new
                {
                    index = 0, confidence = 90.0, levelFit = "at", levelNote = "n", rationale = "r",
                    matchedFunctions = new[] { 0 }, unmatchedFunctions = Array.Empty<int>(),
                },
            },
        });

        var classifier = Classifier(llm, new StubTitleCodes());
        var result = await classifier.ClassifyAsync("text", profiles);

        result.Proposed.Should().BeNull();
        llm.WasCalled("classify.proposed").Should().BeFalse("no call is worth making");
    }

    [Fact]
    public async Task A_superseded_proposed_code_is_compared_against_its_successor_and_says_so()
    {
        // Otherwise the reasoning would appear to be about a class the unit never named.
        var llm = new FakeStructuredLlm();
        var titleCodes = new StubTitleCodes(
            Tc("007396", "PROJECT POLICY ANL 1"),
            Tc("005255", "PROJECT POLICY ANL 1 RP"));

        var distilled = new DistilledJd
        {
            WorkingTitle = "X",
            Summary = "S",
            Functions = [new DistilledFunction { Name = "F", PctTime = 100, Duties = ["d"] }],
        };

        // No profile and no standard for either code, so the basis is "none" and no call is made.
        var classifier = Classifier(llm, titleCodes);
        var assessment = await classifier.AssessProposedAsync(distilled, "007396", [], "Something Else");

        assessment.Should().NotBeNull();
        assessment!.ComparedAs.Should().Be("PROJECT POLICY ANL 1 RP",
            "the retired code has no class of its own any more");
        assessment.Basis.Should().Be("none");
        llm.WasCalled("classify.proposed").Should().BeFalse(
            "a bare title is not enough to reason from — saying so beats inventing an analysis");
    }

    [Fact]
    public async Task An_unknown_proposed_code_yields_no_assessment_at_all()
    {
        var llm = new FakeStructuredLlm();
        var distilled = new DistilledJd { Summary = "S" };

        var classifier = Classifier(llm, new StubTitleCodes());
        var assessment = await classifier.AssessProposedAsync(distilled, "999999", [], "X");

        assessment.Should().BeNull("there is no such class to assess");
    }

    [Fact]
    public async Task Distillation_of_an_HRTMS_export_is_free_and_skips_the_model()
    {
        // An HRTMS export already carries every field a model would be asked to extract, so parsing
        // it is both free and more accurate than asking a model to re-read a table.
        var llm = new FakeStructuredLlm();
        var html = """
                   <html><body><table>
                   <tr><td>UC Job Title:</td><td>RSCH DATA ANL 2</td></tr>
                   <tr><td>Working Title:</td><td>Evaluation Analyst</td></tr>
                   <tr><td>Does this position supervise employees?</td><td>No</td></tr>
                   <tr><td>Job Summary</td><td>Analyses study data for investigators.</td></tr>
                   <tr><td>% TIME</td><td>Function</td><td>Duties</td></tr>
                   <tr><td>85%</td><td>ANALYSIS</td><td>-@Cleans data -@Runs models</td></tr>
                   <tr><td>15%</td><td>REPORTING</td><td>-@Writes reports</td></tr>
                   </table></body></html>
                   """;

        var classifier = Classifier(llm, new StubTitleCodes());
        var distilled = await classifier.DistillAsync(html);

        distilled.Source.Should().Be("hrtms");
        llm.Requests.Should().BeEmpty("the parser did the work");
        distilled.WorkingTitle.Should().Be("Evaluation Analyst");
        distilled.Functions.Should().HaveCount(2);
        distilled.Functions[0].PctTime.Should().Be(85);
        distilled.Supervises.Should().Be("no");
    }

    [Fact]
    public async Task Non_HRTMS_html_falls_through_to_the_model_rather_than_failing()
    {
        // Some other HTML, or a changed template, must not fail the whole classification.
        var llm = new FakeStructuredLlm();
        llm.Reply("classify.distill", new
        {
            workingTitle = "From model", summary = "S",
            functions = Array.Empty<object>(), supervises = "unclear",
            education = Array.Empty<string>(), experience = Array.Empty<string>(), ksas = Array.Empty<string>(),
        });

        var classifier = Classifier(llm, new StubTitleCodes());
        var distilled = await classifier.DistillAsync("<div>Just a marketing page about our department</div>");

        // The reference gets this WRONG: HrtmsParser never throws on arbitrary HTML, so its catch
        // never fires and the document distills to an empty JD tagged "hrtms". The port checks for
        // a usable parse instead, which is what the reference's own comment describes.
        distilled.Source.Should().Be("text");
        distilled.WorkingTitle.Should().Be("From model");
        llm.WasCalled("classify.distill").Should().BeTrue();
    }

    [Fact]
    public async Task No_ingested_classes_reports_that_rather_than_a_weak_match()
    {
        var llm = new FakeStructuredLlm();
        llm.Reply("classify.distill", new
        {
            workingTitle = "X", summary = "S", functions = Array.Empty<object>(),
            supervises = "no", education = Array.Empty<string>(),
            experience = Array.Empty<string>(), ksas = Array.Empty<string>(),
        });

        var classifier = Classifier(llm, new StubTitleCodes());
        var result = await classifier.ClassifyAsync("text", []);

        result.Verdict.Should().Be("weak");
        result.VerdictNote.Should().Be("No job classes are ingested yet.");
        llm.WasCalled("classify.rank").Should().BeFalse();
    }

    [Fact]
    public async Task The_terse_form_goes_to_the_shortlist_and_the_full_form_to_the_ranker()
    {
        // Stage 1 compares against one-line catalog entries; sending it every duty bullet and KSA
        // pays for context that cannot change a plausibility call.
        var llm = new FakeStructuredLlm();
        var profiles = Catalog(20);

        llm.Reply("classify.distill", new
        {
            workingTitle = "Evaluation Analyst",
            summary = "Analyses data.",
            functions = new object[]
            {
                new { name = "ANALYSIS", pctTime = 85.0, duties = new[] { "A distinctive duty sentence." } },
            },
            supervises = "no",
            education = new[] { "A distinctive education requirement." },
            experience = Array.Empty<string>(),
            ksas = Array.Empty<string>(),
        });
        llm.Reply("intake.shortlist", new { candidates = new[] { 0 } });
        llm.Reply("classify.rank", new
        {
            matches = new object[]
            {
                new
                {
                    index = 0, confidence = 90.0, levelFit = "at", levelNote = "n", rationale = "r",
                    matchedFunctions = new[] { 0 }, unmatchedFunctions = Array.Empty<int>(),
                },
            },
        });

        var classifier = Classifier(llm, new StubTitleCodes());
        await classifier.ClassifyAsync("text", profiles);

        var shortlist = llm.RequestFor("intake.shortlist");
        shortlist.User.Should().NotContain("A distinctive duty sentence.");
        shortlist.User.Should().NotContain("A distinctive education requirement.");

        var rank = llm.RequestFor("classify.rank");
        rank.User.Should().Contain("A distinctive duty sentence.");
        rank.User.Should().Contain("A distinctive education requirement.");
    }
}

using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Ingest;
using Server.Core.Profiles;

namespace Server.Tests.Profiles;

/// <summary>
/// This is the coverage check that actually guards against drift: the stored backwards-coverage on a
/// profile is computed from consolidated MEMBERS and so cannot notice that an envelope has been
/// reworded away from its corpus. This one can, which is why it runs before and after applying an
/// official standard.
///
/// The model decides only which raw function a given envelope function covers; the percentages are
/// computed here.
/// </summary>
public class EnvelopeCoverageCheckerTests
{
    private static HrtmsRecord Jd(string file, params (string Name, int Pct)[] fns) => new()
    {
        SourceFile = file,
        Responsibilities = [.. fns.Select(f => new HrtmsResponsibility { FunctionName = f.Name, Pct = f.Pct })],
    };

    private static JobEnvelope Envelope(params (string Name, string[] Duties)[] resps)
    {
        var e = new JobEnvelope { Summary = "s", ScopeStatement = "s" };
        var i = 0;
        foreach (var (name, duties) in resps)
        {
            var r = new EnvelopeResponsibility { Ordinal = i++, FunctionName = name, PctTime = 50 };
            var j = 0;
            foreach (var d in duties)
            {
                r.Duties.Add(new EnvelopeDuty { Ordinal = j++, Text = d });
            }

            e.KeyResponsibilities.Add(r);
        }

        return e;
    }

    private static object Mappings(params (string Id, int Index)[] ms) =>
        new { mappings = ms.Select(m => new { id = m.Id, envelopeIndex = m.Index }).ToArray() };

    [Fact]
    public async Task A_mapped_function_counts_as_covered()
    {
        var llm = new FakeStructuredLlm().Returns(Mappings(("R0", 0), ("R1", 0)));

        var report = await new EnvelopeCoverageChecker(llm).CheckAsync(
            Envelope(("Field Operations", ["Irrigate"])),
            [Jd("a", ("Cultural Operations", 60), ("Plant Collection", 40))]);

        report.PerJd[0].CoveredPct.Should().Be(100);
    }

    [Fact]
    public async Task Minus_one_means_nothing_covers_it()
    {
        // The model saying "none of these" is a valid, useful answer — not a failure to map.
        var llm = new FakeStructuredLlm().Returns(Mappings(("R0", 0), ("R1", -1)));

        var report = await new EnvelopeCoverageChecker(llm).CheckAsync(
            Envelope(("Field Operations", ["Irrigate"])),
            [Jd("a", ("Cultural Operations", 70), ("Grant Administration", 30))]);

        report.PerJd[0].CoveredPct.Should().Be(70);
        report.PerJd[0].Uncovered.Should().ContainSingle()
            .Which.Name.Should().Be("Grant Administration");
    }

    [Fact]
    public async Task Distinct_raw_functions_are_mapped_once_each_across_the_corpus()
    {
        // The catalogue sent to the model is DISTINCT functions, not one entry per JD — otherwise a
        // 1,367-record class would send the same name hundreds of times.
        var llm = new FakeStructuredLlm().Returns(Mappings(("R0", 0)));

        await new EnvelopeCoverageChecker(llm).CheckAsync(
            Envelope(("Work", ["Does"])),
            [
                Jd("a", ("Cultural Operations", 100)),
                Jd("b", ("CULTURAL OPERATIONS", 100)),
                Jd("c", ("  cultural   operations  ", 100)),
            ]);

        llm.Requests[0].User.Should().Contain("R0: Cultural Operations");
        llm.Requests[0].User.Should().NotContain("R1:", "case and spacing variants are one function");
    }

    [Fact]
    public async Task The_prompt_lists_envelope_functions_with_example_duties()
    {
        var llm = new FakeStructuredLlm().Returns(Mappings(("R0", 0)));

        await new EnvelopeCoverageChecker(llm).CheckAsync(
            Envelope(
                ("Field Operations", ["Irrigate", "Weed", "Harvest", "Ignored fourth"]),
                ("Equipment Use", ["Drive a tractor"])),
            [Jd("a", ("Cultural Operations", 100))]);

        var req = llm.Requests[0];
        req.System.Should().Be(EnvelopeCoverageChecker.System);
        req.Effort.Should().Be(Server.Core.Ai.LlmEffort.Low);
        req.User.Should().Be(
            """
            ENVELOPE KEY RESPONSIBILITY FUNCTIONS:
            0: Field Operations — e.g. Irrigate; Weed; Harvest
            1: Equipment Use — e.g. Drive a tractor

            RAW JD FUNCTIONS TO MAP:
            R0: Cultural Operations
            """);
    }

    [Fact]
    public async Task No_call_is_made_when_there_is_nothing_to_map()
    {
        // An envelope with no responsibilities, or a corpus with no functions, cannot produce a
        // mapping — and paying for a call to learn that would be waste.
        var llm = new FakeStructuredLlm();

        var report = await new EnvelopeCoverageChecker(llm).CheckAsync(
            Envelope(), [Jd("a", ("Anything", 100))]);

        llm.Requests.Should().BeEmpty();
        report.PerJd[0].CoveredPct.Should().Be(0);
    }

    [Fact]
    public async Task An_empty_corpus_makes_no_call_either()
    {
        var llm = new FakeStructuredLlm();

        var report = await new EnvelopeCoverageChecker(llm).CheckAsync(
            Envelope(("Work", ["Does"])), []);

        llm.Requests.Should().BeEmpty();
        report.N.Should().Be(0);
    }

    [Fact]
    public async Task A_mapping_for_an_unknown_id_is_ignored_rather_than_crashing()
    {
        var llm = new FakeStructuredLlm().Returns(Mappings(("R0", 0), ("R99", 0), ("garbage", 0)));

        var report = await new EnvelopeCoverageChecker(llm).CheckAsync(
            Envelope(("Work", ["Does"])),
            [Jd("a", ("Known Work", 100))]);

        report.PerJd[0].CoveredPct.Should().Be(100);
    }

    [Fact]
    public async Task Rewording_the_envelope_can_change_this_result()
    {
        // The whole point of this check. The same corpus, two different envelope wordings, two
        // different verdicts — because here the MODEL judges whether the wording still covers the
        // work, which the consolidated-member check cannot do.
        var records = new List<HrtmsRecord> { Jd("a", ("Cultural Operations", 100)) };

        var covering = new FakeStructuredLlm().Returns(Mappings(("R0", 0)));
        var notCovering = new FakeStructuredLlm().Returns(Mappings(("R0", -1)));

        var good = await new EnvelopeCoverageChecker(covering)
            .CheckAsync(Envelope(("Field Operations", ["Irrigate"])), records);
        var bad = await new EnvelopeCoverageChecker(notCovering)
            .CheckAsync(Envelope(("Grant Administration", ["Manage awards"])), records);

        good.PerJd[0].CoveredPct.Should().Be(100);
        bad.PerJd[0].CoveredPct.Should().Be(0);
    }
}

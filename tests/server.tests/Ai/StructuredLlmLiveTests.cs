using System.ComponentModel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core.Ai;

namespace Server.Tests.Ai;

/// <summary>
/// One real call against the Anthropic API, to prove the seam end to end: schema on the wire,
/// structured response, deserialization back into the type.
///
/// Tagged <c>Category=LiveApi</c> and EXCLUDED from the normal run, because it spends real money
/// against a rate-limited key and depends on the network. Run it deliberately:
///
///     dotnet test --filter "Category=LiveApi"
///
/// Kept small on purpose — a handful of tokens is enough to show the contract holds. The nine real
/// call sites are tested by asserting the prompt they assemble, not by calling the model.
/// </summary>
[Trait("Category", "LiveApi")]
public class StructuredLlmLiveTests
{
    private sealed class Classification
    {
        [Description("The single best-matching category number from the list.")]
        public int Index { get; set; }

        [Description("0-100 confidence in that choice.")]
        public int Confidence { get; set; }

        [Description("One short sentence of justification.")]
        public string Rationale { get; set; } = "";
    }

    [Fact]
    public async Task A_structured_call_round_trips_through_the_real_api()
    {
        // The test host does not load server/.env, so the key comes from the exported environment
        // variable. Configuration is built the same way the app builds it, minus the file.
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var llm = new StructuredLlm(NullLogger<StructuredLlm>.Instance, configuration);

        llm.HasApiKey.Should().BeTrue(
            "set ANTHROPIC_API_KEY (server/.env is loaded by the app, not by the test host)");

        var result = await llm.StructuredAsync<Classification>(new StructuredRequest
        {
            System = "You are a classification assistant. Choose exactly one category.",
            User = """
                   CATEGORIES:
                   0: Groundskeeping and agricultural field work
                   1: Research data analysis and statistical reporting
                   2: Building maintenance and HVAC repair

                   DESCRIPTION:
                   Designs survey instruments, cleans and analyses study datasets in R, and writes up
                   findings for principal investigators.

                   Pick the best-fitting category.
                   """,
            Effort = LlmEffort.Low,
            MaxTokens = 1000,
            Label = "smoke.classify",
        });

        // The point is the CONTRACT, not the answer: a well-formed object bound back into the type.
        result.Should().NotBeNull();
        result.Index.Should().Be(1, "the description is unambiguously data analysis");
        result.Confidence.Should().BeInRange(0, 100);
        result.Rationale.Should().NotBeNullOrWhiteSpace();
    }
}

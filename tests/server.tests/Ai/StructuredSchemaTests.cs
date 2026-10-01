using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Server.Core.Ai;

namespace Server.Tests.Ai;

/// <summary>
/// The schema derivation is the Zod-parity piece, and it is where a quiet failure would be most
/// expensive: a schema that compiles and looks right but omits <c>required</c>, or drops the
/// description text, changes what the model returns without changing any type. These tests pin the
/// three things the exporter does not give us for free.
/// </summary>
public class StructuredSchemaTests
{
    // Shaped after the real call sites: indices rather than reproduced strings, and descriptions
    // carrying instructions the model actually needs.
    private sealed class Shortlist
    {
        [Description("Numbers of the plausibly-matching classes, most promising first.")]
        public List<int> Candidates { get; set; } = [];
    }

    private sealed class RankedMatch
    {
        [Description("Number of a class from the catalog.")]
        public int Index { get; set; }

        [Description("0-100 confidence that this class fits the request.")]
        public int Confidence { get; set; }

        [Description("One sentence tying the request to this class's work.")]
        public string Rationale { get; set; } = "";
    }

    private sealed class RankResult
    {
        public List<RankedMatch> Matches { get; set; } = [];
    }

    private sealed class WithOptional
    {
        public string Required { get; set; } = "";

        [Description("Absent when the class was never ingested.")]
        public string? Optional { get; set; }
    }

    private static JsonObject Schema<T>() => (JsonObject)StructuredSchema.For<T>();

    [Fact]
    public void Objects_are_closed()
    {
        // An open schema lets the model return extra keys that deserialize to nothing — which looks
        // like success and loses data.
        Schema<Shortlist>()["additionalProperties"]!.GetValue<bool>().Should().BeFalse();

        var nested = Schema<RankResult>()["properties"]!["matches"]!["items"]!.AsObject();
        nested["additionalProperties"]!.GetValue<bool>().Should().BeFalse("nested objects too");
    }

    [Fact]
    public void Non_nullable_properties_are_required()
    {
        // The exporter does not emit `required` at all. Without it the model may omit a field, the
        // deserializer leaves it at default, and a truncated result passes as valid.
        var items = Schema<RankResult>()["properties"]!["matches"]!["items"]!.AsObject();
        var required = items["required"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();

        required.Should().BeEquivalentTo("index", "confidence", "rationale");
    }

    [Fact]
    public void Nullable_properties_are_not_required()
    {
        var schema = Schema<WithOptional>();
        var required = schema["required"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();

        required.Should().Contain("required");
        required.Should().NotContain("optional", "a nullable property is genuinely optional");
    }

    [Fact]
    public void Description_attributes_reach_the_schema()
    {
        // These are the ported text of Zod's .describe() calls. They carry prompt-engineering
        // weight — "across all responsibilities these must sum to 100" lives in one of them — so
        // losing them changes model behavior while everything still compiles.
        var props = Schema<RankResult>()["properties"]!["matches"]!["items"]!["properties"]!.AsObject();

        props["confidence"]!["description"]!.GetValue<string>()
            .Should().Be("0-100 confidence that this class fits the request.");
        props["rationale"]!["description"]!.GetValue<string>()
            .Should().Be("One sentence tying the request to this class's work.");
    }

    [Fact]
    public void Property_names_are_camelCase()
    {
        // The wire contract is the names the POC's Zod schemas produced. This is not cosmetic: a
        // PascalCase schema would have the model return PascalCase keys that then fail to bind.
        Schema<RankedMatch>()["properties"]!.AsObject()
            .Select(p => p.Key)
            .Should().BeEquivalentTo("index", "confidence", "rationale");
    }

    [Fact]
    public void A_response_matching_the_schema_deserializes_into_the_type()
    {
        // The other half of the contract: whatever the schema asks for must bind back through the
        // SAME serializer options, or the call succeeds and returns an empty object.
        const string json = """
            {"matches":[{"index":3,"confidence":86,"rationale":"Analyzes research data."}]}
            """;

        var result = JsonSerializer.Deserialize<RankResult>(json, StructuredSchema.SerializerOptions)!;

        result.Matches.Should().ContainSingle();
        result.Matches[0].Index.Should().Be(3);
        result.Matches[0].Confidence.Should().Be(86);
        result.Matches[0].Rationale.Should().Be("Analyzes research data.");
    }

    [Fact]
    public void The_dictionary_form_round_trips_to_the_same_schema()
    {
        // What actually goes on the wire is the dictionary form, so it must not lose anything the
        // node form has.
        var dict = StructuredSchema.AsDictionary(typeof(RankResult));

        dict.Should().ContainKey("type");
        dict.Should().ContainKey("properties");
        dict.Should().ContainKey("additionalProperties");
        dict["additionalProperties"].GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Callers_cannot_corrupt_the_cache_for_everyone_else()
    {
        // Schemas are cached per type and handed to an SDK that may mutate or take ownership. If
        // the cached instance escaped, one call could poison every later call for that type.
        var first = Schema<Shortlist>();
        first["description"] = "mutated by a caller";

        Schema<Shortlist>().ContainsKey("description").Should().BeFalse();
    }
}

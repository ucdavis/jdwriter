using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Tests.Standards;

/// <summary>
/// The standards lookup index. All the logic lives here with no EF dependency, so these run without
/// a database — the store itself only answers where rows come from.
/// </summary>
public class StandardsStoreTests
{
    private static ClassStandardRecord S(string longTitle, string? code = null, string grade = "") => new()
    {
        LongTitle = longTitle,
        Code = code,
        Grade = grade,
        GenericScope = $"Scope for {longTitle}.",
        KeyResponsibilities = [$"Does {longTitle} work."],
    };

    [Fact]
    public void A_title_resolves_on_its_strict_key()
    {
        var index = new StandardsIndex([S("Project Policy Anl 2 RP", "005256")]);

        index.ForTitle("Project Policy Anl 2 RP")!.Code.Should().Be("005256");
    }

    [Fact]
    public void Abbreviated_and_spelled_out_titles_both_resolve()
    {
        // The corpus says "Anl", the standards say "Analyst", and the reference expands both.
        var index = new StandardsIndex([S("Project and Policy Analyst 2 RP", "005256")]);

        index.ForTitle("Project Policy Anl 2 RP").Should().NotBeNull();
        index.ForTitle("Project and Policy Analyst 2 RP").Should().NotBeNull();
    }

    [Fact]
    public void The_strict_key_wins_over_a_loose_collision()
    {
        // THE reason the store deduplicates on the strict key. "Analyst 3 RP" and "Analyst 3 RP GF"
        // share a loose key but are different classifications with different codes; letting the
        // loose key win silently discards one of them.
        var index = new StandardsIndex([
            S("Widget Analyst 3 RP", "111111"),
            S("Widget Analyst 3 RP GF", "222222"),
        ]);

        index.ForTitle("Widget Analyst 3 RP")!.Code.Should().Be("111111");
        index.ForTitle("Widget Analyst 3 RP GF")!.Code.Should().Be("222222");
        index.All.Should().HaveCount(2, "neither may be discarded");
    }

    [Fact]
    public void A_loose_key_still_resolves_to_a_reasonable_neighbour()
    {
        // Fallback, not primary: a caller asking with a variant-free spelling gets something rather
        // than nothing.
        var index = new StandardsIndex([S("Widget Analyst 3 RP", "111111")]);

        index.ForTitle("Widget Analyst 3").Should().NotBeNull();
    }

    [Fact]
    public void An_unknown_title_resolves_to_null_rather_than_a_guess()
    {
        var index = new StandardsIndex([S("Widget Analyst 3 RP", "111111")]);

        index.ForTitle("Farm Laborer").Should().BeNull();
    }

    [Fact]
    public void All_is_distinct_unlike_the_reference()
    {
        // DELIBERATE DEVIATION, recorded. The reference returns the lookup map's VALUES, so a record
        // reachable under both a loose and a strict key appears twice — 228 entries for 214 real
        // standards in the live data. That inflates the count and would emit duplicate bootstrap
        // candidates. The map is an index; the list comes from the rows.
        var records = new[] { S("Curriculum Planner 2 SV", "111111"), S("Widget Analyst 1", "222222") };
        var index = new StandardsIndex(records);

        index.All.Should().HaveCount(2);
        index.Count.Should().Be(2);
        index.All.Select(s => s.LongTitle).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Lookup_by_code_tolerates_padding()
    {
        // Forms state codes unpadded; the reference stores them padded.
        var index = new StandardsIndex([S("Widget Analyst 4", "007399")]);

        index.ForCode("7399")!.LongTitle.Should().Be("Widget Analyst 4");
        index.ForCode("007399")!.LongTitle.Should().Be("Widget Analyst 4");
    }

    [Fact]
    public void A_standard_with_no_code_is_never_matched_by_code()
    {
        // Layout A never states a code, so many standards carry none. Padding an empty code yields
        // "000000", which must not become a wildcard that matches every uncoded standard.
        var index = new StandardsIndex([S("Widget Analyst 5")]);

        index.ForCode("000000").Should().BeNull();
        index.ForCode("").Should().BeNull();
    }

    [Fact]
    public void Entity_rows_map_onto_the_parser_shape()
    {
        // One type serves both sources, so a consumer cannot tell whether a standard came from a
        // workbook or from the database.
        var entity = new JobStandard
        {
            LongTitle = "Widget Analyst 2",
            Code = "111111",
            PersProg = "PSS",
            Grade = "Grade 21",
            Flsa = "Exempt",
            Union = "RX",
            GenericScope = "Generic.",
            CustomScope = "Custom.",
        };

        // Deliberately out of order, to prove Ordinal drives the result rather than insertion.
        entity.Items.Add(new JobStandardItem { Kind = StandardItemKind.KeyResponsibility, Ordinal = 1, Text = "Second." });
        entity.Items.Add(new JobStandardItem { Kind = StandardItemKind.KeyResponsibility, Ordinal = 0, Text = "First." });
        entity.Items.Add(new JobStandardItem { Kind = StandardItemKind.Ksa, Ordinal = 0, Text = "Knows things." });
        entity.Items.Add(new JobStandardItem { Kind = StandardItemKind.Education, Ordinal = 0, Text = "Degree." });
        entity.Items.Add(new JobStandardItem { Kind = StandardItemKind.License, Ordinal = 0, Text = "Licence." });
        entity.Items.Add(new JobStandardItem { Kind = StandardItemKind.SpecialCondition, Ordinal = 0, Text = "Condition." });

        var record = StandardsStore.ToRecord(entity);

        record.LongTitle.Should().Be("Widget Analyst 2");
        record.Code.Should().Be("111111");
        record.KeyResponsibilities.Should().Equal("First.", "Second.");
        record.Ksa.Should().Equal("Knows things.");
        record.Education.Should().Equal("Degree.");
        record.Licenses.Should().Equal("Licence.");
        record.SpecialConditions.Should().Equal("Condition.");
    }

    [Fact]
    public void Slugs_follow_the_reference_rule_including_the_std_fallback()
    {
        // Getting this wrong is how a re-bootstrap creates a SECOND profile for one class under a
        // different name.
        Bootstrapper.SlugFor("006256", "Rsch Data Anl 2").Should().Be("006256-rsch-data-anl-2");
        Bootstrapper.SlugFor("", "Academic Achievement Counselor 2 SV")
            .Should().Be("std-academic-achievement-counselor-2-sv");

        // Punctuation collapses to single separators with no leading or trailing dash.
        Bootstrapper.SlugFor("007399", "Project & Policy Analyst 4 (RP)")
            .Should().Be("007399-project-policy-analyst-4-rp");
    }
}

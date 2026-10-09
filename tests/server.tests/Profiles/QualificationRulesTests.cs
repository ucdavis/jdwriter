using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Profiles;

namespace Server.Tests.Profiles;

/// <summary>
/// UC Davis's education rules for envelopes: a preferred degree belongs in preferred
/// qualifications, and a degree always reads "or equivalent experience". Every string here is
/// synthetic — written in the shapes real envelopes take, never copied from one.
/// </summary>
public class QualificationRulesTests
{
    private static JobEnvelope Envelope(string[] education, string[]? preferred = null)
    {
        var env = new JobEnvelope();
        for (var i = 0; i < education.Length; i++)
        {
            env.Items.Add(new EnvelopeListItem { Kind = EnvelopeListKind.Education, Ordinal = i, Text = education[i] });
        }

        var pref = preferred ?? [];
        for (var i = 0; i < pref.Length; i++)
        {
            env.Items.Add(new EnvelopeListItem { Kind = EnvelopeListKind.PrefQualification, Ordinal = i, Text = pref[i] });
        }

        return env;
    }

    private static string[] Texts(JobEnvelope env, EnvelopeListKind kind) =>
        env.Items.Where(i => i.Kind == kind).OrderBy(i => i.Ordinal).Select(i => i.Text).ToArray();

    [Theory]
    [InlineData("Master's degree in horticulture preferred.", "Master's degree in horticulture or equivalent experience.")]
    [InlineData("Preferred: Graduate degree in public policy.", "Graduate degree in public policy or equivalent experience.")]
    [InlineData("Advanced degree in a related field (preferred)", "Advanced degree in a related field or equivalent experience.")]
    [InlineData("Advanced degree in soil science may be preferred for some positions.", "Advanced degree in soil science or equivalent experience.")]
    [InlineData("A master's degree in counseling or a related field is often preferred.", "A master's degree in counseling or a related field or equivalent experience.")]
    public void A_wholly_preferred_degree_moves_to_preferred_qualifications(string education, string preferred)
    {
        var env = Envelope([education]);

        var result = QualificationRules.Apply(env);

        Texts(env, EnvelopeListKind.Education).Should().BeEmpty();
        Texts(env, EnvelopeListKind.PrefQualification).Should().Equal(preferred);
        result.MovedToPreferred.Should().Be(1);
    }

    [Fact]
    public void A_mixed_item_keeps_its_requirement_and_moves_only_the_preference()
    {
        var env = Envelope(["Bachelor's degree in agronomy or equivalent experience; advanced degree (M.S. or Ph.D.) preferred for field research roles."]);

        QualificationRules.Apply(env);

        Texts(env, EnvelopeListKind.Education).Should().Equal("Bachelor's degree in agronomy or equivalent experience.");
        Texts(env, EnvelopeListKind.PrefQualification).Should().Equal(
            "Advanced degree (M.S. or Ph.D.) for field research roles or equivalent experience.");
    }

    [Fact]
    public void A_preferred_sentence_after_a_requirement_moves_on_its_own()
    {
        var env = Envelope(["No formal education required; equivalent experience accepted. A bachelor's degree in plant biology is commonly preferred."]);

        QualificationRules.Apply(env);

        Texts(env, EnvelopeListKind.Education).Should().Equal("No formal education required; equivalent experience accepted.");
        Texts(env, EnvelopeListKind.PrefQualification).Should().Equal("A bachelor's degree in plant biology or equivalent experience.");
    }

    [Fact]
    public void Some_positions_may_prefer_reads_as_the_qualification_itself()
    {
        var env = Envelope(["Equivalent experience accepted. Some positions may prefer a certificate in animal care."]);

        QualificationRules.Apply(env);

        Texts(env, EnvelopeListKind.PrefQualification).Should().Equal("A certificate in animal care.");
    }

    [Theory]
    [InlineData("Bachelor's degree in accounting.", "Bachelor's degree in accounting or equivalent experience.")]
    [InlineData("Degree in environmental science", "Degree in environmental science or equivalent experience.")]
    [InlineData("Ph.D. in entomology.", "Ph.D. in entomology or equivalent experience.")]
    public void A_degree_always_reads_or_equivalent_experience(string education, string expected)
    {
        var env = Envelope([education]);

        var result = QualificationRules.Apply(env);

        Texts(env, EnvelopeListKind.Education).Should().Equal(expected);
        result.EquivalentAdded.Should().Be(1);
    }

    [Fact]
    public void The_equivalent_rule_applies_to_preferred_degrees_too_but_not_to_passing_mentions()
    {
        var env = Envelope([], ["Master's degree in nutrition.", "Experience with degree certification systems.", "Strong writing skills."]);

        QualificationRules.Apply(env);

        Texts(env, EnvelopeListKind.PrefQualification).Should().Equal(
            "Master's degree in nutrition or equivalent experience.",
            "Experience with degree certification systems.",
            "Strong writing skills.");
    }

    [Theory]
    [InlineData("Bachelor's degree in biology and/or equivalent experience/training.")]
    [InlineData("Bachelor's degree or an equivalent combination of education and experience.")]
    [InlineData("High school diploma or GED.")]
    [InlineData("No formal education required; equivalent experience accepted.")]
    public void Text_that_already_complies_is_left_alone(string education)
    {
        var env = Envelope([education]);

        QualificationRules.Apply(env).Changed.Should().BeFalse();
        Texts(env, EnvelopeListKind.Education).Should().Equal(education);
    }

    [Fact]
    public void A_second_pass_changes_nothing()
    {
        var env = Envelope(
            ["Bachelor's degree in economics; master's degree preferred.", "Degree in statistics"],
            ["Master's degree in economics."]);

        QualificationRules.Apply(env);
        var before = env.Items.Select(i => (i.Kind, i.Ordinal, i.Text)).ToList();

        QualificationRules.Apply(env).Changed.Should().BeFalse();
        env.Items.Select(i => (i.Kind, i.Ordinal, i.Text)).Should().Equal(before);
    }

    [Fact]
    public void A_moved_preference_already_listed_is_not_added_twice()
    {
        var env = Envelope(["Bachelor's degree or equivalent experience; Master's degree in finance preferred."], ["Master's degree in finance or equivalent experience."]);

        QualificationRules.Apply(env);

        Texts(env, EnvelopeListKind.PrefQualification).Should().Equal("Master's degree in finance or equivalent experience.");
    }

    [Fact]
    public void A_vague_preference_is_not_added_beside_a_more_specific_one()
    {
        var env = Envelope(["Bachelor's degree or equivalent experience.", "Advanced degree preferred"], ["Advanced degree in a related field."]);

        QualificationRules.Apply(env);

        Texts(env, EnvelopeListKind.Education).Should().Equal("Bachelor's degree or equivalent experience.");
        Texts(env, EnvelopeListKind.PrefQualification).Should().Equal("Advanced degree in a related field or equivalent experience.");
    }

    [Fact]
    public void Moved_items_join_the_end_of_the_preferred_list_and_ordinals_stay_contiguous()
    {
        var env = Envelope(
            ["Master's degree preferred.", "Bachelor's degree or equivalent experience."],
            ["Spanish fluency.", "Grant-writing experience."]);

        QualificationRules.Apply(env);

        env.Items.Where(i => i.Kind == EnvelopeListKind.Education).Select(i => i.Ordinal).Should().Equal(0);
        Texts(env, EnvelopeListKind.PrefQualification).Should().Equal(
            "Spanish fluency.", "Grant-writing experience.", "Master's degree or equivalent experience.");
        env.Items.Where(i => i.Kind == EnvelopeListKind.PrefQualification).Select(i => i.Ordinal).Should().BeEquivalentTo([0, 1, 2]);
    }
}

using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Jd;
using Server.Core.Titles;

namespace Server.Tests.Titles;

/// <summary>
/// A class's bargaining unit comes from its title suffix before its corpus, and union-represented
/// classes may lead but not supervise.
/// </summary>
public class BargainingUnitsTests
{
    private static ClassProfile Profile(string title, string? unionConsensus) => new()
    {
        Title = title,
        Distributions = unionConsensus is null
            ? []
            : [new ProfileDistribution { Field = DistributionField.UnionCode, Consensus = unionConsensus }],
    };

    [Theory]
    [InlineData("Academic Achievement Counselor 2 SV", "SV")]
    [InlineData("Acad Achievement Cnslr 3 Sv", "SV")]
    [InlineData("Financial Analyst 3 CX", "CX")]
    [InlineData("Applications Programmer 3 TX GF", "TX")]
    [InlineData("Project Policy Anl 4 Rp", "RP")]
    [InlineData("Dietitian 1 EX", null)]
    [InlineData("Lab Ast 1", null)]
    public void The_title_suffix_names_the_unit(string title, string? unit) =>
        BargainingUnits.FromTitle(title).Should().Be(unit);

    [Fact]
    public void The_title_suffix_wins_over_a_stale_corpus_consensus()
    {
        // Exports predating the SV accretion still say non-represented.
        var p = Profile("Academic Achievement Counselor 2 SV", "99 - Non-Represented (PPSM)");

        BargainingUnits.For(p).Should().Be("SV");
        BargainingUnits.IsRepresented(p).Should().BeTrue();
    }

    [Theory]
    [InlineData("99", false)]
    [InlineData("99 - Non-Represented (PPSM)", false)]
    [InlineData("CX-Clerical/Admin", true)]
    [InlineData("SX-Service Workers", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Without_a_suffix_the_consensus_decides(string? consensus, bool represented)
    {
        var p = Profile("Lab Ast 1", consensus);

        BargainingUnits.IsRepresented(p).Should().Be(represented);
        BargainingUnits.For(p).Should().Be(consensus);
    }

    [Fact]
    public void A_represented_class_may_lead_but_not_supervise()
    {
        var p = Profile("Student Academic Advisor 3 SV", null);

        BargainingUnits.SupervisionProblem(p, supervises: true, count: 3).Should().Contain("can't supervise").And.Contain("It may lead");
        BargainingUnits.SupervisionProblem(p, supervises: false, count: null).Should().BeNull();
        BargainingUnits.SupervisionProblem(p, supervises: null, count: null).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(10_001)]
    public void A_supervising_position_says_how_many(int? count) =>
        BargainingUnits.SupervisionProblem(Profile("Admin Ofcr 3", "99"), true, count)
            .Should().Be("Enter how many people this position supervises.");

    [Fact]
    public void A_non_represented_supervisor_with_a_count_is_fine() =>
        BargainingUnits.SupervisionProblem(Profile("Admin Ofcr 3", "99"), true, 4).Should().BeNull();

    [Fact]
    public void The_documents_state_supervision_as_given()
    {
        var saved = new SavedJd { Supervises = true, SupervisesCount = 4, Leads = false };

        JdHandoffBuilder.SupervisionFacts(saved).Should().Equal("Supervises: Yes (4 people)", "Leads: No");
        JdHandoffBuilder.SupervisionFacts(new SavedJd { Supervises = true, SupervisesCount = 1 }).Should().Equal("Supervises: Yes (1 person)");
        JdHandoffBuilder.SupervisionFacts(new SavedJd()).Should().BeEmpty("nothing stated, nothing claimed");
    }
}

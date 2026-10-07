using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Titles;

namespace Server.Tests.Titles;

/// <summary>
/// The CX, TX, RX and HX extension of supersession, over the full title reference.
///
/// The POC derived RP pairs only, so there is no oracle for these: the expectations below were
/// read off the reference by hand. The traps are the suffixes that look like units and are not —
/// NEX (non-exempt), SV, PD — because retiring a live class hides it from browse and refiles its
/// JDs under another one.
/// </summary>
public class UnionSupersessionTests
{
    private static readonly TitleCodeIndex Index = TitleCodeIndexParityTests.Index;

    private static IEnumerable<Supersession> Unit(string unit) =>
        Index.AllSupersessions().Where(s => s.ToTitle.Split(' ').Contains(unit));

    [Fact]
    public void Each_unit_contributes_its_pairs()
    {
        Unit("RP").Should().HaveCount(29, "the POC's RP pairs are unchanged");
        Unit("CX").Should().HaveCount(24);
        Unit("TX").Should().HaveCount(11, "RSCH AND DEV ENGR 4 is ambiguous, not paired");
        Unit("HX").Should().HaveCount(7);
        Unit("RX").Should().HaveCount(2);
        Index.AllSupersessions().Should().HaveCount(73);
    }

    [Theory]
    [InlineData("007709", "005183", "FINANCIAL ANL 3 CX")]
    [InlineData("006205", "004486", "RSCH ADM 2 CX")]
    [InlineData("000520", "006375", "SYS ADM 4 TX")]
    [InlineData("009453", "005304", "ATH TRAINER 1 HX")]
    [InlineData("007146", "005303", "EHS SPEC 3 RX")]
    public void A_non_represented_code_is_retired_in_favour_of_its_union_successor(
        string from, string to, string toTitle)
    {
        Index.IsSuperseded(from).Should().BeTrue();
        Index.ResolveCode(from).Should().Be(to);
        Index.SupersededBy(from)!.ToTitle.Should().Be(toTitle);
        Index.IsSuperseded(to).Should().BeFalse("{0} is the live successor", to);
        Index.InUseTitleCodes().Select(t => t.Code).Should().NotContain(from).And.Contain(to);
    }

    [Fact]
    public void A_union_suffix_after_a_variant_pairs_with_that_variant()
    {
        // "EHS SPEC 2 NEX" is the retired class; NEX belongs to both sides of the pair.
        Index.SupersededBy("007145")!.ToTitle.Should().Be("EHS SPEC 2 NEX RX");
    }

    [Fact]
    public void Non_exempt_is_an_flsa_variant_not_a_union()
    {
        // SRA 2 and SRA 2 NEX are both current. Reading NEX as a unit would retire SRA 2 and refile
        // its 118 JDs under the non-exempt class.
        Index.IsSuperseded("009612").Should().BeFalse();
        Index.IsSuperseded("009617").Should().BeFalse();
    }

    [Fact]
    public void Supervisor_and_per_diem_variants_are_never_successors()
    {
        Index.AllSupersessions()
            .Where(s => s.ToTitle.EndsWith(" SV", StringComparison.Ordinal)
                        || s.ToTitle.EndsWith(" PD", StringComparison.Ordinal)
                        || s.ToTitle.EndsWith(" NEX", StringComparison.Ordinal))
            .Should().BeEmpty();
    }

    [Fact]
    public void A_grandfathered_union_variant_makes_its_group_ambiguous_rather_than_guessed()
    {
        Index.IsSuperseded("000442").Should().BeFalse("RSCH AND DEV ENGR 4 has a TX and a TX GF successor");
        Index.AmbiguousSupersessions().Should().ContainSingle()
            .Which.Should().Contain("004899").And.Contain("006392");
    }
}

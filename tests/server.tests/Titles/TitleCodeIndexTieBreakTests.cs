using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Titles;

namespace Server.Tests.Titles;

/// <summary>
/// The in-use tie-break, on constructed rows.
///
/// These cannot be fixture tests. The real reference has 3,471 rows and NOT ONE strict key with
/// more than one in-use record, so the branch that rejects such a tie is unreachable from real
/// data — a mutation changing `inUse.Count == 1` to `>= 1` (guess instead of refuse) passes the
/// entire fixture suite. The four genuinely ambiguous titles all have zero in-use records, which
/// exercises a different path.
///
/// That branch still matters: the reference is regenerated from a UCD workbook plus payroll OCR,
/// and a refresh could introduce a real tie at any time. A wrong job code is worse than a missing
/// one, so refusing has to stay refusing.
/// </summary>
public class TitleCodeIndexTieBreakTests
{
    private static TitleCode Row(string code, string title, string source) => new()
    {
        Code = code,
        Title = title,
        Source = source,
        Grade = "",
        Function = "",
        Family = "",
        TitleKey = TitleNormalizer.TitleKey(title),
        TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
    };

    [Fact]
    public void A_single_match_resolves_regardless_of_whether_it_is_in_use()
    {
        var index = new TitleCodeIndex([Row("111111", "WIDGET ANALYST 2", "matrix")]);

        index.FindTitleCode("WIDGET ANALYST 2")!.Code.Should().Be("111111");
        index.ExplainTitleCode("WIDGET ANALYST 2").Should().Be(TitleCodeResolution.Ok);
    }

    [Fact]
    public void One_in_use_record_wins_against_matrix_only_duplicates()
    {
        // The documented reason this tie-break exists: the reference lists some titles twice,
        // once as possible at UCD and once as observed on payroll, and the payroll record is the
        // real classification.
        var index = new TitleCodeIndex([
            Row("222222", "WIDGET ANALYST 2", "matrix"),
            Row("333333", "WIDGET ANALYST 2", "payroll_list"),
        ]);

        index.FindTitleCode("WIDGET ANALYST 2")!.Code.Should().Be("333333");
        index.ExplainTitleCode("WIDGET ANALYST 2").Should().Be(TitleCodeResolution.Ok);
    }

    [Fact]
    public void Source_both_also_counts_as_in_use()
    {
        var index = new TitleCodeIndex([
            Row("444444", "WIDGET ANALYST 3", "matrix"),
            Row("555555", "WIDGET ANALYST 3", "both"),
        ]);

        index.FindTitleCode("WIDGET ANALYST 3")!.Code.Should().Be("555555");
    }

    [Fact]
    public void Two_in_use_records_refuse_to_resolve()
    {
        // THE BRANCH REAL DATA CANNOT REACH. Returning either code here would be a coin flip
        // presented as an answer, and the caller has no way to tell a guess from a fact.
        var index = new TitleCodeIndex([
            Row("666666", "WIDGET ANALYST 4", "payroll_list"),
            Row("777777", "WIDGET ANALYST 4", "both"),
        ]);

        index.FindTitleCode("WIDGET ANALYST 4").Should().BeNull();
        index.ExplainTitleCode("WIDGET ANALYST 4").Should().Be(TitleCodeResolution.Ambiguous);
    }

    [Fact]
    public void Several_matrix_only_records_refuse_to_resolve()
    {
        // This is the shape the four real ambiguous titles take.
        var index = new TitleCodeIndex([
            Row("888888", "WIDGET ANALYST 5", "matrix"),
            Row("999999", "WIDGET ANALYST 5", "matrix"),
        ]);

        index.FindTitleCode("WIDGET ANALYST 5").Should().BeNull();
        index.ExplainTitleCode("WIDGET ANALYST 5").Should().Be(TitleCodeResolution.Ambiguous);
    }

    [Fact]
    public void Variant_suffixes_keep_two_real_classifications_apart()
    {
        // The whole reason for the strict key: these are different codes, and the loose key
        // cannot tell them apart.
        var index = new TitleCodeIndex([
            Row("007396", "PROJECT POLICY ANL 1", "both"),
            Row("005255", "PROJECT POLICY ANL 1 RP", "both"),
        ]);

        index.FindTitleCode("PROJECT POLICY ANL 1")!.Code.Should().Be("007396");
        index.FindTitleCode("PROJECT POLICY ANL 1 RP")!.Code.Should().Be("005255");
        index.FindTitleCode("Project and Policy Analyst 1")!.Code.Should().Be("007396");
    }

    [Fact]
    public void An_rp_pair_supersedes_and_hides_the_retired_code()
    {
        var index = new TitleCodeIndex([
            Row("007396", "PROJECT POLICY ANL 1", "both"),
            Row("005255", "PROJECT POLICY ANL 1 RP", "both"),
        ]);

        index.AllSupersessions().Should().ContainSingle();
        index.ResolveCode("007396").Should().Be("005255");
        index.InUseTitleCodes().Select(t => t.Code).Should().Equal("005255");
    }

    [Fact]
    public void A_pair_is_only_derived_when_both_sides_are_in_use()
    {
        // A code nobody is paid under cannot supersede anything — otherwise a merely-possible
        // matrix title could retire a live classification.
        var index = new TitleCodeIndex([
            Row("007396", "PROJECT POLICY ANL 1", "matrix"),
            Row("005255", "PROJECT POLICY ANL 1 RP", "both"),
        ]);

        index.AllSupersessions().Should().BeEmpty();
        index.IsSuperseded("007396").Should().BeFalse();
    }

    [Fact]
    public void More_than_one_candidate_on_either_side_is_reported_not_guessed()
    {
        // Deprecating the wrong code would hide a live class, so this reports rather than picks.
        var index = new TitleCodeIndex([
            Row("100001", "WIDGET PLANNER 1", "both"),
            Row("100002", "WIDGET PLANNER 1 RP", "both"),
            Row("100003", "WIDGET PLANNER 1 GF", "both"),
        ]);

        index.AllSupersessions().Should().BeEmpty();
        index.AmbiguousSupersessions().Should().ContainSingle()
            .Which.Should().Contain("100001").And.Contain("100002").And.Contain("100003");
    }
}

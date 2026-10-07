using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Titles;

namespace Server.Tests.Titles;

/// <summary>
/// Byte-parity of job-code resolution and RP supersession derivation against the POC, over the
/// full 3,471-row title reference.
///
/// The POC derived RP pairs only. The port extends the same rule to the CX, TX, RX and HX units
/// (see <see cref="UnionSupersessionTests"/>), so these tests compare the RP subset to the oracle
/// exactly and account for the extension explicitly rather than loosening to a superset check.
///
/// Supersession in particular is derived, never authored, and it CANNOT be re-derived from the JD
/// corpus — exports under a retired code still read non-represented. So this derivation is the
/// source of truth for which classifications are alive, and it has to be exactly right.
/// </summary>
public class TitleCodeIndexParityTests
{
    private sealed class ReferenceRow
    {
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string Grade { get; set; } = "";
        public string Function { get; set; } = "";
        public string Family { get; set; } = "";
        public string Source { get; set; } = "";
    }

    private sealed class ResolutionFile
    {
        public List<ResolutionCase> Cases { get; set; } = [];
        public List<ByCodeCase> ByCode { get; set; } = [];
    }

    private sealed class ResolutionCase
    {
        public string Title { get; set; } = "";
        public string Verdict { get; set; } = "";
        public string? Code { get; set; }
        public string? ResolvedTitle { get; set; }
        public string? Grade { get; set; }
        public string? Family { get; set; }
        public string? Function { get; set; }
        public string? Source { get; set; }
    }

    private sealed class ByCodeCase
    {
        public string Probe { get; set; } = "";
        public string? Code { get; set; }
        public string? Title { get; set; }
        public string? Grade { get; set; }
    }

    private sealed class SupersessionFile
    {
        public List<SupersessionCase> Supersessions { get; set; } = [];
        public List<string> Ambiguous { get; set; } = [];
        public List<string> InUseCodes { get; set; } = [];
    }

    private sealed class SupersessionCase
    {
        public string From { get; set; } = "";
        public string FromTitle { get; set; } = "";
        public string To { get; set; } = "";
        public string ToTitle { get; set; } = "";
    }

    internal static readonly TitleCodeIndex Index = BuildIndex();

    private static TitleCodeIndex BuildIndex()
    {
        var rows = Fixtures.Load<List<ReferenceRow>>("title-codes.json");
        return new TitleCodeIndex(rows.Select(r => new TitleCode
        {
            Code = r.Code,
            Title = r.Title,
            Grade = r.Grade,
            Function = r.Function,
            Family = r.Family,
            Source = r.Source,
            TitleKey = TitleNormalizer.TitleKey(r.Title),
            TitleCodeKey = TitleNormalizer.TitleCodeKey(r.Title),
        }));
    }

    [Fact]
    public void The_reference_loaded_at_full_size()
    {
        // A truncated input would make every comparison below pass vacuously.
        Index.All.Should().HaveCount(3471);
    }

    [Fact]
    public void Code_resolution_matches_the_reference_implementation()
    {
        var fixture = Fixtures.Load<ResolutionFile>("titleCodes.resolution.json");
        fixture.Cases.Should().HaveCountGreaterThan(3000);

        var mismatches = new List<string>();

        foreach (var c in fixture.Cases)
        {
            var hit = Index.FindTitleCode(c.Title);
            var verdict = Index.ExplainTitleCode(c.Title).ToString().ToLowerInvariant();

            if (verdict != c.Verdict)
            {
                mismatches.Add($"  verdict  {c.Title,-50} expected {c.Verdict} got {verdict}");
            }

            if (hit?.Code != c.Code)
            {
                mismatches.Add($"  code     {c.Title,-50} expected {c.Code ?? "null"} got {hit?.Code ?? "null"}");
            }

            // The rest of the resolved row matters too: standards ingest backfills salary grade
            // from it and bootstrap copies family/function onto new profiles, so resolving the
            // right code off the wrong row is still broken.
            if (hit?.Title != c.ResolvedTitle || hit?.Grade != c.Grade
                || hit?.Family != c.Family || hit?.Function != c.Function || hit?.Source != c.Source)
            {
                mismatches.Add($"  row      {c.Title,-50} resolved to a different reference row");
            }
        }

        mismatches.Should().BeEmpty(
            "resolution must reproduce the fixture exactly — {0} differences:\n{1}",
            mismatches.Count, string.Join('\n', mismatches.Take(15)));
    }

    [Fact]
    public void Unresolvable_titles_are_reproduced_exactly()
    {
        // 27 unknown (UC systemwide titles with no UCD equivalent) and 4 ambiguous. These are
        // CORRECT outcomes, not gaps: loosening the key to "fix" them is what produces wrong
        // codes. Pinned so nobody tries.
        var fixture = Fixtures.Load<ResolutionFile>("titleCodes.resolution.json");

        var unknown = fixture.Cases.Where(c => c.Verdict == "unknown").Select(c => c.Title).ToList();
        var ambiguous = fixture.Cases.Where(c => c.Verdict == "ambiguous").Select(c => c.Title).ToList();

        unknown.Should().NotBeEmpty();
        ambiguous.Should().NotBeEmpty();

        foreach (var title in unknown)
        {
            Index.ExplainTitleCode(title).Should().Be(TitleCodeResolution.Unknown, "{0}", title);
            Index.FindTitleCode(title).Should().BeNull("{0}", title);
        }

        foreach (var title in ambiguous)
        {
            Index.ExplainTitleCode(title).Should().Be(TitleCodeResolution.Ambiguous, "{0}", title);
            // Ambiguity must resolve to null, never to a guess.
            Index.FindTitleCode(title).Should().BeNull("{0}", title);
        }
    }

    [Fact]
    public void Lookup_by_code_tolerates_unpadded_input()
    {
        // PD forms state the code without zero padding ("7399"), so both spellings must land on
        // the same row.
        var fixture = Fixtures.Load<ResolutionFile>("titleCodes.resolution.json");
        fixture.ByCode.Should().NotBeEmpty();

        foreach (var probe in fixture.ByCode)
        {
            var hit = Index.FindByCode(probe.Probe);
            hit?.Code.Should().Be(probe.Code, "probe '{0}'", probe.Probe);
            hit?.Title.Should().Be(probe.Title, "probe '{0}'", probe.Probe);
            hit?.Grade.Should().Be(probe.Grade, "probe '{0}'", probe.Probe);
        }
    }

    [Fact]
    public void Supersessions_are_derived_exactly()
    {
        var fixture = Fixtures.Load<SupersessionFile>("titleCodes.supersession.json");
        var actual = Index.AllSupersessions().Where(IsRp).ToList();

        actual.Should().HaveCount(fixture.Supersessions.Count);

        for (var i = 0; i < fixture.Supersessions.Count; i++)
        {
            var want = fixture.Supersessions[i];
            var got = actual[i];
            got.FromCode.Should().Be(want.From);
            got.FromTitle.Should().Be(want.FromTitle);
            got.ToCode.Should().Be(want.To);
            got.ToTitle.Should().Be(want.ToTitle);
        }

        // The oracle has no ambiguous RP group; the extension adds exactly one, pinned by
        // UnionSupersessionTests.
        Index.AmbiguousSupersessions().Where(a => !a.StartsWith("RSCH AND DEV ENGR 4 ", StringComparison.Ordinal))
            .Should().BeEquivalentTo(fixture.Ambiguous);
    }

    private static bool IsRp(Supersession s) =>
        s.ToTitle.Split(' ').Contains("RP", StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Superseded_codes_resolve_forward_and_are_hidden_from_the_in_use_list()
    {
        var fixture = Fixtures.Load<SupersessionFile>("titleCodes.supersession.json");

        foreach (var s in fixture.Supersessions)
        {
            Index.IsSuperseded(s.From).Should().BeTrue("{0} is retired", s.From);
            Index.ResolveCode(s.From).Should().Be(s.To);
            Index.SupersededBy(s.From)!.ToCode.Should().Be(s.To);

            // Unpadded spellings must behave identically, or a PD form could reach a dead class.
            Index.IsSuperseded(s.From.TrimStart('0')).Should().BeTrue("{0} unpadded", s.From);

            // The successor is current and must NOT itself be treated as retired.
            Index.IsSuperseded(s.To).Should().BeFalse("{0} is the live successor", s.To);
            Index.ResolveCode(s.To).Should().Be(s.To);
        }

        // Hiding a dead class from browse is exactly what this list is for.
        // The oracle's in-use list, less the codes the union extension retires.
        var retiredByExtension = Index.AllSupersessions().Where(s => !IsRp(s)).Select(s => s.FromCode).ToHashSet();
        var inUse = Index.InUseTitleCodes().Select(t => t.Code).OrderBy(c => c, StringComparer.Ordinal).ToList();
        inUse.Should().Equal(fixture.InUseCodes.Where(c => !retiredByExtension.Contains(c)));
        inUse.Should().NotIntersectWith(fixture.Supersessions.Select(s => s.From));
    }

    [Fact]
    public void An_unknown_code_resolves_to_itself()
    {
        // ResolveCode is applied to every parsed JD, so it must be a no-op for the overwhelming
        // majority that were never accreted.
        Index.ResolveCode("008543").Should().Be("008543");
        Index.IsSuperseded("008543").Should().BeFalse();
        Index.SupersededBy("008543").Should().BeNull();
    }
}

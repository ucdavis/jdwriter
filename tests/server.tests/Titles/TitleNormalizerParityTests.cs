using FluentAssertions;
using Server.Core.Titles;

namespace Server.Tests.Titles;

/// <summary>
/// Byte-parity of the C# title normalizer against the POC's TypeScript, over every real title in
/// the reference, the official standards, and the ingested profiles.
///
/// Written as fixture comparisons rather than hand-picked cases on purpose. Hand-written cases
/// cover the transformations we remember, which is exactly the set that already works; 3,733 real
/// titles cover the ones we do not.
/// </summary>
public class TitleNormalizerParityTests
{
    private sealed class NormalizationFile
    {
        public List<NormalizationCase> Cases { get; set; } = [];
    }

    private sealed class NormalizationCase
    {
        public string Title { get; set; } = "";
        public List<string> Sources { get; set; } = [];
        public string TitleKey { get; set; } = "";
        public string TitleCodeKey { get; set; } = "";
        public List<string> TitleWords { get; set; } = [];
    }

    private sealed class KeySplitFile
    {
        public List<KeySplitCase> KeySplit { get; set; } = [];
        public List<LooseCollision> LooseCollisions { get; set; } = [];
    }

    private sealed class KeySplitCase
    {
        public string Title { get; set; } = "";
        public string TitleKey { get; set; } = "";
        public string TitleCodeKey { get; set; } = "";
    }

    private sealed class LooseCollision
    {
        public string TitleKey { get; set; } = "";
        public List<string> StrictKeys { get; set; } = [];
        public List<string> Titles { get; set; } = [];
    }

    private sealed class SearchFile
    {
        public List<string> Titles { get; set; } = [];
        public List<SearchCase> Cases { get; set; } = [];
    }

    private sealed class SearchCase
    {
        public string Query { get; set; } = "";
        public List<string> QueryWords { get; set; } = [];
        public List<string> Matches { get; set; } = [];
    }

    private static readonly NormalizationFile Normalization =
        Fixtures.Load<NormalizationFile>("titles.normalization.json");

    [Fact]
    public void Fixture_covers_all_three_title_vocabularies()
    {
        // The reference is machine-generated and consistent, the standards use UC-system
        // spellings ("Analyst" where the reference says "Anl"), and the profile titles are HRTMS
        // corpus spellings. A port that satisfies only one of them is not done.
        Normalization.Cases.Should().HaveCountGreaterThan(3000);
        var sources = Normalization.Cases.SelectMany(c => c.Sources).Distinct().ToList();
        sources.Should().Contain(["reference", "standard", "profile"]);
    }

    [Fact]
    public void TitleKey_matches_the_reference_implementation()
    {
        AssertAll(c => TitleNormalizer.TitleKey(c.Title), c => c.TitleKey, nameof(TitleNormalizer.TitleKey));
    }

    [Fact]
    public void TitleCodeKey_matches_the_reference_implementation()
    {
        AssertAll(c => TitleNormalizer.TitleCodeKey(c.Title), c => c.TitleCodeKey, nameof(TitleNormalizer.TitleCodeKey));
    }

    [Fact]
    public void TitleWords_matches_the_reference_implementation()
    {
        AssertAll(
            c => string.Join('|', TitleNormalizer.TitleWords(c.Title)),
            c => string.Join('|', c.TitleWords),
            nameof(TitleNormalizer.TitleWords));
    }

    private static void AssertAll(
        Func<NormalizationCase, string> actual,
        Func<NormalizationCase, string> expected,
        string label)
    {
        var mismatches = new List<string>();

        foreach (var c in Normalization.Cases)
        {
            var got = actual(c);
            var want = expected(c);
            if (!string.Equals(got, want, StringComparison.Ordinal))
            {
                mismatches.Add($"  {c.Title,-55} expected: {want,-45} got: {got}");
            }
        }

        // Report a sample rather than 3,733 lines; the count is what says how bad it is.
        mismatches.Should().BeEmpty(
            "{0} must reproduce the fixture exactly — {1} of {2} differ:\n{3}",
            label, mismatches.Count, Normalization.Cases.Count,
            string.Join('\n', mismatches.Take(15)));
    }

    [Fact]
    public void The_strict_and_loose_keys_differ_on_exactly_the_expected_titles()
    {
        // This is the guard against the two keys being quietly re-merged. If the split ever
        // collapses, this set empties out and the test fails loudly rather than the system
        // resolving confidently wrong job codes.
        var fixture = Fixtures.Load<KeySplitFile>("titles.keySplit.json");
        fixture.KeySplit.Should().NotBeEmpty();

        foreach (var c in fixture.KeySplit)
        {
            TitleNormalizer.TitleKey(c.Title).Should().Be(c.TitleKey, "loose key for {0}", c.Title);
            TitleNormalizer.TitleCodeKey(c.Title).Should().Be(c.TitleCodeKey, "strict key for {0}", c.Title);
            c.TitleKey.Should().NotBe(c.TitleCodeKey);
        }
    }

    [Fact]
    public void Loose_key_collisions_are_reproduced_exactly()
    {
        // Each of these is a loose key covering more than one real classification — precisely
        // what the strict key exists to keep apart.
        var fixture = Fixtures.Load<KeySplitFile>("titles.keySplit.json");
        fixture.LooseCollisions.Should().NotBeEmpty();

        foreach (var collision in fixture.LooseCollisions)
        {
            var strict = collision.Titles
                .Select(TitleNormalizer.TitleCodeKey)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            strict.Should().BeEquivalentTo(collision.StrictKeys,
                "titles sharing loose key '{0}' must keep their distinct strict keys",
                collision.TitleKey);

            collision.Titles.Select(TitleNormalizer.TitleKey)
                .Distinct(StringComparer.Ordinal)
                .Should().ContainSingle();
        }
    }

    [Fact]
    public void Search_matching_matches_the_reference_implementation()
    {
        var fixture = Fixtures.Load<SearchFile>("titles.search.json");
        var titles = fixture.Titles;

        foreach (var c in fixture.Cases)
        {
            TitleNormalizer.TitleWords(c.Query).Should().Equal(c.QueryWords, "query words for '{0}'", c.Query);

            var matched = titles
                .Where(t => TitleNormalizer.TitleMatches(c.Query, t))
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();

            matched.Should().Equal(c.Matches, "search results for '{0}'", c.Query);
        }
    }
}

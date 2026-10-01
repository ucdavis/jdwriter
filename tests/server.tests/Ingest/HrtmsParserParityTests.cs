using FluentAssertions;
using Server.Core.Ingest;

namespace Server.Tests.Ingest;

/// <summary>
/// Byte-parity of the C# HRTMS parser against the POC's TypeScript, over all 1,367 real exports.
///
/// The corpus is 150MB of real job descriptions and is gitignored, so it cannot live in this repo.
/// These tests are therefore tagged <c>Category=CorpusParity</c> and must be EXCLUDED explicitly
/// where the corpus is absent:
///
///     dotnet test --filter "Category!=CorpusParity"
///
/// They deliberately FAIL rather than silently pass when the corpus cannot be found. A parity
/// suite that quietly skips is worse than no suite: the run goes green and nobody learns that the
/// 246 lines of parsing logic were never checked.
/// </summary>
[Trait("Category", "CorpusParity")]
public class HrtmsParserParityTests
{
    private sealed class HashesFile
    {
        public HashesManifest Manifest { get; set; } = new();
        public List<FileEntry> Files { get; set; } = [];
    }

    private sealed class HashesManifest
    {
        public int Files { get; set; }
        public int Parsed { get; set; }
        public int Failures { get; set; }
        public string CorpusDigest { get; set; } = "";
    }

    private sealed class FileEntry
    {
        public string Id { get; set; } = "";
        public string ClassDir { get; set; } = "";
        public string? Sha256 { get; set; }
        public Shape? Shape { get; set; }
    }

    private sealed class Shape
    {
        public int Responsibilities { get; set; }
        public List<int> DutiesPerResponsibility { get; set; } = [];
        public List<int?> PctValues { get; set; } = [];
        public int PctSum { get; set; }
        public bool PemPopulated { get; set; }
        public int JobSummaryLength { get; set; }
        public string? OriginalUcJobCode { get; set; }
    }

    private static readonly HashesFile Expected = Fixtures.Load<HashesFile>("hrtms.hashes.json");

    /// <summary>
    /// Locate the corpus. <c>JDW_CORPUS_DIR</c> wins; otherwise probe the sibling POC checkout,
    /// which is where it lives on a development machine.
    /// </summary>
    private static string ResolveCorpusDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable("JDW_CORPUS_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            Directory.Exists(fromEnv).Should().BeTrue(
                "JDW_CORPUS_DIR is set to '{0}' but that directory does not exist", fromEnv);
            return fromEnv;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "..", "JDWriter", "Sample JDs");
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "HRTMS corpus not found. Set JDW_CORPUS_DIR to the 'Sample JDs' directory, or exclude " +
            "these tests where the corpus is unavailable: dotnet test --filter \"Category!=CorpusParity\".");
    }

    /// <summary>
    /// Enumerate the corpus the same way the generator does: every .htm/.html file, recursively,
    /// with forward-slash relative paths.
    /// </summary>
    private static List<string> CorpusFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                        || p.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void The_corpus_is_present_and_the_expected_size()
    {
        // Guards every other test here: a partial corpus would make the comparisons below pass on
        // whatever subset happened to be available.
        var files = CorpusFiles(ResolveCorpusDir());

        files.Should().HaveCount(Expected.Manifest.Files,
            "the fixture was generated from {0} exports", Expected.Manifest.Files);
        Expected.Manifest.Failures.Should().Be(0);
        Expected.Files.Should().HaveCount(Expected.Manifest.Files);
    }

    [Fact]
    public void Every_export_parses_to_a_byte_identical_record()
    {
        var root = ResolveCorpusDir();
        var expectedById = Expected.Files.ToDictionary(f => f.Id, f => f);

        var mismatches = new List<string>();
        var parsed = 0;
        var threw = new List<string>();

        foreach (var rel in CorpusFiles(root))
        {
            var id = HrtmsCanonical.FileId(rel);
            expectedById.Should().ContainKey(id,
                "corpus file '{0}' has no fixture entry — regenerate with `npm run fixtures`", rel);

            var want = expectedById[id];

            HrtmsRecord record;
            try
            {
                record = HrtmsParser.Parse(File.ReadAllText(Path.Combine(root, rel)), rel);
            }
            catch (Exception ex)
            {
                // The reference parses all 1,367 without throwing, so any exception is a port bug.
                threw.Add($"  {id} ({want.ClassDir}): {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            parsed++;
            var got = HrtmsCanonical.Sha256(HrtmsCanonical.Project(record));
            if (got != want.Sha256)
            {
                // Identify by id and class folder, never by path: the filename embeds a position
                // number. The shape fields below are what localize the drift.
                mismatches.Add($"  {id} ({want.ClassDir})");
            }
        }

        threw.Should().BeEmpty("the reference parses every export without throwing:\n{0}",
            string.Join('\n', threw.Take(10)));

        parsed.Should().Be(Expected.Manifest.Files);

        mismatches.Should().BeEmpty(
            "{0} of {1} exports parsed differently. Resolve an id to a path with " +
            "fixtures/local/hrtms.filemap.json in the POC repo, then diff against the POC parser:\n{2}",
            mismatches.Count, parsed, string.Join('\n', mismatches.Take(20)));
    }

    [Fact]
    public void The_corpus_digest_matches()
    {
        // One number that says whether anything at all drifted. Computed over id:hash pairs in
        // the fixture's own order, so it also catches a file appearing or disappearing.
        var root = ResolveCorpusDir();
        var byId = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rel in CorpusFiles(root))
        {
            var record = HrtmsParser.Parse(File.ReadAllText(Path.Combine(root, rel)), rel);
            byId[HrtmsCanonical.FileId(rel)] = HrtmsCanonical.Sha256(HrtmsCanonical.Project(record));
        }

        var lines = Expected.Files.Select(f =>
            $"{f.Id}:{(byId.TryGetValue(f.Id, out var h) ? h : "ERR")}");

        HrtmsCanonical.Sha256(string.Join('\n', lines))
            .Should().Be(Expected.Manifest.CorpusDigest);
    }

    [Fact]
    public void Structural_shape_matches_for_every_export()
    {
        // A hash says "this file differs" and nothing more. These per-file structure counts are
        // what turn that into "the duty split changed on this record", and they are aggregate
        // enough to carry no JD content.
        var root = ResolveCorpusDir();
        var expectedById = Expected.Files.ToDictionary(f => f.Id, f => f);
        var problems = new List<string>();

        foreach (var rel in CorpusFiles(root))
        {
            var id = HrtmsCanonical.FileId(rel);
            var want = expectedById[id].Shape!;
            var got = HrtmsParser.Parse(File.ReadAllText(Path.Combine(root, rel)), rel);

            if (got.Responsibilities.Count != want.Responsibilities)
            {
                problems.Add($"  {id} responsibilities: expected {want.Responsibilities}, got {got.Responsibilities.Count}");
            }

            var duties = got.Responsibilities.Select(r => r.Duties.Count).ToList();
            if (!duties.SequenceEqual(want.DutiesPerResponsibility))
            {
                problems.Add($"  {id} duties/responsibility: expected [{string.Join(",", want.DutiesPerResponsibility)}], got [{string.Join(",", duties)}]");
            }

            var pcts = got.Responsibilities.Select(r => r.Pct).ToList();
            if (!pcts.SequenceEqual(want.PctValues))
            {
                problems.Add($"  {id} pct values differ");
            }

            if (got.JobSummary.Length != want.JobSummaryLength)
            {
                problems.Add($"  {id} jobSummary length: expected {want.JobSummaryLength}, got {got.JobSummary.Length}");
            }

            if (got.Pem.Populated != want.PemPopulated)
            {
                problems.Add($"  {id} pem.populated differs");
            }
        }

        problems.Should().BeEmpty("structural drift in {0} places:\n{1}",
            problems.Count, string.Join('\n', problems.Take(20)));
    }

    [Fact]
    public void Corpus_wide_invariants_hold()
    {
        // Facts about the corpus that the schema and the loader depend on. If any of these change,
        // Phase 2's decisions need revisiting — so they are asserted, not assumed.
        var root = ResolveCorpusDir();
        var records = CorpusFiles(root)
            .Select(rel => HrtmsParser.Parse(File.ReadAllText(Path.Combine(root, rel)), rel))
            .ToList();

        records.Should().HaveCount(1367);

        // No PEM grid is marked anywhere, which is why JdPemEntry is empty in practice.
        records.Should().OnlyContain(r => !r.Pem.Populated);

        // Conditions of employment is never populated by this export template.
        records.SelectMany(r => r.ConditionsOfEmployment).Should().BeEmpty();

        // Six JDs do not sum to 100. This is THE reason JdResponsibility.Pct carries no check
        // constraint, so the exact count is pinned.
        var sums = records
            .Where(r => r.Responsibilities.Any(x => x.Pct.HasValue))
            .Select(r => r.Responsibilities.Sum(x => x.Pct ?? 0))
            .ToList();
        sums.Count(s => s == 100).Should().Be(1361);
        sums.Should().Contain(s => s != 100);

        // Every scalar field is populated on every export; a regression here would be a silent
        // data loss rather than an error.
        records.Should().OnlyContain(r =>
            r.UcJobCode.Length > 0 && r.UcJobTitle.Length > 0 && r.DepartmentName.Length > 0
            && r.JobSummary.Length > 0 && r.UnionCode.Length > 0);

        // 60 distinct classes, which is what the profile count is derived from.
        records.Select(r => r.UcJobCode).Distinct().Should().HaveCount(60);

        // Totals that Phase 3's loader sizes its work against.
        records.Sum(r => r.Responsibilities.Count).Should().Be(4443);
        records.Sum(r => r.Responsibilities.Sum(x => x.Duties.Count)).Should().Be(21353);
    }
}

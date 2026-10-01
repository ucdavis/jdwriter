using System.Text.Json;
using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Intake;
using Server.Core.Standards;

namespace Server.Tests.Intake;

/// <summary>
/// Byte-parity of prompt assembly against the POC, from fixtures/prompts.json.
///
/// LLM-dependent code cannot be parity-tested by calling the model — the answer is not
/// deterministic and every call costs money. But the model's answer is not what a port regresses:
/// the STRING WE SEND IT is. A prompt that lost a newline, dropped a separator or reflowed a
/// sentence changes behaviour while every type still compiles and every other test still passes.
///
/// So these assert the exact system prompts, the exact assembled entries and user prompts, and the
/// pure arithmetic the project rule keeps in code rather than asking the model for.
/// </summary>
public class PromptParityTests
{
    // ---- fixture shape

    private sealed class PromptFixture
    {
        public Constants Constants { get; set; } = new();
        public SystemPrompts SystemPrompts { get; set; } = new();
        public Inputs Inputs { get; set; } = new();
        public Entries Entries { get; set; } = new();
        public Dictionary<string, RequestCase> Requests { get; set; } = [];
        public Dictionary<string, string> UserPrompts { get; set; } = [];
        public Computations Computations { get; set; } = new();
    }

    private sealed class Constants
    {
        public int ShortlistTarget { get; set; }
        public int SkipShortlistBelow { get; set; }
        public int MaxResults { get; set; }
        public int ClassifyMaxResults { get; set; }
    }

    private sealed class SystemPrompts
    {
        public string Shortlist { get; set; } = "";
        public string Rank { get; set; } = "";
        public string Distill { get; set; } = "";
        public string ClassifyRank { get; set; } = "";
        public string Compare { get; set; } = "";
    }

    private sealed class Inputs
    {
        public PocProfile WithEnvelope { get; set; } = new();
        public PocProfile NoEnvelope { get; set; } = new();
        public PocStandard Standard { get; set; } = new();
        public PocDistilled Distilled { get; set; } = new();
        public PocDistilled DistilledNoPct { get; set; } = new();
    }

    // The POC's JSON shapes, so the test rebuilds the identical input the generator used.
    private sealed class PocProfile
    {
        public string Slug { get; set; } = "";
        public string UcJobCode { get; set; } = "";
        public string Title { get; set; } = "";
        public string CtJobFamily { get; set; } = "";
        public string CtJobFunction { get; set; } = "";
        public string PersonnelProgram { get; set; } = "";
        public int CorpusSize { get; set; }
        public string RepresentativeSummary { get; set; } = "";
        public PocDist SalaryGrade { get; set; } = new();
        public PocDist Supervises { get; set; } = new();
        public PocEnvelope? Envelope { get; set; }
    }

    private sealed class PocDist
    {
        public JsonElement? Consensus { get; set; }
    }

    private sealed class PocEnvelope
    {
        public string Summary { get; set; } = "";
        public string ScopeStatement { get; set; } = "";
        public List<PocResponsibility> KeyResponsibilities { get; set; } = [];
        public List<string> MinQualifications { get; set; } = [];
    }

    private sealed class PocResponsibility
    {
        public string FunctionName { get; set; } = "";
        public int PctTime { get; set; }
        public List<string> Duties { get; set; } = [];
    }

    private sealed class PocStandard
    {
        public string LongTitle { get; set; } = "";
        public string? Code { get; set; }
        public string PersProg { get; set; } = "";
        public string Grade { get; set; } = "";
        public string Flsa { get; set; } = "";
        public string Union { get; set; } = "";
        public string GenericScope { get; set; } = "";
        public string CustomScope { get; set; } = "";
        public List<string> KeyResponsibilities { get; set; } = [];
        public List<string> Ksa { get; set; } = [];
        public List<string> Education { get; set; } = [];
        public List<string> Licenses { get; set; } = [];
        public List<string> SpecialConditions { get; set; } = [];
    }

    private sealed class PocDistilled
    {
        public string WorkingTitle { get; set; } = "";
        public string Summary { get; set; } = "";
        public List<PocDistilledFn> Functions { get; set; } = [];
        public string Supervises { get; set; } = "";
        public List<string> Education { get; set; } = [];
        public List<string> Experience { get; set; } = [];
        public List<string> Ksas { get; set; } = [];
    }

    private sealed class PocDistilledFn
    {
        public string Name { get; set; } = "";
        public double PctTime { get; set; }
        public List<string> Duties { get; set; } = [];
    }

    private sealed class Entries
    {
        public List<EntryCase> Terse { get; set; } = [];
        public List<EntryCase> Detailed { get; set; } = [];
        public List<EntryCase> LevelFacts { get; set; } = [];
    }

    private sealed class EntryCase
    {
        public int Index { get; set; }
        public string Profile { get; set; } = "";
        public bool Standard { get; set; }
        public string Expected { get; set; } = "";
    }

    private sealed class RequestCase
    {
        public string Distilled { get; set; } = "";
        public string Expected { get; set; } = "";
    }

    private sealed class Computations
    {
        public List<CoverageCase> Coverage { get; set; } = [];
        public List<SplitCase> Split { get; set; } = [];
        public List<JudgeCase> Judge { get; set; } = [];
    }

    private sealed class CoverageCase
    {
        public string Why { get; set; } = "";
        public string Distilled { get; set; } = "";
        public List<int> Matched { get; set; } = [];
        public int Expected { get; set; }
    }

    private sealed class SplitCase
    {
        public string Why { get; set; } = "";
        public string Distilled { get; set; } = "";
        public List<int> Matched { get; set; } = [];
        public SplitExpectation Expected { get; set; } = new();
    }

    private sealed class SplitExpectation
    {
        public List<string> InClass { get; set; } = [];
        public List<string> OutOfClass { get; set; } = [];
    }

    private sealed class JudgeCase
    {
        public string Why { get; set; } = "";
        public List<JudgeMatch> Matches { get; set; } = [];
        public JudgeExpectation Expected { get; set; } = new();
    }

    private sealed class JudgeMatch
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string UcJobCode { get; set; } = "";
        public int Confidence { get; set; }
        public string LevelFit { get; set; } = "";
        public int CoveredPct { get; set; }
    }

    private sealed class JudgeExpectation
    {
        public string Verdict { get; set; } = "";
        public string VerdictNote { get; set; } = "";
    }

    private static readonly PromptFixture Fixture = Fixtures.Load<PromptFixture>("prompts.json");

    // ---- mapping the POC shapes onto the EF entities the C# services take

    private static ClassProfile ToEntity(PocProfile p)
    {
        var entity = new ClassProfile
        {
            Slug = p.Slug,
            UcJobCode = p.UcJobCode,
            Title = p.Title,
            CtJobFamily = p.CtJobFamily,
            CtJobFunction = p.CtJobFunction,
            PersonnelProgram = p.PersonnelProgram,
            CorpusSize = p.CorpusSize,
            RepresentativeSummary = p.RepresentativeSummary,
        };

        void AddDist(DistributionField field, PocDist d)
        {
            // The POC stores a raw JSON value: string for grade, bool-or-null for supervises. The
            // schema stores every consensus as a string, with null meaning no consensus.
            string? consensus = d.Consensus is null || d.Consensus.Value.ValueKind is JsonValueKind.Null
                ? null
                : d.Consensus.Value.ValueKind switch
                {
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => d.Consensus.Value.GetString(),
                };

            entity.Distributions.Add(new ProfileDistribution { Field = field, Consensus = consensus });
        }

        AddDist(DistributionField.SalaryGrade, p.SalaryGrade);
        AddDist(DistributionField.Supervises, p.Supervises);

        if (p.Envelope is not null)
        {
            var env = new JobEnvelope
            {
                Summary = p.Envelope.Summary,
                ScopeStatement = p.Envelope.ScopeStatement,
            };

            for (var i = 0; i < p.Envelope.KeyResponsibilities.Count; i++)
            {
                var r = p.Envelope.KeyResponsibilities[i];
                var resp = new EnvelopeResponsibility
                {
                    Ordinal = i,
                    FunctionName = r.FunctionName,
                    PctTime = r.PctTime,
                };

                for (var j = 0; j < r.Duties.Count; j++)
                {
                    resp.Duties.Add(new EnvelopeDuty { Ordinal = j, Text = r.Duties[j] });
                }

                env.KeyResponsibilities.Add(resp);
            }

            for (var i = 0; i < p.Envelope.MinQualifications.Count; i++)
            {
                env.Items.Add(new EnvelopeListItem
                {
                    Kind = EnvelopeListKind.MinQualification,
                    Ordinal = i,
                    Text = p.Envelope.MinQualifications[i],
                });
            }

            entity.Envelope = env;
        }

        return entity;
    }

    private static ClassStandardRecord ToRecord(PocStandard s) => new()
    {
        LongTitle = s.LongTitle,
        Code = s.Code,
        PersProg = s.PersProg,
        Grade = s.Grade,
        Flsa = s.Flsa,
        Union = s.Union,
        GenericScope = s.GenericScope,
        CustomScope = s.CustomScope,
        KeyResponsibilities = s.KeyResponsibilities,
        Ksa = s.Ksa,
        Education = s.Education,
        Licenses = s.Licenses,
        SpecialConditions = s.SpecialConditions,
    };

    private static DistilledJd ToDistilled(PocDistilled d) => new()
    {
        WorkingTitle = d.WorkingTitle,
        Summary = d.Summary,
        Functions = d.Functions.Select(f => new DistilledFunction
        {
            Name = f.Name,
            PctTime = f.PctTime,
            Duties = f.Duties,
        }).ToList(),
        Supervises = d.Supervises,
        Education = d.Education,
        Experience = d.Experience,
        Ksas = d.Ksas,
    };

    private static ClassProfile Profile(string name) => ToEntity(
        name == "withEnvelope" ? Fixture.Inputs.WithEnvelope : Fixture.Inputs.NoEnvelope);

    /// <summary>
    /// The fixture refers to its two distilled inputs by two different naming schemes: the request
    /// cases use "distilled"/"distilledNoPct", the computation cases use "withPct"/"noPct".
    /// </summary>
    private static DistilledJd Distilled(string name) => ToDistilled(name switch
    {
        "distilled" or "withPct" => Fixture.Inputs.Distilled,
        "distilledNoPct" or "noPct" => Fixture.Inputs.DistilledNoPct,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown distilled input"),
    });

    // ---- constants

    [Fact]
    public void The_tuning_constants_match()
    {
        // Sized for recall. Shrinking the shortlist is the one change that can lose a class
        // outright, so the numbers are pinned rather than left to judgement.
        IntakeMatcher.ShortlistTarget.Should().Be(Fixture.Constants.ShortlistTarget);
        IntakeMatcher.SkipShortlistBelow.Should().Be(Fixture.Constants.SkipShortlistBelow);
        IntakeMatcher.MaxResults.Should().Be(Fixture.Constants.MaxResults);
        DescriptionClassifier.MaxResults.Should().Be(Fixture.Constants.ClassifyMaxResults);
    }

    // ---- system prompts, byte for byte

    [Fact]
    public void The_shortlist_system_prompt_matches_byte_for_byte()
    {
        IntakeMatcher.ShortlistSystem.Should().Be(Fixture.SystemPrompts.Shortlist);
    }

    [Fact]
    public void The_rank_system_prompt_matches_byte_for_byte()
    {
        IntakeMatcher.RankSystem.Should().Be(Fixture.SystemPrompts.Rank);
    }

    [Fact]
    public void The_distill_system_prompt_matches_byte_for_byte()
    {
        DescriptionClassifier.DistillSystem.Should().Be(Fixture.SystemPrompts.Distill);
    }

    [Fact]
    public void The_classify_rank_system_prompt_matches_byte_for_byte()
    {
        DescriptionClassifier.RankSystem.Should().Be(Fixture.SystemPrompts.ClassifyRank);
    }

    [Fact]
    public void The_compare_system_prompt_matches_byte_for_byte()
    {
        DescriptionClassifier.CompareSystem.Should().Be(Fixture.SystemPrompts.Compare);
    }

    [Fact]
    public void The_shortlist_prompt_states_the_actual_target()
    {
        // The reference interpolates SHORTLIST_TARGET into the prompt. If the constant and the text
        // ever drift, the model is told a number the code does not enforce.
        IntakeMatcher.ShortlistSystem.Should().Contain($"Return up to {IntakeMatcher.ShortlistTarget} class numbers");
    }

    // ---- prompt hashes
    //
    // Byte equality against the fixture (above) is the primary guard. These SHA-256s are a second,
    // independent one, added after DescriptionClassifier.cs was destroyed by a write collision and
    // had to be RECONSTRUCTED. A reconstructed prompt that merely looks right is exactly the failure
    // that survives review and silently changes model behaviour, so the hashes are pinned to values
    // taken from the POC's TypeScript literals rather than from anything reconstructed.

    private static string Sha256(string s) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(s)));

    [Theory]
    // Hashed directly from src/lib/intake/match.ts and src/lib/intake/classify.ts.
    [InlineData("SHORTLIST_SYSTEM", "11eb0970d44b08a9d9978fefa6609f7cf01de85098d77d8e9127e0b2e44594eb")]
    [InlineData("RANK_SYSTEM", "0300cfe2f69a2d260b8a38dd1ea141ac1c46251fa4484ef358b5062cdf849dc5")]
    [InlineData("DISTILL_SYSTEM", "3fb7b9a7f02bc51f29d137b7ea91a175b4d7066b9abf6474ba588f502ac59c61")]
    [InlineData("CLASSIFY_RANK_SYSTEM", "33d4ebf3d3798d77d94a56420b46fabd39544538ac1ad69646da5c50c351a32f")]
    [InlineData("COMPARE_SYSTEM", "f6cac2a8ef004ed2d5c8a7cdb91d5e14f98d3482dde2d785b756d83669fa356d")]
    public void System_prompts_hash_to_the_reference_values(string name, string expected)
    {
        var actual = name switch
        {
            "SHORTLIST_SYSTEM" => IntakeMatcher.ShortlistSystem,
            "RANK_SYSTEM" => IntakeMatcher.RankSystem,
            "DISTILL_SYSTEM" => DescriptionClassifier.DistillSystem,
            "CLASSIFY_RANK_SYSTEM" => DescriptionClassifier.RankSystem,
            "COMPARE_SYSTEM" => DescriptionClassifier.CompareSystem,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        Sha256(actual).Should().Be(expected,
            "{0} must be byte-identical to the POC literal — regenerate the hash only when the " +
            "reference itself changes, never to make a port compile", name);
    }

    [Fact]
    public void The_hashed_prompts_are_the_same_strings_the_fixture_holds()
    {
        // Ties the two guards together: if the fixture were regenerated from a drifted POC, the
        // hashes above would still pass while the fixture silently moved. This fails if they
        // disagree about what the reference says.
        Sha256(Fixture.SystemPrompts.Shortlist).Should().Be(Sha256(IntakeMatcher.ShortlistSystem));
        Sha256(Fixture.SystemPrompts.Rank).Should().Be(Sha256(IntakeMatcher.RankSystem));
        Sha256(Fixture.SystemPrompts.Distill).Should().Be(Sha256(DescriptionClassifier.DistillSystem));
        Sha256(Fixture.SystemPrompts.ClassifyRank).Should().Be(Sha256(DescriptionClassifier.RankSystem));
        Sha256(Fixture.SystemPrompts.Compare).Should().Be(Sha256(DescriptionClassifier.CompareSystem));
    }

    // ---- entry assembly

    [Fact]
    public void Terse_entries_match()
    {
        foreach (var c in Fixture.Entries.Terse)
        {
            IntakeMatcher.TerseEntry(Profile(c.Profile), c.Index)
                .Should().Be(c.Expected, "terse entry for {0} at {1}", c.Profile, c.Index);
        }
    }

    [Fact]
    public void Detailed_entries_match_with_and_without_a_standard()
    {
        // Covers all three combinations the fixture captures, including a profile with no envelope
        // and no Career Tracks function, which the reference renders as "(code , )" — a port that
        // "helpfully" tidies that up diverges.
        foreach (var c in Fixture.Entries.Detailed)
        {
            var std = c.Standard ? ToRecord(Fixture.Inputs.Standard) : null;
            IntakeMatcher.DetailedEntry(Profile(c.Profile), c.Index, std)
                .Should().Be(c.Expected, "detailed entry for {0} (standard: {1})", c.Profile, c.Standard);
        }
    }

    [Fact]
    public void Level_facts_match()
    {
        foreach (var c in Fixture.Entries.LevelFacts)
        {
            DescriptionClassifier.LevelFacts(Profile(c.Profile))
                .Should().Be(c.Expected, "level facts for {0}", c.Profile);
        }
    }

    [Fact]
    public void Request_rendering_matches_in_both_terse_and_full_form()
    {
        foreach (var (name, c) in Fixture.Requests)
        {
            var terse = name.StartsWith("terse", StringComparison.Ordinal);
            DescriptionClassifier.AsRequest(Distilled(c.Distilled), terse)
                .Should().Be(c.Expected, "request '{0}'", name);
        }
    }

    // ---- assembled user prompts

    [Fact]
    public void The_shortlist_user_prompt_matches()
    {
        var profiles = new List<ClassProfile> { Profile("withEnvelope"), Profile("noEnvelope") };
        var request = DescriptionClassifier.AsRequest(Distilled("distilled"), terse: true);

        IntakeMatcher.BuildShortlistUser(request, profiles)
            .Should().Be(Fixture.UserPrompts["shortlist"]);
    }

    [Fact]
    public void The_rank_user_prompt_matches()
    {
        var profiles = new List<ClassProfile> { Profile("withEnvelope"), Profile("noEnvelope") };

        // Only the first profile's title resolves to a standard in the fixture.
        var standards = new StandardsIndex([ToRecord(Fixture.Inputs.Standard)]);

        IntakeMatcher.BuildRankUser("Someone to run our survey programme", profiles, standards)
            .Should().Be(Fixture.UserPrompts["rank"]);
    }

    [Fact]
    public void The_classify_rank_user_prompt_matches()
    {
        var profiles = new List<ClassProfile> { Profile("withEnvelope"), Profile("noEnvelope") };
        var standards = new StandardsIndex([ToRecord(Fixture.Inputs.Standard)]);
        var distilled = Distilled("distilled");

        DescriptionClassifier.BuildRankUser(
                distilled, DescriptionClassifier.AsRequest(distilled), profiles, standards)
            .Should().Be(Fixture.UserPrompts["classifyRank"]);
    }

    // ---- pure computation

    [Fact]
    public void Coverage_matches()
    {
        // Recomputed from the model's own mapping rather than asked for. Weighted by stated % time,
        // or by function count when the source states none — which is common in hand-written JDs.
        foreach (var c in Fixture.Computations.Coverage)
        {
            DescriptionClassifier.Coverage(Distilled(c.Distilled), c.Matched)
                .Should().Be(c.Expected, "{0}", c.Why);
        }
    }

    [Fact]
    public void Split_matches_and_always_partitions_the_functions()
    {
        foreach (var c in Fixture.Computations.Split)
        {
            var d = Distilled(c.Distilled);
            var (inClass, outOfClass) = DescriptionClassifier.Split(d, c.Matched);

            inClass.Should().Equal(c.Expected.InClass, "{0}", c.Why);
            outOfClass.Should().Equal(c.Expected.OutOfClass, "{0}", c.Why);

            // The displayed split is derived here precisely so the two lists always partition the
            // functions and always agree with the coverage number above them.
            (inClass.Count + outOfClass.Count).Should().Be(d.Functions.Count, "{0}", c.Why);
        }
    }

    [Fact]
    public void Verdicts_match()
    {
        // The verdict is what an HR reviewer acts on, so it is decided by inspectable rules rather
        // than by the model.
        foreach (var c in Fixture.Computations.Judge)
        {
            var matches = c.Matches.Select(m => new ClassificationMatch
            {
                Slug = m.Slug,
                Title = m.Title,
                UcJobCode = m.UcJobCode,
                Confidence = m.Confidence,
                LevelFit = m.LevelFit,
                CoveredPct = m.CoveredPct,
            }).ToList();

            var (verdict, note) = DescriptionClassifier.Judge(matches);
            verdict.Should().Be(c.Expected.Verdict, "{0}", c.Why);
            note.Should().Be(c.Expected.VerdictNote, "{0}", c.Why);
        }
    }
}

using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Jd;
using Server.Core.Profiles;

namespace Server.Tests.Jd;

/// <summary>
/// Assembly driven through a fake model, including deliberately hostile responses.
///
/// The division of labour is what is under test: the model writes the summary, polishes duties, and
/// judges duplication; everything else is assembled in code so it cannot drift. These tests mostly
/// check that a misbehaving model CANNOT corrupt the parts that are ours — because a dropped duty
/// or a renamed function is still schema-valid, so nothing else would catch it.
/// </summary>
public class JdAssemblerTests
{
    /// <summary>The shapes the fake has to return. Mirrors the assembler's private response types.</summary>
    private sealed class Gen
    {
        public string JobSummary { get; set; } = "";
        public List<GenFn> Functions { get; set; } = [];
        public List<GenDup> RemoveDuplicates { get; set; } = [];
    }

    private sealed class GenFn
    {
        public int Index { get; set; }
        public List<string> Duties { get; set; } = [];
    }

    private sealed class GenDup
    {
        public int Index { get; set; }
        public string Reason { get; set; } = "";
    }

    private sealed class EditsDto
    {
        public List<EditLine> Edits { get; set; } = [];
    }

    private sealed class EditLine
    {
        public int Index { get; set; }
        public string After { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    private sealed class VerdictDto
    {
        public EnvelopeVerdict Verdict { get; set; }
        public List<string> MatchedSignals { get; set; } = [];
        public string Rationale { get; set; } = "";
    }

    private sealed class AltDto
    {
        public int Index { get; set; }
        public string FallbackClass { get; set; } = "";
    }

    private sealed class BetterFitDto
    {
        public int Index { get; set; }
        public string Rationale { get; set; } = "";
    }

    /// <summary>
    /// The fake is generic over the requested type, so responses are registered as the anonymous
    /// shapes the assembler's private classes deserialize from. Reflection bridges the two: the
    /// assembler asks for its own private type, and this returns a structurally identical object.
    /// </summary>
    private static object AsAssemblerType(Type target, object source)
    {
        var result = Activator.CreateInstance(target)!;
        foreach (var targetProp in target.GetProperties())
        {
            var sourceProp = source.GetType().GetProperty(targetProp.Name);
            if (sourceProp is null)
            {
                continue;
            }

            targetProp.SetValue(result, Convert(targetProp.PropertyType, sourceProp.GetValue(source)));
        }

        return result;
    }

    private static object? Convert(Type targetType, object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (targetType.IsAssignableFrom(value.GetType()))
        {
            return value;
        }

        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
        {
            var itemType = targetType.GetGenericArguments()[0];
            var list = (System.Collections.IList)Activator.CreateInstance(targetType)!;
            foreach (var item in (System.Collections.IEnumerable)value)
            {
                list.Add(itemType.IsPrimitive || itemType == typeof(string)
                    ? item
                    : AsAssemblerType(itemType, item!));
            }

            return list;
        }

        return value;
    }

    private static FakeStructuredLlm Llm(Gen? gen = null, EditsDto? edits = null,
        VerdictDto? verdict = null, AltDto? alt = null, BetterFitDto? better = null)
    {
        var fake = new FakeStructuredLlm();

        if (gen is not null)
        {
            fake.Respond("build.generateJd", _ => gen);
        }

        fake.Respond("build.compliance", _ => edits ?? new EditsDto());

        if (verdict is not null)
        {
            fake.Respond("build.checkEnvelope", _ => verdict);
        }

        if (alt is not null)
        {
            fake.Respond("build.checkAlternative", _ => alt);
        }

        if (better is not null)
        {
            fake.Respond("build.betterFit", _ => better);
        }

        return fake;
    }

    // Registering typed responses through the fake requires the assembler's private types, which
    // tests cannot name. The fake is therefore wrapped to translate on the way out.
    private sealed class TranslatingLlm : Server.Core.Ai.IStructuredLlm
    {
        private readonly FakeStructuredLlm _inner;

        public TranslatingLlm(FakeStructuredLlm inner) => _inner = inner;

        public bool HasApiKey => true;
        public List<Server.Core.Ai.StructuredRequest> Requests => _inner.Requests;

        public async Task<T> StructuredAsync<T>(
            Server.Core.Ai.StructuredRequest request, CancellationToken ct = default)
        {
            var raw = await _inner.StructuredAsync<object>(request, ct);
            return (T)AsAssemblerType(typeof(T), raw);
        }
    }

    private static (JdAssembler Assembler, TranslatingLlm Llm) Build(
        Gen? gen = null, EditsDto? edits = null, VerdictDto? verdict = null, AltDto? alt = null,
        BetterFitDto? better = null)
    {
        var translating = new TranslatingLlm(Llm(gen, edits, verdict, alt, better));
        return (new JdAssembler(translating), translating);
    }

    private static Gen CleanGeneration() => new()
    {
        JobSummary = "Analyses study data and reports findings to investigators.",
        Functions =
        [
            new GenFn { Index = 0, Duties = ["Cleans and validates study datasets.", "Runs statistical analyses."] },
            new GenFn { Index = 1, Duties = ["Prepares written summaries of findings."] },
        ],
        RemoveDuplicates = [],
    };

    // ------------------------------------------------------------------ no changes, no model

    /// <summary>Exactly what the test envelope offers — nothing added, dropped, moved or noted.</summary>
    private static BuildInputs Unchanged()
    {
        var e = EnvelopeWire.From(JdTestData.Profile().Envelope!);
        return new BuildInputs
        {
            WorkingTitle = "Evaluation Analyst",
            Department = "Office of Evaluation",
            KeptResponsibilities = [.. e.KeyResponsibilities],
            KeptCerts = e.RequiredCertifications,
            KeptEducation = e.Education,
            KeptWorkExperience = e.WorkExperience,
            KeptMinKSA = e.MinQualifications,
            KeptPrefKSA = e.PrefQualifications,
            KeptWorkEnvironment = e.WorkEnvironment,
        };
    }

    [Fact]
    public async Task An_unchanged_build_is_assembled_from_the_envelope_with_no_model_call()
    {
        var (assembler, llm) = Build(CleanGeneration());

        var result = await assembler.AssembleAsync(JdTestData.Profile(), Unchanged(), JdTestData.Rules());

        llm.Requests.Should().BeEmpty("nothing changed, so there is nothing to generate or review");
        result.FromEnvelope.Should().BeTrue();
        result.Jd.JobSummary.Should().Be(JdTestData.Profile().Envelope!.Summary);
        result.Jd.KeyResponsibilities.Select(r => (r.FunctionName, r.PctTime))
            .Should().Equal(("DATA ANALYSIS", 60), ("REPORTING", 40));
        result.Jd.ConditionsOfEmployment.Should().Equal("Background check required.");
        result.CanPublish.Should().BeTrue();
    }

    [Fact]
    public async Task The_deterministic_compliance_rules_still_apply_to_an_unchanged_build()
    {
        // Rules are HR policy, applied to every JD; only the MODEL review is skipped.
        var profile = JdTestData.Profile();
        profile.Envelope!.KeyResponsibilities[0].Duties[0].Text = "Must be able to lift 25 pounds.";
        var inputs = Unchanged();
        inputs.KeptResponsibilities = [.. EnvelopeWire.From(profile.Envelope).KeyResponsibilities];
        var rules = new List<ComplianceRule>
        {
            new() { Pattern = "Must be able to", Replacement = "Is able to", Reason = "Ability, not requirement, phrasing." },
        };
        var (assembler, llm) = Build(CleanGeneration());

        var result = await assembler.AssembleAsync(profile, inputs, rules);

        llm.Requests.Should().BeEmpty();
        result.ComplianceEdits.Should().ContainSingle(e => e.Source == ComplianceEditSource.Rule);
    }

    [Theory]
    [InlineData("notes")]
    [InlineData("dropped duty")]
    [InlineData("added item")]
    public async Task Any_change_or_note_takes_the_model_path(string change)
    {
        var inputs = Unchanged();
        switch (change)
        {
            case "notes": inputs.Notes = "Emphasise survey design."; break;
            case "dropped duty": inputs.KeptResponsibilities[0].Duties = ["Runs statistical analyses."]; break;
            case "added item": inputs.AddedItems = ["Coordinates the annual evaluation symposium."]; break;
        }

        var (assembler, llm) = Build(CleanGeneration());

        var result = await assembler.AssembleAsync(JdTestData.Profile(), inputs, JdTestData.Rules());

        result.FromEnvelope.Should().BeFalse();
        llm.Requests.Select(r => r.Label).Should().Contain("build.generateJd");
    }

    // ------------------------------------------------------------------ percentages

    [Fact]
    public async Task Dropping_a_responsibility_leaves_the_kept_percentages_untouched()
    {
        // The author dropped a standard responsibility, so the kept shares sum to 70. They are
        // carried through EXACTLY as set — 50 stays 50.
        //
        // Rescaling was the obvious move and is wrong: publishing a kept 50% as 71% silently moves
        // a number the author chose, and nobody is told. The shortfall is surfaced instead, and the
        // author reallocates it.
        var inputs = JdTestData.Inputs();
        inputs.KeptResponsibilities[0].PctTime = 50;
        inputs.KeptResponsibilities[1].PctTime = 20;

        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(JdTestData.Profile(), inputs, JdTestData.Rules());

        result.Jd.KeyResponsibilities.Select(r => r.PctTime).Should().Equal(50, 20);
        result.UnallocatedPct.Should().Be(30);
        result.CanPublish.Should().BeFalse();
    }

    [Fact]
    public async Task A_fully_allocated_build_reports_no_gap_and_may_publish()
    {
        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        result.Jd.KeyResponsibilities.Select(r => r.PctTime).Should().Equal(60, 40);
        result.UnallocatedPct.Should().Be(0);
        result.CanPublish.Should().BeTrue();
    }

    [Fact]
    public async Task Over_allocating_past_100_is_reported_as_a_negative_gap()
    {
        // Equally unpublishable, and the sign is what tells the author which way to move.
        var inputs = JdTestData.Inputs();
        inputs.KeptResponsibilities[0].PctTime = 70;
        inputs.KeptResponsibilities[1].PctTime = 40;

        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(JdTestData.Profile(), inputs, JdTestData.Rules());

        result.Jd.KeyResponsibilities.Select(r => r.PctTime).Should().Equal(70, 40);
        result.UnallocatedPct.Should().Be(-10);
        result.CanPublish.Should().BeFalse();
    }

    [Fact]
    public async Task All_zero_shares_are_left_alone_rather_than_split_evenly()
    {
        // An even split would invent an allocation the author never made — the same silent-invention
        // problem as rescaling, just less obvious.
        var inputs = JdTestData.Inputs();
        inputs.KeptResponsibilities[0].PctTime = 0;
        inputs.KeptResponsibilities[1].PctTime = 0;

        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(JdTestData.Profile(), inputs, JdTestData.Rules());

        result.Jd.KeyResponsibilities.Select(r => r.PctTime).Should().Equal(0, 0);
        result.UnallocatedPct.Should().Be(100);
        result.CanPublish.Should().BeFalse();
    }

    [Theory]
    [InlineData(new[] { 60, 40 }, 0)]
    [InlineData(new[] { 50, 20 }, 30)]
    [InlineData(new[] { 100 }, 0)]
    [InlineData(new[] { 0, 0 }, 100)]
    [InlineData(new[] { 70, 40 }, -10)]
    [InlineData(new int[0], 100)]
    public void The_gap_is_one_hundred_minus_the_kept_shares(int[] shares, int expectedGap)
    {
        var responsibilities = shares
            .Select(p => new JdKeyResponsibility { FunctionName = "F", PctTime = p, Duties = ["d"] })
            .ToList();

        JdAssembler.UnallocatedPct(responsibilities).Should().Be(expectedGap);
    }

    [Fact]
    public async Task An_under_allocated_draft_is_saved_only_as_a_draft()
    {
        // Drafts are kept (they are what gets fed back into the corpus), so the 100% invariant
        // lives in the status: Ready is derived from the allocation at the persistence boundary
        // and nowhere else, and nothing is rescaled to reach it.
        var inputs = JdTestData.Inputs();
        inputs.KeptResponsibilities[1].PctTime = 10;

        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(JdTestData.Profile(), inputs, JdTestData.Rules());

        var entity = result.ToEntity(classProfileId: 1, inputs: inputs);

        entity.Status.Should().Be(AuthoredJdStatus.Draft);
        entity.UnallocatedPct.Should().Be(30);
        entity.KeyResponsibilities.Sum(r => r.PctTime).Should().Be(70, "the author's numbers are kept, not rescaled");
    }

    [Fact]
    public async Task An_over_allocated_draft_is_saved_only_as_a_draft_too()
    {
        var inputs = JdTestData.Inputs();
        inputs.KeptResponsibilities[0].PctTime = 80;

        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(JdTestData.Profile(), inputs, JdTestData.Rules());

        var entity = result.ToEntity(classProfileId: 1, inputs: inputs);

        entity.Status.Should().Be(AuthoredJdStatus.Draft);
        entity.UnallocatedPct.Should().Be(-20);
    }

    [Fact]
    public async Task A_fully_allocated_jd_is_ready_and_keeps_what_the_author_added()
    {
        var inputs = JdTestData.Inputs();
        inputs.AddedItems = ["Coordinates the unit's annual symposium"];
        inputs.Notes = "Replaces a retiring incumbent.";

        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(JdTestData.Profile(), inputs, JdTestData.Rules());

        var entity = result.ToEntity(classProfileId: 1, inputs: inputs, envelopeSource: EnvelopeSource.Manual);

        entity.Status.Should().Be(AuthoredJdStatus.Ready);
        entity.UnallocatedPct.Should().Be(0);
        entity.Notes.Should().Be("Replaces a retiring incumbent.");
        entity.EnvelopeSource.Should().Be(EnvelopeSource.Manual);
        entity.Items.Where(i => i.Kind == AuthoredJdListKind.AuthorAddition).Select(i => i.Text)
            .Should().Equal("Coordinates the unit's annual symposium");
    }

    [Fact]
    public async Task An_unpublishable_draft_is_still_returned_so_the_author_can_fix_it()
    {
        // Assembly succeeds and only persistence refuses — the author has to SEE the work in order
        // to reallocate it.
        var inputs = JdTestData.Inputs();
        inputs.KeptResponsibilities[1].PctTime = 10;

        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(JdTestData.Profile(), inputs, JdTestData.Rules());

        result.CanPublish.Should().BeFalse();
        result.Jd.KeyResponsibilities.Should().HaveCount(2);
        result.Jd.JobSummary.Should().NotBeEmpty();
        result.Jd.Education.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Responsibilities_are_ordered_by_share_of_time()
    {
        var inputs = JdTestData.Inputs();
        inputs.KeptResponsibilities[0].PctTime = 30;
        inputs.KeptResponsibilities[1].PctTime = 70;

        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(JdTestData.Profile(), inputs, JdTestData.Rules());

        result.Jd.KeyResponsibilities.Select(r => r.FunctionName).Should().Equal("REPORTING", "DATA ANALYSIS");
        result.Jd.KeyResponsibilities.Select(r => r.PctTime).Should().Equal(70, 30);
    }

    // ------------------------------------------------------------------ what the model may not touch

    [Fact]
    public async Task Function_names_come_from_the_manager_not_the_model()
    {
        // The model is given function names for context but returns only duties keyed by index, so
        // it has no way to rename one. This asserts the assembler does not somehow adopt one.
        var (assembler, _) = await Task.FromResult(Build(CleanGeneration()));
        var result = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        result.Jd.KeyResponsibilities.Select(r => r.FunctionName)
            .Should().Equal("DATA ANALYSIS", "REPORTING");
    }

    [Fact]
    public async Task A_skipped_function_falls_back_to_the_managers_own_bullets()
    {
        // The model returned nothing for function 1. Silently emitting an empty responsibility
        // would lose the manager's work.
        var gen = CleanGeneration();
        gen.Functions = [new GenFn { Index = 0, Duties = ["Polished analysis duty."] }];

        var (assembler, _) = Build(gen);
        var result = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        var reporting = result.Jd.KeyResponsibilities.Single(r => r.FunctionName == "REPORTING");
        reporting.Duties.Should().Equal("Prepares written summaries of findings.");
    }

    [Fact]
    public async Task An_all_blank_duty_list_falls_back_rather_than_emptying_the_function()
    {
        var gen = CleanGeneration();
        gen.Functions = [new GenFn { Index = 1, Duties = ["   ", ""] }];

        var (assembler, _) = Build(gen);
        var result = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        result.Jd.KeyResponsibilities.Single(r => r.FunctionName == "REPORTING")
            .Duties.Should().Equal("Prepares written summaries of findings.");
    }

    [Fact]
    public async Task Qualification_sections_are_assembled_in_code()
    {
        // These are a straight copy of what the manager kept. The model used to retype them, which
        // is exactly how a KSA gets silently reworded.
        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        result.Jd.Education.Should().Equal("Bachelor's degree or equivalent experience.");
        result.Jd.WorkExperience.Should().Equal("Two years of related experience.");
        result.Jd.MinKSA.Should().Equal("Working knowledge of statistical methods.");
        result.Jd.WorkEnvironment.Should().Equal("Standard office environment.");
        result.Jd.LicensesCertifications.Should().BeEmpty();
        result.Jd.PrefKSA.Should().BeEmpty();
    }

    [Fact]
    public async Task Boilerplate_sections_come_from_the_envelope_not_the_model()
    {
        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        result.Jd.ConditionsOfEmployment.Should().Equal("Background check required.");
        result.Jd.PhysicalRequirements.Should().Equal(
            "Work is sedentary, with reasonable accommodation available.");
    }

    [Fact]
    public async Task A_duplicate_qualification_is_removed_by_index()
    {
        var gen = CleanGeneration();
        // Item 2 is the minKSA, which restates the analysis duty.
        gen.RemoveDuplicates = [new GenDup { Index = 2, Reason = "Restates the analysis duty." }];

        var (assembler, _) = Build(gen);
        var result = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        result.Jd.MinKSA.Should().BeEmpty();
        // Removing one item must not disturb the others.
        result.Jd.Education.Should().Equal(["Bachelor's degree or equivalent experience."]);
    }

    [Fact]
    public async Task An_out_of_range_duplicate_index_is_ignored()
    {
        var gen = CleanGeneration();
        gen.RemoveDuplicates = [new GenDup { Index = 99, Reason = "nonsense" }, new GenDup { Index = -1, Reason = "nonsense" }];

        var (assembler, _) = Build(gen);
        var result = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        result.Jd.Education.Should().HaveCount(1);
        result.Jd.MinKSA.Should().HaveCount(1);
        result.Jd.WorkExperience.Should().HaveCount(1);
        result.Jd.WorkEnvironment.Should().HaveCount(1);
    }

    // ------------------------------------------------------------------ compliance ordering

    [Fact]
    public async Task Deterministic_rules_run_before_the_model()
    {
        // Cheap, reproducible, auditable changes should not cost tokens, and the trail should
        // attribute them to a rule.
        var gen = CleanGeneration();
        gen.JobSummary = "Seeking a rockstar analyst.";

        var (assembler, llm) = Build(gen);
        var result = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        result.Jd.JobSummary.Should().Be("Seeking a expert analyst.");
        result.ComplianceEdits.Should().ContainSingle();
        result.ComplianceEdits[0].Source.Should().Be(ComplianceEditSource.Rule);

        // The model's compliance pass saw the ALREADY-CORRECTED text.
        llm.Requests.Single(r => r.Label == "build.compliance").User
            .Should().Contain("Seeking a expert analyst.")
            .And.NotContain("rockstar");
    }

    [Fact]
    public async Task Rule_edits_precede_model_edits_in_the_audit_trail()
    {
        var gen = CleanGeneration();
        gen.JobSummary = "Seeking a rockstar analyst.";

        var edits = new EditsDto
        {
            Edits = [new EditLine { Index = 0, After = "Seeking an expert analyst.", Reason = "Grammar" }],
        };

        var (assembler, _) = Build(gen, edits);
        var result = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        result.ComplianceEdits.Select(e => e.Source).Should().Equal(
            ComplianceEditSource.Rule, ComplianceEditSource.Llm);
        result.Jd.JobSummary.Should().Be("Seeking an expert analyst.");
    }

    [Fact]
    public async Task A_clean_draft_produces_an_empty_audit_trail()
    {
        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        result.ComplianceEdits.Should().BeEmpty();
    }

    [Fact]
    public async Task The_compliance_pass_numbers_every_line_it_shows_the_model()
    {
        var (assembler, llm) = Build(CleanGeneration());
        await assembler.AssembleAsync(JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        var prompt = llm.Requests.Single(r => r.Label == "build.compliance").User;
        prompt.Should().StartWith("Draft JD lines:\n0: ");
        prompt.Should().Contain("\n1: DATA ANALYSIS");
    }

    // ------------------------------------------------------------------ fixed attributes

    [Fact]
    public async Task Fixed_attributes_are_copied_from_the_class_consensus()
    {
        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        result.Slug.Should().Be("006256-rsch-data-anl-2");
        result.Title.Should().Be("Rsch Data Anl 2");
        result.WorkingTitle.Should().Be("Evaluation Analyst");
        result.Department.Should().Be("Office of Evaluation");
        result.UcJobCode.Should().Be("006256");
        result.SalaryGrade.Should().Be("Grade 21");
        result.FlsaStatus.Should().Be("Exempt");
        result.BargainingUnit.Should().Be("99 - Non-Represented (PPSM)");
    }

    [Fact]
    public async Task A_class_with_no_consensus_leaves_fixed_attributes_null()
    {
        // Blank beats invented: a class bootstrapped from a standard with no corpus has nothing
        // authoritative to copy.
        var profile = JdTestData.Profile();
        profile.Distributions.Clear();

        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(profile, JdTestData.Inputs(), JdTestData.Rules());

        result.SalaryGrade.Should().BeNull();
        result.FlsaStatus.Should().BeNull();
        result.BargainingUnit.Should().BeNull();
    }

    [Fact]
    public async Task A_blank_working_title_falls_back_to_the_class_title()
    {
        var inputs = JdTestData.Inputs();
        inputs.WorkingTitle = "";

        var (assembler, _) = Build(CleanGeneration());
        var result = await assembler.AssembleAsync(JdTestData.Profile(), inputs, JdTestData.Rules());

        result.WorkingTitle.Should().Be("Rsch Data Anl 2");
    }

    // ------------------------------------------------------------------ envelope check

    [Fact]
    public async Task An_in_envelope_verdict_never_pays_for_the_catalog()
    {
        // Stage 2 costs roughly ten thousand tokens to usually name the class the manager is
        // already in. Most builds are in_envelope, so most must not pay it.
        var (assembler, llm) = Build(verdict: new VerdictDto
        {
            Verdict = EnvelopeVerdict.InEnvelope,
            MatchedSignals = [],
            Rationale = "The additions are routine analysis work.",
        });

        var others = new List<ClassDescriptor>
        {
            new() { Slug = "x", Title = "Other Class", UcJobCode = "111111", Summary = "s" },
        };

        var result = await assembler.CheckEnvelopeAsync(JdTestData.Profile(), JdTestData.Inputs(), others);

        result.Verdict.Should().Be(EnvelopeVerdict.InEnvelope);
        result.SuggestedClass.Should().BeEmpty();
        result.SuggestedSlug.Should().BeEmpty();
        llm.Requests.Should().ContainSingle();
        llm.Requests.Should().NotContain(r => r.Label == "build.checkAlternative");
    }

    private static readonly VerdictDto Stretch = new()
    {
        Verdict = EnvelopeVerdict.Borderline,
        MatchedSignals = ["Managing a budget."],
        Rationale = "Mild stretch.",
    };

    private static List<ClassDescriptor> Catalog() =>
    [
        new() { Slug = "x", Title = "Other Class", UcJobCode = "111111", Summary = "Other work." },
        new() { Slug = "y", Title = "Budget Analyst 2", UcJobCode = "222222", Summary = "Budgets." },
    ];

    [Fact]
    public async Task Borderline_also_suggests_a_better_fit_without_changing_the_verdict()
    {
        // Borderline still fits, so the manager is not routed away. But the product decision
        // (2026-10-08) is to show what else might fit: a second query, asked as "is anything clearly
        // better?" rather than the out-of-envelope "where should this go?".
        var (assembler, llm) = Build(verdict: Stretch, better: new BetterFitDto { Index = 1, Rationale = "Budget work dominates." });

        var result = await assembler.CheckEnvelopeAsync(JdTestData.Profile(), JdTestData.Inputs(), Catalog());

        result.Verdict.Should().Be(EnvelopeVerdict.Borderline);
        result.SuggestedSlug.Should().Be("y");
        result.SuggestedClass.Should().Be("Budget Analyst 2");
        result.SuggestedRationale.Should().Be("Budget work dominates.");
        result.BetterFitChecked.Should().BeTrue();
        llm.Requests.Select(r => r.Label).Should().Equal("build.checkEnvelope", "build.betterFit");
    }

    [Fact]
    public async Task Borderline_says_so_when_the_current_class_is_still_the_best_fit()
    {
        var (assembler, _) = Build(verdict: Stretch, better: new BetterFitDto { Index = -1, Rationale = "Nothing fits better." });

        var result = await assembler.CheckEnvelopeAsync(JdTestData.Profile(), JdTestData.Inputs(), Catalog());

        result.SuggestedSlug.Should().BeEmpty();
        result.SuggestedClass.Should().BeEmpty();
        result.BetterFitChecked.Should().BeTrue("an empty suggestion is an answer, not a search that never ran");
    }

    [Fact]
    public async Task A_better_fit_index_outside_the_catalog_is_ignored()
    {
        var (assembler, _) = Build(better: new BetterFitDto { Index = 7, Rationale = "?" });

        var result = await assembler.FindBetterFitAsync(JdTestData.Profile(), JdTestData.Inputs(), Catalog());

        result.SuggestedSlug.Should().BeEmpty();
    }

    [Fact]
    public async Task The_better_fit_search_sees_the_whole_position_and_the_catalog()
    {
        var (assembler, llm) = Build(better: new BetterFitDto { Index = -1, Rationale = "Fine." });
        var inputs = JdTestData.Inputs();

        await assembler.FindBetterFitAsync(JdTestData.Profile(), inputs, Catalog());

        var request = llm.Requests.Single();
        request.System.Should().Be(JdAssembler.BetterFitSystem);
        request.User.Should().Contain($"CURRENT CLASS: {JdTestData.Profile().Title}")
            .And.Contain($"{inputs.KeptResponsibilities[0].PctTime}% {inputs.KeptResponsibilities[0].FunctionName}")
            .And.Contain("1: Budget Analyst 2 (code 222222) — Budgets.");
    }

    [Fact]
    public async Task With_no_other_classes_there_is_nothing_to_ask()
    {
        var (assembler, llm) = Build();

        var result = await assembler.FindBetterFitAsync(JdTestData.Profile(), JdTestData.Inputs(), []);

        result.SuggestedSlug.Should().BeEmpty();
        llm.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Out_of_envelope_resolves_the_chosen_index_to_a_slug()
    {
        // The model returns an INDEX and never handles slug strings — a fluffed slug would become a
        // silently dropped result with no signal.
        var (assembler, llm) = Build(
            verdict: new VerdictDto
            {
                Verdict = EnvelopeVerdict.OutOfEnvelope,
                MatchedSignals = ["Supervising career staff."],
                Rationale = "The additions add supervision.",
            },
            alt: new AltDto { Index = 1, FallbackClass = "" });

        var others = new List<ClassDescriptor>
        {
            new() { Slug = "aaa", Title = "Wrong Class", UcJobCode = "111111", Summary = "s" },
            new() { Slug = "006257-rsch-data-anl-3", Title = "Rsch Data Anl 3", UcJobCode = "006257", Summary = "s" },
        };

        var result = await assembler.CheckEnvelopeAsync(JdTestData.Profile(), JdTestData.Inputs(), others);

        result.SuggestedClass.Should().Be("Rsch Data Anl 3");
        result.SuggestedSlug.Should().Be("006257-rsch-data-anl-3");

        // The catalog prompt carries the reason it left the class, so stage 2 is not guessing.
        var altPrompt = llm.Requests.Single(r => r.Label == "build.checkAlternative").User;
        altPrompt.Should().Contain("WHY IT LEFT THE CLASS: The additions add supervision.");
        altPrompt.Should().Contain("SIGNALS TRIGGERED: Supervising career staff.");
        altPrompt.Should().Contain("1: Rsch Data Anl 3 (code 006257)");
    }

    [Fact]
    public async Task An_index_of_minus_one_uses_the_named_fallback_with_no_slug()
    {
        var (assembler, _) = Build(
            verdict: new VerdictDto { Verdict = EnvelopeVerdict.OutOfEnvelope, Rationale = "r" },
            alt: new AltDto { Index = -1, FallbackClass = "  Administrative Supervisor 2  " });

        var others = new List<ClassDescriptor>
        {
            new() { Slug = "x", Title = "Other", UcJobCode = "111111", Summary = "s" },
        };

        var result = await assembler.CheckEnvelopeAsync(JdTestData.Profile(), JdTestData.Inputs(), others);

        result.SuggestedClass.Should().Be("Administrative Supervisor 2");
        result.SuggestedSlug.Should().BeEmpty("the class is not ingested, so there is nowhere to send them");
    }

    [Fact]
    public async Task The_manager_is_never_routed_to_the_class_they_are_already_in()
    {
        var (assembler, _) = Build(
            verdict: new VerdictDto { Verdict = EnvelopeVerdict.OutOfEnvelope, Rationale = "r" },
            alt: new AltDto { Index = -1, FallbackClass = "rsch data anl 2" });

        var others = new List<ClassDescriptor>
        {
            new() { Slug = "x", Title = "Other", UcJobCode = "111111", Summary = "s" },
        };

        var result = await assembler.CheckEnvelopeAsync(JdTestData.Profile(), JdTestData.Inputs(), others);

        result.SuggestedClass.Should().BeEmpty("suggesting the current class is not advice");
    }

    [Fact]
    public async Task An_out_of_range_alternative_index_is_refused()
    {
        var (assembler, _) = Build(
            verdict: new VerdictDto { Verdict = EnvelopeVerdict.OutOfEnvelope, Rationale = "r" },
            alt: new AltDto { Index = 42, FallbackClass = "" });

        var others = new List<ClassDescriptor>
        {
            new() { Slug = "x", Title = "Other", UcJobCode = "111111", Summary = "s" },
        };

        var result = await assembler.CheckEnvelopeAsync(JdTestData.Profile(), JdTestData.Inputs(), others);

        result.SuggestedSlug.Should().BeEmpty();
        result.SuggestedClass.Should().BeEmpty();
    }

    [Fact]
    public async Task With_no_peer_catalog_stage_two_is_skipped_entirely()
    {
        var (assembler, llm) = Build(verdict: new VerdictDto
        {
            Verdict = EnvelopeVerdict.OutOfEnvelope, Rationale = "r",
        });

        var result = await assembler.CheckEnvelopeAsync(JdTestData.Profile(), JdTestData.Inputs(), []);

        result.Verdict.Should().Be(EnvelopeVerdict.OutOfEnvelope);
        llm.Requests.Should().ContainSingle();
    }

    [Fact]
    public void Has_additions_is_false_for_an_untouched_standard()
    {
        // The caller uses this to skip the check entirely: kept and dropped standard items are
        // inherently in scope, so there is nothing to police and no reason to spend a call.
        var assembler = new JdAssembler(new FakeStructuredLlm());

        assembler.HasAdditions(JdTestData.Inputs()).Should().BeFalse();

        var withItem = JdTestData.Inputs();
        withItem.AddedItems = ["Supervises staff."];
        assembler.HasAdditions(withItem).Should().BeTrue();
    }

    [Fact]
    public void The_gap_serializes_as_the_field_name_the_client_gates_on()
    {
        // docs/API-CONTRACT.md documents `unallocatedPct` and the client already implements the
        // gate against it. A PascalCase field would leave the client reading undefined, which
        // JavaScript happily compares as != 0 — so every draft would look publishable.
        var json = System.Text.Json.JsonSerializer.Serialize(
            new AssembledJd { UnallocatedPct = 30 },
            Server.Core.Ai.StructuredSchema.SerializerOptions);

        json.Should().Contain("\"unallocatedPct\":30");
        json.Should().NotContain("UnallocatedPct");
    }

    // ------------------------------------------------------------------ persistence shape

    [Fact]
    public async Task The_entity_mapping_assigns_ordinals_and_section_kinds()
    {
        // SQL has no inherent row order and these lists all read in sequence, so ordinals are the
        // only thing keeping a job description from coming back scrambled.
        var (assembler, _) = Build(CleanGeneration());
        var assembled = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        var entity = assembled.ToEntity(classProfileId: 1, createdByUserId: 7);

        entity.ClassProfileId.Should().Be(1);
        entity.CreatedByUserId.Should().Be(7);
        entity.JobSummary.Should().Be(assembled.Jd.JobSummary);

        entity.KeyResponsibilities.Select(r => r.Ordinal).Should().Equal(0, 1);
        entity.KeyResponsibilities[0].Duties.Select(d => d.Ordinal).Should().Equal(0, 1);
        entity.KeyResponsibilities.Sum(r => r.PctTime).Should().Be(100);

        entity.Items.Where(i => i.Kind == AuthoredJdListKind.Education)
            .Select(i => i.Text).Should().Equal("Bachelor's degree or equivalent experience.");
        entity.Items.Where(i => i.Kind == AuthoredJdListKind.MinKsa)
            .Select(i => i.Text).Should().Equal("Working knowledge of statistical methods.");
        entity.Items.Where(i => i.Kind == AuthoredJdListKind.PhysicalRequirement)
            .Should().ContainSingle();

        // Ordinals restart per section, so each list reads in its own order.
        foreach (var group in entity.Items.GroupBy(i => i.Kind))
        {
            group.Select(i => i.Ordinal).Should().Equal(Enumerable.Range(0, group.Count()));
        }
    }

    [Fact]
    public async Task The_audit_trail_is_persisted_in_order()
    {
        var gen = CleanGeneration();
        gen.JobSummary = "A rockstar chairman role.";

        var (assembler, _) = Build(gen);
        var assembled = await assembler.AssembleAsync(
            JdTestData.Profile(), JdTestData.Inputs(), JdTestData.Rules());

        var entity = assembled.ToEntity(classProfileId: 1);

        entity.ComplianceEdits.Should().HaveCount(2);
        entity.ComplianceEdits.Select(e => e.Ordinal).Should().Equal(0, 1);
        entity.ComplianceEdits.Should().OnlyContain(e => e.Source == ComplianceEditSource.Rule);
        entity.ComplianceEdits.Should().OnlyContain(e => e.Reason.Length > 0);
    }
}

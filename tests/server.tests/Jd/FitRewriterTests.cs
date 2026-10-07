using System.Text.Json;
using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Jd;
using CannedLlm = Server.Tests.Profiles.FakeStructuredLlm;

namespace Server.Tests.Jd;

/// <summary>
/// Rewriting a misfitting JD to fit a class.
///
/// The model returns placements by index and nothing else. These tests feed it plans a careful model
/// would not produce — indices out of range, a responsibility left unplaced, % time that sums to 120,
/// a carried duty that repeats a kept one — to prove that the words come only from the envelope and
/// the incumbent, and every number is computed here.
/// </summary>
public class FitRewriterTests
{
    private static ClassProfile Target()
    {
        var env = new JobEnvelope { Summary = "Analyzes finances." };
        env.KeyResponsibilities.Add(Fn(0, "Financial Analysis", 60, "Prepares budget forecasts.", "Reconciles ledgers monthly."));
        env.KeyResponsibilities.Add(Fn(1, "Reporting", 30, "Produces financial reports.", "Presents results to leadership."));
        env.KeyResponsibilities.Add(Fn(2, "Policy", 10, "Interprets fiscal policy."));
        env.Items.Add(new EnvelopeListItem { Kind = EnvelopeListKind.Education, Ordinal = 0, Text = "Bachelor's degree in accounting." });
        env.Items.Add(new EnvelopeListItem { Kind = EnvelopeListKind.MinQualification, Ordinal = 0, Text = "Advanced Excel." });
        return new ClassProfile { Slug = "005183-financial-anl-3-cx", Title = "Financial Anl 3 Cx", UcJobCode = "005183", Envelope = env };
    }

    private static EnvelopeResponsibility Fn(int ordinal, string name, int pct, params string[] duties)
    {
        var r = new EnvelopeResponsibility { Ordinal = ordinal, FunctionName = name, PctTime = pct };
        for (var i = 0; i < duties.Length; i++)
        {
            r.Duties.Add(new EnvelopeDuty { Ordinal = i, Text = duties[i] });
        }

        return r;
    }

    private static JobDescription Incumbent(params (string Name, int? Pct, string[] Duties)[] resps)
    {
        var jd = new JobDescription
        {
            SourceFile = "JD-001.html",
            WorkingTitle = "Budget Analyst",
            DepartmentName = "Plant Sciences",
            JobSummary = "Runs the department budget.",
        };
        for (var r = 0; r < resps.Length; r++)
        {
            var resp = new JdResponsibility { Ordinal = r, FunctionName = resps[r].Name, Pct = resps[r].Pct };
            for (var d = 0; d < resps[r].Duties.Length; d++)
            {
                resp.Duties.Add(new JdDuty { Ordinal = d, Text = resps[r].Duties[d] });
            }

            jd.Responsibilities.Add(resp);
        }

        return jd;
    }

    private static readonly JobDescription Budget = Incumbent(
        ("Budgeting", 70, ["Builds the annual budget for 12 labs.", "Prepares budget forecasts."]),
        ("Reports", 50, ["Writes monthly variance reports."]),
        ("Event planning", 10, ["Plans the department holiday party."]));

    private static async Task<(FitRewrite Rewrite, CannedLlm Llm)> Run(JobDescription jd, object plan)
    {
        var llm = new CannedLlm().Returns(plan);
        var rewrite = await new FitRewriter(llm).RewriteAsync(Target(), jd);
        return (rewrite, llm);
    }

    private static readonly object TypicalPlan = new
    {
        placements = new[]
        {
            new { responsibility = 0, function = 0 },
            new { responsibility = 1, function = 1 },
            new { responsibility = 2, function = -1 },
        },
        functions = new[]
        {
            new { function = 0, keepDuties = new[] { 0, 9 } },
            new { function = 1, keepDuties = new[] { 0 } },
        },
        carryOver = new[]
        {
            new { responsibility = 0, duty = 0, function = 0 },
            // Repeats the kept standard duty "Prepares budget forecasts." — must not appear twice.
            new { responsibility = 0, duty = 1, function = 0 },
            // Points at a function that is not kept — ignored.
            new { responsibility = 1, duty = 0, function = 2 },
        },
        outside = new[] { new { responsibility = 2, duty = -1, reason = "Event planning is not financial analysis." } },
    };

    [Fact]
    public async Task Time_is_the_incumbents_own_rescaled_to_exactly_100()
    {
        // 70 + 50 placed (the 10% outside is dropped): 120 rescales to 58 / 42.
        var (rewrite, _) = await Run(Budget, TypicalPlan);

        rewrite.Inputs.KeptResponsibilities.Select(r => (r.FunctionName, r.PctTime))
            .Should().Equal(("Financial Analysis", 58), ("Reporting", 42));
        rewrite.KeptFunctions.Should().Be(2);
        rewrite.DroppedFunctions.Should().Be(1);
    }

    [Fact]
    public async Task Every_word_is_the_envelopes_or_the_incumbents_and_bad_indices_are_ignored()
    {
        var (rewrite, _) = await Run(Budget, TypicalPlan);

        var analysis = rewrite.Inputs.KeptResponsibilities[0];
        analysis.Duties.Should().Equal("Prepares budget forecasts.", "Builds the annual budget for 12 labs.");
        rewrite.Inputs.KeptResponsibilities[1].Duties.Should().Equal("Produces financial reports.");

        // Carried duties are additions, which the envelope check polices before assembly.
        rewrite.Inputs.AddedItems.Should().Equal("Builds the annual budget for 12 labs.");
        rewrite.CarriedDuties.Should().Be(1);

        // The class's qualifications are its guidelines and all stand.
        rewrite.Inputs.KeptEducation.Should().Equal("Bachelor's degree in accounting.");
        rewrite.Inputs.KeptMinKSA.Should().Equal("Advanced Excel.");
        rewrite.Inputs.WorkingTitle.Should().Be("Budget Analyst");
    }

    [Fact]
    public async Task Work_outside_the_class_is_reported_with_its_reason()
    {
        var (rewrite, _) = await Run(Budget, TypicalPlan);

        var omitted = rewrite.Outside.Should().ContainSingle().Subject;
        omitted.Text.Should().Be("Event planning");
        omitted.Pct.Should().Be(10);
        omitted.Reason.Should().Be("Event planning is not financial analysis.");
    }

    [Fact]
    public async Task The_draft_opens_in_the_build_screen_aligned_with_the_envelope()
    {
        var (rewrite, _) = await Run(Budget, TypicalPlan);

        using var doc = JsonDocument.Parse(rewrite.DraftState);
        var root = doc.RootElement;
        root.GetProperty("version").GetInt32().Should().Be(1);
        root.GetProperty("workingTitle").GetString().Should().Be("Budget Analyst");
        root.GetProperty("department").GetString().Should().Be("Plant Sciences");

        // One entry per envelope function, in envelope order, as the build screen expects.
        var resps = root.GetProperty("resps").EnumerateArray().ToList();
        resps.Select(r => r.GetProperty("functionName").GetString())
            .Should().Equal("Financial Analysis", "Reporting", "Policy");
        resps.Select(r => r.GetProperty("functionKept").GetBoolean()).Should().Equal(true, true, false);
        resps[0].GetProperty("pctTime").GetInt32().Should().Be(58);

        var duties = resps[0].GetProperty("duties").EnumerateArray()
            .Select(d => (d.GetProperty("text").GetString(), d.GetProperty("kept").GetBoolean(), d.GetProperty("added").GetBoolean()))
            .ToList();
        duties.Should().Equal(
            ("Prepares budget forecasts.", true, false),
            ("Reconciles ledgers monthly.", false, false),
            ("Builds the annual budget for 12 labs.", true, true));

        root.GetProperty("education").EnumerateArray().Single().GetProperty("kept").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_responsibility_the_model_left_unplaced_is_reported_not_lost()
    {
        var (rewrite, _) = await Run(Budget, new
        {
            placements = new[] { new { responsibility = 0, function = 0 }, new { responsibility = 1, function = 1 } },
            functions = Array.Empty<object>(),
            carryOver = Array.Empty<object>(),
            outside = Array.Empty<object>(),
        });

        rewrite.Outside.Should().ContainSingle(o => o.Text == "Event planning" && o.Reason.Contains("Not placed"));
    }

    [Fact]
    public async Task A_kept_function_with_no_duties_chosen_keeps_its_standard_duties_rather_than_vanishing()
    {
        // The build screen drops a function with no kept duties, taking its time with it.
        var (rewrite, _) = await Run(Budget, new
        {
            placements = new[] { new { responsibility = 0, function = 0 }, new { responsibility = 1, function = 1 } },
            functions = Array.Empty<object>(),
            carryOver = Array.Empty<object>(),
            outside = Array.Empty<object>(),
        });

        rewrite.Inputs.KeptResponsibilities[0].Duties.Should().Equal("Prepares budget forecasts.", "Reconciles ledgers monthly.");
        rewrite.Inputs.KeptResponsibilities.Sum(r => r.PctTime).Should().Be(100);
    }

    [Fact]
    public async Task With_no_stated_time_the_class_split_is_used_for_the_functions_performed()
    {
        var jd = Incumbent(("Budgeting", null, ["Builds budgets."]), ("Reports", null, ["Writes reports."]));

        var (rewrite, _) = await Run(jd, new
        {
            placements = new[] { new { responsibility = 0, function = 0 }, new { responsibility = 1, function = 1 } },
            functions = Array.Empty<object>(),
            carryOver = Array.Empty<object>(),
            outside = Array.Empty<object>(),
        });

        // 60 and 30 from the envelope, rescaled over the two performed: 67 / 33.
        rewrite.Inputs.KeptResponsibilities.Select(r => r.PctTime).Should().Equal(67, 33);
    }

    [Fact]
    public async Task Nothing_that_fits_is_refused_rather_than_producing_an_empty_jd()
    {
        var act = () => Run(Budget, new
        {
            placements = new[] { new { responsibility = 0, function = -1 } },
            functions = Array.Empty<object>(),
            carryOver = Array.Empty<object>(),
            outside = Array.Empty<object>(),
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*None of this JD's work fits*");
    }

    [Fact]
    public async Task The_prompt_numbers_both_sides_and_asks_only_for_numbers()
    {
        var (_, llm) = await Run(Budget, TypicalPlan);

        var request = llm.Requests.Single();
        request.Label.Should().Be("fit.rewrite");
        request.User.Should().Contain("F0. Financial Analysis (60%)")
            .And.Contain("  F1.1 Presents results to leadership.")
            .And.Contain("R0. Budgeting (70%)")
            .And.Contain("  R2.0 Plans the department holiday party.");
        request.System.Should().Contain("never write job description text yourself");
    }
}

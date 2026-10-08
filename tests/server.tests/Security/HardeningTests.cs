using System.Diagnostics;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Server.Controllers;
using Server.Core.Ingest;
using Server.Core.Intake;
using Server.Core.Jd;
using Server.Core.Profiles;
using Server.Helpers;
using Server.Tests.Jd;

namespace Server.Tests.Security;

/// <summary>
/// The fixes from the adversarial review (2026-10-08), each pinned to the attack it stops.
/// </summary>
public class HardeningTests
{
    // ------------------------------------------------------------------ cross-site requests

    [Theory]
    [InlineData("POST", "/api/admin/standards/upload", false, true)]
    [InlineData("DELETE", "/api/jds/12", false, true)]
    [InlineData("POST", "/api/admin/standards/upload", true, false)]
    [InlineData("GET", "/api/jds/12/docx", false, false)]
    [InlineData("POST", "/login/local", false, false)]
    public void A_state_changing_api_call_must_carry_the_clients_header(string method, string path, bool withHeader, bool forgeable)
    {
        // Another *.ucdavis.edu page is same-site to a Lax cookie and can post a form or a no-cors
        // multipart fetch with it. It cannot add a custom header without a preflight, which is refused.
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (withHeader)
        {
            context.Request.Headers[WebHardening.ClientHeader] = "JDWriter";
        }

        WebHardening.IsForgeable(context.Request).Should().Be(forgeable);
    }

    // ------------------------------------------------------------------ request bounds

    private static BuildRequest Build() => new()
    {
        Slug = "x",
        WorkingTitle = "Analyst",
        KeptResponsibilities = [new JdKeyResponsibility { FunctionName = "Analysis", PctTime = 100, Duties = ["Analyzes data."] }],
        AddedItems = ["Runs the survey program."],
    };

    [Fact]
    public void A_real_build_is_within_bounds()
    {
        RequestLimits.Build(Build()).Should().BeNull();
    }

    [Fact]
    public void An_oversized_build_is_refused_before_any_model_call()
    {
        var oneHuge = Build();
        oneHuge.AddedItems = [new string('x', 2_001)];
        RequestLimits.Build(oneHuge).Should().NotBeNull();

        var tooMany = Build();
        tooMany.AddedItems = [.. Enumerable.Repeat("An addition.", 201)];
        RequestLimits.Build(tooMany).Should().NotBeNull();

        var tooMuchInTotal = Build();
        tooMuchInTotal.KeptResponsibilities = [.. Enumerable.Range(0, 40).Select(i => new JdKeyResponsibility
        {
            FunctionName = $"F{i}", Duties = [.. Enumerable.Repeat(new string('d', 1_000), 5)],
        })];
        RequestLimits.Build(tooMuchInTotal).Should().NotBeNull("200,000 characters across items that are each within bounds");
    }

    [Fact]
    public void Pasted_descriptions_and_distilled_jds_are_bounded()
    {
        RequestLimits.Description(new string('x', 140_000)).Should().BeNull("the largest real HRTMS export is about 140 KB");
        RequestLimits.Description(new string('x', RequestLimits.DescriptionChars + 1)).Should().NotBeNull();

        var flood = new DistilledJd
        {
            Functions = [.. Enumerable.Range(0, 40).Select(i => new DistilledFunction
            {
                Name = $"F{i}", Duties = [.. Enumerable.Repeat(new string('d', 3_000), 60)],
            })],
        };
        RequestLimits.Distilled(flood).Should().NotBeNull();
    }

    // ------------------------------------------------------------------ the envelope check

    [Fact]
    public void Anything_sent_as_kept_that_is_not_in_the_envelope_is_an_addition()
    {
        // The check polices additions only, and "kept" is just text the client sends — so a rewritten
        // kept duty used to skip the check and reach the corpus unreviewed.
        var profile = JdTestData.Profile();
        var standard = EnvelopeWire.From(profile.Envelope!);
        var realDuty = standard.KeyResponsibilities[0].Duties[0];
        var inputs = new BuildInputs
        {
            KeptResponsibilities =
            [
                new JdKeyResponsibility
                {
                    FunctionName = standard.KeyResponsibilities[0].FunctionName,
                    PctTime = 100,
                    // The standard duty retyped in other case and punctuation is still the standard duty.
                    Duties = [realDuty.ToUpperInvariant().TrimEnd('.') + "!", "Approves all campus budgets."],
                },
            ],
            KeptMinKSA = ["Holds a pilot's license."],
            AddedItems = ["Approves all campus budgets."],
        };

        var declared = JdAssembler.WithUndeclaredAdditions(profile, inputs);

        declared.AddedItems.Should().Equal("Approves all campus budgets.", "Holds a pilot's license.");
        new JdAssembler(new FakeStructuredLlm()).HasAdditions(declared).Should().BeTrue();
    }

    [Fact]
    public void A_build_that_only_keeps_standard_items_declares_nothing()
    {
        var profile = JdTestData.Profile();
        var inputs = JdTestData.Inputs();
        inputs.AddedItems = [];

        // Only what this test envelope actually holds (the shared inputs keep a few lines it lacks).
        inputs.KeptEducation = [];
        inputs.KeptWorkExperience = [];
        inputs.KeptWorkEnvironment = [];

        JdAssembler.WithUndeclaredAdditions(profile, inputs).Should().BeSameAs(inputs);
    }

    // ------------------------------------------------------------------ untrusted files and text

    [Fact]
    public void A_worksheet_claiming_billions_of_cells_is_refused_not_walked()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Form");
        ws.Cell(1, 1).Value = "Working Title";
        ws.Cell(1_048_576, 16_384).Value = "x";

        var act = () => WorksheetLimits.UsedRange(ws);

        act.Should().Throw<InvalidDataException>().WithMessage("*beyond*");
    }

    [Fact]
    public void Pasted_markup_that_never_closes_its_cells_cannot_hold_the_cpu()
    {
        // Measured at 17.6 s of CPU for 100 KB before the regex time limit; a paste is capped at
        // 400,000 characters, which would have been minutes.
        var crafted = "<div>" + string.Concat(Enumerable.Repeat("<td>x", 20_000));
        var clock = Stopwatch.StartNew();

        try
        {
            HrtmsParser.Parse(crafted, "pasted");
        }
        catch (RegexMatchTimeoutException)
        {
            // The expected outcome: the caller treats it as not an export.
        }

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }
}

using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Server.Core.Ai;
using Server.Core.Domain;
using Server.Core.Profiles;

namespace Server.Core.Jd;

/// <summary>
/// The model's plan for rewriting one real JD to fit a class: indices only. It never writes a duty,
/// a title or a percentage; every word of the result is either the class envelope's or the
/// incumbent's own, and every number is computed here.
/// </summary>
public sealed class RewritePlan
{
    [Description("Every responsibility of the incumbent's JD, each placed in exactly one class function.")]
    public List<RewritePlacement> Placements { get; set; } = [];

    [Description("For each class function the incumbent performs, which of its standard duties describe their work.")]
    public List<RewriteFunctionPick> Functions { get; set; } = [];

    [Description("Incumbent duties to carry over word for word: inside this class, but not already said by a kept standard duty.")]
    public List<RewriteCarry> CarryOver { get; set; } = [];

    [Description("Incumbent work that does not belong in this class at all, with a one-sentence reason each.")]
    public List<RewriteOutside> Outside { get; set; } = [];
}

public sealed class RewritePlacement
{
    [Description("The incumbent responsibility's number, exactly as listed.")]
    public int Responsibility { get; set; }

    [Description("The class function's number it belongs to, or -1 if the work is outside this class.")]
    public int Function { get; set; }
}

public sealed class RewriteFunctionPick
{
    [Description("The class function's number, exactly as listed.")]
    public int Function { get; set; }

    [Description("Numbers of that function's standard duties that describe the incumbent's actual work.")]
    public List<int> KeepDuties { get; set; } = [];
}

public sealed class RewriteCarry
{
    [Description("The incumbent responsibility's number.")]
    public int Responsibility { get; set; }

    [Description("The duty's number within that responsibility.")]
    public int Duty { get; set; }

    [Description("The class function's number to place it under.")]
    public int Function { get; set; }
}

public sealed class RewriteOutside
{
    [Description("The incumbent responsibility's number.")]
    public int Responsibility { get; set; }

    [Description("The duty's number within it, or -1 for the whole responsibility.")]
    public int Duty { get; set; }

    [Description("One sentence: why this work does not belong in the class.")]
    public string Reason { get; set; } = "";
}

/// <summary>Incumbent work the rewrite left out, for the analyst to see.</summary>
public sealed class RewriteOmission
{
    public string Text { get; set; } = "";
    public int? Pct { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>A rewrite as the build screen will open it, plus what it means for the analyst.</summary>
public sealed class FitRewrite
{
    public BuildInputs Inputs { get; set; } = new();

    /// <summary>The build screen's own state (DraftState version 1), so "Continue editing" opens it.</summary>
    public string DraftState { get; set; } = "";

    public int KeptFunctions { get; set; }
    public int DroppedFunctions { get; set; }
    public int CarriedDuties { get; set; }
    public List<RewriteOmission> Outside { get; set; } = [];
}

public interface IFitRewriter
{
    /// <summary>
    /// Rewrite a real JD to fit <paramref name="target"/>: its functions, duties and qualifications,
    /// drawing the incumbent's actual work into them. Produces a build-screen draft, never a
    /// finished JD — the analyst reviews it and assembles it through the normal checks.
    /// </summary>
    Task<FitRewrite> RewriteAsync(ClassProfile target, JobDescription incumbent, CancellationToken ct = default);
}

public sealed partial class FitRewriter : IFitRewriter
{
    private readonly IStructuredLlm _llm;

    public FitRewriter(IStructuredLlm llm) => _llm = llm;

    private const string SystemPrompt = """
        You help a UC Davis HR classification analyst correct a job description that does not fit its job class.

        You are given the CLASS ENVELOPE — the class's standard functions, each with numbered standard duties — and the INCUMBENT'S JD: what the person in the position actually does, as numbered responsibilities with % time and numbered duties.

        Produce a plan for rewriting the incumbent's JD so it fits the class while still describing their real work. Answer only with numbers that appear in the lists; never write job description text yourself.

        1. Place every incumbent responsibility in the one class function its work most belongs to. Use -1 only when the work genuinely falls outside this class (another class's work, or a different level).
        2. For each class function that received work, keep the standard duties that describe what this incumbent actually does. Do not keep duties for work they do not do.
        3. Carry over an incumbent duty only when it is within the class and no kept standard duty already says it. Put it under the function it belongs to.
        4. List incumbent work that does not belong in this class, with a one-sentence reason each. A whole responsibility placed at -1 must appear here.
        """;

    public async Task<FitRewrite> RewriteAsync(ClassProfile target, JobDescription incumbent, CancellationToken ct = default)
    {
        var envelope = target.Envelope ?? throw new InvalidOperationException($"{target.Title} has no envelope to rewrite against.");
        var functions = envelope.KeyResponsibilities.OrderBy(r => r.Ordinal).ToList();
        var resps = incumbent.Responsibilities.OrderBy(r => r.Ordinal).ToList();
        if (functions.Count == 0 || resps.Count == 0)
        {
            throw new InvalidOperationException("Both the class envelope and the job description need responsibilities to rewrite.");
        }

        var plan = await _llm.StructuredAsync<RewritePlan>(new StructuredRequest
        {
            System = SystemPrompt,
            User = BuildUser(target, functions, incumbent, resps),
            Effort = LlmEffort.Medium,
            MaxTokens = 8000,
            Label = "fit.rewrite",
        }, ct);

        return Assemble(envelope, functions, incumbent, resps, plan);
    }

    private static string BuildUser(
        ClassProfile target, List<EnvelopeResponsibility> functions, JobDescription incumbent, List<JdResponsibility> resps)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CLASS: {target.Title} ({target.UcJobCode})");
        sb.AppendLine();
        sb.AppendLine("CLASS ENVELOPE — functions (standard % time) and their standard duties:");
        for (var f = 0; f < functions.Count; f++)
        {
            sb.AppendLine($"F{f}. {functions[f].FunctionName} ({functions[f].PctTime}%)");
            var duties = functions[f].Duties.OrderBy(d => d.Ordinal).ToList();
            for (var d = 0; d < duties.Count; d++)
            {
                sb.AppendLine($"  F{f}.{d} {duties[d].Text}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"INCUMBENT'S JD — working title: {incumbent.WorkingTitle}");
        if (!string.IsNullOrWhiteSpace(incumbent.JobSummary))
        {
            sb.AppendLine($"Summary: {incumbent.JobSummary}");
        }

        for (var r = 0; r < resps.Count; r++)
        {
            var stated = resps[r].Pct.HasValue ? $"{resps[r].Pct}%" : "% not stated";
            sb.AppendLine($"R{r}. {resps[r].FunctionName} ({stated})");
            var duties = resps[r].Duties.OrderBy(d => d.Ordinal).ToList();
            for (var d = 0; d < duties.Count; d++)
            {
                sb.AppendLine($"  R{r}.{d} {duties[d].Text}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Use the bare numbers in your answer: function 2 for F2, responsibility 0 and duty 3 for R0.3.");
        return sb.ToString();
    }

    /// <summary>
    /// Turn the plan into the build screen's state. Everything the model said is checked against the
    /// lists it was given; an index that does not exist is ignored rather than trusted.
    /// </summary>
    internal static FitRewrite Assemble(
        JobEnvelope envelope,
        List<EnvelopeResponsibility> functions,
        JobDescription incumbent,
        List<JdResponsibility> resps,
        RewritePlan plan)
    {
        var dutiesOf = functions.Select(f => f.Duties.OrderBy(d => d.Ordinal).Select(d => d.Text).ToList()).ToList();
        var incumbentDuties = resps.Select(r => r.Duties.OrderBy(d => d.Ordinal).Select(d => d.Text).ToList()).ToList();
        bool IsFunction(int f) => f >= 0 && f < functions.Count;

        // Each incumbent responsibility lands in one function. The first valid placement wins; one
        // the model left out counts as outside the class, so no work silently vanishes.
        var placedIn = new int[resps.Count];
        Array.Fill(placedIn, -1);
        var seen = new HashSet<int>();
        foreach (var p in plan.Placements)
        {
            if (p.Responsibility >= 0 && p.Responsibility < resps.Count && seen.Add(p.Responsibility) && IsFunction(p.Function))
            {
                placedIn[p.Responsibility] = p.Function;
            }
        }

        // % time is the incumbent's own, summed per function and rescaled to exactly 100. Six real JDs
        // do not sum to 100 and some state no % at all; with no usable shares the class's standard
        // split is used for the functions they perform.
        var share = new double[functions.Count];
        for (var r = 0; r < resps.Count; r++)
        {
            if (placedIn[r] >= 0)
            {
                share[placedIn[r]] += Math.Max(0, resps[r].Pct ?? 0);
            }
        }

        var kept = Enumerable.Range(0, functions.Count).Where(f => placedIn.Contains(f)).ToList();
        if (kept.Count == 0)
        {
            throw new InvalidOperationException("None of this JD's work fits the class, so there is nothing to rewrite into it.");
        }

        if (kept.Sum(f => share[f]) <= 0)
        {
            foreach (var f in kept)
            {
                share[f] = Math.Max(0, functions[f].PctTime);
            }
        }

        var pct = ToHundred(kept, share);

        var keepDuty = functions.Select(_ => new HashSet<int>()).ToList();
        foreach (var pick in plan.Functions.Where(x => IsFunction(x.Function)))
        {
            foreach (var d in pick.KeepDuties.Where(d => d >= 0 && d < dutiesOf[pick.Function].Count))
            {
                keepDuty[pick.Function].Add(d);
            }
        }

        var carried = functions.Select(_ => new List<string>()).ToList();
        var said = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in kept)
        {
            foreach (var d in keepDuty[f])
            {
                said.Add(Norm(dutiesOf[f][d]));
            }
        }

        foreach (var c in plan.CarryOver)
        {
            if (c.Responsibility < 0 || c.Responsibility >= resps.Count || c.Duty < 0
                || c.Duty >= incumbentDuties[c.Responsibility].Count || !kept.Contains(c.Function))
            {
                continue;
            }

            // The incumbent's words, verbatim; skipped when a kept duty or an earlier carry says the same.
            var text = incumbentDuties[c.Responsibility][c.Duty];
            if (said.Add(Norm(text)))
            {
                carried[c.Function].Add(text);
            }
        }

        var resps0 = new List<object>();
        var inputs = new BuildInputs
        {
            WorkingTitle = incumbent.WorkingTitle,
            Department = incumbent.DepartmentName,
        };

        for (var f = 0; f < functions.Count; f++)
        {
            var isKept = kept.Contains(f);

            // A kept function with nothing kept and nothing carried would be dropped by the build
            // screen, taking its share of time with it; its standard duties stand in instead.
            var keepAll = isKept && keepDuty[f].Count == 0 && carried[f].Count == 0;
            var items = dutiesOf[f]
                .Select((text, d) => new { added = false, kept = isKept && (keepAll || keepDuty[f].Contains(d)), text })
                .Concat(carried[f].Select(text => new { added = true, kept = true, text }))
                .ToList();

            resps0.Add(new
            {
                draft = "",
                duties = items,
                functionKept = isKept,
                functionName = functions[f].FunctionName,
                pctTime = isKept ? pct[f] : functions[f].PctTime,
            });

            if (isKept)
            {
                inputs.KeptResponsibilities.Add(new JdKeyResponsibility
                {
                    FunctionName = functions[f].FunctionName,
                    PctTime = pct[f],
                    Duties = [.. items.Where(i => i.kept).Select(i => i.text)],
                });
                inputs.AddedItems.AddRange(carried[f]);
            }
        }

        // The class's qualifications are its guidelines: all of them stand.
        List<string> Section(EnvelopeListKind kind) =>
            [.. envelope.Items.Where(i => i.Kind == kind).OrderBy(i => i.Ordinal).Select(i => i.Text)];
        inputs.KeptCerts = Section(EnvelopeListKind.RequiredCertification);
        inputs.KeptEducation = Section(EnvelopeListKind.Education);
        inputs.KeptWorkExperience = Section(EnvelopeListKind.WorkExperience);
        inputs.KeptMinKSA = Section(EnvelopeListKind.MinQualification);
        inputs.KeptPrefKSA = Section(EnvelopeListKind.PrefQualification);
        inputs.KeptWorkEnvironment = Section(EnvelopeListKind.WorkEnvironment);

        static IEnumerable<object> Items(List<string> xs) => xs.Select(text => new { added = false, kept = true, text });

        var draftState = JsonSerializer.Serialize(new
        {
            certs = Items(inputs.KeptCerts),
            department = inputs.Department,
            education = Items(inputs.KeptEducation),
            minKSA = Items(inputs.KeptMinKSA),
            notes = "",
            prefKSA = Items(inputs.KeptPrefKSA),
            resps = resps0,
            version = 1,
            workEnv = Items(inputs.KeptWorkEnvironment),
            workExp = Items(inputs.KeptWorkExperience),
            workingTitle = inputs.WorkingTitle,
        });

        return new FitRewrite
        {
            Inputs = inputs,
            DraftState = draftState,
            KeptFunctions = kept.Count,
            DroppedFunctions = functions.Count - kept.Count,
            CarriedDuties = carried.Sum(c => c.Count),
            Outside = Omissions(resps, incumbentDuties, placedIn, plan),
        };
    }

    /// <summary>
    /// What the rewrite leaves out: every responsibility placed outside the class, plus single duties
    /// the model named, each with its reason. A responsibility left unplaced is reported too.
    /// </summary>
    private static List<RewriteOmission> Omissions(
        List<JdResponsibility> resps, List<List<string>> duties, int[] placedIn, RewritePlan plan)
    {
        var reasons = new Dictionary<(int, int), string>();
        foreach (var o in plan.Outside.Where(o => o.Responsibility >= 0 && o.Responsibility < resps.Count))
        {
            var d = o.Duty >= 0 && o.Duty < duties[o.Responsibility].Count ? o.Duty : -1;
            reasons.TryAdd((o.Responsibility, d), o.Reason.Trim());
        }

        var result = new List<RewriteOmission>();
        for (var r = 0; r < resps.Count; r++)
        {
            if (placedIn[r] < 0)
            {
                result.Add(new RewriteOmission
                {
                    Text = resps[r].FunctionName,
                    Pct = resps[r].Pct,
                    Reason = reasons.GetValueOrDefault((r, -1)) ?? "Not placed in any function of this class.",
                });
            }
        }

        foreach (var ((r, d), reason) in reasons.Where(kv => kv.Key.Item2 >= 0 && placedIn[kv.Key.Item1] >= 0))
        {
            result.Add(new RewriteOmission { Text = duties[r][d], Reason = reason });
        }

        return result;
    }

    /// <summary>Largest-remainder rounding, so the kept functions total exactly 100.</summary>
    private static int[] ToHundred(List<int> kept, double[] share)
    {
        var total = kept.Sum(f => share[f]);
        var result = new int[share.Length];
        var exact = kept.ToDictionary(f => f, f => share[f] / total * 100);
        foreach (var f in kept)
        {
            result[f] = (int)Math.Floor(exact[f]);
        }

        var remaining = 100 - kept.Sum(f => result[f]);
        foreach (var f in kept.OrderByDescending(f => exact[f] - result[f]).ThenBy(f => f).Take(remaining))
        {
            result[f]++;
        }

        return result;
    }

    [GeneratedRegex("[^a-z0-9 ]")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>The build screen's redundancy key: lower-case, letters, digits and single spaces.</summary>
    private static string Norm(string s) => Spaces().Replace(NonWord().Replace(s.ToLowerInvariant(), ""), " ").Trim();
}

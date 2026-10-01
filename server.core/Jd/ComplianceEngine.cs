using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Server.Core.Domain;

namespace Server.Core.Jd;

/// <summary>
/// One addressable text field of a job description, with a setter bound to the object it came
/// from.
/// </summary>
public sealed class JdTextField
{
    public required string Path { get; init; }
    public required string Text { get; init; }
    public required Action<string> Set { get; init; }
}

/// <summary>An index-addressed edit returned by the model.</summary>
public sealed class TextEdit
{
    public int Index { get; set; }
    public string After { get; set; } = "";
    public string Reason { get; set; } = "";
}

/// <summary>The outcome of applying edits: a new draft, the audit trail, and what was refused.</summary>
public sealed class EditResult
{
    public required Jd Jd { get; init; }
    public required List<ComplianceEditRecord> Edits { get; init; }

    /// <summary>
    /// Edits that were refused — an unknown index, a duplicate, a blank, or a no-op. Surfaced
    /// rather than swallowed so a model that starts returning nonsense is visible.
    /// </summary>
    public int Dropped { get; init; }
}

/// <summary>
/// Deterministic compliance rules and the machinery for applying index-addressed edits.
///
/// Pure: every method takes what it needs and returns a new draft, with no database or HTTP
/// dependency. That mirrors the split used elsewhere in this codebase (a pure index plus a thin
/// loader) and is what lets the whole audit-trail behaviour be tested without either.
/// </summary>
public static class ComplianceEngine
{
    /// <summary>
    /// The list-valued sections, in the order they are numbered for the model.
    ///
    /// The path strings are the POC's JavaScript property names, not the C# ones. They are written
    /// into the audit trail's Section field, so an analyst reading "minKSA[2]" is reading the same
    /// address the reference implementation produced.
    /// </summary>
    private static readonly (string Path, Func<Jd, List<string>> Select)[] StringArraySections =
    [
        ("licensesCertifications", jd => jd.LicensesCertifications),
        ("education", jd => jd.Education),
        ("workExperience", jd => jd.WorkExperience),
        ("minKSA", jd => jd.MinKSA),
        ("prefKSA", jd => jd.PrefKSA),
        ("conditionsOfEmployment", jd => jd.ConditionsOfEmployment),
        ("workEnvironment", jd => jd.WorkEnvironment),
        ("physicalRequirements", jd => jd.PhysicalRequirements),
    ];

    /// <summary>
    /// Every editable text field, in a stable order, each with a setter bound to the passed object.
    ///
    /// This is what lets a model review a NUMBERED list and return only the lines it wants changed
    /// instead of re-emitting the whole document. It cannot invent a path, drop a bullet, or touch
    /// a percentage — numbers never appear here at all.
    /// </summary>
    public static List<JdTextField> TextFields(Jd jd)
    {
        var fields = new List<JdTextField>
        {
            new() { Path = "jobSummary", Text = jd.JobSummary, Set = v => jd.JobSummary = v },
        };

        for (var i = 0; i < jd.KeyResponsibilities.Count; i++)
        {
            var responsibility = jd.KeyResponsibilities[i];
            fields.Add(new JdTextField
            {
                Path = $"keyResponsibilities[{i}].functionName",
                Text = responsibility.FunctionName,
                Set = v => responsibility.FunctionName = v,
            });

            for (var j = 0; j < responsibility.Duties.Count; j++)
            {
                var dutyIndex = j;
                fields.Add(new JdTextField
                {
                    Path = $"keyResponsibilities[{i}].duties[{j}]",
                    Text = responsibility.Duties[j],
                    Set = v => responsibility.Duties[dutyIndex] = v,
                });
            }
        }

        foreach (var (path, select) in StringArraySections)
        {
            var items = select(jd);
            for (var i = 0; i < items.Count; i++)
            {
                var itemIndex = i;
                fields.Add(new JdTextField
                {
                    Path = $"{path}[{i}]",
                    Text = items[i],
                    Set = v => items[itemIndex] = v,
                });
            }
        }

        return fields;
    }

    /// <summary>
    /// Apply index-addressed edits to a COPY of the draft. Unknown indices, duplicates, blanks and
    /// no-ops are dropped rather than trusted — a model that addresses a line that does not exist
    /// must not be able to corrupt the document.
    /// </summary>
    public static EditResult ApplyTextEdits(Jd jd, IEnumerable<TextEdit> edits, ComplianceEditSource source)
    {
        var draft = jd.Clone();
        var fields = TextFields(draft);
        var applied = new List<ComplianceEditRecord>();
        var seen = new HashSet<int>();
        var dropped = 0;

        foreach (var edit in edits)
        {
            var after = (edit.After ?? "").Trim();

            if (edit.Index < 0 || edit.Index >= fields.Count
                || seen.Contains(edit.Index)
                || after.Length == 0)
            {
                dropped++;
                continue;
            }

            var field = fields[edit.Index];
            if (after == field.Text)
            {
                dropped++;
                continue;
            }

            seen.Add(edit.Index);
            applied.Add(new ComplianceEditRecord
            {
                Section = field.Path,
                Source = source,
                Before = field.Text,
                After = after,
                Reason = edit.Reason ?? "",
            });
            field.Set(after);
        }

        return new EditResult { Jd = draft, Edits = applied, Dropped = dropped };
    }

    /// <summary>
    /// Deterministic first pass: apply the regex rules and record every edit.
    ///
    /// Runs BEFORE any model call, so the model never spends tokens on substitutions a regex can
    /// make, and so the cheap, auditable, reproducible changes are attributed to a rule rather
    /// than to a model.
    /// </summary>
    public static EditResult ApplyRulePass(
        Jd jd, IReadOnlyList<ComplianceRule> rules, ILogger? logger = null)
    {
        var draft = jd.Clone();
        var edits = new List<ComplianceEditRecord>();
        var active = rules.Where(r => r.Enabled).ToList();

        // A rule that cannot compile is reported ONCE, before any line is touched — it is a
        // rule-level fault, not a per-line one, and repeating it for every line would bury it.
        //
        // This is the dangerous failure mode in this system: a compliance rule that quietly stops
        // applying, on a document that goes to a department, is exactly what nobody would notice.
        // So it is skipped (one typo must not block every author) AND made impossible to miss.
        foreach (var rule in active)
        {
            if (Compile(rule.Pattern) is not null)
            {
                continue;
            }

            var error = ErrorFor(rule.Pattern);
            logger?.LogWarning(
                "Compliance rule {RuleKey} was SKIPPED: its pattern {Pattern} is not a valid regular expression ({Error}). " +
                "Job descriptions are being assembled without it — fix the rule.",
                rule.Key, rule.Pattern, error);

            edits.Add(new ComplianceEditRecord
            {
                Section = rule.Key,
                Source = ComplianceEditSource.Rule,
                Before = "",
                After = "",
                Reason = $"{ComplianceEditRecord.RuleFailedMarker}: invalid pattern '{rule.Pattern}' — {error}",
            });
        }

        // Iterating the addressable fields rather than walking the object again keeps the rule
        // pass and the model pass addressing text in exactly the same order.
        foreach (var field in TextFields(draft))
        {
            var current = field.Text;

            foreach (var rule in active)
            {
                var regex = Compile(rule.Pattern);
                if (regex is null)
                {
                    continue;
                }

                var next = regex.Replace(current, rule.Replacement);
                if (next == current)
                {
                    continue;
                }

                // One edit per rule that fired, so a line rewritten by two rules produces two
                // audit entries that chain — each showing what that rule did.
                edits.Add(new ComplianceEditRecord
                {
                    Section = field.Path,
                    Source = ComplianceEditSource.Rule,
                    Before = current,
                    After = next,
                    Reason = $"{rule.Key}: {rule.Reason}",
                });
                current = next;
            }

            if (current != field.Text)
            {
                field.Set(current);
            }
        }

        return new EditResult { Jd = draft, Edits = edits, Dropped = 0 };
    }

    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> RegexErrors = new(StringComparer.Ordinal);

    /// <summary>
    /// Patterns come from the database so HR can change policy language without a deployment, which
    /// also means a malformed one is possible. A bad pattern is skipped rather than allowed to fail
    /// the whole assembly — one broken rule must not block every job description — but the skip is
    /// recorded in the audit trail and logged, never silent.
    /// </summary>
    private static Regex? Compile(string pattern) => RegexCache.GetOrAdd(pattern, static p =>
    {
        try
        {
            return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            RegexErrors[p] = ex.Message;
            return null;
        }
    });

    private static string ErrorFor(string pattern) =>
        RegexErrors.TryGetValue(pattern, out var error) ? error : "invalid regular expression";
}

using System.Text;
using System.Text.RegularExpressions;
using Server.Core.Domain;

namespace Server.Core.Profiles;

/// <summary>What one pass of <see cref="QualificationRules"/> changed in an envelope.</summary>
public sealed record QualificationRulesResult(int MovedToPreferred, int EquivalentAdded)
{
    public bool Changed => MovedToPreferred > 0 || EquivalentAdded > 0;
}

/// <summary>
/// UC Davis's house rules for the education and preferred-qualification sections of an envelope,
/// applied in code after the model (or the standard, or an analyst) has written them:
///
/// 1. A PREFERRED degree is a preferred qualification, not an education requirement. Education
///    text like "Bachelor's degree or equivalent experience; Master's degree preferred" keeps the
///    requirement, and the preference moves to the preferred list.
/// 2. A degree is always stated with "or equivalent experience" — in education and in preferred
///    qualifications alike.
///
/// Code, not prompt: the prompts port byte-for-byte, and the model decides while code assembles.
/// Pure and idempotent — a second pass changes nothing — so it runs on every envelope write and
/// over existing envelopes safely.
/// </summary>
public static partial class QualificationRules
{
    private const string Equivalent = "or equivalent experience";

    [GeneratedRegex(@"\bprefer", RegexOptions.IgnoreCase)]
    private static partial Regex Prefers();

    [GeneratedRegex(@"equivalen", RegexOptions.IgnoreCase)]
    private static partial Regex StatesEquivalent();

    /// <summary>
    /// A degree requirement, not a passing mention: "Bachelor's degree", "advanced degree", "degree
    /// in biology", "Ph.D.", "MBA". "Degree certification systems" is software, not a degree.
    /// </summary>
    [GeneratedRegex(
        @"\b(?:bachelor'?s?|master'?s?|associate'?s?|advanced|graduate|undergraduate|doctoral|professional|terminal|four-year|BA|BS|MA|MS)\s+degrees?\b"
        + @"|\bdegrees?\s+(?:in|from)\b"
        + @"|\b(?:ph\.?\s?d\.?|doctorate|MBA|MPH|MSW|MFA|DNP)(?=\W|$)",
        RegexOptions.IgnoreCase)]
    private static partial Regex NamesDegree();

    // Wording that only says "this is preferred". In the preferred list it is redundant.
    [GeneratedRegex(@"^\s*preferred\s*[:\-–—]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex PreferredLabel();

    [GeneratedRegex(@"^\s*(?:some|many|certain)\s+positions\s+(?:may\s+)?prefer\s+", RegexOptions.IgnoreCase)]
    private static partial Regex SomePositionsPrefer();

    [GeneratedRegex(@"\s*\(\s*(?:is\s+|are\s+)?preferred\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex ParenthesizedPreferred();

    [GeneratedRegex(
        @",?\s+(?:(?:is|are)\s+)?(?:(?:often|commonly|typically|generally|sometimes|usually|also|highly|strongly)\s+)?(?:(?:may|might|will)\s+be\s+)?preferred\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex TrailingPreferred();

    [GeneratedRegex(@"\s+for\s+some\s+positions\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForSomePositions();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    public static QualificationRulesResult Apply(JobEnvelope envelope)
    {
        var education = Of(envelope, EnvelopeListKind.Education);
        var preferred = Of(envelope, EnvelopeListKind.PrefQualification);
        var moved = 0;

        foreach (var item in education)
        {
            var clauses = Clauses(item.Text);
            var stated = clauses.Where(c => Prefers().IsMatch(c.Text)).ToList();
            if (stated.Count == 0)
            {
                continue;
            }

            foreach (var clause in stated)
            {
                var text = AsPreferredItem(clause.Text);
                if (text.Length == 0 || preferred.Any(p => Covers(p.Text, text)))
                {
                    continue;
                }

                var added = new EnvelopeListItem
                {
                    Kind = EnvelopeListKind.PrefQualification,
                    Ordinal = preferred.Count == 0 ? 0 : preferred.Max(p => p.Ordinal) + 1,
                    Text = text,
                };
                envelope.Items.Add(added);
                preferred.Add(added);
                moved++;
            }

            var kept = clauses.Except(stated).ToList();
            if (kept.Count == 0)
            {
                envelope.Items.Remove(item);
            }
            else
            {
                item.Text = Join(kept);
            }
        }

        var equivalent = 0;
        foreach (var item in envelope.Items.Where(i =>
                     i.Kind is EnvelopeListKind.Education or EnvelopeListKind.PrefQualification))
        {
            if (NamesDegree().IsMatch(item.Text) && !StatesEquivalent().IsMatch(item.Text))
            {
                item.Text = $"{item.Text.TrimEnd().TrimEnd('.', ';', ',').TrimEnd()} {Equivalent}.";
                equivalent++;
            }
        }

        if (moved > 0 || education.Count != Of(envelope, EnvelopeListKind.Education).Count)
        {
            Renumber(envelope, EnvelopeListKind.Education);
            Renumber(envelope, EnvelopeListKind.PrefQualification);
        }

        return new QualificationRulesResult(moved, equivalent);
    }

    private static List<EnvelopeListItem> Of(JobEnvelope envelope, EnvelopeListKind kind) =>
        envelope.Items.Where(i => i.Kind == kind).OrderBy(i => i.Ordinal).ToList();

    private static void Renumber(JobEnvelope envelope, EnvelopeListKind kind)
    {
        var i = 0;
        foreach (var item in Of(envelope, kind))
        {
            item.Ordinal = i++;
        }
    }

    private sealed record Clause(string Text, char End);

    /// <summary>
    /// Split education text into clauses at semicolons and sentence ends, outside parentheses — so
    /// "(M.S. or Ph.D.)" and "e.g., Master's" stay whole. A period ends a sentence only when the
    /// next word starts with a capital.
    /// </summary>
    private static List<Clause> Clauses(string text)
    {
        var clauses = new List<Clause>();
        var current = new StringBuilder();
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')' && depth > 0)
            {
                depth--;
            }

            var ends = depth == 0 && (ch == ';' || (ch == '.' && SentenceEndsAt(text, i)));
            if (!ends)
            {
                current.Append(ch);
                continue;
            }

            Flush(current, ch, clauses);
        }

        Flush(current, '.', clauses);
        return clauses;
    }

    private static bool SentenceEndsAt(string text, int i)
    {
        var next = i + 1;
        if (next >= text.Length)
        {
            return true;
        }

        if (!char.IsWhiteSpace(text[next]))
        {
            return false;
        }

        while (next < text.Length && char.IsWhiteSpace(text[next]))
        {
            next++;
        }

        return next < text.Length && char.IsUpper(text[next]);
    }

    private static void Flush(StringBuilder current, char end, List<Clause> clauses)
    {
        var text = current.ToString().Trim();
        current.Clear();
        if (text.Length > 0)
        {
            clauses.Add(new Clause(text, end));
        }
    }

    /// <summary>The kept clauses, re-punctuated: their own separators between, a period at the end.</summary>
    private static string Join(List<Clause> clauses)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < clauses.Count; i++)
        {
            var text = clauses[i].Text;
            if (i == 0)
            {
                text = Capitalize(text);
            }

            sb.Append(text);
            sb.Append(i == clauses.Count - 1 ? '.' : clauses[i].End);
            if (i < clauses.Count - 1)
            {
                sb.Append(' ');
            }
        }

        return sb.ToString();
    }

    /// <summary>A preference as a preferred-list item: the qualification itself, without "is preferred".</summary>
    private static string AsPreferredItem(string clause)
    {
        var text = PreferredLabel().Replace(clause, "");
        text = SomePositionsPrefer().Replace(text, "");
        text = ParenthesizedPreferred().Replace(text, "");
        text = TrailingPreferred().Replace(text, "");
        text = ForSomePositions().Replace(text, "");
        text = Spaces().Replace(text, " ").Trim().TrimEnd('.', ';', ',', ':').Trim();
        return text.Length == 0 ? "" : Capitalize(text) + ".";
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// An existing preferred item already says this — the same words, or a more specific version
    /// ("Advanced degree in a related field" covers "Advanced degree"). Compared without
    /// "or equivalent experience", which may not have been added yet.
    /// </summary>
    private static bool Covers(string existing, string candidate)
    {
        var e = Bare(existing);
        var c = Bare(candidate);
        return string.Equals(e, c, StringComparison.OrdinalIgnoreCase)
               || (e.StartsWith(c, StringComparison.OrdinalIgnoreCase) && e.Length > c.Length && !char.IsLetterOrDigit(e[c.Length]));
    }

    private static string Bare(string text)
    {
        var t = text.Trim().TrimEnd('.').Trim();
        return t.EndsWith(Equivalent, StringComparison.OrdinalIgnoreCase) ? t[..^Equivalent.Length].Trim() : t;
    }
}

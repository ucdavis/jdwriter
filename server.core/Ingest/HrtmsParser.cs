using System.Globalization;
using System.Text.RegularExpressions;

namespace Server.Core.Ingest;

/// <summary>
/// Parses HRTMS "Job Description Management" HTML exports. Ported from the POC's
/// src/lib/ingest/parseHrtms.ts and verified against fixtures/hrtms.hashes.json over all 1,367
/// exports in the corpus.
///
/// The original has no DOM dependency — it is regex and string work throughout — so this is close
/// to a line-for-line port. Two details in it are invisible in the source and load-bearing:
/// the whitespace class in <see cref="StripTags"/> contains a literal U+00A0 non-breaking space,
/// and the bullet-strip class contains a literal U+2022 bullet.
///
/// Label/value pairing rides on the fact that HRTMS lays every field out as a label cell followed
/// by an adjacent value cell, which is why this reads table cells rather than the flattened text
/// for anything structural.
/// </summary>
public static partial class HrtmsParser
{
    // ---------------------------------------------------------------- regexes

    [GeneratedRegex(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<td\b[^>]*>(.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TableCell();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();

    /// <summary>Space, tab, and a literal non-breaking space. The last one is not decorative.</summary>
    [GeneratedRegex("[ \t ]+")]
    private static partial Regex HorizontalSpace();

    [GeneratedRegex(@"\s+")]
    private static partial Regex AnyWhitespace();

    [GeneratedRegex("&#xa0;|&nbsp;", RegexOptions.IgnoreCase)]
    private static partial Regex NbspEntity();

    [GeneratedRegex(@"&#(\d+);")]
    private static partial Regex DecimalEntity();

    [GeneratedRegex("&#x([0-9a-f]+);", RegexOptions.IgnoreCase)]
    private static partial Regex HexEntity();

    [GeneratedRegex("&#39;|&apos;")]
    private static partial Regex ApostropheEntity();

    [GeneratedRegex(@"^[:\s]+")]
    private static partial Regex LeadingColonOrSpace();

    [GeneratedRegex(@"^[=\-@:•\s]+")]
    private static partial Regex LeadingBulletMarker();

    [GeneratedRegex("=?-@|•|·")]
    private static partial Regex HasBulletMarker();

    [GeneratedRegex(@"\s*(?:=?-@|•|·)\s*")]
    private static partial Regex BulletSplit();

    /// <summary>Sentence split: keep the terminator, break before a capital.</summary>
    [GeneratedRegex(@"(?<=[.;])\s+(?=[A-Z])")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"(?:^|\s)-\s*|-(?=[A-Z])")]
    private static partial Regex LicenseSplit();

    [GeneratedRegex(@"^%\s*time$", RegexOptions.IgnoreCase)]
    private static partial Regex PercentTimeHeader();

    [GeneratedRegex(@"^(\d{1,3})\s*%$")]
    private static partial Regex PercentCell();

    [GeneratedRegex("^(function|duties)$", RegexOptions.IgnoreCase)]
    private static partial Regex ResponsibilityHeaderLabel();

    [GeneratedRegex(@"^\s*Work Experience\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingWorkExperience();

    // ---------------------------------------------------------------- low-level helpers

    private static string DecodeEntities(string s)
    {
        s = NbspEntity().Replace(s, " ");
        // String.fromCharCode takes a UTF-16 code unit, so values above 0xFFFF wrap rather than
        // producing a surrogate pair. Masked to match.
        s = DecimalEntity().Replace(s, m =>
            long.TryParse(m.Groups[1].Value, out var n)
                ? ((char)(ushort)(n & 0xFFFF)).ToString()
                : m.Value);
        s = HexEntity().Replace(s, m =>
            long.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)
                ? ((char)(ushort)(n & 0xFFFF)).ToString()
                : m.Value);
        s = s.Replace("&amp;", "&", StringComparison.Ordinal);
        s = s.Replace("&lt;", "<", StringComparison.Ordinal);
        s = s.Replace("&gt;", ">", StringComparison.Ordinal);
        s = s.Replace("&quot;", "\"", StringComparison.Ordinal);
        s = ApostropheEntity().Replace(s, "'");
        s = s.Replace("&rsquo;", "’", StringComparison.Ordinal);
        s = s.Replace("&ndash;", "–", StringComparison.Ordinal);
        return s;
    }

    private static string StripTags(string s) =>
        HorizontalSpace().Replace(DecodeEntities(AnyTag().Replace(s, " ")), " ").Trim();

    /// <summary>
    /// The export as an ordered list of table-cell texts. Label-to-value pairing depends on this
    /// order, so cells are never sorted or deduplicated.
    /// </summary>
    private static List<string> CellTexts(string html)
    {
        var body = ScriptOrStyle().Replace(html, " ");
        var cells = new List<string>();
        foreach (Match m in TableCell().Matches(body))
        {
            cells.Add(StripTags(m.Groups[1].Value));
        }

        return cells;
    }

    /// <summary>One long normalized text stream, for anchored section extraction.</summary>
    private static string FullText(string html) => StripTags(ScriptOrStyle().Replace(html, " "));

    private static string Norm(string t) => AnyWhitespace().Replace(t, " ").Trim().ToLowerInvariant();

    /// <summary>
    /// The value belonging to a label: either trailing text in the same cell, or the next
    /// non-empty cell.
    /// </summary>
    private static string ValueAfter(List<string> cells, string label)
    {
        var target = Norm(label);

        for (var i = 0; i < cells.Count; i++)
        {
            var c = Norm(cells[i]);
            if (c != target && !c.StartsWith(target, StringComparison.Ordinal))
            {
                continue;
            }

            // Sliced from the RAW cell by the RAW label length, matching the reference. The
            // normalized forms are only used to find the cell, never to cut it.
            var inline = cells[i].Length >= label.Length
                ? LeadingColonOrSpace().Replace(cells[i][label.Length..], "").Trim()
                : "";
            if (inline.Length > 0)
            {
                return inline;
            }

            for (var j = i + 1; j < cells.Count; j++)
            {
                if (cells[j].Trim().Length > 0)
                {
                    return cells[j].Trim();
                }
            }

            return "";
        }

        return "";
    }

    /// <summary>Tri-state: anything that is neither yes nor no means the export did not say.</summary>
    private static bool? YesNo(string v)
    {
        var t = v.Trim().ToLowerInvariant();
        if (t.StartsWith("yes", StringComparison.Ordinal))
        {
            return true;
        }

        return t.StartsWith("no", StringComparison.Ordinal) ? false : null;
    }

    /// <summary>
    /// Slice the full text between anchor phrases, case-insensitively. Offsets come from the
    /// lowercased copy but the slice is taken from the original, which is safe only because
    /// lowercasing preserves length for this content.
    /// </summary>
    private static string Section(string text, string start, params string[] ends)
    {
        var lower = text.ToLowerInvariant();
        var i = lower.IndexOf(start.ToLowerInvariant(), StringComparison.Ordinal);
        if (i < 0)
        {
            return "";
        }

        var from = i + start.Length;
        var end = text.Length;
        foreach (var e in ends)
        {
            var j = lower.IndexOf(e.ToLowerInvariant(), from, StringComparison.Ordinal);
            if (j >= 0 && j < end)
            {
                end = j;
            }
        }

        return text[from..end].Trim();
    }

    /// <summary>
    /// Split a blob into bullets. HRTMS is inconsistent: some exports use "-@" / "=-@" / bullet
    /// markers, others write plain sentences separated by ". ". Markers win when present;
    /// otherwise fall back to sentence splitting so both styles yield clean bullets.
    /// </summary>
    private static List<string> Bullets(string blob)
    {
        if (string.IsNullOrEmpty(blob))
        {
            return [];
        }

        var cleaned = AnyWhitespace().Replace(blob, " ").Trim();
        var parts = HasBulletMarker().IsMatch(cleaned)
            ? BulletSplit().Split(cleaned)
            : SentenceSplit().Split(cleaned);

        return CleanParts(parts);
    }

    /// <summary>
    /// License and certification lists separate on "-", often with no surrounding space
    /// ("...6 months of hire-CA Driver's License..."), which the generic splitter would run
    /// together into one bullet.
    /// </summary>
    private static List<string> LicenseBullets(string blob)
    {
        if (string.IsNullOrEmpty(blob))
        {
            return [];
        }

        var cleaned = AnyWhitespace().Replace(blob, " ").Trim();
        if (HasBulletMarker().IsMatch(cleaned))
        {
            return Bullets(cleaned);
        }

        return CleanParts(LicenseSplit().Split(cleaned));
    }

    /// <summary>
    /// Trim markers and collapse whitespace, dropping fragments of two characters or fewer —
    /// those are split artifacts, not requirements.
    /// </summary>
    private static List<string> CleanParts(IEnumerable<string> parts)
    {
        var result = new List<string>();
        foreach (var p in parts)
        {
            var b = AnyWhitespace().Replace(LeadingBulletMarker().Replace(p, ""), " ").Trim();
            if (b.Length > 2)
            {
                result.Add(b);
            }
        }

        return result;
    }

    /// <summary>
    /// Responsibilities live in a clean three-column table: "% TIME | Function | Duties". Reading
    /// the cells directly avoids the flattened-text ambiguity between a Title Case function name
    /// and the duty sentences that follow it.
    /// </summary>
    private static List<HrtmsResponsibility> ParseResponsibilities(List<string> cells)
    {
        var header = cells.FindIndex(c => PercentTimeHeader().IsMatch(c.Trim()));
        if (header < 0)
        {
            return [];
        }

        var result = new List<HrtmsResponsibility>();
        for (var i = header + 1; i < cells.Count; i++)
        {
            var trimmed = cells[i].Trim();
            var pm = PercentCell().Match(trimmed);
            if (!pm.Success)
            {
                // Stop once past the table: a non-percent, non-empty cell that is not one of the
                // header labels means the next section has started.
                if (result.Count > 0 && trimmed.Length > 0 && !ResponsibilityHeaderLabel().IsMatch(trimmed))
                {
                    break;
                }

                continue;
            }

            result.Add(new HrtmsResponsibility
            {
                Pct = int.Parse(pm.Groups[1].Value, CultureInfo.InvariantCulture),
                FunctionName = (i + 1 < cells.Count ? cells[i + 1] : "").Trim(),
                Duties = Bullets(i + 2 < cells.Count ? cells[i + 2] : ""),
            });
            i += 2;
        }

        return result;
    }

    private static HrtmsQualifications ParseQualifications(string text)
    {
        var licBlob = Section(text, "Additional Minimum License/Certification", "Education", "Work Experience");

        return new HrtmsQualifications
        {
            Licenses = LicenseBullets(licBlob),
            DriversLicenseRequired = YesNo(
                Section(text, "Does the position require a driver", "Additional Minimum", "Education")),
            Education = LeadingWorkExperience()
                .Replace(Section(text, "Education", "Work Experience", "Minimum Work Experience"), "")
                .Trim(),
            MinExperience = Bullets(
                Section(text, "Minimum Work Experience", "Knowledge, Skills", "Knowledge, Skills, and Abilities")),
            KsaMin = Bullets(
                Section(text, "Minimum Knowledge, Skills, and Abilities (KSA)", "Preferred Knowledge")),
            KsaPref = Bullets(
                Section(text, "Preferred Knowledge, Skills, and Abilities (KSA)",
                    "Conditions of Employment", "Reporting and Background")),
        };
    }

    /// <summary>
    /// The physical rows of the PEM grid. These grids are unmarked throughout the current corpus,
    /// so the shape is emitted with null bands and <c>Populated = false</c> rather than being
    /// omitted — an absent grid and an unmarked one are different claims.
    /// </summary>
    private static readonly string[] PhysicalRows =
    [
        "Standing", "Walking", "Sitting", "Lifting/Carrying 0-25 Lbs",
        "Lifting/Carrying 26-50 lbs", "Lifting/Carrying over 50 lbs",
        "Pushing/Pulling 0-25 Lbs", "Pushing/Pulling 26-50 lbs",
        "Pushing/Pulling over 50 lbs", "Bending/Stooping", "Squatting/Kneeling",
        "Twisting", "Climbing", "Reaching overhead", "Keyboard use/repetitive motion",
    ];

    private static HrtmsPem EmptyPem() => new()
    {
        Populated = false,
        Physical = [.. PhysicalRows.Select(r => (r, (string?)null))],
        Environmental = [],
        Mental = [],
    };

    // ---------------------------------------------------------------- entry point

    public static HrtmsRecord Parse(string html, string sourceFile)
    {
        var cells = CellTexts(html);
        var text = FullText(html);

        return new HrtmsRecord
        {
            SourceFile = sourceFile,

            BusinessUnit = ValueAfter(cells, "Business Unit (Location):"),
            Division = ValueAfter(cells, "Division Name:"),
            DepartmentName = ValueAfter(cells, "Department Name:"),
            DepartmentCode = ValueAfter(cells, "Department Code:"),

            JdNumber = ValueAfter(cells, "Job Description Number"),
            UcPathPositionNumber = ValueAfter(cells, "UCPath Position Number"),
            UcJobTitle = ValueAfter(cells, "UC Job Title:"),
            UcJobCode = ValueAfter(cells, "UC Job Code:"),
            WorkingTitle = ValueAfter(cells, "Working Title:"),
            CtJobFamily = ValueAfter(cells, "CT Job Family:"),
            CtJobFunction = ValueAfter(cells, "CT Job Function:"),
            PersonnelProgram = ValueAfter(cells, "Classified Indicator Descr (Pers Prog):"),
            SalaryGrade = ValueAfter(cells, "Salary Grade:"),
            FlsaStatus = ValueAfter(cells, "Job FLSA Status:"),
            UnionCode = ValueAfter(cells, "Union Code (Collective Bargaining Unit):"),

            Supervises = YesNo(ValueAfter(cells, "Does this position supervise employees?")),
            Leads = YesNo(ValueAfter(cells, "Does this position lead employees?")),
            ReportsToPositionNumber = ValueAfter(cells, "Reports to UCPath Position Number:"),

            JobSummary = Section(text, "Job Summary", "Key Responsibilities", "Total percent of time").Trim(),
            Responsibilities = ParseResponsibilities(cells),

            Qualifications = ParseQualifications(text),
            ConditionsOfEmployment = Bullets(
                Section(text, "Other Special Conditions of Employment",
                    "Smoke Free Work Environment", "PHYSICAL, ENVIRONMENTAL")),
            WorkEnvironment = Bullets(
                Section(text, "Work Environment", "Performs Work Outdoors", "PHYSICAL, ENVIRONMENTAL")),
            WorksOutdoorsOver50pct = YesNo(
                Section(text, "Performs Work Outdoors More than 50% of Time:", "PHYSICAL, ENVIRONMENTAL")),
            Pem = EmptyPem(),
        };
    }
}

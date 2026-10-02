using Server.Core.Domain;

namespace Server.Core.Ingest;

/// <summary>
/// Projects a stored <see cref="JobDescription"/> back into the <see cref="HrtmsRecord"/> shape the
/// analysis services were written against.
///
/// The services take the parser's output type because that is what they consumed in the POC, where
/// the corpus was re-parsed from disk on every run. The corpus lives in SQL now, and a deployed app
/// has no 150MB export directory — so the runtime path reads entities and projects them here rather
/// than rescanning a filesystem that will not exist.
///
/// A projection, not a round trip: it carries the fields the analysis actually reads. The PEM grid
/// is deliberately omitted, being unpopulated across the entire corpus, and nothing here is used to
/// write back to the database.
/// </summary>
public static class JobDescriptionMapper
{
    public static HrtmsRecord ToHrtmsRecord(this JobDescription jd) => new()
    {
        SourceFile = jd.SourceFile,

        BusinessUnit = jd.BusinessUnit,
        Division = jd.Division,
        DepartmentName = jd.DepartmentName,
        DepartmentCode = jd.DepartmentCode,

        JdNumber = jd.JdNumber,
        UcPathPositionNumber = jd.UcPathPositionNumber,
        UcJobTitle = jd.UcJobTitle,
        UcJobCode = jd.UcJobCode,
        OriginalUcJobCode = jd.OriginalUcJobCode,
        WorkingTitle = jd.WorkingTitle,
        CtJobFamily = jd.CtJobFamily,
        CtJobFunction = jd.CtJobFunction,
        PersonnelProgram = jd.PersonnelProgram,
        SalaryGrade = jd.SalaryGrade,
        FlsaStatus = jd.FlsaStatus,
        UnionCode = jd.UnionCode,

        Supervises = jd.Supervises,
        Leads = jd.Leads,
        ReportsToPositionNumber = jd.ReportsToPositionNumber,
        WorksOutdoorsOver50pct = jd.WorksOutdoorsOver50pct,

        JobSummary = jd.JobSummary,

        // Ordinal ordering is load-bearing: SQL has no inherent row order, and a responsibility
        // list read back scrambled is a scrambled job description.
        Responsibilities = jd.Responsibilities
            .OrderBy(r => r.Ordinal)
            .Select(r => new HrtmsResponsibility
            {
                Pct = r.Pct,
                FunctionName = r.FunctionName,
                Duties = r.Duties.OrderBy(d => d.Ordinal).Select(d => d.Text).ToList(),
            })
            .ToList(),

        Qualifications = new HrtmsQualifications
        {
            Licenses = Items(jd, JdQualificationKind.License),
            DriversLicenseRequired = jd.DriversLicenseRequired,
            // Education is a single free-text field in the source format, so at most one row.
            Education = Items(jd, JdQualificationKind.Education).FirstOrDefault() ?? "",
            MinExperience = Items(jd, JdQualificationKind.MinExperience),
            KsaMin = Items(jd, JdQualificationKind.KsaMin),
            KsaPref = Items(jd, JdQualificationKind.KsaPref),
        },

        ConditionsOfEmployment = Items(jd, JdQualificationKind.ConditionOfEmployment),
        WorkEnvironment = Items(jd, JdQualificationKind.WorkEnvironment),

        Pem = new HrtmsPem { Populated = jd.PemPopulated },
    };

    private static List<string> Items(JobDescription jd, JdQualificationKind kind) =>
        jd.Qualifications
            .Where(q => q.Kind == kind)
            .OrderBy(q => q.Ordinal)
            .Select(q => q.Text)
            .ToList();

    /// <summary>
    /// A parsed export as an entity, filed under <paramref name="resolvedCode"/> — the live class
    /// after supersession. What the export itself said is kept in <c>OriginalUcJobCode</c>, so the
    /// remap is auditable rather than invisible. Shared by the CLI's corpus load and admin upload.
    /// </summary>
    public static JobDescription ToEntity(this HrtmsRecord r, string resolvedCode)
    {
        var jd = new JobDescription
        {
            SourceFile = r.SourceFile,
            BusinessUnit = r.BusinessUnit,
            Division = r.Division,
            DepartmentName = r.DepartmentName,
            DepartmentCode = r.DepartmentCode,
            JdNumber = r.JdNumber,
            UcPathPositionNumber = r.UcPathPositionNumber,
            UcJobTitle = r.UcJobTitle,
            UcJobCode = resolvedCode,
            OriginalUcJobCode = string.Equals(r.UcJobCode, resolvedCode, StringComparison.Ordinal) ? null : r.UcJobCode,
            WorkingTitle = r.WorkingTitle,
            CtJobFamily = r.CtJobFamily,
            CtJobFunction = r.CtJobFunction,
            PersonnelProgram = r.PersonnelProgram,
            SalaryGrade = r.SalaryGrade,
            FlsaStatus = r.FlsaStatus,
            UnionCode = r.UnionCode,
            Supervises = r.Supervises,
            Leads = r.Leads,
            ReportsToPositionNumber = r.ReportsToPositionNumber,
            WorksOutdoorsOver50pct = r.WorksOutdoorsOver50pct,
            JobSummary = r.JobSummary,
            PemPopulated = r.Pem.Populated,
            DriversLicenseRequired = r.Qualifications.DriversLicenseRequired,
        };

        for (var i = 0; i < r.Responsibilities.Count; i++)
        {
            var src = r.Responsibilities[i];
            var resp = new JdResponsibility
            {
                Ordinal = i,
                Pct = src.Pct,
                FunctionName = src.FunctionName,
            };

            for (var d = 0; d < src.Duties.Count; d++)
            {
                resp.Duties.Add(new JdDuty { Ordinal = d, Text = src.Duties[d] });
            }

            jd.Responsibilities.Add(resp);
        }

        AddQuals(jd, JdQualificationKind.License, r.Qualifications.Licenses);
        if (!string.IsNullOrWhiteSpace(r.Qualifications.Education))
        {
            // Education is a single free-text field in this format, not a list.
            jd.Qualifications.Add(new JdQualificationItem
            {
                Kind = JdQualificationKind.Education,
                Ordinal = 0,
                Text = r.Qualifications.Education,
            });
        }

        AddQuals(jd, JdQualificationKind.MinExperience, r.Qualifications.MinExperience);
        AddQuals(jd, JdQualificationKind.KsaMin, r.Qualifications.KsaMin);
        AddQuals(jd, JdQualificationKind.KsaPref, r.Qualifications.KsaPref);
        AddQuals(jd, JdQualificationKind.ConditionOfEmployment, r.ConditionsOfEmployment);
        AddQuals(jd, JdQualificationKind.WorkEnvironment, r.WorkEnvironment);

        // Only MARKED cells become rows. Every export in the current corpus ships the grid blank,
        // so this produces nothing today — an unmarked row is the absence of a claim, not a null.
        AddPem(jd, PemAxis.Physical, r.Pem.Physical);
        AddPem(jd, PemAxis.Environmental, r.Pem.Environmental);
        AddPem(jd, PemAxis.Mental, r.Pem.Mental);

        return jd;
    }

    private static void AddQuals(JobDescription jd, JdQualificationKind kind, List<string> texts)
    {
        for (var i = 0; i < texts.Count; i++)
        {
            jd.Qualifications.Add(new JdQualificationItem { Kind = kind, Ordinal = i, Text = texts[i] });
        }
    }

    private static void AddPem(JobDescription jd, PemAxis axis, List<(string Row, string? Band)> rows)
    {
        foreach (var (row, band) in rows)
        {
            if (string.IsNullOrWhiteSpace(band))
            {
                continue;
            }

            if (Enum.TryParse<PemBand>(band, ignoreCase: true, out var parsed))
            {
                jd.PemEntries.Add(new JdPemEntry { Axis = axis, RowName = row, Band = parsed });
            }
        }
    }
}

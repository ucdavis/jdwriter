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
}

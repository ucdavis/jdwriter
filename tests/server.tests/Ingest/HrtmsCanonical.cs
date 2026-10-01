using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Server.Core.Ingest;

namespace Server.Tests.Ingest;

/// <summary>
/// The canonical projection of a parsed record, byte-identical to the generator's in
/// scripts/fixtures/hrtms.ts.
///
/// Explicitly NOT JSON. Hashing JSON would mean reproducing JavaScript's exact escaping,
/// property order and number formatting from .NET — debugging two serializers instead of one
/// parser. This projection has a fixed field order, one `path\tvalue` line per scalar, and three
/// escapes, so a hash mismatch means the PARSE differs, which is the only thing worth detecting.
///
/// sourceFile is excluded: it is passed into the parser, not extracted from the document, and the
/// filename embeds a UCPath position number.
/// </summary>
public static class HrtmsCanonical
{
    /// <summary>
    /// Null sentinel. A NUL prefix makes it impossible for real parsed text to collide with it.
    /// </summary>
    private const string NullToken = "\u0000null";

    private static string Esc(string s) => s
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal);

    public static string Project(HrtmsRecord r)
    {
        var lines = new List<string>();

        void Put(string k, string? v) => lines.Add($"{k}\t{(v is null ? NullToken : Esc(v))}");
        void PutBool(string k, bool? v) => lines.Add($"{k}\t{(v is null ? NullToken : v.Value ? "true" : "false")}");
        void PutNum(string k, int? v) =>
            lines.Add($"{k}\t{(v is null ? NullToken : v.Value.ToString(CultureInfo.InvariantCulture))}");

        void PutList(string k, List<string> xs)
        {
            PutNum($"{k}.length", xs.Count);
            for (var i = 0; i < xs.Count; i++)
            {
                Put($"{k}[{i}]", xs[i]);
            }
        }

        Put("businessUnit", r.BusinessUnit);
        Put("division", r.Division);
        Put("departmentName", r.DepartmentName);
        Put("departmentCode", r.DepartmentCode);
        Put("jdNumber", r.JdNumber);
        Put("ucPathPositionNumber", r.UcPathPositionNumber);
        Put("ucJobTitle", r.UcJobTitle);
        Put("ucJobCode", r.UcJobCode);
        Put("originalUcJobCode", r.OriginalUcJobCode);
        Put("workingTitle", r.WorkingTitle);
        Put("ctJobFamily", r.CtJobFamily);
        Put("ctJobFunction", r.CtJobFunction);
        Put("personnelProgram", r.PersonnelProgram);
        Put("salaryGrade", r.SalaryGrade);
        Put("flsaStatus", r.FlsaStatus);
        Put("unionCode", r.UnionCode);
        Put("reportsToPositionNumber", r.ReportsToPositionNumber);
        Put("jobSummary", r.JobSummary);

        PutBool("supervises", r.Supervises);
        PutBool("leads", r.Leads);
        PutBool("worksOutdoorsOver50pct", r.WorksOutdoorsOver50pct);

        PutNum("responsibilities.length", r.Responsibilities.Count);
        for (var i = 0; i < r.Responsibilities.Count; i++)
        {
            var resp = r.Responsibilities[i];
            PutNum($"responsibilities[{i}].pct", resp.Pct);
            Put($"responsibilities[{i}].functionName", resp.FunctionName);
            PutList($"responsibilities[{i}].duties", resp.Duties);
        }

        PutList("qualifications.licenses", r.Qualifications.Licenses);
        PutBool("qualifications.driversLicenseRequired", r.Qualifications.DriversLicenseRequired);
        Put("qualifications.education", r.Qualifications.Education);
        PutList("qualifications.minExperience", r.Qualifications.MinExperience);
        PutList("qualifications.ksaMin", r.Qualifications.KsaMin);
        PutList("qualifications.ksaPref", r.Qualifications.KsaPref);

        PutList("conditionsOfEmployment", r.ConditionsOfEmployment);
        PutList("workEnvironment", r.WorkEnvironment);

        PutBool("pem.populated", r.Pem.Populated);
        foreach (var (axis, rows) in new[]
                 {
                     ("physical", r.Pem.Physical),
                     ("environmental", r.Pem.Environmental),
                     ("mental", r.Pem.Mental),
                 })
        {
            PutNum($"pem.{axis}.length", rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                Put($"pem.{axis}[{i}].row", rows[i].Row);
                Put($"pem.{axis}[{i}].band", rows[i].Band);
            }
        }

        return string.Join('\n', lines);
    }

    public static string Sha256(string s) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    /// <summary>
    /// The fixture's file id: the first eight hex characters of sha256 over the corpus-relative
    /// path, with forward slashes. Recomputable, so the test does not need the gitignored
    /// id-to-path map.
    /// </summary>
    public static string FileId(string relativePath) => Sha256(relativePath)[..8];
}

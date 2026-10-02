using Server.Core.Domain;
using Server.Core.Profiles;

namespace Server.Core.Jd;

/// <summary>
/// Whether, and how, a finished JD becomes evidence for its class.
///
/// A JD written in the app is added to the class's corpus at the final stage only when it carries
/// something the envelope does not already say — and only when the envelope check judged the
/// author's additions to belong in the class. An unchanged JD would just echo the envelope back
/// into the data it was derived from; an out-of-envelope one would pull the class toward a
/// neighbour.
/// </summary>
public static class CorpusContribution
{
    public static string NotPublishable => "Not added to the corpus — the time does not total 100%.";
    public static string OutOfEnvelope => "Not added to the corpus — the envelope check found additions that belong to a different class.";
    public static string Unchecked => "Not added to the corpus — the additions were not envelope-checked.";
    public static string Unchanged => "Not added to the corpus — it matches the class envelope, so it adds no new evidence.";
    public static string Added => "Added to this class's corpus. It counts the next time the class is rebuilt.";

    /// <summary>Null when the JD should be added; otherwise why not.</summary>
    public static string? Exclusion(AssembledJd assembled, BuildInputs inputs, JobEnvelope? envelope, EnvelopeVerdict? verdict)
    {
        if (!assembled.CanPublish)
        {
            return NotPublishable;
        }

        if (verdict == EnvelopeVerdict.OutOfEnvelope)
        {
            return OutOfEnvelope;
        }

        if (inputs.AddedItems.Count > 0 && verdict == null)
        {
            return Unchecked;
        }

        return Differs(inputs, envelope) ? null : Unchanged;
    }

    /// <summary>
    /// Did the author change anything relative to the envelope: add an item, drop or reword a
    /// responsibility or duty, move a percentage, or drop a qualification line?
    /// </summary>
    public static bool Differs(BuildInputs inputs, JobEnvelope? envelope)
    {
        if (envelope == null || inputs.AddedItems.Count > 0)
        {
            return true;
        }

        var e = EnvelopeWire.From(envelope);
        static string N(string s) => s.Trim();
        static bool Same(List<string> a, List<string> b) => a.Select(N).SequenceEqual(b.Select(N));

        if (inputs.KeptResponsibilities.Count != e.KeyResponsibilities.Count)
        {
            return true;
        }

        for (var i = 0; i < e.KeyResponsibilities.Count; i++)
        {
            var kept = inputs.KeptResponsibilities[i];
            var orig = e.KeyResponsibilities[i];
            if (N(kept.FunctionName) != N(orig.FunctionName) || kept.PctTime != orig.PctTime || !Same(kept.Duties, orig.Duties))
            {
                return true;
            }
        }

        return !Same(inputs.KeptCerts, e.RequiredCertifications)
               || !Same(inputs.KeptEducation, e.Education)
               || !Same(inputs.KeptWorkExperience, e.WorkExperience)
               || !Same(inputs.KeptMinKSA, e.MinQualifications)
               || !Same(inputs.KeptPrefKSA, e.PrefQualifications)
               || !Same(inputs.KeptWorkEnvironment, e.WorkEnvironment);
    }

    /// <summary>The finished JD — after compliance edits — as a corpus record.</summary>
    public static JobDescription ToCorpusRecord(AssembledJd a, int authoredJdId)
    {
        var jd = new JobDescription
        {
            // No HRTMS file exists for an authored JD; the name identifies the source, not a position.
            SourceFile = $"authored/JD-{authoredJdId}",
            Origin = CorpusOrigin.Authored,
            AuthoredJdId = authoredJdId,
            AddedAt = DateTimeOffset.UtcNow,
            UcJobCode = a.UcJobCode,
            UcJobTitle = a.Title,
            WorkingTitle = a.WorkingTitle,
            DepartmentName = a.Department,
            SalaryGrade = a.SalaryGrade ?? "",
            FlsaStatus = a.FlsaStatus ?? "",
            UnionCode = a.BargainingUnit ?? "",
            JobSummary = a.Jd.JobSummary,
        };

        for (var i = 0; i < a.Jd.KeyResponsibilities.Count; i++)
        {
            var r = a.Jd.KeyResponsibilities[i];
            var resp = new JdResponsibility { Ordinal = i, Pct = r.PctTime, FunctionName = r.FunctionName };
            for (var d = 0; d < r.Duties.Count; d++)
            {
                resp.Duties.Add(new JdDuty { Ordinal = d, Text = r.Duties[d] });
            }

            jd.Responsibilities.Add(resp);
        }

        void Add(JdQualificationKind kind, IEnumerable<string> texts)
        {
            var i = 0;
            foreach (var t in texts)
            {
                jd.Qualifications.Add(new JdQualificationItem { Kind = kind, Ordinal = i++, Text = t });
            }
        }

        Add(JdQualificationKind.License, a.Jd.LicensesCertifications);
        // Education is a single free-text field in the corpus format.
        if (a.Jd.Education.Count > 0)
        {
            Add(JdQualificationKind.Education, [string.Join("; ", a.Jd.Education)]);
        }

        Add(JdQualificationKind.MinExperience, a.Jd.WorkExperience);
        Add(JdQualificationKind.KsaMin, a.Jd.MinKSA);
        Add(JdQualificationKind.KsaPref, a.Jd.PrefKSA);
        Add(JdQualificationKind.ConditionOfEmployment, a.Jd.ConditionsOfEmployment);
        Add(JdQualificationKind.WorkEnvironment, a.Jd.WorkEnvironment);
        return jd;
    }
}

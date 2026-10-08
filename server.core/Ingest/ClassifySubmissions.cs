using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Intake;
using Server.Core.Titles;

namespace Server.Core.Ingest;

/// <summary>
/// Descriptions submitted on the Classify page become corpus evidence (decided 2026-10-02):
/// every submission is filed into a class, and its original kept for future reparsing.
///
/// An HRTMS export is filed under the job code it states — the export is authoritative about its
/// own class. Anything else (a PD, a Word file, pasted text) has no stated class, so it is filed
/// under the classifier's top-ranked match, as the classifier distilled it. Either way it joins the
/// corpus immediately and counts at the class's next rebuild, where the admin queue lists it.
/// </summary>
public sealed partial class ClassifySubmissions
{
    [GeneratedRegex(@"<\s*(html|body|table|div|td|p|span)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LooksLikeHtml();

    private readonly AppDbContext _db;
    private readonly ITitleCodeService _titleCodes;

    public ClassifySubmissions(AppDbContext db, ITitleCodeService titleCodes)
    {
        _db = db;
        _titleCodes = titleCodes;
    }

    /// <summary>
    /// File one classified submission. Returns the class code it was filed under, or null when it
    /// was not filed (already submitted, or nothing to file it under).
    /// </summary>
    public async Task<string?> FileAsync(
        string description, Classification result, int? userId, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(description);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (await _db.CorpusUploads.AnyAsync(u => u.Sha256 == sha, ct))
        {
            return null; // the same text was submitted before; one copy is enough
        }

        var now = DateTimeOffset.UtcNow;
        var original = new CorpusUpload
        {
            FileName = $"Classify submission {now:yyyy-MM-dd HH:mm}",
            Sha256 = sha,
            Content = bytes,
            SizeBytes = bytes.Length,
            Source = CorpusUploadSource.Classify,
            UploadedByUserId = userId,
            UploadedAt = now,
        };

        var record = await FromExportAsync(description, ct) ?? FromDistilled(result);
        if (record == null)
        {
            original.Status = CorpusUploadStatus.Failed;
            original.Error = "No class to file it under — the classifier returned no match.";
            _db.CorpusUploads.Add(original);
            await _db.SaveChangesAsync(ct);
            return null;
        }

        _db.CorpusUploads.Add(original);
        await _db.SaveChangesAsync(ct); // assigns the original's id, which names the corpus record

        record.Origin = CorpusOrigin.Classify;
        record.AddedAt = now;
        if (string.IsNullOrEmpty(record.SourceFile) || record.SourceFile == "pasted")
        {
            record.SourceFile = $"classify/submission-{original.Id}";
        }

        // A re-submitted export of the same position replaces the earlier SUBMISSION — and only that.
        // The position number comes from pasted text any author controls, so it must never reach
        // records it did not create: the CLI load, admin uploads and authored JDs are not replaced
        // from here.
        if (!string.IsNullOrEmpty(record.UcPathPositionNumber))
        {
            _db.JobDescriptions.RemoveRange(await _db.JobDescriptions
                .Where(j => j.Origin == CorpusOrigin.Classify
                            && j.UcJobCode == record.UcJobCode
                            && j.UcPathPositionNumber == record.UcPathPositionNumber)
                .ToListAsync(ct));
        }

        _db.JobDescriptions.Add(record);
        original.Status = CorpusUploadStatus.Filed;
        original.UcJobCode = record.UcJobCode;
        original.UcJobTitle = record.UcJobTitle;
        original.OriginalUcJobCode = record.OriginalUcJobCode;
        await _db.SaveChangesAsync(ct);
        return record.UcJobCode;
    }

    /// <summary>An HRTMS export, filed under the live class for the code it states.</summary>
    private async Task<JobDescription?> FromExportAsync(string text, CancellationToken ct)
    {
        if (!LooksLikeHtml().IsMatch(text))
        {
            return null;
        }

        HrtmsRecord parsed;
        try
        {
            parsed = HrtmsParser.Parse(text, "pasted");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(parsed.UcJobCode))
        {
            return null;
        }

        var index = await _titleCodes.GetAsync(ct);
        var resolved = index.ResolveCode(parsed.UcJobCode);
        return parsed.ToEntity(resolved);
    }

    /// <summary>Anything else, as the classifier read it, filed under its top-ranked class.</summary>
    private static JobDescription? FromDistilled(Classification result)
    {
        var top = result.Matches.FirstOrDefault();
        if (top == null || string.IsNullOrWhiteSpace(top.UcJobCode))
        {
            return null;
        }

        var jd = ToJobDescription(result.Distilled);
        jd.UcJobCode = top.UcJobCode;
        jd.UcJobTitle = top.Title;
        return jd;
    }

    /// <summary>
    /// A distilled description as a JD record, unfiled: the shape both the corpus and "Start a JD
    /// from this class" read. % time is rounded to whole points; the JD keeps the description's own
    /// split even when it does not sum to 100, because real ones often do not.
    /// </summary>
    public static JobDescription ToJobDescription(DistilledJd d)
    {
        var jd = new JobDescription
        {
            WorkingTitle = d.WorkingTitle,
            JobSummary = d.Summary,
            Supervises = d.Supervises == "yes" ? true : d.Supervises == "no" ? false : null,
        };

        for (var i = 0; i < d.Functions.Count; i++)
        {
            var f = d.Functions[i];
            var resp = new JdResponsibility { Ordinal = i, Pct = (int)Math.Round(f.PctTime, MidpointRounding.AwayFromZero), FunctionName = f.Name };
            for (var k = 0; k < f.Duties.Count; k++)
            {
                resp.Duties.Add(new JdDuty { Ordinal = k, Text = f.Duties[k] });
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

        if (d.Education.Count > 0)
        {
            Add(JdQualificationKind.Education, [string.Join("; ", d.Education)]);
        }

        Add(JdQualificationKind.MinExperience, d.Experience);
        Add(JdQualificationKind.KsaMin, d.Ksas);
        return jd;
    }
}

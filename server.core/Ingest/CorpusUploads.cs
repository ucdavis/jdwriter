using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Profiles;
using Server.Core.Titles;

namespace Server.Core.Ingest;

/// <summary>What happened to one uploaded file.</summary>
public sealed class UploadOutcome
{
    public string FileName { get; set; } = "";

    /// <summary>"added", "duplicate" (these exact bytes were uploaded before), or "failed".</summary>
    public string Result { get; set; } = "";

    public string? UcJobCode { get; set; }
    public string? Title { get; set; }
    public string? Error { get; set; }
}

/// <summary>A class with uploaded exports waiting to be ingested.</summary>
public sealed class UploadedClass
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>Set when the class already exists; ingesting refreshes it rather than creating it.</summary>
    public string? ExistingSlug { get; set; }

    public int NewFiles { get; set; }

    /// <summary>JDs already in the corpus for this class.</summary>
    public int CorpusJds { get; set; }
}

/// <summary>
/// Admin uploads of HRTMS exports, for environments that have no export directory on disk — which
/// is every deployed one. Files are stored, parsed on arrival, and ingested per class on request.
/// </summary>
public sealed class CorpusUploads
{
    private readonly AppDbContext _db;
    private readonly ITitleCodeService _titleCodes;
    private readonly IngestPipeline _pipeline;

    public CorpusUploads(AppDbContext db, ITitleCodeService titleCodes, IngestPipeline pipeline)
    {
        _db = db;
        _titleCodes = titleCodes;
        _pipeline = pipeline;
    }

    /// <summary>
    /// Store and parse uploaded files. Each file is judged on its own: one bad file is reported,
    /// not allowed to reject the batch.
    /// </summary>
    public async Task<List<UploadOutcome>> UploadAsync(
        IEnumerable<(string Name, byte[] Bytes)> files, int? userId, CancellationToken ct = default)
    {
        var index = await _titleCodes.GetAsync(ct);
        var outcomes = new List<UploadOutcome>();

        foreach (var (name, bytes) in files)
        {
            var outcome = new UploadOutcome { FileName = name };
            outcomes.Add(outcome);

            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (await _db.CorpusUploads.AnyAsync(u => u.Sha256 == sha, ct)
                || _db.CorpusUploads.Local.Any(u => u.Sha256 == sha))
            {
                outcome.Result = "duplicate";
                continue;
            }

            var upload = new CorpusUpload
            {
                FileName = name,
                Sha256 = sha,
                Content = bytes,
                SizeBytes = bytes.Length,
                UploadedByUserId = userId,
                UploadedAt = DateTimeOffset.UtcNow,
            };

            var problem = Parse(name, bytes, index, out var record);
            if (problem != null)
            {
                upload.Status = CorpusUploadStatus.Failed;
                upload.Error = problem;
                outcome.Result = "failed";
                outcome.Error = problem;
            }
            else
            {
                upload.Status = CorpusUploadStatus.Pending;
                upload.UcJobCode = record!.UcJobCode;
                upload.OriginalUcJobCode = record.OriginalUcJobCode;
                upload.UcJobTitle = record.UcJobTitle;
                outcome.Result = "added";
                outcome.UcJobCode = record.UcJobCode;
                outcome.Title = record.UcJobTitle;
            }

            _db.CorpusUploads.Add(upload);
        }

        await _db.SaveChangesAsync(ct);
        return outcomes;
    }

    /// <summary>Classes with pending uploads, alphabetically.</summary>
    public async Task<List<UploadedClass>> PendingAsync(CancellationToken ct = default)
    {
        var groups = await _db.CorpusUploads.AsNoTracking()
            .Where(u => u.Status == CorpusUploadStatus.Pending && u.UcJobCode != null)
            .GroupBy(u => u.UcJobCode!)
            .Select(g => new { Code = g.Key, Title = g.Max(u => u.UcJobTitle), Count = g.Count() })
            .ToListAsync(ct);

        var codes = groups.Select(g => g.Code).ToList();
        var corpus = await _db.JobDescriptions.AsNoTracking()
            .Where(j => codes.Contains(j.UcJobCode))
            .GroupBy(j => j.UcJobCode)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Code, x => x.Count, ct);
        var profiles = await _db.ClassProfiles.AsNoTracking()
            .Select(p => new { p.UcJobCode, p.Slug, p.Title })
            .ToListAsync(ct);

        return groups
            .Select(g =>
            {
                var profile = profiles.FirstOrDefault(p => TitleCodeIndex.Pad(p.UcJobCode) == TitleCodeIndex.Pad(g.Code));
                return new UploadedClass
                {
                    Code = g.Code,
                    Title = profile?.Title ?? ProfileAggregator.Titleize(g.Title ?? g.Code),
                    ExistingSlug = profile?.Slug,
                    NewFiles = g.Count,
                    CorpusJds = corpus.GetValueOrDefault(g.Code),
                };
            })
            .OrderBy(c => c.Title, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Add a class's pending uploads to the corpus and re-ingest the class from all of its JDs.
    ///
    /// A new export of a position replaces the previous one, matched by UCPath position number
    /// (falling back to the file name when an export states none), so re-exporting a JD revises it
    /// rather than counting the same position twice. The JDs are written before the model work
    /// starts: if ingest then fails, the corpus is already right and the uploads stay pending, so
    /// the admin can simply retry.
    /// </summary>
    public async Task<ClassProfile> IngestAsync(string code, CancellationToken ct = default)
    {
        var index = await _titleCodes.GetAsync(ct);
        var uploads = await _db.CorpusUploads
            .Where(u => u.Status == CorpusUploadStatus.Pending && u.UcJobCode == code)
            .OrderBy(u => u.UploadedAt).ThenBy(u => u.Id)
            .ToListAsync(ct);
        if (uploads.Count == 0)
        {
            throw new InvalidOperationException($"No uploaded JDs are waiting for class {code}.");
        }

        foreach (var upload in uploads)
        {
            if (Parse(upload.FileName, upload.Content, index, out var record) != null)
            {
                continue; // validated on upload; a file cannot become unparseable since
            }

            var position = record!.UcPathPositionNumber;
            var replaced = await _db.JobDescriptions
                .Where(j => j.UcJobCode == record.UcJobCode
                            && (position != "" ? j.UcPathPositionNumber == position : j.SourceFile == record.SourceFile))
                .ToListAsync(ct);
            _db.JobDescriptions.RemoveRange(replaced);
            _db.JobDescriptions.Add(record.ToEntity(record.UcJobCode));
        }

        await _db.SaveChangesAsync(ct);

        var jds = await _db.JobDescriptions.AsNoTracking()
            .Where(j => j.UcJobCode == code)
            .Include(j => j.Responsibilities).ThenInclude(r => r.Duties)
            .Include(j => j.Qualifications)
            .AsSplitQuery()
            .OrderBy(j => j.SourceFile)
            .ToListAsync(ct);
        var records = jds.Select(j => j.ToHrtmsRecord()).ToList();

        var existing = await _db.ClassProfiles.AsNoTracking()
            .Select(p => new { p.UcJobCode, p.Slug })
            .ToListAsync(ct);
        var slug = existing.FirstOrDefault(p => TitleCodeIndex.Pad(p.UcJobCode) == TitleCodeIndex.Pad(code))?.Slug;

        var profile = await _pipeline.IngestRecordsAsync(records, slug, ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var upload in uploads)
        {
            upload.Status = CorpusUploadStatus.Ingested;
            upload.IngestedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        return profile;
    }

    /// <summary>
    /// Parse one export and file it under its live class. Null on success; otherwise a user-facing
    /// reason. The record carries the live code, with what the export said in OriginalUcJobCode.
    /// </summary>
    private static string? Parse(string name, byte[] bytes, TitleCodeIndex index, out HrtmsRecord? record)
    {
        record = null;
        var lower = name.ToLowerInvariant();
        if (!lower.EndsWith(".html", StringComparison.Ordinal) && !lower.EndsWith(".htm", StringComparison.Ordinal))
        {
            return "Not an HRTMS export — expected an .html file.";
        }

        string html;
        using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            html = reader.ReadToEnd();
        }

        HrtmsRecord parsed;
        try
        {
            parsed = HrtmsParser.Parse(html, name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return "Could not be read as an HRTMS job description export.";
        }

        if (string.IsNullOrWhiteSpace(parsed.UcJobCode))
        {
            return "No UC job code found — this does not look like an HRTMS job description export.";
        }

        var sup = index.SupersededBy(parsed.UcJobCode);
        if (sup != null)
        {
            parsed.OriginalUcJobCode = parsed.UcJobCode;
            parsed.UcJobCode = sup.ToCode;
            parsed.UcJobTitle = sup.ToTitle;
        }

        record = parsed;
        return null;
    }
}

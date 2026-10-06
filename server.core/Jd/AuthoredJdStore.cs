using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Core.Jd;

/// <summary>A saved JD in the list view.</summary>
public sealed class SavedJdSummary
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string WorkingTitle { get; set; } = "";
    public string Department { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public AuthoredJdStatus Status { get; set; }
    public int UnallocatedPct { get; set; }

    /// <summary>False for a draft saved before it was ever assembled.</summary>
    public bool Assembled { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One saved JD, in the same shape the build returns (so the same screen renders both), plus who
/// wrote it, when, and what they added beyond the envelope.
/// </summary>
public sealed class SavedJd
{
    public int AuthoredJdId { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string WorkingTitle { get; set; } = "";
    public string Department { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public string? SalaryGrade { get; set; }
    public string? FlsaStatus { get; set; }
    public string? BargainingUnit { get; set; }
    public Jd Jd { get; set; } = new();
    public int UnallocatedPct { get; set; }
    public bool CanPublish => UnallocatedPct == 0;
    public AuthoredJdStatus Status { get; set; }
    public List<ComplianceEditRecord> ComplianceEdits { get; set; } = [];
    public List<string> AuthorAdditions { get; set; } = [];
    public string Notes { get; set; } = "";
    public bool InCorpus { get; set; }
    public string CorpusNote { get; set; } = "";

    /// <summary>The saved build screen, for "Continue editing"; null for JDs saved before drafts existed.</summary>
    public System.Text.Json.JsonElement? DraftState { get; set; }

    /// <summary>False for a draft saved before it was ever assembled.</summary>
    public bool Assembled { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Saved JDs. Every assembly is kept: authors can come back to theirs, admins can see all of
/// them, and they are the material that will later be fed back into the corpus.
/// </summary>
public sealed class AuthoredJdStore
{
    private readonly AppDbContext _db;

    public AuthoredJdStore(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>The most draft state accepted from a client: generous for any real build screen.</summary>
    public const int MaxDraftStateChars = 512 * 1024;

    /// <summary>
    /// Save an assembly. When <paramref name="existingId"/> names a JD this user wrote, it is
    /// updated in place — re-assembling in one build session revises one record rather than
    /// leaving a trail of near-duplicates. Anything else creates a new record. Returns its id.
    /// </summary>
    public async Task<int> SaveAsync(
        AssembledJd assembled,
        BuildInputs inputs,
        ClassProfile profile,
        int? userId,
        int? existingId,
        CancellationToken ct = default,
        string? draftState = null)
    {
        var fresh = assembled.ToEntity(profile.Id, userId, inputs, profile.EnvelopeSource);
        fresh.DraftState = draftState;
        fresh.AssembledAt = DateTimeOffset.UtcNow;
        return await UpsertAsync(fresh, userId, existingId, ct);
    }

    /// <summary>
    /// Save work in progress without assembling it: no model call, always a Draft whatever the
    /// allocation, and never in the corpus — only finished JDs are evidence. Saving a draft over a
    /// JD that was already assembled turns it back into a draft and withdraws its corpus copy.
    /// </summary>
    public async Task<int> SaveDraftAsync(
        BuildInputs inputs,
        ClassProfile profile,
        int? userId,
        int? existingId,
        string? draftState,
        CancellationToken ct = default)
    {
        var asDrafted = new AssembledJd
        {
            Slug = profile.Slug,
            Title = profile.Title,
            WorkingTitle = inputs.WorkingTitle.Length > 0 ? inputs.WorkingTitle : profile.Title,
            Department = inputs.Department,
            UcJobCode = profile.UcJobCode,
            Jd = new Jd
            {
                KeyResponsibilities = [.. inputs.KeptResponsibilities.Select(r => r.Clone())],
                LicensesCertifications = [.. inputs.KeptCerts],
                Education = [.. inputs.KeptEducation],
                WorkExperience = [.. inputs.KeptWorkExperience],
                MinKSA = [.. inputs.KeptMinKSA],
                PrefKSA = [.. inputs.KeptPrefKSA],
                WorkEnvironment = [.. inputs.KeptWorkEnvironment],
            },
            UnallocatedPct = 100 - inputs.KeptResponsibilities.Sum(r => r.PctTime),
        };

        var fresh = asDrafted.ToEntity(profile.Id, userId, inputs, profile.EnvelopeSource);
        fresh.Status = AuthoredJdStatus.Draft;
        fresh.DraftState = draftState;
        fresh.AssembledAt = null;
        fresh.InCorpus = false;
        fresh.CorpusNote = "Draft — not assembled yet, so not in the corpus.";
        var id = await UpsertAsync(fresh, userId, existingId, ct);

        _db.JobDescriptions.RemoveRange(await _db.JobDescriptions.Where(j => j.AuthoredJdId == id).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
        return id;
    }

    private async Task<int> UpsertAsync(AuthoredJd fresh, int? userId, int? existingId, CancellationToken ct)
    {
        AuthoredJd? existing = null;
        if (existingId != null && userId != null)
        {
            existing = await _db.AuthoredJds
                .Include(a => a.KeyResponsibilities).ThenInclude(r => r.Duties)
                .Include(a => a.Items)
                .Include(a => a.ComplianceEdits)
                .FirstOrDefaultAsync(a => a.Id == existingId && a.CreatedByUserId == userId, ct);
        }

        if (existing == null)
        {
            _db.AuthoredJds.Add(fresh);
            await _db.SaveChangesAsync(ct);
            return fresh.Id;
        }

        // Replace the content wholesale; keep identity, author and creation time.
        _db.RemoveRange(existing.KeyResponsibilities);
        _db.RemoveRange(existing.Items);
        _db.RemoveRange(existing.ComplianceEdits);
        existing.ClassProfileId = fresh.ClassProfileId;
        existing.Title = fresh.Title;
        existing.WorkingTitle = fresh.WorkingTitle;
        existing.Department = fresh.Department;
        existing.UcJobCode = fresh.UcJobCode;
        existing.SalaryGrade = fresh.SalaryGrade;
        existing.FlsaStatus = fresh.FlsaStatus;
        existing.BargainingUnit = fresh.BargainingUnit;
        existing.JobSummary = fresh.JobSummary;
        existing.Status = fresh.Status;
        existing.UnallocatedPct = fresh.UnallocatedPct;
        existing.Notes = fresh.Notes;
        existing.EnvelopeSource = fresh.EnvelopeSource;
        existing.DraftState = fresh.DraftState ?? existing.DraftState;
        existing.AssembledAt = fresh.AssembledAt;
        existing.InCorpus = fresh.InCorpus;
        existing.CorpusNote = fresh.CorpusNote;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.KeyResponsibilities = fresh.KeyResponsibilities;
        existing.Items = fresh.Items;
        existing.ComplianceEdits = fresh.ComplianceEdits;
        await _db.SaveChangesAsync(ct);
        return existing.Id;
    }

    /// <summary>
    /// Bring the JD's corpus copy in line with its latest assembly: add or replace it when the JD
    /// qualifies (see <see cref="CorpusContribution"/>), remove it when a revision no longer does.
    /// Records the outcome on the JD and on <paramref name="assembled"/> for the author to see.
    /// </summary>
    public async Task SyncCorpusAsync(
        int authoredJdId,
        AssembledJd assembled,
        BuildInputs inputs,
        ClassProfile profile,
        EnvelopeVerdict? verdict,
        CancellationToken ct = default)
    {
        var jd = await _db.AuthoredJds.FirstAsync(a => a.Id == authoredJdId, ct);
        var exclusion = CorpusContribution.Exclusion(assembled, inputs, profile.Envelope, verdict);

        var previous = await _db.JobDescriptions.Where(j => j.AuthoredJdId == authoredJdId).ToListAsync(ct);
        _db.JobDescriptions.RemoveRange(previous);
        if (exclusion == null)
        {
            _db.JobDescriptions.Add(CorpusContribution.ToCorpusRecord(assembled, authoredJdId));
        }

        jd.EnvelopeVerdict = verdict;
        jd.InCorpus = exclusion == null;
        jd.CorpusNote = exclusion ?? CorpusContribution.Added;
        await _db.SaveChangesAsync(ct);

        assembled.InCorpus = jd.InCorpus;
        assembled.CorpusNote = jd.CorpusNote;
    }

    /// <summary>
    /// Delete a saved JD. Only its author may — or an admin. Returns false when there is no such
    /// JD visible to the caller, so someone else's id reads the same as a missing one.
    /// </summary>
    public async Task<bool> DeleteAsync(int id, int? userId, bool isAdmin, CancellationToken ct = default)
    {
        var jd = await _db.AuthoredJds.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (jd == null || (!isAdmin && (userId == null || jd.CreatedByUserId != userId)))
        {
            return false;
        }

        // Its corpus copy goes too: the author no longer stands behind it. The database cascades
        // this as well; removing it here keeps the rule visible and provider-independent.
        _db.JobDescriptions.RemoveRange(await _db.JobDescriptions.Where(j => j.AuthoredJdId == id).ToListAsync(ct));
        _db.AuthoredJds.Remove(jd);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Newest first. <paramref name="userId"/> null lists everyone's (admins only).</summary>
    public async Task<List<SavedJdSummary>> ListAsync(int? userId, CancellationToken ct = default)
    {
        var q = _db.AuthoredJds.AsNoTracking();
        if (userId != null)
        {
            q = q.Where(a => a.CreatedByUserId == userId);
        }

        return await q
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new SavedJdSummary
            {
                Id = a.Id,
                Slug = a.ClassProfile != null ? a.ClassProfile.Slug : "",
                Title = a.Title,
                WorkingTitle = a.WorkingTitle,
                Department = a.Department,
                UcJobCode = a.UcJobCode,
                Status = a.Status,
                UnallocatedPct = a.UnallocatedPct,
                Assembled = a.AssembledAt != null,
                CreatedBy = a.CreatedBy != null ? (a.CreatedBy.DisplayName ?? a.CreatedBy.LoginId) : null,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
            })
            .ToListAsync(ct);
    }

    /// <summary>The JD and the id of the user who wrote it (for the caller's access check).</summary>
    public async Task<(SavedJd Jd, int? OwnerId)?> GetAsync(int id, CancellationToken ct = default)
    {
        var a = await _db.AuthoredJds.AsNoTracking()
            .Include(x => x.ClassProfile)
            .Include(x => x.CreatedBy)
            .Include(x => x.KeyResponsibilities).ThenInclude(r => r.Duties)
            .Include(x => x.Items)
            .Include(x => x.ComplianceEdits)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a == null)
        {
            return null;
        }

        List<string> Items(AuthoredJdListKind kind) =>
            [.. a.Items.Where(i => i.Kind == kind).OrderBy(i => i.Ordinal).Select(i => i.Text)];

        var jd = new SavedJd
        {
            AuthoredJdId = a.Id,
            Slug = a.ClassProfile?.Slug ?? "",
            Title = a.Title,
            WorkingTitle = a.WorkingTitle,
            Department = a.Department,
            UcJobCode = a.UcJobCode,
            SalaryGrade = a.SalaryGrade,
            FlsaStatus = a.FlsaStatus,
            BargainingUnit = a.BargainingUnit,
            Jd = new Jd
            {
                JobSummary = a.JobSummary,
                KeyResponsibilities =
                [
                    .. a.KeyResponsibilities.OrderBy(r => r.Ordinal).Select(r => new JdKeyResponsibility
                    {
                        FunctionName = r.FunctionName,
                        PctTime = r.PctTime,
                        Duties = [.. r.Duties.OrderBy(d => d.Ordinal).Select(d => d.Text)],
                    }),
                ],
                LicensesCertifications = Items(AuthoredJdListKind.LicensesCertifications),
                Education = Items(AuthoredJdListKind.Education),
                WorkExperience = Items(AuthoredJdListKind.WorkExperience),
                MinKSA = Items(AuthoredJdListKind.MinKsa),
                PrefKSA = Items(AuthoredJdListKind.PrefKsa),
                ConditionsOfEmployment = Items(AuthoredJdListKind.ConditionOfEmployment),
                WorkEnvironment = Items(AuthoredJdListKind.WorkEnvironment),
                PhysicalRequirements = Items(AuthoredJdListKind.PhysicalRequirement),
            },
            UnallocatedPct = a.UnallocatedPct,
            Status = a.Status,
            ComplianceEdits =
            [
                .. a.ComplianceEdits.OrderBy(e => e.Ordinal).Select(e => new ComplianceEditRecord
                {
                    Section = e.Section,
                    Source = e.Source,
                    Before = e.Before,
                    After = e.After,
                    Reason = e.Reason,
                }),
            ],
            AuthorAdditions = Items(AuthoredJdListKind.AuthorAddition),
            Notes = a.Notes,
            InCorpus = a.InCorpus,
            CorpusNote = a.CorpusNote,
            DraftState = a.DraftState == null ? null : System.Text.Json.JsonDocument.Parse(a.DraftState).RootElement.Clone(),
            Assembled = a.AssembledAt != null,
            CreatedBy = a.CreatedBy?.DisplayName ?? a.CreatedBy?.LoginId,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt,
        };

        return (jd, a.CreatedByUserId);
    }
}

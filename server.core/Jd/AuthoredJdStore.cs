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
        CancellationToken ct = default)
    {
        var fresh = assembled.ToEntity(profile.Id, userId, inputs, profile.EnvelopeSource);

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
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.KeyResponsibilities = fresh.KeyResponsibilities;
        existing.Items = fresh.Items;
        existing.ComplianceEdits = fresh.ComplianceEdits;
        await _db.SaveChangesAsync(ct);
        return existing.Id;
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
            CreatedBy = a.CreatedBy?.DisplayName ?? a.CreatedBy?.LoginId,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt,
        };

        return (jd, a.CreatedByUserId);
    }
}

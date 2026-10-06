using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain;

namespace Server.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // ---- reference / catalog
    public DbSet<TitleCode> TitleCodes => Set<TitleCode>();
    public DbSet<Supersession> Supersessions => Set<Supersession>();

    // ---- official standards
    public DbSet<JobStandard> JobStandards => Set<JobStandard>();
    public DbSet<JobStandardItem> JobStandardItems => Set<JobStandardItem>();

    // ---- JD corpus
    public DbSet<JobDescription> JobDescriptions => Set<JobDescription>();
    public DbSet<JdResponsibility> JdResponsibilities => Set<JdResponsibility>();
    public DbSet<JdDuty> JdDuties => Set<JdDuty>();
    public DbSet<JdQualificationItem> JdQualificationItems => Set<JdQualificationItem>();
    public DbSet<JdPemEntry> JdPemEntries => Set<JdPemEntry>();

    // ---- class profiles
    public DbSet<ClassProfile> ClassProfiles => Set<ClassProfile>();
    public DbSet<ProfileSourceFile> ProfileSourceFiles => Set<ProfileSourceFile>();
    public DbSet<ProfileDistribution> ProfileDistributions => Set<ProfileDistribution>();
    public DbSet<ProfileDistributionValue> ProfileDistributionValues => Set<ProfileDistributionValue>();
    public DbSet<ProfileFunction> ProfileFunctions => Set<ProfileFunction>();
    public DbSet<ProfileFunctionSampleDuty> ProfileFunctionSampleDuties => Set<ProfileFunctionSampleDuty>();
    public DbSet<ProfileQualItem> ProfileQualItems => Set<ProfileQualItem>();

    // ---- envelopes
    public DbSet<JobEnvelope> JobEnvelopes => Set<JobEnvelope>();
    public DbSet<EnvelopeResponsibility> EnvelopeResponsibilities => Set<EnvelopeResponsibility>();
    public DbSet<EnvelopeDuty> EnvelopeDuties => Set<EnvelopeDuty>();
    public DbSet<EnvelopeListItem> EnvelopeListItems => Set<EnvelopeListItem>();

    // ---- consolidation
    public DbSet<ConsolidatedFunction> ConsolidatedFunctions => Set<ConsolidatedFunction>();
    public DbSet<ConsolidatedFunctionMember> ConsolidatedFunctionMembers => Set<ConsolidatedFunctionMember>();
    public DbSet<ConsolidatedFunctionSampleDuty> ConsolidatedFunctionSampleDuties => Set<ConsolidatedFunctionSampleDuty>();
    public DbSet<ConsolidatedQual> ConsolidatedQuals => Set<ConsolidatedQual>();
    public DbSet<ConsolidatedQualMember> ConsolidatedQualMembers => Set<ConsolidatedQualMember>();
    public DbSet<ProfileDroppedItem> ProfileDroppedItems => Set<ProfileDroppedItem>();

    // ---- coverage
    public DbSet<CoverageReport> CoverageReports => Set<CoverageReport>();
    public DbSet<JdCoverage> JdCoverages => Set<JdCoverage>();
    public DbSet<JdCoverageUncovered> JdCoverageUncovereds => Set<JdCoverageUncovered>();

    // ---- authored output
    public DbSet<AuthoredJd> AuthoredJds => Set<AuthoredJd>();
    public DbSet<AuthoredJdResponsibility> AuthoredJdResponsibilities => Set<AuthoredJdResponsibility>();
    public DbSet<AuthoredJdDuty> AuthoredJdDuties => Set<AuthoredJdDuty>();
    public DbSet<AuthoredJdListItem> AuthoredJdListItems => Set<AuthoredJdListItem>();
    public DbSet<ComplianceEdit> ComplianceEdits => Set<ComplianceEdit>();
    public DbSet<ComplianceRule> ComplianceRules => Set<ComplianceRule>();

    // ---- users
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AdminGrant> AdminGrants => Set<AdminGrant>();
    public DbSet<AppSecret> AppSecrets => Set<AppSecret>();
    public DbSet<AppSecretAudit> AppSecretAudits => Set<AppSecretAudit>();
    public DbSet<CorpusUpload> CorpusUploads => Set<CorpusUpload>();
    public DbSet<StandardsWorkbook> StandardsWorkbooks => Set<StandardsWorkbook>();

    // Index-bearing string columns need an explicit length: SQL Server caps a key at 900 bytes,
    // and nvarchar(max) cannot be indexed at all. Free text is deliberately left unbounded.
    private const int CodeLen = 20;
    private const int TitleLen = 300;
    private const int KeyLen = 400;
    private const int SlugLen = 200;
    private const int PathLen = 400;

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Every enum is stored as its name. This database is meant to be queried directly for
        // cross-class reporting, and "KsaMin" beats "3" in a result grid; it also means adding
        // an enum member can never silently re-map existing rows.
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
                if (type.IsEnum)
                {
                    prop.SetProviderClrType(typeof(string));
                    prop.SetMaxLength(60);
                }
            }
        }

        // ---------------------------------------------------------------- reference
        b.Entity<TitleCode>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(CodeLen);
            e.Property(x => x.Title).HasMaxLength(TitleLen);
            e.Property(x => x.TitleKey).HasMaxLength(KeyLen);
            e.Property(x => x.TitleCodeKey).HasMaxLength(KeyLen);
            e.Property(x => x.Grade).HasMaxLength(100);
            e.Property(x => x.Function).HasMaxLength(TitleLen);
            e.Property(x => x.Family).HasMaxLength(TitleLen);
            e.Property(x => x.Source).HasMaxLength(40);
            e.Ignore(x => x.IsInUse);

            // Code is NOT unique: the reference lists some titles twice, once as possible at UCD
            // and once as observed on payroll.
            e.HasIndex(x => x.Code);
            e.HasIndex(x => x.TitleCodeKey);
            e.HasIndex(x => x.TitleKey);
        });

        b.Entity<Supersession>(e =>
        {
            e.Property(x => x.FromCode).HasMaxLength(CodeLen);
            e.Property(x => x.ToCode).HasMaxLength(CodeLen);
            e.Property(x => x.FromTitle).HasMaxLength(TitleLen);
            e.Property(x => x.ToTitle).HasMaxLength(TitleLen);
            // One successor per retired code, or the remap is not a function.
            e.HasIndex(x => x.FromCode).IsUnique();
        });

        // ---------------------------------------------------------------- standards
        b.Entity<JobStandard>(e =>
        {
            e.Property(x => x.LongTitle).HasMaxLength(TitleLen);
            e.Property(x => x.Code).HasMaxLength(CodeLen);
            e.Property(x => x.PersProg).HasMaxLength(60);
            e.Property(x => x.Grade).HasMaxLength(100);
            e.Property(x => x.Flsa).HasMaxLength(60);
            e.Property(x => x.Union).HasMaxLength(120);
            e.Property(x => x.TitleKey).HasMaxLength(KeyLen);
            e.Property(x => x.TitleCodeKey).HasMaxLength(KeyLen);

            // Dedup is on the STRICT key. The loose key merges "Analyst 3 RP" into
            // "Analyst 3 RP GF" and silently discards ~25 real standards.
            e.HasIndex(x => x.TitleCodeKey).IsUnique();
            e.HasIndex(x => x.Code);
            e.HasIndex(x => x.TitleKey);

            e.HasMany(x => x.Items).WithOne(x => x.JobStandard)
                .HasForeignKey(x => x.JobStandardId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------------------------------------------------------------- corpus
        b.Entity<JobDescription>(e =>
        {
            e.Property(x => x.SourceFile).HasMaxLength(PathLen);
            e.HasIndex(x => new { x.UcJobCode, x.AddedAt });
            // Deleting a saved JD removes its corpus copy: the author no longer stands behind it.
            e.HasOne(x => x.AuthoredJd).WithMany()
                .HasForeignKey(x => x.AuthoredJdId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.UcJobCode).HasMaxLength(CodeLen);
            e.Property(x => x.OriginalUcJobCode).HasMaxLength(CodeLen);
            e.Property(x => x.UcJobTitle).HasMaxLength(TitleLen);
            e.Property(x => x.WorkingTitle).HasMaxLength(TitleLen);
            e.Property(x => x.BusinessUnit).HasMaxLength(TitleLen);
            e.Property(x => x.Division).HasMaxLength(TitleLen);
            e.Property(x => x.DepartmentName).HasMaxLength(TitleLen);
            e.Property(x => x.DepartmentCode).HasMaxLength(CodeLen);
            e.Property(x => x.JdNumber).HasMaxLength(60);
            e.Property(x => x.UcPathPositionNumber).HasMaxLength(60);
            e.Property(x => x.ReportsToPositionNumber).HasMaxLength(60);
            e.Property(x => x.CtJobFamily).HasMaxLength(TitleLen);
            e.Property(x => x.CtJobFunction).HasMaxLength(TitleLen);
            e.Property(x => x.PersonnelProgram).HasMaxLength(60);
            e.Property(x => x.SalaryGrade).HasMaxLength(100);
            e.Property(x => x.FlsaStatus).HasMaxLength(60);
            e.Property(x => x.UnionCode).HasMaxLength(120);

            // Re-ingesting a JD must update it, never duplicate it.
            e.HasIndex(x => x.SourceFile).IsUnique();
            e.HasIndex(x => x.UcJobCode);

            e.HasMany(x => x.Responsibilities).WithOne(x => x.JobDescription)
                .HasForeignKey(x => x.JobDescriptionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Qualifications).WithOne(x => x.JobDescription)
                .HasForeignKey(x => x.JobDescriptionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.PemEntries).WithOne(x => x.JobDescription)
                .HasForeignKey(x => x.JobDescriptionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<JdResponsibility>(e =>
        {
            e.Property(x => x.FunctionName).HasMaxLength(TitleLen);
            e.HasIndex(x => new { x.JobDescriptionId, x.Ordinal });
            e.HasMany(x => x.Duties).WithOne(x => x.JdResponsibility)
                .HasForeignKey(x => x.JdResponsibilityId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<JdPemEntry>(e => e.Property(x => x.RowName).HasMaxLength(TitleLen));

        // ---------------------------------------------------------------- profiles
        b.Entity<ClassProfile>(e =>
        {
            e.Property(x => x.Slug).HasMaxLength(SlugLen);
            e.Property(x => x.UcJobCode).HasMaxLength(CodeLen);
            e.Property(x => x.Title).HasMaxLength(TitleLen);
            e.Property(x => x.CtJobFamily).HasMaxLength(TitleLen);
            e.Property(x => x.CtJobFunction).HasMaxLength(TitleLen);
            e.Property(x => x.PersonnelProgram).HasMaxLength(60);

            e.HasIndex(x => x.Slug).IsUnique();
            // Not unique: a class can legitimately hold both a corpus profile and, transiently,
            // a standard-bootstrapped one during migration. Collisions are resolved by CODE in
            // the loader, which is what stops one class getting two profiles.
            e.HasIndex(x => x.UcJobCode);

            e.HasOne(x => x.Envelope).WithOne(x => x.ClassProfile)
                .HasForeignKey<JobEnvelope>(x => x.ClassProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Coverage).WithOne(x => x.ClassProfile)
                .HasForeignKey<CoverageReport>(x => x.ClassProfileId).OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Distributions).WithOne(x => x.ClassProfile)
                .HasForeignKey(x => x.ClassProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Functions).WithOne(x => x.ClassProfile)
                .HasForeignKey(x => x.ClassProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Qualifications).WithOne(x => x.ClassProfile)
                .HasForeignKey(x => x.ClassProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ConsolidatedFunctions).WithOne(x => x.ClassProfile)
                .HasForeignKey(x => x.ClassProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ConsolidatedQuals).WithOne(x => x.ClassProfile)
                .HasForeignKey(x => x.ClassProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.DroppedItems).WithOne(x => x.ClassProfile)
                .HasForeignKey(x => x.ClassProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.SourceFiles).WithOne(x => x.ClassProfile)
                .HasForeignKey(x => x.ClassProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProfileSourceFile>(e => e.Property(x => x.SourceFile).HasMaxLength(PathLen));

        b.Entity<ProfileDistribution>(e =>
        {
            e.Property(x => x.Consensus).HasMaxLength(TitleLen);
            e.HasIndex(x => new { x.ClassProfileId, x.Field }).IsUnique();
            e.HasMany(x => x.Values).WithOne(x => x.ProfileDistribution)
                .HasForeignKey(x => x.ProfileDistributionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProfileDistributionValue>(e => e.Property(x => x.Value).HasMaxLength(TitleLen));

        b.Entity<ProfileFunction>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(TitleLen);
            e.HasMany(x => x.SampleDuties).WithOne(x => x.ProfileFunction)
                .HasForeignKey(x => x.ProfileFunctionId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------------------------------------------------------------- envelopes
        b.Entity<JobEnvelope>(e =>
        {
            e.HasIndex(x => x.ClassProfileId).IsUnique();
            e.HasMany(x => x.KeyResponsibilities).WithOne(x => x.JobEnvelope)
                .HasForeignKey(x => x.JobEnvelopeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Items).WithOne(x => x.JobEnvelope)
                .HasForeignKey(x => x.JobEnvelopeId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EnvelopeResponsibility>(e =>
        {
            e.Property(x => x.FunctionName).HasMaxLength(TitleLen);
            e.HasMany(x => x.Duties).WithOne(x => x.EnvelopeResponsibility)
                .HasForeignKey(x => x.EnvelopeResponsibilityId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------------------------------------------------------------- consolidation
        b.Entity<ConsolidatedFunction>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(TitleLen);
            e.HasMany(x => x.Members).WithOne(x => x.ConsolidatedFunction)
                .HasForeignKey(x => x.ConsolidatedFunctionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.SampleDuties).WithOne(x => x.ConsolidatedFunction)
                .HasForeignKey(x => x.ConsolidatedFunctionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ConsolidatedQual>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(TitleLen);
            e.HasMany(x => x.Members).WithOne(x => x.ConsolidatedQual)
                .HasForeignKey(x => x.ConsolidatedQualId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------------------------------------------------------------- coverage
        b.Entity<CoverageReport>(e =>
        {
            e.HasIndex(x => x.ClassProfileId).IsUnique();
            e.HasMany(x => x.PerJd).WithOne(x => x.CoverageReport)
                .HasForeignKey(x => x.CoverageReportId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<JdCoverage>(e =>
        {
            e.Property(x => x.SourceFile).HasMaxLength(PathLen);
            e.HasMany(x => x.Uncovered).WithOne(x => x.JdCoverage)
                .HasForeignKey(x => x.JdCoverageId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<JdCoverageUncovered>(e => e.Property(x => x.Name).HasMaxLength(TitleLen));

        // ---------------------------------------------------------------- authoring
        b.Entity<AuthoredJd>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(TitleLen);
            e.Property(x => x.WorkingTitle).HasMaxLength(TitleLen);
            e.Property(x => x.Department).HasMaxLength(TitleLen);
            e.Property(x => x.UcJobCode).HasMaxLength(CodeLen);
            e.Property(x => x.SalaryGrade).HasMaxLength(100);
            e.Property(x => x.FlsaStatus).HasMaxLength(60);
            e.Property(x => x.BargainingUnit).HasMaxLength(120);
            e.HasIndex(x => x.ClassProfileId);

            // A profile must not be deletable out from under a finished JD.
            e.HasOne(x => x.ClassProfile).WithMany()
                .HasForeignKey(x => x.ClassProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedBy).WithMany()
                .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);

            e.HasMany(x => x.KeyResponsibilities).WithOne(x => x.AuthoredJd)
                .HasForeignKey(x => x.AuthoredJdId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Items).WithOne(x => x.AuthoredJd)
                .HasForeignKey(x => x.AuthoredJdId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ComplianceEdits).WithOne(x => x.AuthoredJd)
                .HasForeignKey(x => x.AuthoredJdId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuthoredJdResponsibility>(e =>
        {
            e.Property(x => x.FunctionName).HasMaxLength(TitleLen);
            e.HasMany(x => x.Duties).WithOne(x => x.AuthoredJdResponsibility)
                .HasForeignKey(x => x.AuthoredJdResponsibilityId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ComplianceEdit>(e => e.Property(x => x.Section).HasMaxLength(TitleLen));

        b.Entity<ComplianceRule>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(100);
            e.HasIndex(x => x.Key).IsUnique();
        });

        // ---------------------------------------------------------------- users
        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.NameIdentifier).HasMaxLength(SlugLen);
            e.Property(x => x.IamId).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(TitleLen);
            e.Property(x => x.DisplayName).HasMaxLength(TitleLen);
            e.HasIndex(x => x.NameIdentifier).IsUnique();
            e.Property(x => x.LoginId).HasMaxLength(64);
            e.HasIndex(x => x.LoginId);
        });

        b.Entity<StandardsWorkbook>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(TitleLen);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.HasIndex(x => x.Sha256).IsUnique();
            e.HasOne(x => x.UploadedBy).WithMany()
                .HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<CorpusUpload>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(TitleLen);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.HasIndex(x => x.Sha256).IsUnique();
            e.Property(x => x.UcJobCode).HasMaxLength(20);
            e.Property(x => x.OriginalUcJobCode).HasMaxLength(20);
            e.Property(x => x.UcJobTitle).HasMaxLength(TitleLen);
            e.HasIndex(x => new { x.Status, x.UcJobCode });
            e.HasOne(x => x.UploadedBy).WithMany()
                .HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AppSecretAudit>(e =>
        {
            e.Property(x => x.SecretName).HasMaxLength(100);
            e.Property(x => x.Action).HasMaxLength(20);
            e.Property(x => x.LastFour).HasMaxLength(4);
            e.HasIndex(x => x.At);
            // The audit outlives the user it names.
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AppSecret>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.LastFour).HasMaxLength(4);
            e.HasOne(x => x.UpdatedBy).WithMany()
                .HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AdminGrant>(e =>
        {
            e.Property(x => x.LoginId).HasMaxLength(64);
            e.HasIndex(x => x.LoginId).IsUnique();
            // Removing a user must not silently remove the admins they granted.
            e.HasOne(x => x.GrantedBy).WithMany()
                .HasForeignKey(x => x.GrantedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        // The eight ITextItem tables share one configuration rather than eight near-identical
        // copies. They stay separate tables so every foreign key is real and cascades mean what
        // they say, but nothing about their shape is restated per table.
        ConfigureTextItems(b);
    }

    private static void ConfigureTextItems(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        {
            if (!typeof(ITextItem).IsAssignableFrom(entity.ClrType))
            {
                continue;
            }

            var builder = b.Entity(entity.ClrType);

            // Free text: no length cap. These hold duty statements and KSA sentences, and
            // truncating one silently would corrupt a job description.
            builder.Property(nameof(ITextItem.Text));

            // Every one of these is an ordered list, and SQL has no inherent row order, so the
            // ordinal is indexed alongside the owning key to keep the sort cheap.
            var fk = entity.GetForeignKeys().FirstOrDefault();
            if (fk is not null && fk.Properties.Count == 1)
            {
                builder.HasIndex(fk.Properties[0].Name, nameof(ITextItem.Ordinal));
            }
        }
    }
}

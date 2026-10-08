using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Profiles;
using Server.Core.Titles;

namespace Server.Core.Standards;

/// <summary>
/// Standard-derived envelopes, carried from one environment to another — bootstrap on a laptop,
/// where model calls are cheap to retry and nothing is live, then load the results into production.
///
/// Only the envelope travels, keyed by the standard's title and the job code it resolved to. That is
/// the one part a model produced; every other field of the class is rebuilt by the receiving
/// environment from its own standard and title reference, through the same create path and guards
/// as a bootstrap there.
/// </summary>
public sealed class EnvelopeBundle
{
    public const string FormatName = "jdwriter.standard-envelopes";

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = 1;
    public DateTimeOffset ExportedAt { get; set; }
    public List<BundledEnvelope> Envelopes { get; set; } = [];
}

public sealed class BundledEnvelope
{
    /// <summary>The standard's long title, which is also the class title a bootstrap gives it.</summary>
    public string Title { get; set; } = "";

    /// <summary>The job code the standard resolved to where the envelope was made; "" for none.</summary>
    public string UcJobCode { get; set; } = "";

    public EnvelopeWire Envelope { get; set; } = new();
}

public sealed class ImportedClass
{
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string UcJobCode { get; set; } = "";
}

public sealed class SkippedEnvelope
{
    public string Title { get; set; } = "";
    public BootstrapRefusal Reason { get; set; }
    public string Message { get; set; } = "";
}

public sealed class EnvelopeImportResult
{
    public List<ImportedClass> Created { get; set; } = [];
    public List<SkippedEnvelope> Skipped { get; set; } = [];
}

public sealed class EnvelopeTransfer
{
    private readonly AppDbContext _db;
    private readonly IBootstrapper _bootstrapper;
    private readonly ITitleCodeService _titleCodes;
    private readonly ILogger<EnvelopeTransfer> _logger;

    public EnvelopeTransfer(
        AppDbContext db, IBootstrapper bootstrapper, ITitleCodeService titleCodes, ILogger<EnvelopeTransfer> logger)
    {
        _db = db;
        _bootstrapper = bootstrapper;
        _titleCodes = titleCodes;
        _logger = logger;
    }

    /// <summary>
    /// Every class still built from its standard alone. A hand-edited envelope is excluded (its
    /// source is Manual), and so is any class that has since learned from real JDs, or whose code a
    /// union successor has superseded — a dead class awaiting retirement is not worth carrying.
    /// </summary>
    public async Task<EnvelopeBundle> ExportAsync(CancellationToken ct = default)
    {
        var titleCodes = await _titleCodes.GetAsync(ct);

        var profiles = await _db.ClassProfiles.AsNoTracking()
            .Where(p => p.EnvelopeSource == EnvelopeSource.Standard && p.CorpusSize == 0 && p.Envelope != null)
            .Include(p => p.Envelope!).ThenInclude(e => e.KeyResponsibilities).ThenInclude(r => r.Duties)
            .Include(p => p.Envelope!).ThenInclude(e => e.Items)
            .AsSplitQuery()
            .OrderBy(p => p.Title)
            .ToListAsync(ct);

        return new EnvelopeBundle
        {
            ExportedAt = DateTimeOffset.UtcNow,
            Envelopes =
            [
                .. profiles.Where(p => !titleCodes.IsSuperseded(p.UcJobCode)).Select(p => new BundledEnvelope
                {
                    Title = p.Title,
                    UcJobCode = p.UcJobCode,
                    Envelope = EnvelopeWire.From(p.Envelope!),
                }),
            ],
        };
    }

    private static bool WithinBounds(BundledEnvelope e)
    {
        var w = e.Envelope;
        var lists = new[]
        {
            w.RequiredCertifications, w.Education, w.WorkExperience, w.MinQualifications, w.PrefQualifications,
            w.ConditionsOfEmployment, w.WorkEnvironment, w.PhysicalRequirements, w.OutOfEnvelope,
        };
        var texts = lists.SelectMany(l => l)
            .Concat(w.KeyResponsibilities.SelectMany(r => r.Duties.Append(r.FunctionName)))
            .Append(w.Summary).Append(w.ScopeStatement).Append(e.Title);
        return w.KeyResponsibilities.Count <= 40
               && w.KeyResponsibilities.All(r => r.Duties.Count <= 60)
               && lists.All(l => l.Count <= 200)
               && texts.All(t => (t ?? "").Length <= 4_000);
    }

    /// <summary>
    /// Create a class for each bundled envelope that this environment does not already have. Never
    /// overwrites: an existing class — learned from JDs, edited by hand, or bootstrapped here — is
    /// reported and left alone. Each class is its own save, so one refusal does not undo the rest.
    /// </summary>
    public async Task<EnvelopeImportResult> ImportAsync(EnvelopeBundle bundle, CancellationToken ct = default)
    {
        if (bundle.Format != EnvelopeBundle.FormatName || bundle.Version != 1)
        {
            throw new InvalidOperationException(
                $"Not a JDWriter envelope export (format \"{bundle.Format}\", version {bundle.Version}).");
        }

        // Admin-only, but still a file from elsewhere: bounded before any of it is stored.
        if (bundle.Envelopes.Count > 2_000 || bundle.Envelopes.Any(e => !WithinBounds(e)))
        {
            throw new InvalidOperationException("This export is larger than any real set of envelopes; it was not imported.");
        }

        var note = $"Standard-derived — envelope generated in another environment (exported "
                   + $"{bundle.ExportedAt:yyyy-MM-dd}) and imported. Ingest JDs for this class to learn the real envelope.";

        var result = new EnvelopeImportResult();
        foreach (var item in bundle.Envelopes)
        {
            try
            {
                var profile = await _bootstrapper.ImportAsync(item.Title, item.UcJobCode, item.Envelope.ToEntity(), note, ct);
                result.Created.Add(new ImportedClass { Title = profile.Title, Slug = profile.Slug, UcJobCode = profile.UcJobCode });
            }
            catch (BootstrapRefusedException ex)
            {
                result.Skipped.Add(new SkippedEnvelope { Title = item.Title, Reason = ex.Reason, Message = ex.Message });
            }
            finally
            {
                // A refused class can leave its half-built profile tracked; it must not ride along
                // on the next class's save.
                _db.ChangeTracker.Clear();
            }
        }

        _logger.LogInformation(
            "Envelope import: {Created} created, {Skipped} skipped of {Total} (exported {ExportedAt:o})",
            result.Created.Count, result.Skipped.Count, bundle.Envelopes.Count, bundle.ExportedAt);
        return result;
    }
}

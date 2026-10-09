using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Core.Profiles;

/// <summary>One class's education and preferred qualifications, before and after the rules.</summary>
public sealed class QualificationRulesExample
{
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public List<string> EducationBefore { get; set; } = [];
    public List<string> EducationAfter { get; set; } = [];

    /// <summary>Preferred qualifications that are new (moved from education) or reworded.</summary>
    public List<string> PreferredChanged { get; set; } = [];
}

public sealed class QualificationRulesSummary
{
    public bool Applied { get; set; }
    public int Envelopes { get; set; }
    public int Changed { get; set; }
    public int MovedToPreferred { get; set; }
    public int EquivalentAdded { get; set; }

    /// <summary>A sample of the changes, for the admin to check before applying.</summary>
    public List<QualificationRulesExample> Examples { get; set; } = [];
}

/// <summary>
/// Applies <see cref="QualificationRules"/> to every existing envelope — the ones written before
/// the rules existed. A preview changes nothing; applying saves. Idempotent, so running it again
/// (or in another environment) is safe. The envelope's source is left as it was: house rules are
/// not an analyst's hand edit.
/// </summary>
public sealed class QualificationRulesPass
{
    private const int MaxExamples = 12;

    private readonly AppDbContext _db;

    public QualificationRulesPass(AppDbContext db) => _db = db;

    public async Task<QualificationRulesSummary> RunAsync(bool apply, CancellationToken ct = default)
    {
        var profiles = await _db.ClassProfiles
            .Include(p => p.Envelope!).ThenInclude(e => e.Items)
            .Where(p => p.Envelope != null)
            .OrderBy(p => p.Title)
            .ToListAsync(ct);

        var summary = new QualificationRulesSummary { Applied = apply, Envelopes = profiles.Count };
        foreach (var profile in profiles)
        {
            var envelope = profile.Envelope!;
            var before = Texts(envelope, EnvelopeListKind.Education);
            var preferredBefore = Texts(envelope, EnvelopeListKind.PrefQualification);

            var result = QualificationRules.Apply(envelope);
            if (!result.Changed)
            {
                continue;
            }

            summary.Changed++;
            summary.MovedToPreferred += result.MovedToPreferred;
            summary.EquivalentAdded += result.EquivalentAdded;
            if (summary.Examples.Count < MaxExamples)
            {
                summary.Examples.Add(new QualificationRulesExample
                {
                    Title = profile.Title,
                    Slug = profile.Slug,
                    EducationBefore = before,
                    EducationAfter = Texts(envelope, EnvelopeListKind.Education),
                    PreferredChanged = Texts(envelope, EnvelopeListKind.PrefQualification)
                        .Where(t => !preferredBefore.Contains(t))
                        .ToList(),
                });
            }
        }

        if (apply)
        {
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            _db.ChangeTracker.Clear();
        }

        return summary;
    }

    private static List<string> Texts(JobEnvelope envelope, EnvelopeListKind kind) =>
        envelope.Items.Where(i => i.Kind == kind).OrderBy(i => i.Ordinal).Select(i => i.Text).ToList();
}

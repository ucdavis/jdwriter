using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Profiles;
using Server.Core.Titles;

namespace Server.Core.Standards;

/// <summary>A class we hold a standard for but no JD corpus — a class we could bootstrap.</summary>
public sealed class BootstrapCandidate
{
    public string Title { get; set; } = "";

    /// <summary>Resolved UC job code, or "" when the title does not resolve.</summary>
    public string Code { get; set; } = "";

    public string Family { get; set; } = "";
    public string Function { get; set; } = "";
    public string Grade { get; set; } = "";

}

/// <summary>
/// Builds a job envelope from an official standard.
///
/// A SEAM, not an implementation. The envelope synthesis itself is the port of the POC's
/// `describe.ts` (`synthesizeEnvelopeFromStandard` / `deterministicEnvelopeFromStandard`) and lives
/// with the rest of the profile services. Bootstrapping needs it but does not own it.
/// </summary>
public interface IStandardEnvelopeBuilder
{
    /// <summary>
    /// Synthesize an envelope from a standard, falling back to a deterministic one when no API key
    /// is configured or the model call fails. The reference does exactly that: a failed synthesis
    /// degrades to the deterministic envelope rather than failing the bootstrap.
    /// </summary>
    Task<JobEnvelope> BuildAsync(
        ClassStandardRecord standard, BootstrapMeta meta, CancellationToken ct = default);
}

/// <summary>Class identity passed to envelope synthesis alongside the standard.</summary>
public sealed class BootstrapMeta
{
    public string Title { get; set; } = "";
    public string Code { get; set; } = "";
    public string Family { get; set; } = "";
    public string Function { get; set; } = "";
    public string Program { get; set; } = "";
}

/// <summary>Why a class was not created from its standard.</summary>
/// <summary>
/// The classes that can be bootstrapped, and how many standards were left out because their title is
/// not on UC Davis payroll — those could never be used, however good their envelope.
/// </summary>
public sealed class BootstrapCandidates
{
    public List<BootstrapCandidate> Candidates { get; set; } = [];

    /// <summary>Standards whose title is in the UC title matrix but not on UC Davis payroll.</summary>
    public int NotOnPayroll { get; set; }

    /// <summary>Standards whose title matches no UC job code at all.</summary>
    public int NoCodeMatch { get; set; }
}

public enum BootstrapRefusal
{
    NoStandard,

    /// <summary>The standard's title is not on UC Davis payroll, so its class could not be used.</summary>
    NotOnPayroll,
    Superseded,
    Exists,

    /// <summary>The standard resolves to a different job code here than where the envelope was made.</summary>
    CodeMismatch,
    Invalid,
}

/// <summary>
/// A refusal to create a class, with a machine-readable reason. Still an
/// <see cref="InvalidOperationException"/>, so a caller that only shows the message keeps working.
/// </summary>
public sealed class BootstrapRefusedException : InvalidOperationException
{
    public BootstrapRefusedException(BootstrapRefusal reason, string message) : base(message) => Reason = reason;

    public BootstrapRefusal Reason { get; }
}

public interface IBootstrapper
{
    Task<BootstrapCandidates> GetCandidatesAsync(CancellationToken ct = default);

    Task<ClassProfile> BootstrapAsync(string title, CancellationToken ct = default);

    /// <summary>
    /// Create a class from its standard with an envelope made elsewhere — another environment's
    /// bootstrap — instead of calling the model. Everything but the envelope is rebuilt from THIS
    /// environment's standard and title reference, under the same guards as <see cref="BootstrapAsync"/>.
    /// </summary>
    /// <param name="expectedCode">
    /// The code the class had where the envelope was made. Refused if this environment resolves the
    /// standard differently: the envelope was written for that classification.
    /// </param>
    Task<ClassProfile> ImportAsync(
        string title, string expectedCode, JobEnvelope envelope, string note, CancellationToken ct = default);
}

/// <summary>
/// Bootstraps a class envelope from an official job standard alone — for classes we hold a standard
/// for but no JD corpus yet. Ported from the POC's src/lib/standards/bootstrap.ts.
///
/// The result is clearly marked <see cref="EnvelopeSource.Standard"/> and converges on a real
/// learned envelope once JDs are ingested for the class.
/// </summary>
public sealed class Bootstrapper : IBootstrapper
{
    private readonly AppDbContext _db;
    private readonly IStandardsStore _standards;
    private readonly ITitleCodeService _titleCodes;
    private readonly IStandardEnvelopeBuilder _envelopes;

    public Bootstrapper(
        AppDbContext db,
        IStandardsStore standards,
        ITitleCodeService titleCodes,
        IStandardEnvelopeBuilder envelopes)
    {
        _db = db;
        _standards = standards;
        _titleCodes = titleCodes;
        _envelopes = envelopes;
    }

    /// <summary>
    /// Standards with no existing profile.
    ///
    /// Matched on the LOOSE key, because "does this class already have a profile?" is a
    /// same-class question and a variant suffix should not make a profile invisible.
    /// </summary>
    public async Task<BootstrapCandidates> GetCandidatesAsync(CancellationToken ct = default)
    {
        var standards = await _standards.GetIndexAsync(ct);
        var titleCodes = await _titleCodes.GetAsync(ct);

        var profiled = await _db.ClassProfiles.AsNoTracking()
            .Select(p => new { p.Title, p.UcJobCode })
            .ToListAsync(ct);

        // A profile still filed under a superseded code is a dead class awaiting retirement, and it
        // must not count as "having" its successor: the loose key drops the union suffix, so
        // "Financial Anl 3" would otherwise hide "Financial Analyst 3 CX" from this list.
        var have = profiled
            .Where(p => !titleCodes.IsSuperseded(p.UcJobCode))
            .Select(p => Sites.ClassKey(p.Title))
            .ToHashSet(StringComparer.Ordinal);

        var result = new BootstrapCandidates();

        foreach (var s in standards.All)
        {
            if (have.Contains(Sites.ClassKey(s.LongTitle)))
            {
                continue;
            }

            var tc = titleCodes.FindTitleCode(s.LongTitle);

            // A standard for a SUPERSEDED code must not be offered: bootstrapping it would recreate
            // a class that browse and the classifier deliberately hide. Nothing is lost — the RP
            // successor has its own standard in this same list, carrying the live code.
            //
            // This guard is the whole lesson of that bug: hiding a dead class from browse and from
            // the classifier was not enough, because a CREATE path had no guard of its own.
            if (tc is not null && titleCodes.IsSuperseded(tc.Code))
            {
                continue;
            }

            // Only classes UC Davis can actually use: a title on its payroll. A matrix-only title, or
            // one matching no code, would get an envelope nobody could ever write a JD against.
            if (tc is null)
            {
                result.NoCodeMatch++;
                continue;
            }

            if (!tc.IsInUse)
            {
                result.NotOnPayroll++;
                continue;
            }

            result.Candidates.Add(new BootstrapCandidate
            {
                Title = s.LongTitle,
                Code = tc.Code,
                Family = tc.Family,
                Function = tc.Function,
                Grade = !string.IsNullOrEmpty(s.Grade) ? s.Grade : tc.Grade,
            });
        }

        result.Candidates = result.Candidates.OrderBy(c => c.Title, StringComparer.Ordinal).ToList();
        return result;
    }

    /// <summary>
    /// Build and persist a standard-derived profile. Refuses to overwrite an existing profile —
    /// bootstrapping is only for classes with no corpus.
    /// </summary>
    public Task<ClassProfile> BootstrapAsync(string title, CancellationToken ct = default) =>
        CreateAsync(title, null, (std, meta) => _envelopes.BuildAsync(std, meta, ct), null, ct);

    public Task<ClassProfile> ImportAsync(
        string title, string expectedCode, JobEnvelope envelope, string note, CancellationToken ct = default)
    {
        if (envelope.KeyResponsibilities.Count == 0)
        {
            throw new BootstrapRefusedException(BootstrapRefusal.Invalid, $"The envelope for \"{title}\" has no responsibilities.");
        }

        return CreateAsync(title, expectedCode, (_, _) => Task.FromResult(envelope), note, ct);
    }

    /// <summary>
    /// The one way a standard-derived class is created, whatever supplies its envelope. Every guard
    /// lives here so that a second create path cannot skip one.
    /// </summary>
    private async Task<ClassProfile> CreateAsync(
        string title,
        string? expectedCode,
        Func<ClassStandardRecord, BootstrapMeta, Task<JobEnvelope>> buildEnvelope,
        string? note,
        CancellationToken ct)
    {
        var standards = await _standards.GetIndexAsync(ct);
        var titleCodes = await _titleCodes.GetAsync(ct);

        var std = FindStandard(standards, title)
                  ?? throw new BootstrapRefusedException(BootstrapRefusal.NoStandard, $"No standard found for \"{title}\"");

        var tc = titleCodes.FindTitleCode(std.LongTitle);

        // Refuse rather than silently building the class under the successor's code: the two
        // standards differ, and the successor's own standard is the right source to build from.
        var sup = tc is not null ? titleCodes.SupersededBy(tc.Code) : null;
        if (sup is not null)
        {
            throw new BootstrapRefusedException(BootstrapRefusal.Superseded,
                $"{std.LongTitle} ({sup.FromCode}) is superseded by {sup.ToTitle} ({sup.ToCode}) — bootstrap that class instead.");
        }

        // The same rule as the candidate list, enforced where classes are made: hiding a class from a
        // list is not enough when another path (one class at a time, or an envelope moved in from
        // another environment) can still create it.
        if (tc is null || !tc.IsInUse)
        {
            throw new BootstrapRefusedException(BootstrapRefusal.NotOnPayroll,
                tc is null
                    ? $"{std.LongTitle} doesn't match a UC job code, so its class couldn't be used."
                    : $"{std.LongTitle} ({tc.Code}) isn't on UC Davis payroll, so its class couldn't be used.");
        }

        var code = tc.Code;
        if (expectedCode is not null && TitleCodeIndex.Pad(expectedCode) != TitleCodeIndex.Pad(code))
        {
            throw new BootstrapRefusedException(BootstrapRefusal.CodeMismatch,
                $"{std.LongTitle} resolves to {(code.Length > 0 ? code : "no job code")} here, but the envelope was made for {(expectedCode.Length > 0 ? expectedCode : "no job code")}.");
        }

        var slug = SlugFor(code, std.LongTitle);
        await RefuseIfProfiledAsync(slug, code, std.LongTitle, titleCodes, ct);

        var meta = new BootstrapMeta
        {
            Title = std.LongTitle,
            Code = code,
            Family = tc?.Family ?? "",
            Function = tc?.Function ?? "",
            Program = std.PersProg,
        };

        var envelope = await buildEnvelope(std, meta);

        // House rules for education, applied in code whether the envelope was synthesized here or
        // moved in from another environment.
        QualificationRules.Apply(envelope);

        var profile = new ClassProfile
        {
            Slug = slug,
            UcJobCode = code,
            Title = std.LongTitle,
            CtJobFamily = tc?.Family ?? "",
            CtJobFunction = tc?.Function ?? "",
            PersonnelProgram = std.PersProg,
            CorpusSize = 0,
            RepresentativeSummary = !string.IsNullOrEmpty(std.GenericScope) ? std.GenericScope : std.CustomScope,
            Envelope = envelope,
            EnvelopeSource = EnvelopeSource.Standard,
            Coverage = null,
            GeneratedNote = note ??
                "Standard-derived — no JD corpus yet. Ingest JDs for this class to learn the real envelope.",
        };

        // Fixed attributes come from the standard, with the title reference as the grade fallback.
        // Each is a distribution of ONE observation, which is honest: a standard is one source.
        AddSoleDistribution(profile, DistributionField.SalaryGrade,
            !string.IsNullOrEmpty(std.Grade) ? std.Grade : tc?.Grade);
        AddSoleDistribution(profile, DistributionField.FlsaStatus, std.Flsa);
        AddSoleDistribution(profile, DistributionField.UnionCode, std.Union);

        // A standard says nothing about supervision or working conditions, and "unknown" is a real
        // observation rather than an absent one — so it is stored as a null value with a count.
        AddUnknownDistribution(profile, DistributionField.Supervises);
        AddUnknownDistribution(profile, DistributionField.Leads);
        AddUnknownDistribution(profile, DistributionField.WorksOutdoorsOver50pct);

        AddQualItems(profile, ProfileQualKind.License, std.Licenses);
        AddQualItems(profile, ProfileQualKind.Education, std.Education);
        AddQualItems(profile, ProfileQualKind.KsaMin, std.Ksa);

        _db.ClassProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
        return profile;
    }

    /// <summary>
    /// The standard a title names: the exact title, then the strict key, and only then the loose one.
    ///
    /// The loose key drops union suffixes, so "Financial Analyst 3 CX" and the retired "Financial
    /// Analyst 3" share it, and a loose-only lookup returned whichever came first — usually the
    /// retired class, which the supersession guard then refused. The strict key keeps the suffix
    /// while still expanding abbreviations ("Project Policy Anl 4 Rp"). A loose-only match remains
    /// for titles that differ in nothing else, and a wrong one can only be refused, never built.
    /// </summary>
    private static ClassStandardRecord? FindStandard(StandardsIndex standards, string title)
    {
        var strict = TitleNormalizer.TitleCodeKey(title);
        // Site-aware: a Health Center or Student Health class is never built from the regular class's standard.
        var loose = Sites.ClassKey(title);
        return standards.All.FirstOrDefault(s => s.LongTitle == title)
               ?? standards.All.FirstOrDefault(s => TitleNormalizer.TitleCodeKey(s.LongTitle) == strict)
               ?? standards.All.FirstOrDefault(s => Sites.ClassKey(s.LongTitle) == loose);
    }

    /// <summary>
    /// A class already has a profile when one shares its slug, its job code, or its loose title key —
    /// the same test the candidate list applies. Checking the slug alone let a standard-derived class
    /// be created beside a corpus profile filed under a differently spelled title. A profile under a
    /// superseded code does not count: it is a dead class awaiting retirement.
    /// </summary>
    private async Task RefuseIfProfiledAsync(
        string slug, string code, string title, TitleCodeIndex titleCodes, CancellationToken ct)
    {
        var profiles = await _db.ClassProfiles.AsNoTracking()
            .Select(p => new { p.Slug, p.UcJobCode, p.Title })
            .ToListAsync(ct);

        var key = Sites.ClassKey(title);
        var hit = profiles.FirstOrDefault(p => p.Slug == slug)
                  ?? profiles.FirstOrDefault(p =>
                      !titleCodes.IsSuperseded(p.UcJobCode)
                      && ((code.Length > 0 && TitleCodeIndex.Pad(p.UcJobCode) == TitleCodeIndex.Pad(code))
                          || Sites.ClassKey(p.Title) == key));

        if (hit is not null)
        {
            throw new BootstrapRefusedException(BootstrapRefusal.Exists,
                $"A profile already exists for {title}: {hit.Title} ({hit.Slug}).");
        }
    }

    /// <summary>
    /// Slug rule, matching the reference exactly — including the "std-" fallback when no code
    /// resolved. Getting this wrong is how a re-bootstrap creates a DUPLICATE profile for one class
    /// under a second name.
    /// </summary>
    public static string SlugFor(string code, string title)
    {
        var chars = title.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        var collapsed = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return !string.IsNullOrEmpty(code) ? $"{code}-{collapsed}" : $"std-{collapsed}";
    }

    private static void AddSoleDistribution(ClassProfile p, DistributionField field, string? value)
    {
        var dist = new ProfileDistribution { Field = field };

        if (!string.IsNullOrEmpty(value))
        {
            dist.Consensus = value;
            dist.Agreement = 1;
            dist.Values.Add(new ProfileDistributionValue { Ordinal = 0, Value = value, Count = 1 });
        }
        else
        {
            dist.Consensus = null;
            dist.Agreement = 0;
        }

        p.Distributions.Add(dist);
    }

    private static void AddUnknownDistribution(ClassProfile p, DistributionField field)
    {
        var dist = new ProfileDistribution { Field = field, Consensus = null, Agreement = 1 };
        dist.Values.Add(new ProfileDistributionValue { Ordinal = 0, Value = null, Count = 1 });
        p.Distributions.Add(dist);
    }

    /// <summary>
    /// Frequency 1 on every item: a standard states each requirement once, and claiming anything
    /// else would fabricate corpus evidence that does not exist.
    /// </summary>
    private static void AddQualItems(ClassProfile p, ProfileQualKind kind, List<string> texts)
    {
        for (var i = 0; i < texts.Count; i++)
        {
            p.Qualifications.Add(new ProfileQualItem
            {
                Kind = kind,
                Ordinal = i,
                Text = texts[i],
                Freq = 1,
            });
        }
    }
}

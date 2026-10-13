using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Server.Core.Ai;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Profiles;
using Server.Core.Titles;

namespace Server.Core.Standards;

/// <summary>The class a job is attached to.</summary>
public sealed class JobClass
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public int CorpusSize { get; set; }
    public EnvelopeSource? EnvelopeSource { get; set; }

    /// <summary>A starter envelope built from a blank standard: little more than a title went into it.</summary>
    public bool FromBlankStandard { get; set; }
}

/// <summary>The standard a job is attached to, and how.</summary>
public sealed class JobStandardLink
{
    public string Title { get; set; } = "";

    /// <summary>"title" — its title matches the job's; "hand" — an admin matched or entered it.</summary>
    public string How { get; set; } = "title";

    /// <summary>Matched, but with no key responsibilities: it can't be bootstrapped until the full standard is added.</summary>
    public bool Blank { get; set; }

    /// <summary>The job is superseded, and this is its successor's standard — where its JDs now go.</summary>
    public bool FromSuccessor { get; set; }
}

/// <summary>One UC job code, and what JDWriter has attached to it.</summary>
public sealed class JobRow
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>Active at UC Davis: on payroll, unless an admin has said otherwise.</summary>
    public bool Active { get; set; }

    /// <summary>What the payroll reference says, which <see cref="Active"/> departs from when set by hand.</summary>
    public bool OnPayroll { get; set; }

    public string? Family { get; set; }
    public string? Grade { get; set; }
    public string? Site { get; set; }

    /// <summary>Set when a union successor has superseded this code: the code to use instead.</summary>
    public string? SupersededBy { get; set; }

    public JobClass? Class { get; set; }
    public JobStandardLink? Standard { get; set; }

    /// <summary>The code in the Career Tracks listing, when it's there.</summary>
    public JobCt? Ct { get; set; }
}

/// <summary>What the Career Tracks listing says of a code.</summary>
public sealed class JobCt
{
    public string Title { get; set; } = "";
    public string Family { get; set; } = "";
    public string Function { get; set; } = "";
    public string Level { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Grade { get; set; } = "";
    public string Flsa { get; set; } = "";
    public string PersonnelProgram { get; set; } = "";
    public string Status { get; set; } = "";
    public bool OpOnly { get; set; }
    public bool Supervisory { get; set; }
    public DateOnly? LastUpdated { get; set; }
}

/// <summary>
/// Every UC job code, and the class and standard each is attached to — the one place to see
/// coverage. A superseded code shows its successor's class, since that is where its JDs go.
/// </summary>
public sealed class JobsDirectory
{
    private readonly AppDbContext _db;
    private readonly IStandardsStore _standards;
    private readonly ITitleCodeService _titleCodes;

    public JobsDirectory(AppDbContext db, IStandardsStore standards, ITitleCodeService titleCodes)
    {
        _db = db;
        _standards = standards;
        _titleCodes = titleCodes;
    }

    /// <param name="scope">"active" (on payroll), "inactive", or "all".</param>
    public async Task<List<JobRow>> ListAsync(string scope, CancellationToken ct = default)
    {
        var index = await _titleCodes.GetAsync(ct);
        var standards = await _standards.GetIndexAsync(ct);

        var classes = (await _db.ClassProfiles.AsNoTracking()
                .Select(p => new { p.UcJobCode, p.Slug, p.Title, p.CorpusSize, p.EnvelopeSource })
                .ToListAsync(ct))
            .GroupBy(p => TitleCodeIndex.Pad(p.UcJobCode))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var byHand = (await _db.StandardMatchDecisions.AsNoTracking().Where(d => d.IsMatch)
                .Select(d => d.StandardTitle).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var standardByCode = new Dictionary<string, JobStandardLink>(StringComparer.Ordinal);
        foreach (var s in standards.All)
        {
            if (StandardCodes.For(s, index) is { } tc)
            {
                standardByCode.TryAdd(TitleCodeIndex.Pad(tc.Code), new JobStandardLink
                {
                    Title = s.LongTitle,
                    How = byHand.Contains(s.LongTitle) ? "hand" : "title",
                    Blank = s.IsBlank,
                });
            }
        }

        var onPayroll = index.All
            .GroupBy(t => TitleCodeIndex.Pad(t.Code))
            .ToDictionary(g => g.Key, g => g.Any(t => t.OnPayroll), StringComparer.Ordinal);

        return index.All
            .Where(t => scope switch
            {
                "active" => t.IsInUse,
                "inactive" => !t.IsInUse,
                _ => true,
            })
            .DistinctBy(t => TitleCodeIndex.Pad(t.Code))
            .Select(t =>
            {
                var code = TitleCodeIndex.Pad(t.Code);
                var successor = index.SupersededBy(t.Code)?.ToCode;
                var cls = classes.GetValueOrDefault(code)
                          ?? (successor is null ? null : classes.GetValueOrDefault(TitleCodeIndex.Pad(successor)));
                // A superseded job is tied to its successor's standard, as it is to its successor's class.
                var standard = standardByCode.GetValueOrDefault(code);
                if (standard is null && successor is not null
                    && standardByCode.GetValueOrDefault(TitleCodeIndex.Pad(successor)) is { } next)
                {
                    standard = new JobStandardLink { Title = next.Title, How = next.How, Blank = next.Blank, FromSuccessor = true };
                }

                return new JobRow
                {
                    Code = t.Code,
                    Title = ProfileAggregator.Titleize(t.Title),
                    Active = t.IsInUse,
                    OnPayroll = onPayroll[code],
                    Family = string.IsNullOrEmpty(t.Family) ? null : t.Family,
                    Grade = string.IsNullOrEmpty(t.Grade) ? null : t.Grade,
                    Site = Sites.SiteOf(t.Title),
                    SupersededBy = successor,
                    Class = cls is null
                        ? null
                        : new JobClass
                        {
                            Slug = cls.Slug,
                            Title = cls.Title,
                            CorpusSize = cls.CorpusSize,
                            EnvelopeSource = cls.EnvelopeSource,
                            FromBlankStandard = cls.EnvelopeSource == EnvelopeSource.Standard && cls.CorpusSize == 0
                                                && standards.For(cls.Title, cls.UcJobCode) is { IsBlank: true },
                        },
                    Standard = standard,
                    Ct = t.Ct is not { } c
                        ? null
                        : new JobCt
                        {
                            Title = c.Title, Family = c.JobFamily, Function = c.JobFunction, Level = c.LevelCode, Origin = c.Origin,
                            Grade = c.Grade, Flsa = c.Flsa, PersonnelProgram = c.PersonnelProgram, Status = c.JobStatus,
                            OpOnly = c.OpOnly, Supervisory = c.IsSupervisory, LastUpdated = c.LastUpdated,
                        },
                };
            })
            .OrderBy(r => r.Title, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>A standard as an admin enters it: from a Word export the AI has read, or typed.</summary>
public sealed class StandardDraft
{
    [Description("The standard's title exactly as the document states it (e.g. 'Animal Health Technician 3').")]
    public string Title { get; set; } = "";

    [Description("The scope / summary paragraph(s), verbatim.")]
    public string Scope { get; set; } = "";

    [Description("Key responsibilities, one per item, verbatim.")]
    public List<string> KeyResponsibilities { get; set; } = [];

    [Description("Knowledge, skills and abilities, one per item, verbatim.")]
    public List<string> Ksa { get; set; } = [];

    [Description("Education requirements, one per item, verbatim.")]
    public List<string> Education { get; set; } = [];

    [Description("Licenses and certifications, one per item, verbatim.")]
    public List<string> Licenses { get; set; } = [];

    [Description("Special conditions of employment, one per item, verbatim.")]
    public List<string> SpecialConditions { get; set; } = [];
}

/// <summary>
/// Standards added by hand, for a job whose standard isn't in any uploaded workbook: read from a
/// Word export of the standard, then reviewed and saved by an admin.
/// </summary>
public sealed class ManualStandards
{
    /// <summary>Generous bounds on one standard: an admin's input, still stored as given.</summary>
    private const int MaxItems = 100;

    private const int MaxText = 8_000;

    public const string ReadSystem =
        """
        You are given the text of one official University of California job standard, exported to a
        Word document. Return its title and its sections — scope, key responsibilities, knowledge /
        skills / abilities, education, licenses and certifications, special conditions — exactly as
        the document words them. Copy; never paraphrase, summarize, merge or invent. Leave a section
        empty when the document has none. Ignore page headers, footers and boilerplate about the
        standard itself.
        """;

    private readonly AppDbContext _db;
    private readonly IStandardsStore _standards;
    private readonly ITitleCodeService _titleCodes;
    private readonly IStructuredLlm _llm;

    public ManualStandards(AppDbContext db, IStandardsStore standards, ITitleCodeService titleCodes, IStructuredLlm llm)
    {
        _db = db;
        _standards = standards;
        _titleCodes = titleCodes;
        _llm = llm;
    }

    /// <summary>Whether a document can be read: reading needs an AI provider.</summary>
    public bool CanRead => _llm.HasApiKey;

    /// <summary>Read a standard document's text into its sections, for an admin to review.</summary>
    public Task<StandardDraft> ReadAsync(string text, CancellationToken ct = default) =>
        _llm.StructuredAsync<StandardDraft>(new StructuredRequest
        {
            System = ReadSystem,
            User = $"JOB STANDARD:\n{text}",
            Effort = LlmEffort.Low,
            MaxTokens = 8000,
            Label = "standards.read",
        }, ct);

    /// <summary>
    /// Save a standard for a job: added, or replacing one of the same title. It is linked to the job
    /// by a confirmed match, so the link holds whatever its title, and survives re-uploading the
    /// workbooks — which replace standards by title — and travels with an envelope export.
    /// </summary>
    public async Task SaveAsync(string code, StandardDraft draft, int? userId, CancellationToken ct = default)
    {
        var index = await _titleCodes.GetAsync(ct);
        var tc = index.FindByCode(code) ?? throw new ArgumentException($"No UC job code {code}.");

        var title = draft.Title.Trim();
        var lists = new[] { draft.KeyResponsibilities, draft.Ksa, draft.Education, draft.Licenses, draft.SpecialConditions };
        if (title.Length is 0 or > 300)
        {
            throw new ArgumentException("The standard needs a title (up to 300 characters).");
        }

        if (lists.Any(l => l.Count > MaxItems) || draft.Scope.Length > MaxText || lists.SelectMany(l => l).Any(i => i.Length > MaxText))
        {
            throw new ArgumentException("This standard is larger than any real one; it was not saved.");
        }

        static List<string> Clean(List<string> items) => [.. items.Select(i => i.Trim()).Where(i => i.Length > 0)];

        var record = new ClassStandardRecord
        {
            LongTitle = title,
            Code = tc.Code,
            Grade = tc.Grade ?? "",
            GenericScope = draft.Scope.Trim(),
            KeyResponsibilities = Clean(draft.KeyResponsibilities),
            Ksa = Clean(draft.Ksa),
            Education = Clean(draft.Education),
            Licenses = Clean(draft.Licenses),
            SpecialConditions = Clean(draft.SpecialConditions),
        };

        _db.JobStandards.RemoveRange(await _db.JobStandards.Where(s => s.LongTitle == title).ToListAsync(ct));
        _db.JobStandards.Add(StandardsStore.ToEntity(record));

        _db.StandardMatchDecisions.RemoveRange(await _db.StandardMatchDecisions
            .Where(d => d.StandardTitle == title && (d.IsMatch || d.UcJobCode == tc.Code)).ToListAsync(ct));
        _db.StandardMatchDecisions.Add(new StandardMatchDecision
        {
            StandardTitle = title,
            UcJobCode = tc.Code,
            IsMatch = true,
            DecidedByUserId = userId,
            DecidedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct);
        _standards.Invalidate();
    }
}

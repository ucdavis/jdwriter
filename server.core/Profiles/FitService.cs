namespace Server.Core.Profiles;

/// <summary>
/// A real JD that fits its assigned class poorly — low coverage of that class's envelope — and is
/// therefore a candidate for reclassification or for the review-and-nudge workflow.
/// </summary>
/// <summary>
/// A function in a real JD that its class envelope does not cover, and the share of that
/// position's time it holds.
///
/// A named type rather than a tuple, and that is not style. A ValueTuple exposes its members as
/// FIELDS, which System.Text.Json ignores — every entry serialized as an empty object, so the
/// review screen's actual content arrived as blank rows with no error anywhere.
/// </summary>
public sealed class IdiosyncraticFunction
{
    public string Name { get; set; } = "";
    public double Pct { get; set; }
}

public sealed class Misfit
{
    public string Slug { get; set; } = "";
    public string ClassTitle { get; set; } = "";
    public string UcJobCode { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public double CoveredPct { get; set; }

    /// <summary>The work this JD does that its class envelope does not account for.</summary>
    public List<IdiosyncraticFunction> Idiosyncratic { get; set; } = [];
}

public sealed class FitSummary
{
    public int TotalJds { get; set; }
    public List<Misfit> Misfits { get; set; } = [];
    public int Threshold { get; set; }
}

public interface IFitService
{
    Task<FitSummary> GetMisfitsAsync(int threshold = 90, CancellationToken ct = default);
}

/// <summary>
/// Collects, across every class, the JDs whose fit against their own envelope falls below a
/// threshold. Ported from the POC's src/lib/profile/fit.ts.
///
/// Reads the coverage already stored on each profile and recomputes nothing. That is deliberate:
/// this is a browsing surface over work done at ingest, and recomputing would make opening a list
/// page cost a corpus scan.
/// </summary>
public sealed class FitService : IFitService
{
    private readonly IClassProfileRepository _profiles;

    public FitService(IClassProfileRepository profiles)
    {
        _profiles = profiles;
    }

    public async Task<FitSummary> GetMisfitsAsync(int threshold = 90, CancellationToken ct = default)
    {
        var misfits = new List<Misfit>();
        var totalJds = 0;

        foreach (var p in await _profiles.GetAllAsync(ct))
        {
            // A profile with no coverage report has not been through ingest's comparison step, so
            // there is nothing to judge it against — skip rather than count it as a perfect fit.
            if (p.Coverage is null)
            {
                continue;
            }

            totalJds += p.Coverage.N;

            foreach (var jd in p.Coverage.PerJd)
            {
                if (jd.CoveredPct >= threshold)
                {
                    continue;
                }

                misfits.Add(new Misfit
                {
                    Slug = p.Slug,
                    ClassTitle = p.Title,
                    UcJobCode = p.UcJobCode,
                    SourceFile = jd.SourceFile,
                    CoveredPct = jd.CoveredPct,
                    Idiosyncratic = jd.Uncovered
                        .OrderBy(u => u.Ordinal)
                        .Select(u => new IdiosyncraticFunction { Name = u.Name, Pct = u.Pct })
                        .ToList(),
                });
            }
        }

        return new FitSummary
        {
            TotalJds = totalJds,
            // Worst fit first: this is a work queue, so the JDs needing attention belong on top.
            Misfits = misfits.OrderBy(m => m.CoveredPct).ToList(),
            Threshold = threshold,
        };
    }
}

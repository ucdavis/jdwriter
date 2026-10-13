using Server.Core.Domain;

namespace Server.Core.Titles;

/// <summary>
/// Lays the Career Tracks listing over the title reference as the index loads, so it survives the
/// reference being reimported. A code the reference lacks is added (not on payroll, so not active);
/// a code it has takes the listing's grade, family and function, which are newer.
/// </summary>
public static class CtOverlay
{
    /// <summary>The source of a code that only the Career Tracks listing has.</summary>
    public const string Source = "ct_listing";

    public static List<TitleCode> Apply(List<TitleCode> rows, IReadOnlyCollection<CtTitle> listing)
    {
        if (listing.Count == 0)
        {
            return rows;
        }

        var byCode = listing.GroupBy(c => TitleCodeIndex.Pad(c.UcJobCode))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var code = TitleCodeIndex.Pad(row.Code);
            known.Add(code);
            if (!byCode.TryGetValue(code, out var ct))
            {
                continue;
            }

            row.Ct = ct;
            if (ct.Grade.Length > 0)
            {
                row.Grade = $"Grade {ct.Grade}";
            }

            if (ct.JobFamily.Length > 0)
            {
                row.Family = ct.JobFamily;
            }

            if (ct.JobFunction.Length > 0)
            {
                row.Function = ct.JobFunction;
            }
        }

        foreach (var (code, ct) in byCode.Where(kv => !known.Contains(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            rows.Add(new TitleCode
            {
                Code = code,
                Title = ct.Title,
                TitleKey = TitleNormalizer.TitleKey(ct.Title),
                TitleCodeKey = TitleNormalizer.TitleCodeKey(ct.Title),
                Grade = ct.Grade.Length > 0 ? $"Grade {ct.Grade}" : "",
                Family = ct.JobFamily,
                Function = ct.JobFunction,
                Source = Source,
                Ct = ct,
            });
        }

        return rows;
    }
}

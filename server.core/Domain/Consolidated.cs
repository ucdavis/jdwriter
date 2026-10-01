namespace Server.Core.Domain;

/// <summary>
/// A semantically merged category of responsibility functions.
///
/// The governing rule is visible in the split between this type's fields: the model decides
/// which raw function names belong together and which are outliers worth dropping
/// (<see cref="Members"/>, <see cref="ProfileDroppedItem"/>), while every number here is
/// recomputed deterministically from the records so the statistics stay honest.
/// </summary>
public class ConsolidatedFunction
{
    public int Id { get; set; }
    public int ClassProfileId { get; set; }
    public ClassProfile? ClassProfile { get; set; }

    public int Ordinal { get; set; }

    /// <summary>Broad category name chosen by the model.</summary>
    public string Name { get; set; } = "";

    /// <summary>Mean percent across the JDs that include this category.</summary>
    public double MeanPct { get; set; }

    /// <summary>
    /// Mean share of a typical JD's 100%, averaged across ALL JDs in the class rather than only
    /// those including the category. These sum to about 100; <see cref="MeanPct"/> does not.
    /// </summary>
    public double TemplatePct { get; set; }

    public double MinPct { get; set; }
    public double MaxPct { get; set; }

    /// <summary>Share of JDs with any member function, 0..1.</summary>
    public double Prevalence { get; set; }

    public List<ConsolidatedFunctionMember> Members { get; set; } = [];
    public List<ConsolidatedFunctionSampleDuty> SampleDuties { get; set; } = [];
}

/// <summary>A raw function name merged into a consolidated category.</summary>
public class ConsolidatedFunctionMember : ITextItem
{
    public int Id { get; set; }
    public int ConsolidatedFunctionId { get; set; }
    public ConsolidatedFunction? ConsolidatedFunction { get; set; }

    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>A representative duty bullet for a consolidated category.</summary>
public class ConsolidatedFunctionSampleDuty : ITextItem
{
    public int Id { get; set; }
    public int ConsolidatedFunctionId { get; set; }
    public ConsolidatedFunction? ConsolidatedFunction { get; set; }

    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>A canonical requirement, merged from variant phrasings across the corpus.</summary>
public class ConsolidatedQual
{
    public int Id { get; set; }
    public int ClassProfileId { get; set; }
    public ClassProfile? ClassProfile { get; set; }

    public ConsolidatedQualKind Kind { get; set; }
    public int Ordinal { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Share of JDs requiring it, 0..1.</summary>
    public double Freq { get; set; }

    public List<ConsolidatedQualMember> Members { get; set; } = [];
}

/// <summary>A variant phrasing merged into a canonical requirement.</summary>
public class ConsolidatedQualMember : ITextItem
{
    public int Id { get; set; }
    public int ConsolidatedQualId { get; set; }
    public ConsolidatedQual? ConsolidatedQual { get; set; }

    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// A function or qualification the consolidation step discarded as an outlier. Retained rather
/// than forgotten: a dropped item is a judgement worth being able to review.
/// </summary>
public class ProfileDroppedItem : ITextItem
{
    public int Id { get; set; }
    public int ClassProfileId { get; set; }
    public ClassProfile? ClassProfile { get; set; }

    public DroppedItemKind Kind { get; set; }
    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
}

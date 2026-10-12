namespace Server.Core.Ingest;

/// <summary>
/// Names for JDs added in the app, as their <c>SourceFile</c>. They identify the JD in every
/// environment, so an environment package can match it: never a local row id, which means a
/// different JD on each server. An HRTMS export is named by its own file name already.
/// </summary>
public static class CorpusNames
{
    /// <summary>A document from another unit, by the hash of its words: the same document added in two places is one JD.</summary>
    public static string Imported(string sha256) => $"import/{sha256[..16]}";

    /// <summary>A Classify submission, by the hash of its text.</summary>
    public static string Classified(string sha256) => $"classify/{sha256[..16]}";

    /// <summary>A finished JD written in the app, by its draft's permanent key: re-finishing it replaces it.</summary>
    public static string Authored(Guid corpusKey) => $"authored/{corpusKey:N}";
}

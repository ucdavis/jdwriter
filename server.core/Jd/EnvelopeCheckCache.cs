using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace Server.Core.Jd;

/// <summary>
/// Remembers the envelope check's verdict for a class and a set of additions, so assembly can use
/// the SERVER's verdict without paying for the check twice.
///
/// The verdict decides whether a JD joins the corpus, so it is not taken from the browser. The
/// build's check step records it here; assembly looks it up and, if it is missing (a restart,
/// another instance, an API caller that skipped the step), runs the check itself.
/// </summary>
public sealed class EnvelopeCheckCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);
    private readonly IMemoryCache _cache;

    public EnvelopeCheckCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public void Remember(string slug, BuildInputs inputs, EnvelopeVerdict verdict) =>
        _cache.Set(Key(slug, inputs), verdict, Lifetime);

    public EnvelopeVerdict? Recall(string slug, BuildInputs inputs) =>
        _cache.TryGetValue(Key(slug, inputs), out EnvelopeVerdict verdict) ? verdict : null;

    /// <summary>The class plus exactly what the check judged: the additions and notes.</summary>
    private static string Key(string slug, BuildInputs inputs) =>
        "jdw:envcheck:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(slug + "\n" + JdAssembler.AdditionsText(inputs))));
}

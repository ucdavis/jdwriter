using System.Collections.Concurrent;

namespace Server.Core.Ai;

/// <summary>One AI call, as measured: what it was, how long it took, what it cost. Never its content.</summary>
public sealed record AiCall(
    DateTimeOffset At,
    string Label,
    long Ms,
    long? InputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    string Effort,
    string Provider,
    string Outcome);

/// <summary>
/// The last few AI calls, in memory, for the admin's Settings page: an admin can time a slow step
/// without reading server logs. Metadata only — no prompts, no answers — and gone on restart.
/// </summary>
public sealed class AiCallLog
{
    public const int Capacity = 50;

    private readonly ConcurrentQueue<AiCall> _calls = new();

    public void Record(AiCall call)
    {
        _calls.Enqueue(call);
        while (_calls.Count > Capacity && _calls.TryDequeue(out _))
        {
        }
    }

    /// <summary>Newest first.</summary>
    public List<AiCall> Recent() => [.. _calls.Reverse()];
}

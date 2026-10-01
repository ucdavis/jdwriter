using System.Text.Json;
using Server.Core.Ai;

namespace Server.Tests.Profiles;

/// <summary>
/// A stand-in for the model that records every request and returns canned replies.
///
/// Two jobs. It keeps these tests free of network calls and API spend — the nine real call sites are
/// verified by asserting the prompt they ASSEMBLE, since prompt drift during a port is the realistic
/// failure. And by returning values a real model would not produce (percentages that do not sum to
/// 100, for instance) it proves the numbers in the output are recomputed in C# rather than taken on
/// trust.
/// </summary>
public sealed class FakeStructuredLlm : IStructuredLlm
{
    private readonly Queue<string> _responses = new();

    public List<StructuredRequest> Requests { get; } = [];

    public bool HasApiKey { get; set; } = true;

    /// <summary>Set to throw on the next call, to exercise the deterministic fallback path.</summary>
    public Exception? ThrowOnNext { get; set; }

    public FakeStructuredLlm Returns(object response)
    {
        _responses.Enqueue(JsonSerializer.Serialize(response, StructuredSchema.SerializerOptions));
        return this;
    }

    /// <summary>Queue a raw JSON reply, for shapes a C# object cannot express naturally.</summary>
    public FakeStructuredLlm ReturnsJson(string json)
    {
        _responses.Enqueue(json);
        return this;
    }

    public Task<T> StructuredAsync<T>(StructuredRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);

        if (ThrowOnNext is not null)
        {
            var ex = ThrowOnNext;
            ThrowOnNext = null;
            throw ex;
        }

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"FakeStructuredLlm has no queued response for '{request.Label ?? "call"}'. "
                + $"Requests so far: {Requests.Count}.");
        }

        return Task.FromResult(
            JsonSerializer.Deserialize<T>(_responses.Dequeue(), StructuredSchema.SerializerOptions)!);
    }

    public StructuredRequest RequestFor(string label) =>
        Requests.SingleOrDefault(r => r.Label == label)
        ?? throw new InvalidOperationException(
            $"No request labelled '{label}'. Saw: {string.Join(", ", Requests.Select(r => r.Label ?? "(none)"))}.");
}

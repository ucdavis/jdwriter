using System.Text.Json;
using Server.Core.Ai;

namespace Server.Tests.Intake;

/// <summary>
/// A stand-in for <see cref="IStructuredLlm"/> that records every request and replays canned
/// responses by label.
///
/// Recording the requests is the point. The model's answer is not deterministic and is not what a
/// port can regress; the STRING WE SEND is, and it is what these tests assert.
/// </summary>
public sealed class FakeStructuredLlm : IStructuredLlm
{
    private readonly Dictionary<string, Queue<object>> _responses = new(StringComparer.Ordinal);

    public bool HasApiKey { get; set; } = true;

    /// <summary>Every request made, in order.</summary>
    public List<StructuredRequest> Requests { get; } = [];

    /// <summary>Queue a response for a call label. Several may be queued for repeated calls.</summary>
    public FakeStructuredLlm Reply(string label, object response)
    {
        if (!_responses.TryGetValue(label, out var queue))
        {
            queue = new Queue<object>();
            _responses[label] = queue;
        }

        queue.Enqueue(response);
        return this;
    }

    public StructuredRequest RequestFor(string label) =>
        Requests.SingleOrDefault(r => r.Label == label)
        ?? throw new InvalidOperationException(
            $"No request was made with label '{label}'. Labels seen: {string.Join(", ", Requests.Select(r => r.Label))}");

    public bool WasCalled(string label) => Requests.Any(r => r.Label == label);

    /// <summary>The text of every prompt sent, system and user, for leak checks.</summary>
    public IEnumerable<string> AllPromptText =>
        Requests.SelectMany(r => new[] { r.System, r.User });

    public Task<T> StructuredAsync<T>(StructuredRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);

        var label = request.Label ?? "";
        if (!_responses.TryGetValue(label, out var queue) || queue.Count == 0)
        {
            throw new InvalidOperationException(
                $"FakeStructuredLlm has no queued response for label '{label}'.");
        }

        var response = queue.Dequeue();

        // Round-trip through JSON so the fake exercises the same binding the real seam does — a
        // response shape that would not deserialize in production must not pass here either.
        var json = JsonSerializer.Serialize(response, StructuredSchema.SerializerOptions);
        var typed = JsonSerializer.Deserialize<T>(json, StructuredSchema.SerializerOptions)
                    ?? throw new InvalidOperationException($"Canned response for '{label}' deserialized to null.");

        return Task.FromResult(typed);
    }
}

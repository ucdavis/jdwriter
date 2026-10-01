using Server.Core.Ai;

namespace Server.Tests.Jd;

/// <summary>
/// A stand-in for the model that records what it was asked and returns canned answers, keyed by
/// call label.
///
/// The nine real call sites are tested by asserting the PROMPT they assemble, not by calling the
/// model: prompt drift is the realistic port failure, and it costs nothing to detect. This fake is
/// what makes that possible, and it also lets the assembler be driven through deliberately
/// hostile responses — a model that skips a function, addresses a line that does not exist, or
/// returns percentages that do not sum to 100.
/// </summary>
public sealed class FakeStructuredLlm : IStructuredLlm
{
    private readonly Dictionary<string, Func<StructuredRequest, object>> _responses = new(StringComparer.Ordinal);

    public bool HasApiKey => true;

    /// <summary>Every request made, in order, so a test can assert the exact prompt text.</summary>
    public List<StructuredRequest> Requests { get; } = [];

    public FakeStructuredLlm Respond<T>(string label, T response)
    {
        _responses[label] = _ => response!;
        return this;
    }

    public FakeStructuredLlm Respond<T>(string label, Func<StructuredRequest, T> factory)
    {
        _responses[label] = r => factory(r)!;
        return this;
    }

    public StructuredRequest RequestFor(string label) =>
        Requests.SingleOrDefault(r => r.Label == label)
        ?? throw new InvalidOperationException(
            $"No request was made with label '{label}'. Labels seen: " +
            $"{string.Join(", ", Requests.Select(r => r.Label ?? "(none)"))}");

    public bool Called(string label) => Requests.Any(r => r.Label == label);

    public Task<T> StructuredAsync<T>(StructuredRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);

        var label = request.Label ?? "";
        if (!_responses.TryGetValue(label, out var factory))
        {
            throw new InvalidOperationException(
                $"FakeStructuredLlm has no canned response for label '{label}'. " +
                "Register one with Respond<T>(label, response).");
        }

        return Task.FromResult((T)factory(request));
    }
}

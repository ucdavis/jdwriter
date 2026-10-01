using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Server.Core.Ai;

/// <summary>
/// How much thinking a call should do. Mirrors the reference's effort ladder; the cheap call sites
/// sit at Low and stay there until a fixture set says otherwise.
/// </summary>
public enum LlmEffort
{
    Low,
    Medium,
    High,
}

/// <summary>One structured-output request.</summary>
public sealed class StructuredRequest
{
    public required string System { get; init; }
    public required string User { get; init; }
    public LlmEffort Effort { get; init; } = LlmEffort.Medium;
    public int MaxTokens { get; init; } = 8000;

    /// <summary>
    /// Stable identifier for this call site, e.g. "intake.shortlist". Carried verbatim from the
    /// POC so measured token costs stay comparable across the port.
    /// </summary>
    public string? Label { get; init; }
}

/// <summary>
/// A structured call that did not produce a result. Separate from a transport failure because the
/// remedies are different and the caller usually wants to say so.
/// </summary>
public sealed class StructuredLlmException : Exception
{
    public StructuredLlmException(string message) : base(message)
    {
    }
}

public interface IStructuredLlm
{
    /// <summary>Whether a key is configured. Lets a caller degrade rather than throw at request time.</summary>
    bool HasApiKey { get; }

    Task<T> StructuredAsync<T>(StructuredRequest request, CancellationToken ct = default);
}

/// <summary>
/// Provider-agnostic seam for structured-output completions. Ported from the POC's
/// src/lib/ai/llm.ts.
///
/// Every model call in the system goes through this one method, which is what makes the governing
/// rule enforceable: **the model decides, code assembles**. Call sites return indices rather than
/// reproduced strings, return deltas rather than whole documents, and recompute anything numeric in
/// C#. A model used as a data bus is both the cost driver and a silent reliability risk — a dropped
/// duty or reworded KSA is still schema-valid, so nothing catches it.
///
/// Differences from the reference worth knowing:
///   • There is no <c>messages.parse()</c> helper in the C# SDK, so the text block is deserialized
///     here against the same schema the request declared.
///   • The schema is derived from the POCO by <see cref="StructuredSchema"/> rather than from Zod.
/// </summary>
public sealed class StructuredLlm : IStructuredLlm
{
    /// <summary>
    /// Claude Opus 5. Thinking is on by default on this model, and the ladder's low/medium settings
    /// are strong enough that the cheap call sites stay there.
    /// </summary>
    public const string Model = "claude-opus-5";

    private readonly ILogger<StructuredLlm> _logger;
    private readonly IApiKeySource _keys;
    private readonly bool _tokenDebug;
    private readonly object _clientLock = new();
    private (string Key, AnthropicClient Client)? _client;

    public StructuredLlm(ILogger<StructuredLlm> logger, IApiKeySource keys)
    {
        _logger = logger;

        // The key is resolved per call, not captured here: an admin can rotate it in Settings and
        // the next call must use the new one without a restart. It is passed to the SDK
        // explicitly — the SDK's zero-arg constructor reads the process environment, which does
        // NOT contain what the app loaded from server/.env, and that once produced 503 "no API
        // key configured" on a machine where the key was sitting in the file the app had read.
        _keys = keys;

        // Off by default and kept deliberately: measuring before optimizing reordered the whole
        // priority list last time, because char-count estimates were badly wrong.
        _tokenDebug = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JDW_TOKEN_DEBUG"));
    }

    /// <summary>Whether a key is available — entered in the app, or configured.</summary>
    public bool HasApiKey => !string.IsNullOrEmpty(_keys.Current);

    /// <summary>One client per key, rebuilt only when the key changes.</summary>
    private AnthropicClient Client()
    {
        var key = _keys.Current
                  ?? throw new StructuredLlmException("No Anthropic API key is configured.");
        lock (_clientLock)
        {
            if (_client is not { } current || current.Key != key)
            {
                _client = (key, new AnthropicClient { ApiKey = key });
            }

            return _client.Value.Client;
        }
    }

    public async Task<T> StructuredAsync<T>(StructuredRequest request, CancellationToken ct = default)
    {
        var label = request.Label ?? "call";

        var parameters = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = request.MaxTokens,
            Thinking = new ThinkingConfigAdaptive(),
            OutputConfig = new OutputConfig
            {
                Effort = request.Effort switch
                {
                    LlmEffort.Low => Anthropic.Models.Messages.Effort.Low,
                    LlmEffort.High => Anthropic.Models.Messages.Effort.High,
                    _ => Anthropic.Models.Messages.Effort.Medium,
                },
                Format = new JsonOutputFormat
                {
                    Schema = StructuredSchema.AsDictionary(typeof(T)),
                },
            },
            System = request.System,
            Messages = [new() { Role = Role.User, Content = request.User }],
        };

        var response = await Client().Messages.Create(parameters, cancellationToken: ct);

        if (_tokenDebug)
        {
            _logger.LogInformation("[tok] {Label} in={In} out={Out} effort={Effort}",
                label, response.Usage.InputTokens, response.Usage.OutputTokens,
                request.Effort.ToString().ToLowerInvariant());
        }

        // The two realistic failures need OPPOSITE fixes, so they are named apart rather than
        // collapsed into one "the call failed".
        if (response.StopReason == "max_tokens")
        {
            throw new StructuredLlmException(
                $"{label}: output truncated at max_tokens={request.MaxTokens}. " +
                "Thinking and response share this budget — raise MaxTokens for this call.");
        }

        if (response.StopReason == "refusal")
        {
            // A refusal is a successful HTTP 200 with a safety verdict, not a transport error.
            var category = response.StopDetails?.Category;
            throw new StructuredLlmException(
                $"{label}: the model declined this request" +
                $"{(category is not null ? $" ({category})" : "")}. " +
                "Check the input for content that tripped a safety classifier.");
        }

        var text = ExtractText(response);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new StructuredLlmException(
                $"{label}: structured output failed (stop_reason={response.StopReason}) — no text returned.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, StructuredSchema.SerializerOptions)
                   ?? throw new StructuredLlmException($"{label}: structured output deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new StructuredLlmException(
                $"{label}: structured output did not match the declared schema — {ex.Message}");
        }
    }

    private static string ExtractText(Message response)
    {
        // Thinking blocks precede the text block; concatenate only the text.
        var parts = new List<string>();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? text) && text is not null)
            {
                parts.Add(text.Text);
            }
        }

        return string.Concat(parts);
    }
}

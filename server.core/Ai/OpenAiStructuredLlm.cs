using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Server.Core.Ai;

/// <summary>
/// Structured output through the OpenAI chat-completions API — Azure OpenAI, or any
/// OpenAI-compatible server (Ollama, LM Studio, vLLM, a gateway). Same seam, same prompts, same
/// schemas as the Anthropic path: the governing rule (the model decides, code assembles) holds
/// whichever model answers.
///
/// Prompts are sent byte-for-byte as written. They were tuned on Claude; quality on other models
/// is a matter for evaluation, not something this class can promise.
/// </summary>
public sealed class OpenAiStructuredLlm : IStructuredLlm
{
    /// <summary>Named client: a long timeout, because a large structured call can take minutes.</summary>
    public const string HttpClientName = "llm";

    private readonly IHttpClientFactory _http;
    private readonly IApiKeySource _keys;
    private readonly LlmOptions _options;
    private readonly ILogger<OpenAiStructuredLlm> _logger;
    private readonly bool _tokenDebug;

    /// <summary>
    /// Set when the model refuses reasoning_effort: every later call leaves it out. A refusal costs
    /// one retry, once — never a failed call.
    /// </summary>
    private volatile bool _effortRefused;

    private readonly AiCallLog? _calls;

    public OpenAiStructuredLlm(IHttpClientFactory http, IApiKeySource keys, LlmOptions options, ILogger<OpenAiStructuredLlm> logger, AiCallLog? calls = null)
    {
        _calls = calls;
        _http = http;
        _keys = keys;
        _options = options;
        _logger = logger;
        _tokenDebug = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JDW_TOKEN_DEBUG"));
    }

    /// <summary>
    /// Configured enough to call: a valid endpoint and a model, plus a key where one is required,
    /// and never a key bound for plain HTTP off this machine.
    /// </summary>
    public bool HasApiKey => _options.ConfigurationProblem(_keys.Current) == null;

    /// <summary>
    /// Whether to send each call's effort as reasoning_effort: always to OpenAI's own API, whose
    /// reasoning models take it, and to another server only when configured — a local model may
    /// reject it. Without it, every call thinks at the model's default, the quick ones included.
    /// </summary>
    private bool SendsEffort => !_effortRefused && (_options.SendReasoningEffort || _options.HostedOpenAi);

    private static long? Number(JsonNode? node)
    {
        try
        {
            return node?.GetValue<long>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>A 400 that names reasoning_effort: the model doesn't take the parameter.</summary>
    private static bool RefusesEffort(string body) => body.Contains("reasoning_effort", StringComparison.OrdinalIgnoreCase);

    private async Task<(HttpResponseMessage Response, string Text)> SendAsync(
        Type type, StructuredRequest request, string? key, bool withEffort, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Url())
        {
            Content = new StringContent(Body(type, request, withEffort).ToJsonString(), Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(key))
        {
            if (_options.Provider == LlmProvider.AzureOpenAi)
            {
                message.Headers.Add("api-key", key);
            }
            else
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            }
        }

        var response = await _http.CreateClient(HttpClientName).SendAsync(message, ct);
        return (response, await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<T> StructuredAsync<T>(StructuredRequest request, CancellationToken ct = default)
    {
        var label = request.Label ?? "call";
        var key = _keys.Current;
        var problem = _options.ConfigurationProblem(key);
        if (problem != null)
        {
            throw new StructuredLlmException($"{label}: the AI provider is not usable — {problem}.");
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var withEffort = SendsEffort;
        var (response, text) = await SendAsync(typeof(T), request, key, withEffort, ct);
        if (withEffort && response.StatusCode == HttpStatusCode.BadRequest && RefusesEffort(text))
        {
            // This model doesn't take reasoning_effort: carry on without it, now and from now on.
            _effortRefused = true;
            _logger.LogWarning("{Label}: the model refused reasoning_effort; calls now go without it", label);
            response.Dispose();
            withEffort = false;
            (response, text) = await SendAsync(typeof(T), request, key, withEffort, ct);
        }

        using var _ = response;

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new StructuredLlmException($"{label}: the AI provider rejected the credentials.");
        }

        if (!response.IsSuccessStatusCode)
        {
            _calls?.Record(new AiCall(DateTimeOffset.UtcNow, label, clock.ElapsedMilliseconds, null, null, null,
                withEffort ? request.Effort.ToString().ToLowerInvariant() : "default", _options.Provider.ToString(),
                $"failed: HTTP {(int)response.StatusCode}"));
            // The provider's own message helps diagnose a bad deployment name or schema; it never
            // contains the key, which travels only in a header.
            throw new StructuredLlmException(
                $"{label}: the AI provider returned {(int)response.StatusCode} — {ProviderError(text)}");
        }

        JsonNode root;
        string? finish, refusal, content;
        try
        {
            root = JsonNode.Parse(text) ?? throw new JsonException("empty body");
            var choice = root["choices"]?[0];
            finish = choice?["finish_reason"]?.GetValue<string>();
            refusal = choice?["message"]?["refusal"]?.GetValue<string>();
            content = choice?["message"]?["content"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // A 2xx that is not a chat completion: a proxy page, or a server that is not
            // OpenAI-compatible after all.
            throw new StructuredLlmException($"{label}: the AI provider's response was not a chat completion.");
        }

        // One line per call, metadata only: how long it took and what it cost, to find the slow ones.
        _logger.LogInformation(
            "[ai] {Label} {Ms}ms in={In} out={Out} reasoning={Reasoning} effort={Effort} provider={Provider}",
            label, clock.ElapsedMilliseconds, root["usage"]?["prompt_tokens"], root["usage"]?["completion_tokens"],
            root["usage"]?["completion_tokens_details"]?["reasoning_tokens"],
            withEffort ? request.Effort.ToString().ToLowerInvariant() : "default", _options.Provider);
        _calls?.Record(new AiCall(DateTimeOffset.UtcNow, label, clock.ElapsedMilliseconds,
            Number(root["usage"]?["prompt_tokens"]), Number(root["usage"]?["completion_tokens"]),
            Number(root["usage"]?["completion_tokens_details"]?["reasoning_tokens"]),
            withEffort ? request.Effort.ToString().ToLowerInvariant() : _effortRefused ? "refused — default" : "default",
            _options.Provider.ToString(), "ok"));
        if (_tokenDebug)
        {
            _logger.LogInformation("[tok] {Label} in={In} out={Out} provider={Provider}",
                label, root["usage"]?["prompt_tokens"], root["usage"]?["completion_tokens"], _options.Provider);
        }

        if (finish == "length")
        {
            throw new StructuredLlmException(
                $"{label}: output truncated at max_tokens={request.MaxTokens}. Raise MaxTokens for this call.");
        }

        if (!string.IsNullOrEmpty(refusal) || finish == "content_filter")
        {
            throw new StructuredLlmException(
                $"{label}: the model declined this request. Check the input for content that tripped a safety filter.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new StructuredLlmException($"{label}: structured output failed (finish_reason={finish}) — no text returned.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(StripFences(content), StructuredSchema.SerializerOptions)
                   ?? throw new StructuredLlmException($"{label}: structured output deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new StructuredLlmException(
                $"{label}: structured output did not match the declared schema — {ex.Message}");
        }
    }

    private string Url() => _options.Provider == LlmProvider.AzureOpenAi
        ? $"{_options.Endpoint}/openai/deployments/{Uri.EscapeDataString(_options.EffectiveModel)}/chat/completions?api-version={Uri.EscapeDataString(_options.ApiVersion)}"
        : $"{_options.Endpoint}/chat/completions";

    internal JsonObject Body(Type type, StructuredRequest request, bool withEffort)
    {
        var body = new JsonObject
        {
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = request.System },
                new JsonObject { ["role"] = "user", ["content"] = request.User },
            },
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = SchemaName(request.Label),
                    ["strict"] = true,
                    ["schema"] = StrictSchema(type),
                },
            },
        };

        if (_options.Provider == LlmProvider.AzureOpenAi || _options.HostedOpenAi)
        {
            // OpenAI reasoning models reject max_tokens, including on regional endpoints.
            body["max_completion_tokens"] = request.MaxTokens;
        }
        else
        {
            // Preserve compatibility with local servers and other gateways.
            body["max_tokens"] = request.MaxTokens;
        }

        if (_options.Provider != LlmProvider.AzureOpenAi)
        {
            body["model"] = _options.EffectiveModel;
        }

        if (withEffort)
        {
            body["reasoning_effort"] = request.Effort.ToString().ToLowerInvariant();
        }

        return body;
    }

    /// <summary>
    /// The shared schema, made strict-mode valid: OpenAI's strict structured outputs require every
    /// property to be listed in <c>required</c> (optional ones say so by allowing null). Objects
    /// are already closed by <see cref="StructuredSchema"/>.
    /// </summary>
    internal static JsonNode StrictSchema(Type type)
    {
        var schema = StructuredSchema.For(type);
        RequireAll(schema);
        return schema;

        static void RequireAll(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    if (obj["properties"] is JsonObject props)
                    {
                        obj["required"] = new JsonArray([.. props.Select(p => (JsonNode?)JsonValue.Create(p.Key))]);
                    }

                    foreach (var (_, child) in obj.ToList())
                    {
                        RequireAll(child);
                    }

                    break;
                case JsonArray arr:
                    foreach (var child in arr)
                    {
                        RequireAll(child);
                    }

                    break;
            }
        }
    }

    /// <summary>Schema names allow only letters, digits, underscores and hyphens.</summary>
    private static string SchemaName(string? label)
    {
        var name = new string((label ?? "result").Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
        return name.Length > 0 ? name : "result";
    }

    /// <summary>Some local models wrap JSON in a Markdown fence even under a schema.</summary>
    private static string StripFences(string content)
    {
        var t = content.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal))
        {
            return t;
        }

        var firstNewline = t.IndexOf('\n');
        var lastFence = t.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline > 0 && lastFence > firstNewline ? t[(firstNewline + 1)..lastFence].Trim() : t;
    }

    private static string ProviderError(string text)
    {
        try
        {
            var message = JsonNode.Parse(text)?["error"]?["message"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message.Length > 300 ? message[..300] : message;
            }
        }
        catch (JsonException)
        {
        }

        return text.Length > 200 ? text[..200] : text;
    }
}

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

    public OpenAiStructuredLlm(IHttpClientFactory http, IApiKeySource keys, LlmOptions options, ILogger<OpenAiStructuredLlm> logger)
    {
        _http = http;
        _keys = keys;
        _options = options;
        _logger = logger;
        _tokenDebug = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JDW_TOKEN_DEBUG"));
    }

    /// <summary>Configured enough to call: an endpoint and model, plus a key where one is required.</summary>
    public bool HasApiKey =>
        _options.Endpoint.Length > 0
        && _options.EffectiveModel.Length > 0
        && (!_options.KeyRequired || !string.IsNullOrEmpty(_keys.Current));

    public async Task<T> StructuredAsync<T>(StructuredRequest request, CancellationToken ct = default)
    {
        var label = request.Label ?? "call";
        using var message = new HttpRequestMessage(HttpMethod.Post, Url())
        {
            Content = new StringContent(Body(typeof(T), request).ToJsonString(), Encoding.UTF8, "application/json"),
        };

        var key = _keys.Current;
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

        using var response = await _http.CreateClient(HttpClientName).SendAsync(message, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new StructuredLlmException($"{label}: the AI provider rejected the credentials.");
        }

        if (!response.IsSuccessStatusCode)
        {
            // The provider's own message helps diagnose a bad deployment name or schema; it never
            // contains the key, which travels only in a header.
            throw new StructuredLlmException(
                $"{label}: the AI provider returned {(int)response.StatusCode} — {ProviderError(text)}");
        }

        var root = JsonNode.Parse(text)!;
        var choice = root["choices"]?[0];
        var finish = choice?["finish_reason"]?.GetValue<string>();
        var refusal = choice?["message"]?["refusal"]?.GetValue<string>();
        var content = choice?["message"]?["content"]?.GetValue<string>();

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

    internal JsonObject Body(Type type, StructuredRequest request)
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

        if (_options.Provider == LlmProvider.AzureOpenAi)
        {
            // Azure names the output budget differently, and reasoning models require it.
            body["max_completion_tokens"] = request.MaxTokens;
        }
        else
        {
            body["model"] = _options.EffectiveModel;
            body["max_tokens"] = request.MaxTokens;
        }

        if (_options.SendReasoningEffort)
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

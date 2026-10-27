using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core.Ai;

namespace Server.Tests.Ai;

/// <summary>
/// The OpenAI-compatible provider (Azure OpenAI and local servers): the request each sends, strict
/// schemas, and the failures named apart — all against a fake HTTP handler, no network.
/// </summary>
public class OpenAiStructuredLlmTests
{
    public sealed class Answer
    {
        [Description("The category.")]
        public string Category { get; set; } = "";

        public int Confidence { get; set; }
        public List<Part> Parts { get; set; } = [];
    }

    public sealed class Part
    {
        public string Name { get; set; } = "";
        public string? Note { get; set; }
    }

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? SentBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            SentBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedKey(string? key) : IApiKeySource
    {
        public string? Current => key;
        public void Invalidate() { }
    }

    private static string Completion(string content, string finish = "stop") =>
        new JsonObject
        {
            ["choices"] = new JsonArray { new JsonObject { ["finish_reason"] = finish, ["message"] = new JsonObject { ["content"] = content } } },
            ["usage"] = new JsonObject { ["prompt_tokens"] = 10, ["completion_tokens"] = 5 },
        }.ToJsonString();

    private static (OpenAiStructuredLlm Llm, FakeHandler Handler) Make(LlmOptions options, string? key, HttpStatusCode status, string body)
    {
        var handler = new FakeHandler(status, body);
        return (new OpenAiStructuredLlm(new Factory(handler), new FixedKey(key), options, NullLogger<OpenAiStructuredLlm>.Instance), handler);
    }

    private static readonly StructuredRequest Request = new() { System = "sys", User = "user", Label = "test.call", MaxTokens = 500 };

    private static readonly LlmOptions Azure = new()
    {
        Provider = LlmProvider.AzureOpenAi, Endpoint = "https://campus.openai.azure.com", Model = "gpt-4o-jd", ApiVersion = "2024-10-21",
    };

    private static readonly LlmOptions Local = new()
    {
        Provider = LlmProvider.OpenAiCompatible, Endpoint = "http://localhost:11434/v1", Model = "qwen3.8:latest",
    };

    [Fact]
    public async Task Azure_calls_the_deployment_with_the_api_key_header()
    {
        var (llm, h) = Make(Azure, "azure-key-0000000000000000", HttpStatusCode.OK, Completion("""{"category":"Plants","confidence":80,"parts":[]}"""));

        var answer = await llm.StructuredAsync<Answer>(Request);

        answer.Category.Should().Be("Plants");
        h.Request!.RequestUri!.ToString().Should().Be(
            "https://campus.openai.azure.com/openai/deployments/gpt-4o-jd/chat/completions?api-version=2024-10-21");
        h.Request.Headers.GetValues("api-key").Single().Should().Be("azure-key-0000000000000000");
        h.Request.Headers.Authorization.Should().BeNull();
        var body = JsonNode.Parse(h.SentBody!)!;
        body["max_completion_tokens"]!.GetValue<int>().Should().Be(500);
        body["model"].Should().BeNull("Azure routes by deployment, not model");
    }

    [Fact]
    public async Task A_local_server_gets_the_model_and_no_key_when_none_is_configured()
    {
        var (llm, h) = Make(Local, null, HttpStatusCode.OK, Completion("""{"category":"Plants","confidence":80,"parts":[]}"""));

        llm.HasApiKey.Should().BeTrue("a local server needs an endpoint and model, not a key");
        await llm.StructuredAsync<Answer>(Request);

        h.Request!.RequestUri!.ToString().Should().Be("http://localhost:11434/v1/chat/completions");
        h.Request.Headers.Authorization.Should().BeNull();
        var body = JsonNode.Parse(h.SentBody!)!;
        body["model"]!.GetValue<string>().Should().Be("qwen3.8:latest");
        body["max_tokens"]!.GetValue<int>().Should().Be(500);
        body["messages"]![0]!["content"]!.GetValue<string>().Should().Be("sys", "prompts are sent exactly as written");
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", true)]
    [InlineData("https://us.api.openai.com/v1", true)]
    [InlineData("https://eu.api.openai.com/v1", true)]
    [InlineData("https://api.openai.com.example.org/v1", false)]
    [InlineData("https://gateway.example.org/v1", false)]
    public async Task Hosted_OpenAI_uses_the_reasoning_model_token_budget(string endpoint, bool hostedOpenAi)
    {
        var options = new LlmOptions
        {
            Provider = LlmProvider.OpenAiCompatible, Endpoint = endpoint, Model = "gpt-6-luna",
        };
        var (llm, h) = Make(options, "test-key-0000000000000000", HttpStatusCode.OK,
            Completion("""{"category":"Plants","confidence":80,"parts":[]}"""));

        await llm.StructuredAsync<Answer>(Request);

        var body = JsonNode.Parse(h.SentBody!)!;
        body[hostedOpenAi ? "max_completion_tokens" : "max_tokens"]!.GetValue<int>().Should().Be(500);
        body[hostedOpenAi ? "max_tokens" : "max_completion_tokens"].Should().BeNull();
        body["model"]!.GetValue<string>().Should().Be("gpt-6-luna");
        h.Request!.RequestUri!.ToString().Should().Be($"{endpoint}/chat/completions");
        h.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
    }

    [Fact]
    public void The_schema_is_strict_mode_valid_every_property_required_and_closed()
    {
        var schema = OpenAiStructuredLlm.StrictSchema(typeof(Answer));

        Required(schema).Should().BeEquivalentTo("category", "confidence", "parts");
        var part = schema["properties"]!["parts"]!["items"]!;
        Required(part).Should().BeEquivalentTo(["name", "note"], "strict mode lists optional fields too; they allow null instead");
        part["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        schema["properties"]!["category"]!["description"]!.GetValue<string>().Should().Be("The category.");

        static IEnumerable<string> Required(JsonNode n) => n["required"]!.AsArray().Select(x => x!.GetValue<string>());
    }

    [Fact]
    public async Task Reasoning_effort_is_sent_only_when_the_deployment_takes_it()
    {
        var (plain, h1) = Make(Azure, "k-000000000000000000000", HttpStatusCode.OK, Completion("""{"category":"x","confidence":1,"parts":[]}"""));
        await plain.StructuredAsync<Answer>(Request);
        JsonNode.Parse(h1.SentBody!)!["reasoning_effort"].Should().BeNull();

        var reasoning = new LlmOptions { Provider = Azure.Provider, Endpoint = Azure.Endpoint, Model = Azure.Model, SendReasoningEffort = true };
        var (r, h2) = Make(reasoning, "k-000000000000000000000", HttpStatusCode.OK, Completion("""{"category":"x","confidence":1,"parts":[]}"""));
        await r.StructuredAsync<Answer>(new StructuredRequest { System = "s", User = "u", Effort = LlmEffort.Low });
        JsonNode.Parse(h2.SentBody!)!["reasoning_effort"]!.GetValue<string>().Should().Be("low");
    }

    /// <summary>Answers in turn, recording what was sent.</summary>
    private sealed class SequenceHandler(params (HttpStatusCode Status, string Body)[] replies) : HttpMessageHandler
    {
        private int _next;
        public List<string> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sent.Add(await request.Content!.ReadAsStringAsync(ct));
            var (status, body) = replies[Math.Min(_next++, replies.Length - 1)];
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (status == HttpStatusCode.TooManyRequests)
            {
                // As OpenAI says it: wait this long. Kept tiny so the test is quick.
                response.Headers.Add("retry-after-ms", "5");
            }

            return response;
        }
    }

    private static readonly LlmOptions HostedOpenAi = new()
    {
        Provider = LlmProvider.OpenAiCompatible, Endpoint = "https://us.api.openai.com/v1", Model = "gpt-6-luna",
    };

    [Fact]
    public async Task OpenAI_s_own_API_gets_each_call_s_effort_without_being_configured_and_a_local_server_does_not()
    {
        var ok = Completion("""{"category":"x","confidence":1,"parts":[]}""");
        var (hosted, h1) = Make(HostedOpenAi, "k-000000000000000000000", HttpStatusCode.OK, ok);
        await hosted.StructuredAsync<Answer>(new StructuredRequest { System = "s", User = "u", Effort = LlmEffort.Low });
        JsonNode.Parse(h1.SentBody!)!["reasoning_effort"]!.GetValue<string>().Should().Be("low");

        var local = new LlmOptions { Provider = LlmProvider.OpenAiCompatible, Endpoint = "http://localhost:11434/v1", Model = "qwen3.8:latest" };
        var (l, h2) = Make(local, null, HttpStatusCode.OK, ok);
        await l.StructuredAsync<Answer>(Request);
        JsonNode.Parse(h2.SentBody!)!["reasoning_effort"].Should().BeNull();
    }

    [Fact]
    public async Task A_model_that_refuses_reasoning_effort_is_retried_without_it_once_and_never_sent_it_again()
    {
        var ok = Completion("""{"category":"x","confidence":1,"parts":[]}""");
        var handler = new SequenceHandler(
            (HttpStatusCode.BadRequest, """{"error":{"message":"Unsupported parameter: 'reasoning_effort' is not supported with this model."}}"""),
            (HttpStatusCode.OK, ok));
        var llm = new OpenAiStructuredLlm(new Factory(handler), new FixedKey("k-000000000000000000000"), HostedOpenAi, NullLogger<OpenAiStructuredLlm>.Instance);

        (await llm.StructuredAsync<Answer>(Request)).Category.Should().Be("x");
        await llm.StructuredAsync<Answer>(Request);

        handler.Sent.Select(b => JsonNode.Parse(b)!["reasoning_effort"] != null).Should().Equal(true, false, false);
    }

    [Fact]
    public async Task Each_call_is_recorded_for_the_admin_with_its_time_tokens_and_effort_and_no_content()
    {
        var calls = new AiCallLog();
        var body = new JsonObject
        {
            ["choices"] = new JsonArray { new JsonObject { ["finish_reason"] = "stop", ["message"] = new JsonObject { ["content"] = """{"category":"x","confidence":1,"parts":[]}""" } } },
            ["usage"] = new JsonObject { ["prompt_tokens"] = 1200, ["completion_tokens"] = 900, ["completion_tokens_details"] = new JsonObject { ["reasoning_tokens"] = 700 } },
        }.ToJsonString();
        var handler = new SequenceHandler((HttpStatusCode.OK, body), (HttpStatusCode.InternalServerError, "{}"));
        var llm = new OpenAiStructuredLlm(new Factory(handler), new FixedKey("k-000000000000000000000"), HostedOpenAi, NullLogger<OpenAiStructuredLlm>.Instance, calls);

        await llm.StructuredAsync<Answer>(new StructuredRequest { System = "secret prompt", User = "secret text", Label = "classify.rank", Effort = LlmEffort.Medium });
        var fail = () => llm.StructuredAsync<Answer>(new StructuredRequest { System = "s", User = "u", Label = "classify.distill", Effort = LlmEffort.Low });
        await fail.Should().ThrowAsync<StructuredLlmException>();

        var recent = calls.Recent();
        recent.Select(c => (c.Label, c.Outcome)).Should().Equal(("classify.distill", "failed: HTTP 500"), ("classify.rank", "ok"));
        recent[1].Should().BeEquivalentTo(new { InputTokens = 1200L, OutputTokens = 900L, ReasoningTokens = 700L, Effort = "medium", Provider = "OpenAiCompatible" });
        System.Text.Json.JsonSerializer.Serialize(recent).Should().NotContain("secret");
    }

    [Fact]
    public void The_call_log_keeps_only_the_newest()
    {
        var calls = new AiCallLog();
        for (var i = 0; i < AiCallLog.Capacity + 5; i++)
        {
            calls.Record(new AiCall(DateTimeOffset.UtcNow, $"call {i}", i, null, null, null, "low", "Anthropic", "ok"));
        }

        var recent = calls.Recent();
        recent.Should().HaveCount(AiCallLog.Capacity);
        recent[0].Label.Should().Be($"call {AiCallLog.Capacity + 4}");
    }

    private static readonly string BusyBody = """{"error":{"message":"Rate limit reached for requests","type":"requests","code":"rate_limit_exceeded"}}""";

    [Fact]
    public async Task A_busy_provider_is_waited_on_and_retried()
    {
        var ok = Completion("""{"category":"x","confidence":1,"parts":[]}""");
        var handler = new SequenceHandler((HttpStatusCode.TooManyRequests, BusyBody), (HttpStatusCode.TooManyRequests, BusyBody), (HttpStatusCode.OK, ok));
        var llm = new OpenAiStructuredLlm(new Factory(handler), new FixedKey("k-000000000000000000000"), HostedOpenAi, NullLogger<OpenAiStructuredLlm>.Instance);

        var answer = await llm.StructuredAsync<Answer>(new StructuredRequest { System = "s", User = "u" });

        answer.Category.Should().Be("x");
        handler.Sent.Should().HaveCount(3);
    }

    [Fact]
    public async Task A_provider_still_busy_after_the_retries_says_so_and_a_spent_quota_is_not_retried()
    {
        var busy = new SequenceHandler((HttpStatusCode.TooManyRequests, BusyBody));
        var llm = new OpenAiStructuredLlm(new Factory(busy), new FixedKey("k-000000000000000000000"), HostedOpenAi, NullLogger<OpenAiStructuredLlm>.Instance);
        var call = () => llm.StructuredAsync<Answer>(new StructuredRequest { System = "s", User = "u" });
        await call.Should().ThrowAsync<StructuredLlmBusyException>();
        busy.Sent.Should().HaveCount(1 + OpenAiStructuredLlm.BusyRetries);

        var spent = new SequenceHandler((HttpStatusCode.TooManyRequests, """{"error":{"message":"You exceeded your current quota","code":"insufficient_quota"}}"""));
        var quota = new OpenAiStructuredLlm(new Factory(spent), new FixedKey("k-000000000000000000000"), HostedOpenAi, NullLogger<OpenAiStructuredLlm>.Instance);
        var spentCall = () => quota.StructuredAsync<Answer>(new StructuredRequest { System = "s", User = "u" });
        (await spentCall.Should().ThrowAsync<StructuredLlmException>()).Which.Should().NotBeOfType<StructuredLlmBusyException>();
        spent.Sent.Should().HaveCount(1);
    }

    [Fact]
    public void The_wait_is_what_the_provider_asks_capped_by_the_budget()
    {
        using var ms = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        ms.Headers.Add("retry-after-ms", "1500");
        OpenAiStructuredLlm.RetryAfter(ms).Should().Be(TimeSpan.FromMilliseconds(1500));

        using var seconds = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        seconds.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
        OpenAiStructuredLlm.RetryAfter(seconds).Should().Be(TimeSpan.FromSeconds(3));

        using var huge = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        huge.Headers.Add("retry-after-ms", "600000");
        OpenAiStructuredLlm.RetryAfter(huge).Should().Be(OpenAiStructuredLlm.BusyBudget);
    }

    [Fact]
    public async Task Another_bad_request_is_not_retried()
    {
        var handler = new SequenceHandler((HttpStatusCode.BadRequest, """{"error":{"message":"Invalid schema."}}"""));
        var llm = new OpenAiStructuredLlm(new Factory(handler), new FixedKey("k-000000000000000000000"), HostedOpenAi, NullLogger<OpenAiStructuredLlm>.Instance);

        var act = () => llm.StructuredAsync<Answer>(Request);

        (await act.Should().ThrowAsync<StructuredLlmException>()).Which.Message.Should().Contain("Invalid schema");
        handler.Sent.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{}", "rejected the credentials")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"The API deployment for this resource does not exist."}}""", "deployment for this resource does not exist")]
    public async Task Provider_errors_are_named_and_never_echo_the_key(HttpStatusCode status, string body, string expected)
    {
        var (llm, _) = Make(Azure, "secret-key-123456789012345", status, body);

        var call = () => llm.StructuredAsync<Answer>(Request);

        var ex = (await call.Should().ThrowAsync<StructuredLlmException>()).Which;
        ex.Message.Should().Contain(expected).And.NotContain("secret-key");
    }

    [Fact]
    public async Task Truncation_and_refusal_are_told_apart()
    {
        var (cut, _) = Make(Local, null, HttpStatusCode.OK, Completion("{\"category\":", finish: "length"));
        await ((Func<Task>)(() => cut.StructuredAsync<Answer>(Request))).Should().ThrowAsync<StructuredLlmException>().WithMessage("*truncated*");

        var (filtered, _) = Make(Local, null, HttpStatusCode.OK, Completion("", finish: "content_filter"));
        await ((Func<Task>)(() => filtered.StructuredAsync<Answer>(Request))).Should().ThrowAsync<StructuredLlmException>().WithMessage("*declined*");
    }

    [Fact]
    public async Task A_markdown_fence_around_the_json_is_tolerated()
    {
        var (llm, _) = Make(Local, null, HttpStatusCode.OK, Completion("```json\n{\"category\":\"Fenced\",\"confidence\":1,\"parts\":[]}\n```"));

        (await llm.StructuredAsync<Answer>(Request)).Category.Should().Be("Fenced");
    }

    [Theory]
    [InlineData(null, LlmProvider.Anthropic)]
    [InlineData("anthropic", LlmProvider.Anthropic)]
    [InlineData("azure-openai", LlmProvider.AzureOpenAi)]
    [InlineData("Azure_OpenAI", LlmProvider.AzureOpenAi)]
    [InlineData("openai-compatible", LlmProvider.OpenAiCompatible)]
    [InlineData("ollama", LlmProvider.OpenAiCompatible)]
    public void The_provider_is_read_from_configuration(string? value, LlmProvider expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:Provider"] = value }).Build();

        LlmOptions.From(config).Provider.Should().Be(expected);
    }

    [Fact]
    public void An_unknown_provider_fails_at_startup_rather_than_at_the_first_call()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:Provider"] = "gemini" }).Build();

        ((Action)(() => LlmOptions.From(config))).Should().Throw<InvalidOperationException>().WithMessage("*anthropic, azure-openai or openai-compatible*");
    }

    [Theory]
    [InlineData("http://models.example.edu/v1", "a-real-key-0000000000000", false)]
    [InlineData("http://localhost:11434/v1", "a-real-key-0000000000000", true)]
    [InlineData("http://models.example.edu/v1", null, true)]
    [InlineData("not a url", null, false)]
    public async Task A_key_is_never_sent_over_plain_http_off_this_machine(string endpoint, string? key, bool usable)
    {
        var options = new LlmOptions { Provider = LlmProvider.OpenAiCompatible, Endpoint = endpoint, Model = "m" };
        var (llm, h) = Make(options, key, HttpStatusCode.OK, Completion("""{"category":"x","confidence":1,"parts":[]}"""));

        llm.HasApiKey.Should().Be(usable);
        if (!usable)
        {
            await ((Func<Task>)(() => llm.StructuredAsync<Answer>(Request))).Should().ThrowAsync<StructuredLlmException>();
            h.Request.Should().BeNull("nothing — least of all the key — leaves the process");
        }
    }

    [Theory]
    [InlineData("<html>gateway error</html>")]
    [InlineData("")]
    [InlineData("""{"choices":[{"finish_reason":1,"message":{"content":2}}]}""")]
    public async Task A_success_status_that_is_not_a_chat_completion_is_a_structured_failure(string body)
    {
        var (llm, _) = Make(Local, null, HttpStatusCode.OK, body);

        await ((Func<Task>)(() => llm.StructuredAsync<Answer>(Request))).Should()
            .ThrowAsync<StructuredLlmException>().WithMessage("*not a chat completion*");
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    [Fact]
    public async Task A_provider_that_never_answers_a_key_check_gets_a_message_not_a_hung_request()
    {
        var verifier = new OpenAiKeyVerifier(new Factory(new HangingHandler()), Azure, TimeSpan.FromMilliseconds(50));

        (await verifier.VerifyAsync("azure-key-0000000000000000")).Should().Contain("did not answer in time");
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", true)]
    [InlineData("https://us.api.openai.com/v1", true)]
    [InlineData("http://localhost:11434/v1", false)]
    [InlineData("https://models.caes.example/v1", false)]
    public void OpenAI_s_own_API_needs_a_key_even_reached_as_OpenAI_compatible(string endpoint, bool required)
    {
        var options = new LlmOptions { Provider = LlmProvider.OpenAiCompatible, Endpoint = endpoint, Model = "gpt-6-luna" };

        options.KeyRequired.Should().Be(required);
        if (required)
        {
            // A lost key reads as missing — not "this server does not need one" while every call is refused.
            options.ConfigurationProblem(null).Should().Be("no key (OPENAI_API_KEY)");
        }
        else
        {
            options.ConfigurationProblem(null).Should().BeNull();
        }
    }
}

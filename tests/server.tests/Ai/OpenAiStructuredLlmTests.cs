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
}

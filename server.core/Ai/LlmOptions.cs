using Microsoft.Extensions.Configuration;

namespace Server.Core.Ai;

/// <summary>Which model provider the app talks to. Chosen per environment by configuration.</summary>
public enum LlmProvider
{
    /// <summary>Anthropic's API (Claude). The default, and what the prompts were written against.</summary>
    Anthropic,

    /// <summary>Azure OpenAI: a campus Azure resource with GPT model deployments.</summary>
    AzureOpenAi,

    /// <summary>
    /// Any OpenAI-compatible endpoint — a local model server (Ollama, LM Studio, vLLM) or a
    /// gateway. Keeps JD text on infrastructure the college controls when run locally.
    /// </summary>
    OpenAiCompatible,
}

/// <summary>
/// The <c>Llm</c> configuration section. Set per environment (deployment settings), never chosen in
/// the app: which provider is allowed to receive JD text is an institutional decision.
/// </summary>
public sealed class LlmOptions
{
    public LlmProvider Provider { get; init; } = LlmProvider.Anthropic;

    /// <summary>
    /// Model id (Anthropic, OpenAI-compatible) or deployment name (Azure OpenAI). Defaults to the
    /// Claude model the prompts were tuned on when the provider is Anthropic.
    /// </summary>
    public string Model { get; init; } = "";

    /// <summary>
    /// Azure OpenAI resource endpoint (https://NAME.openai.azure.com) or an OpenAI-compatible base
    /// URL ending in /v1 (http://localhost:11434/v1 for Ollama).
    /// </summary>
    public string Endpoint { get; init; } = "";

    /// <summary>Azure OpenAI REST API version.</summary>
    public string ApiVersion { get; init; } = "2024-10-21";

    /// <summary>
    /// Send the call's effort level as <c>reasoning_effort</c>. Only reasoning models accept it
    /// (other models reject the request), so it is off unless the deployment is known to take it.
    /// Anthropic ignores this — its effort setting is always sent.
    /// </summary>
    public bool SendReasoningEffort { get; init; }

    /// <summary>
    /// Whether an admin may enter or rotate the provider key in the app. When false, keys come only
    /// from configuration — in Azure, App Service settings backed by Key Vault references.
    /// </summary>
    public bool AllowKeyEntryInApp { get; init; } = true;

    /// <summary>The configuration value that holds this provider's key.</summary>
    public string KeySetting => Provider switch
    {
        LlmProvider.AzureOpenAi => "AZURE_OPENAI_API_KEY",
        LlmProvider.OpenAiCompatible => "OPENAI_API_KEY",
        _ => "ANTHROPIC_API_KEY",
    };

    /// <summary>A local OpenAI-compatible server usually needs no key; every hosted provider does.</summary>
    public bool KeyRequired => Provider != LlmProvider.OpenAiCompatible;

    public string EffectiveModel => Model.Length > 0
        ? Model
        : Provider == LlmProvider.Anthropic ? StructuredLlm.Model : "";

    public static LlmOptions From(IConfiguration config)
    {
        var section = config.GetSection("Llm");
        var provider = (section["Provider"] ?? "").Trim().ToLowerInvariant().Replace("-", "").Replace("_", "") switch
        {
            "" or "anthropic" or "claude" => LlmProvider.Anthropic,
            "azureopenai" or "azure" => LlmProvider.AzureOpenAi,
            "openaicompatible" or "openai" or "local" or "ollama" => LlmProvider.OpenAiCompatible,
            var other => throw new InvalidOperationException(
                $"Unknown Llm:Provider '{other}'. Use anthropic, azure-openai or openai-compatible."),
        };

        return new LlmOptions
        {
            Provider = provider,
            Model = section["Model"]?.Trim() ?? "",
            Endpoint = section["Endpoint"]?.Trim().TrimEnd('/') ?? "",
            ApiVersion = section["ApiVersion"]?.Trim() is { Length: > 0 } v ? v : "2024-10-21",
            SendReasoningEffort = section.GetValue<bool>("SendReasoningEffort"),
            AllowKeyEntryInApp = section.GetValue("AllowKeyEntryInApp", true),
        };
    }
}

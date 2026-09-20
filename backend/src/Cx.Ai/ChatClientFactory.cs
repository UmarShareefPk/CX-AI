using System.ClientModel;
using Anthropic;
using Cx.Core.Rag;
using Google.GenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;

namespace Cx.Ai;

public interface IChatClientFactory
{
    /// <summary>A ready-to-use client: provider adapter wrapped in the agent loop and telemetry.</summary>
    IChatClient Create(string provider, string model);
}

/// <summary>
/// The ONE place that knows about concrete providers. Everything else depends only on IChatClient /
/// IEmbeddingGenerator. Pipeline, outermost first: function invocation (the agent loop) -> logging -> OpenTelemetry -> provider.
/// </summary>
public sealed class ChatClientFactory(
    IOptions<AiOptions> aiOptions,
    IOptions<EmbeddingOptions> embeddingOptions,
    ILoggerFactory loggerFactory,
    IServiceProvider services) : IChatClientFactory
{
    public const string TelemetrySource = "Cx.Ai";

    private AiOptions Ai => aiOptions.Value;

    public IChatClient Create(string provider, string model)
    {
        var raw = CreateRaw(provider, model);
        return new ChatClientBuilder(raw)
            .Use((inner, sp) => new FunctionInvokingChatClient(inner, loggerFactory, sp)
            {
                MaximumIterationsPerRequest = Ai.MaxToolIterations,
                AllowConcurrentInvocation = false,
                IncludeDetailedErrors = false, // tool exception details are not echoed back to the model
            })
            .UseLogging(loggerFactory)
            .UseOpenTelemetry(loggerFactory, TelemetrySource)
            .Build(services);
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator()
    {
        var e = embeddingOptions.Value;
        IEmbeddingGenerator<string, Embedding<float>> raw = e.Provider.ToLowerInvariant() switch
        {
            Providers.Ollama => new OllamaApiClient(OllamaHttp(), e.Model),
            Providers.OpenAI => OpenAiClient().GetEmbeddingClient(e.Model).AsIEmbeddingGenerator(),
            Providers.Google => GoogleClient().AsIEmbeddingGenerator(e.Model),
            _ => throw new InvalidOperationException($"Embedding provider '{e.Provider}' is not supported. Use ollama, openai or google."),
        };
        return new EmbeddingGeneratorBuilder<string, Embedding<float>>(raw)
            .UseOpenTelemetry(loggerFactory, TelemetrySource)
            .Build(services);
    }

    private IChatClient CreateRaw(string provider, string model) => provider.ToLowerInvariant() switch
    {
        Providers.Ollama => new OllamaApiClient(OllamaHttp(), model),
        Providers.OpenAI => OpenAiClient().GetChatClient(model).AsIChatClient(),
        Providers.Anthropic => new AnthropicClient { ApiKey = RequireKey(Ai.Anthropic, "Anthropic") }
            .AsIChatClient(model, Ai.MaxOutputTokens),
        Providers.Google => GoogleClient().AsIChatClient(model),
        _ => throw new ArgumentException($"Unknown provider '{provider}'."),
    };

    private HttpClient OllamaHttp() => new()
    {
        BaseAddress = new Uri(Ai.Ollama.Endpoint),
        Timeout = TimeSpan.FromSeconds(Ai.RequestTimeoutSeconds),
    };

    private OpenAIClient OpenAiClient()
    {
        var options = new OpenAIClientOptions { NetworkTimeout = TimeSpan.FromSeconds(Ai.RequestTimeoutSeconds) };
        if (!string.IsNullOrWhiteSpace(Ai.OpenAI.Endpoint)) options.Endpoint = new Uri(Ai.OpenAI.Endpoint);
        return new OpenAIClient(new ApiKeyCredential(RequireKey(Ai.OpenAI, "OpenAI")), options);
    }

    private Client GoogleClient() => new(apiKey: RequireKey(Ai.Google, "Google"));

    private static string RequireKey(HostedProviderOptions o, string name) =>
        o.Enabled ? o.ApiKey! : throw new InvalidOperationException($"{name} is not configured. Set Ai__{name}__ApiKey as an environment variable or user-secret.");
}

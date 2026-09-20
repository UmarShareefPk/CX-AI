using Cx.Ai.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cx.Ai;

public static class AiServiceCollectionExtensions
{
    /// <summary>Provider factory and the embedding generator. Needed by every host (API, seeder, MCP server).</summary>
    public static IServiceCollection AddCxAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<ChatClientFactory>();
        services.AddSingleton<IChatClientFactory>(sp => sp.GetRequiredService<ChatClientFactory>());
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp => sp.GetRequiredService<ChatClientFactory>().CreateEmbeddingGenerator());
        return services;
    }

    /// <summary>The agent itself: model catalog, MCP tool processes and the agent loop. API host only.</summary>
    public static IServiceCollection AddCxAgent(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<McpOptions>().Bind(configuration.GetSection(McpOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<ModelCatalog>();
        services.AddSingleton<McpToolProvider>();
        services.AddSingleton<IAgentToolSource>(sp => sp.GetRequiredService<McpToolProvider>());
        services.AddSingleton<AgentService>();
        return services;
    }
}

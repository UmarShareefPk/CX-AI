using Cx.Core.Data;
using Cx.Core.Rag;
using Cx.Core.Scoring;
using Cx.Core.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Cx.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>Data access, scoring and RAG services. The embedding generator itself is registered by Cx.Ai.</summary>
    public static IServiceCollection AddCxCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MongoOptions>().Bind(configuration.GetSection(MongoOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<EmbeddingOptions>().Bind(configuration.GetSection(EmbeddingOptions.Section)).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMongoClient>(sp => new MongoClient(sp.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString));
        services.AddSingleton<CxDatabase>();
        services.AddSingleton<ScoreService>();
        services.AddSingleton<IConversationStore, ConversationStore>();
        services.AddSingleton<PolicyIngestor>();
        services.AddSingleton<PolicySearch>();
        services.AddSingleton<DemoDataSeeder>();
        return services;
    }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;

namespace Cx.Ai;

public sealed record ModelInfo(string Provider, string Model, string DisplayName, bool SupportsTools, bool IsLocal, bool IsDefault)
{
    public string Id => $"{Provider}/{Model}";
}

/// <summary>What the user may pick: models discovered on the local Ollama server plus configured hosted models.</summary>
public sealed class ModelCatalog(IOptions<AiOptions> options, TimeProvider clock, ILogger<ModelCatalog> log)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<ModelInfo> _cache = [];
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken ct = default)
    {
        if (clock.GetUtcNow() - _cachedAt < CacheFor) return _cache;

        await _gate.WaitAsync(ct);
        try
        {
            if (clock.GetUtcNow() - _cachedAt < CacheFor) return _cache;

            var ai = options.Value;
            var models = new List<ModelInfo>();
            models.AddRange(await DiscoverOllamaAsync(ai, ct));
            AddHosted(models, Providers.OpenAI, ai.OpenAI, ai);
            AddHosted(models, Providers.Anthropic, ai.Anthropic, ai);
            AddHosted(models, Providers.Google, ai.Google, ai);

            _cache = models;
            _cachedAt = clock.GetUtcNow();
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Validates a user's choice: it must exist and support tool calling (the agent cannot work without it).</summary>
    public async Task<ModelInfo> RequireUsableAsync(string provider, string model, CancellationToken ct = default)
    {
        var match = (await ListAsync(ct)).FirstOrDefault(m =>
            m.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) && m.Model.Equals(model, StringComparison.OrdinalIgnoreCase));
        if (match is null) throw new ArgumentException($"Model '{provider}/{model}' is not available.");
        if (!match.SupportsTools) throw new ArgumentException($"Model '{provider}/{model}' does not support tool calling, which the assistant needs.");
        return match;
    }

    private async Task<IEnumerable<ModelInfo>> DiscoverOllamaAsync(AiOptions ai, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(ai.Ollama.Endpoint), Timeout = TimeSpan.FromSeconds(10) };
            var client = new OllamaApiClient(http);
            var found = new List<ModelInfo>();
            foreach (var m in await client.ListLocalModelsAsync(ct))
            {
                var details = await client.ShowModelAsync(new ShowModelRequest { Model = m.Name }, ct);
                var caps = details.Capabilities ?? [];
                if (!caps.Contains("completion")) continue; // embedding-only models are not chat models
                found.Add(new ModelInfo(Providers.Ollama, m.Name, $"{m.Name} (local)", caps.Contains("tools"), true, IsDefault(ai, Providers.Ollama, m.Name)));
            }
            return found;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            log.LogWarning("Ollama is not reachable at {Endpoint}: {Message}", ai.Ollama.Endpoint, ex.Message);
            return [];
        }
    }

    private static void AddHosted(List<ModelInfo> models, string provider, HostedProviderOptions o, AiOptions ai)
    {
        if (!o.Enabled) return;
        models.AddRange(o.Models.Select(m => new ModelInfo(provider, m, $"{m} ({provider})", true, false, IsDefault(ai, provider, m))));
    }

    private static bool IsDefault(AiOptions ai, string provider, string model) =>
        ai.Default.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) && ai.Default.Model.Equals(model, StringComparison.OrdinalIgnoreCase);
}

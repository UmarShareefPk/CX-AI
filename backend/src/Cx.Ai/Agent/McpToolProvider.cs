using System.Collections.Concurrent;
using Cx.Core.Data;
using Cx.Core.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace Cx.Ai.Agent;

/// <summary>The tools an agent may use on behalf of a given user.</summary>
public interface IAgentToolSource
{
    Task<IReadOnlyList<AITool>> GetToolsAsync(AgentIdentity user, CancellationToken ct = default);
}

/// <summary>
/// Launches the BUILT MCP server DLL over stdio, one process per data scope (one per dealer, one for admins).
/// The scope is fixed in the process environment when it starts, so the model can never widen it: the tools have no
/// "which dealer" argument that a prompt could manipulate. Idle processes are shut down.
/// </summary>
public sealed class McpToolProvider : IAgentToolSource, IAsyncDisposable
{
    /// <summary>Tools a dealer session never receives. The server enforces this too; hiding them also saves the model a wrong turn.</summary>
    private static readonly HashSet<string> AdminOnlyTools = ["get_leaderboard"];

    private sealed class Handle(McpClient client, IList<McpClientTool> tools)
    {
        public McpClient Client { get; } = client;
        public IList<McpClientTool> Tools { get; } = tools;
        public DateTimeOffset LastUsed { get; set; }
    }

    private readonly ConcurrentDictionary<string, Lazy<Task<Handle>>> _handles = new();
    private readonly McpOptions _mcp;
    private readonly MongoOptions _mongo;
    private readonly EmbeddingOptions _embeddings;
    private readonly AiOptions _ai;
    private readonly TimeProvider _clock;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpToolProvider> _log;
    private readonly Timer _sweeper;

    public McpToolProvider(
        IOptions<McpOptions> mcp, IOptions<MongoOptions> mongo, IOptions<EmbeddingOptions> embeddings, IOptions<AiOptions> ai,
        TimeProvider clock, ILoggerFactory loggerFactory)
    {
        _mcp = mcp.Value;
        _mongo = mongo.Value;
        _embeddings = embeddings.Value;
        _ai = ai.Value;
        _clock = clock;
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger<McpToolProvider>();
        _sweeper = new Timer(_ => _ = SweepIdleAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(AgentIdentity user, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            var lazy = _handles.GetOrAdd(user.ScopeKey, _ => new Lazy<Task<Handle>>(() => StartAsync(user)));
            try
            {
                var handle = await lazy.Value.WaitAsync(ct);
                if (handle.Client.Completion.IsCompleted) throw new InvalidOperationException("The MCP server process has exited.");
                handle.LastUsed = _clock.GetUtcNow();
                return handle.Tools.Where(t => user.IsAdmin || !AdminOnlyTools.Contains(t.Name)).Cast<AITool>().ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt == 0)
            {
                _log.LogWarning(ex, "MCP server for {Scope} is unavailable; restarting it", user.ScopeKey);
                await EvictAsync(user.ScopeKey, lazy);
            }
        }
    }

    private async Task<Handle> StartAsync(AgentIdentity user)
    {
        var dll = Path.IsPathRooted(_mcp.ServerDll) ? _mcp.ServerDll : Path.Combine(AppContext.BaseDirectory, _mcp.ServerDll);
        if (!File.Exists(dll)) throw new FileNotFoundException($"MCP server not found at '{dll}'. Build the solution so the server is copied next to the API.", dll);

        var env = new Dictionary<string, string?>
        {
            ["Cx__Scope__Role"] = user.Role.ToString(),
            ["Cx__Scope__DealerId"] = user.DealerId ?? "",
            ["Mongo__ConnectionString"] = _mongo.ConnectionString,
            ["Mongo__Database"] = _mongo.Database,
            ["Ai__Embeddings__Provider"] = _embeddings.Provider,
            ["Ai__Embeddings__Model"] = _embeddings.Model,
            ["Ai__Embeddings__DocumentTemplate"] = _embeddings.DocumentTemplate,
            ["Ai__Embeddings__QueryTemplate"] = _embeddings.QueryTemplate,
            ["Ai__Embeddings__BatchSize"] = _embeddings.BatchSize.ToString(),
            ["Ai__Ollama__Endpoint"] = _ai.Ollama.Endpoint,
            ["Ai__RequestTimeoutSeconds"] = _ai.RequestTimeoutSeconds.ToString(),
        };
        if (_ai.OpenAI.Enabled) env["Ai__OpenAI__ApiKey"] = _ai.OpenAI.ApiKey;
        if (!string.IsNullOrWhiteSpace(_ai.OpenAI.Endpoint)) env["Ai__OpenAI__Endpoint"] = _ai.OpenAI.Endpoint;
        if (_ai.Google.Enabled) env["Ai__Google__ApiKey"] = _ai.Google.ApiKey;

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = $"cx-mcp-{user.ScopeKey}",
            Command = _mcp.Command,
            Arguments = [dll],
            EnvironmentVariables = env,
            StandardErrorLines = line => _log.LogDebug("[mcp:{Scope}] {Line}", user.ScopeKey, line),
        }, _loggerFactory);

        var client = await McpClient.CreateAsync(transport, loggerFactory: _loggerFactory);
        var tools = await client.ListToolsAsync();
        _log.LogInformation("Started MCP server for {Scope} with {Count} tools", user.ScopeKey, tools.Count);
        return new Handle(client, tools) { LastUsed = _clock.GetUtcNow() };
    }

    private async Task EvictAsync(string key, Lazy<Task<Handle>> lazy)
    {
        _handles.TryRemove(new KeyValuePair<string, Lazy<Task<Handle>>>(key, lazy));
        if (lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
            await lazy.Value.Result.Client.DisposeAsync();
    }

    private async Task SweepIdleAsync()
    {
        var cutoff = _clock.GetUtcNow() - TimeSpan.FromMinutes(_mcp.IdleMinutes);
        foreach (var (key, lazy) in _handles)
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully && lazy.Value.Result.LastUsed < cutoff)
            {
                _log.LogInformation("Stopping idle MCP server for {Scope}", key);
                await EvictAsync(key, lazy);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sweeper.DisposeAsync();
        foreach (var (key, lazy) in _handles.ToArray()) await EvictAsync(key, lazy);
    }
}

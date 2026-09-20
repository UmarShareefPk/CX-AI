using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Cx.Ai;
using Cx.Ai.Agent;
using Cx.Core.Data;
using Cx.Core.Domain;
using Microsoft.Extensions.AI;

namespace Cx.Tests.Support;

/// <summary>Replays a fixed script of model replies. No test ever calls a real LLM.</summary>
public sealed class ScriptedChatClient(params Func<IReadOnlyList<ChatMessage>, ChatResponse>[] script) : IChatClient
{
    private int _calls;

    /// <summary>The message list the model received on each call (snapshot).</summary>
    public List<IReadOnlyList<ChatMessage>> Seen { get; } = [];

    public int Calls => _calls;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToList();
        Seen.Add(snapshot);
        var step = script[Math.Min(_calls++, script.Length - 1)];
        return Task.FromResult(step(snapshot));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    public static Func<IReadOnlyList<ChatMessage>, ChatResponse> Say(string text) =>
        _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, text));

    public static Func<IReadOnlyList<ChatMessage>, ChatResponse> Call(string tool, Dictionary<string, object?>? args = null, string callId = "call-1") =>
        _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, tool, args ?? [])]));

    public static Func<IReadOnlyList<ChatMessage>, ChatResponse> Fail(string message) =>
        _ => throw new InvalidOperationException(message);
}

/// <summary>Wraps the scripted model in the REAL FunctionInvokingChatClient, so the agent loop under test is the production one.</summary>
public sealed class FakeChatClientFactory(IChatClient scripted, int maxIterations = 10) : IChatClientFactory
{
    public IChatClient Create(string provider, string model) =>
        new ChatClientBuilder(scripted)
            .Use(inner => new FunctionInvokingChatClient(inner) { MaximumIterationsPerRequest = maxIterations })
            .Build();
}

public sealed class FakeToolSource(params AITool[] tools) : IAgentToolSource
{
    public Task<IReadOnlyList<AITool>> GetToolsAsync(AgentIdentity user, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AITool>>(tools);
}

public sealed class InMemoryConversationStore : IConversationStore
{
    public Dictionary<string, Conversation> Items { get; } = [];

    public Task<Conversation?> GetAsync(string id, string userId, CancellationToken ct = default) =>
        Task.FromResult(Items.TryGetValue(id, out var c) && c.UserId == userId ? c : null);

    public Task SaveAsync(Conversation conversation, CancellationToken ct = default)
    {
        Items[conversation.Id] = conversation;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, string userId, CancellationToken ct = default)
    {
        Items.Remove(id);
        return Task.CompletedTask;
    }
}

/// <summary>Deterministic bag-of-words embedding: texts sharing words get similar vectors. Lets retrieval be tested with no model.</summary>
public sealed partial class HashEmbeddingGenerator(int dimensions = 256) : IEmbeddingGenerator<string, Embedding<float>>
{
    public int CallCount { get; private set; }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        CallCount++;
        var result = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var value in values)
        {
            var v = new float[dimensions];
            foreach (Match m in Word().Matches(value.ToLowerInvariant()))
            {
                var hash = 17;
                foreach (var ch in m.Value) hash = unchecked(hash * 31 + ch);
                v[(hash & int.MaxValue) % dimensions] += 1;
            }
            var norm = MathF.Sqrt(v.Sum(x => x * x));
            if (norm > 0) for (var i = 0; i < v.Length; i++) v[i] /= norm;
            result.Add(new Embedding<float>(v));
        }
        return Task.FromResult(result);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    [GeneratedRegex("[a-z0-9]{3,}")]
    private static partial Regex Word();
}

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

using System.Runtime.CompilerServices;
using System.Text.Json;
using Cx.Core.Data;
using Cx.Core.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;

namespace Cx.Ai.Agent;

/// <summary>
/// The agentic RAG loop. The model is given the MCP tools (data queries + policy search) and decides for itself which to
/// call, in what order and how often; FunctionInvokingChatClient executes them and feeds the results back until the model
/// answers. A turn is only persisted if it completes cleanly, so a failed model call can never corrupt the conversation.
/// </summary>
public sealed class AgentService(
    IChatClientFactory factory,
    IAgentToolSource mcp,
    IConversationStore conversations,
    IOptions<AiOptions> options,
    TimeProvider clock,
    ILogger<AgentService> log)
{
    private const int ToolResultPreviewChars = 4000;

    public async IAsyncEnumerable<AgentEvent> RunAsync(AgentRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var ai = options.Value;
        var started = clock.GetTimestamp();

        var prep = await PrepareAsync(request, ai, ct);
        if (prep.Error is not null)
        {
            yield return new AgentEvent("error") { Text = prep.Error };
            yield break;
        }

        var (conversation, history, tools) = (prep.Conversation!, prep.History!, prep.Tools!);
        yield return new AgentEvent("conversation")
        {
            ConversationId = conversation.Id, Provider = conversation.Provider, Model = conversation.Model,
        };

        var userMessage = new ChatMessage(ChatRole.User, request.Message);
        var messages = new List<ChatMessage>(history.Count + 2)
        {
            new(ChatRole.System, AgentPrompt.Build(request.User, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), request.ViewStart, request.ViewEnd)),
        };
        messages.AddRange(history);
        messages.Add(userMessage);

        var chatOptions = new ChatOptions
        {
            Tools = [.. tools],
            ToolMode = ChatToolMode.Auto,
            Temperature = ai.Temperature,
            MaxOutputTokens = ai.MaxOutputTokens,
        };

        using var client = factory.Create(request.Provider, request.Model);
        var updates = new List<ChatResponseUpdate>();
        var toolNames = new Dictionary<string, string>();
        long inputTokens = 0, outputTokens = 0;
        Exception? failure = null;

        await using var stream = client.GetStreamingResponseAsync(messages, chatOptions, ct).GetAsyncEnumerator(ct);
        while (true)
        {
            try
            {
                if (!await stream.MoveNextAsync()) break;
            }
            catch (Exception ex)
            {
                failure = ex;
                break;
            }

            var update = stream.Current;
            updates.Add(update);
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent { Text.Length: > 0 } reasoning:
                        yield return new AgentEvent("reasoning") { Text = reasoning.Text };
                        break;
                    case TextContent { Text.Length: > 0 } text:
                        yield return new AgentEvent("text") { Text = text.Text };
                        break;
                    case FunctionCallContent call:
                        toolNames[call.CallId] = call.Name;
                        yield return new AgentEvent("tool_call")
                        {
                            CallId = call.CallId, ToolName = call.Name,
                            Arguments = call.Arguments is null ? "{}" : JsonSerializer.Serialize(call.Arguments, AIJsonUtilities.DefaultOptions),
                        };
                        break;
                    case FunctionResultContent result:
                        var (resultText, isError) = ToolResultFormatter.Describe(result.Result);
                        if (result.Exception is not null) isError = true;
                        yield return new AgentEvent("tool_result")
                        {
                            CallId = result.CallId, ToolName = toolNames.GetValueOrDefault(result.CallId),
                            Result = resultText.Length > ToolResultPreviewChars ? resultText[..ToolResultPreviewChars] + "…" : resultText,
                            IsError = isError,
                        };
                        break;
                    case UsageContent usage:
                        inputTokens += usage.Details.InputTokenCount ?? 0;
                        outputTokens += usage.Details.OutputTokenCount ?? 0;
                        break;
                }
            }
        }

        if (ct.IsCancellationRequested) yield break; // client went away: nothing is persisted

        var response = failure is null ? updates.ToChatResponse() : null;
        var problem = failure is not null ? "The model call failed. Please try again or pick another model."
            : IncompleteReason(response!);
        if (problem is not null)
        {
            if (failure is not null) log.LogError(failure, "Agent run failed for {Provider}/{Model}", request.Provider, request.Model);
            else log.LogWarning("Agent run discarded: {Problem}", problem);
            yield return new AgentEvent("error") { Text = problem }; // turn rolled back: history is unchanged
            yield break;
        }

        // FunctionInvokingChatClient works on a copy, so we append everything it produced ourselves. All of it is kept,
        // including reasoning content, because providers such as Gemini need their opaque thought signatures echoed back.
        conversation.Messages.AddRange(new[] { userMessage }.Concat(response!.Messages).Select(Serialize));
        conversation.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        if (string.IsNullOrEmpty(conversation.Title)) conversation.Title = Truncate(request.Message, 60);

        var saved = await TrySaveAsync(conversation, ct);
        if (!saved)
        {
            yield return new AgentEvent("error") { Text = "The answer could not be saved to the conversation history." };
            yield break;
        }

        yield return new AgentEvent("done")
        {
            ConversationId = conversation.Id, InputTokens = inputTokens, OutputTokens = outputTokens,
            ElapsedMs = (long)clock.GetElapsedTime(started).TotalMilliseconds,
        };
    }

    private sealed record Prepared(string? Error, Conversation? Conversation, List<ChatMessage>? History, IReadOnlyList<AITool>? Tools);

    private async Task<Prepared> PrepareAsync(AgentRequest request, AiOptions ai, CancellationToken ct)
    {
        try
        {
            Conversation conversation;
            if (request.ConversationId is { Length: > 0 } id)
            {
                conversation = await conversations.GetAsync(id, request.User.UserId, ct)
                    ?? throw new ArgumentException("Conversation not found.");
                // Hidden reasoning state is provider-specific, so a conversation cannot change provider midway.
                if (!conversation.Provider.Equals(request.Provider, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"This conversation was started with {conversation.Provider}. Start a new chat to switch provider.");
                conversation.Model = request.Model;
            }
            else
            {
                conversation = new Conversation
                {
                    Id = ObjectId.GenerateNewId().ToString(), UserId = request.User.UserId,
                    Provider = request.Provider.ToLowerInvariant(), Model = request.Model, CreatedAt = clock.GetUtcNow().UtcDateTime,
                };
            }

            var history = conversation.Messages
                .Select(m => JsonSerializer.Deserialize<ChatMessage>(m, AIJsonUtilities.DefaultOptions)!)
                .ToList();
            var tools = await mcp.GetToolsAsync(request.User, ct);
            return new Prepared(null, conversation, TrimToRecentTurns(history, ai.MaxHistoryTurns), tools);
        }
        catch (ArgumentException ex)
        {
            return new Prepared(ex.Message, null, null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Could not prepare the agent run");
            return new Prepared("The assistant is temporarily unavailable (its tools could not be started).", null, null, null);
        }
    }

    private async Task<bool> TrySaveAsync(Conversation conversation, CancellationToken ct)
    {
        try
        {
            await conversations.SaveAsync(conversation, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Could not save conversation {Id}", conversation.Id);
            return false;
        }
    }

    /// <summary>A turn is complete only if it ends with a text answer and no tool call is left unanswered.</summary>
    internal static string? IncompleteReason(ChatResponse response)
    {
        var last = response.Messages.LastOrDefault();
        if (last is null || last.Role != ChatRole.Assistant)
            return "The model returned no answer.";
        if (last.Contents.OfType<FunctionCallContent>().Any())
            return "The assistant stopped before finishing (tool-call limit reached). Try a more specific question.";
        if (string.IsNullOrWhiteSpace(response.Text))
            return "The model returned an empty answer. Try again or pick another model.";
        return null;
    }

    /// <summary>Keeps the last <paramref name="turns"/> user turns, cutting only at a user message so tool call/result pairs stay intact.</summary>
    internal static List<ChatMessage> TrimToRecentTurns(List<ChatMessage> history, int turns)
    {
        var userIndexes = history.Select((m, i) => (m, i)).Where(x => x.m.Role == ChatRole.User).Select(x => x.i).ToList();
        return userIndexes.Count <= turns ? history : history.Skip(userIndexes[^turns]).ToList();
    }

    private static string Serialize(ChatMessage message) => JsonSerializer.Serialize(message, AIJsonUtilities.DefaultOptions);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
}

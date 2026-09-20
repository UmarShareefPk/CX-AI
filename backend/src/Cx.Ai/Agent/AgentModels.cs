using System.Text.Json.Serialization;
using Cx.Core.Domain;

namespace Cx.Ai.Agent;

/// <summary>The authenticated caller, taken from the validated JWT (never from the request body or the model).</summary>
public sealed record AgentIdentity(string UserId, string DisplayName, UserRole Role, string? DealerId, string? DealerName)
{
    public bool IsAdmin => Role == UserRole.Admin;
    public string ScopeKey => IsAdmin ? "admin" : $"dealer:{DealerId}";
}

/// <param name="ViewStart">The date range currently selected in the UI; used as the default period for the questions.</param>
public sealed record AgentRequest(
    AgentIdentity User, string Provider, string Model, string Message, string? ConversationId, DateOnly? ViewStart, DateOnly? ViewEnd);

/// <summary>One streamed step of an agent run. Serialized as JSON to the browser over server-sent events.</summary>
public sealed record AgentEvent(
    [property: JsonPropertyName("type")] string Type)
{
    /// <summary>conversation | text | reasoning | tool_call | tool_result | done | error</summary>
    public string? Text { get; init; }
    public string? ConversationId { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? CallId { get; init; }
    public string? ToolName { get; init; }
    public string? Arguments { get; init; }
    public string? Result { get; init; }
    public bool? IsError { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? ElapsedMs { get; init; }
}

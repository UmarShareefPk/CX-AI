using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cx.Ai;
using Cx.Ai.Agent;
using Cx.Api.Infrastructure;
using Cx.Core.Data;
using Cx.Core.Rag;
using Microsoft.Extensions.Options;

namespace Cx.Api.Endpoints;

public sealed record ChatRequest(
    string Message, string? Provider, string? Model, string? ConversationId, DateOnly? ViewStart, DateOnly? ViewEnd);

public static class AiEndpoints
{
    private const int MaxMessageChars = 2000;

    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void MapAi(this IEndpointRouteBuilder app)
    {
        var ai = app.MapGroup("/api/ai").WithTags("AI");

        ai.MapGet("/models", async (ModelCatalog catalog, IOptions<AiOptions> options, CancellationToken ct) =>
            Results.Ok(new { @default = options.Value.Default, models = await catalog.ListAsync(ct) }));

        // Server-sent events: one JSON object per "data:" line, so the browser can render tool calls and tokens as they happen.
        ai.MapPost("/chat", async (ChatRequest request, HttpContext http, ClaimsPrincipal user, ModelCatalog catalog, AgentService agent, IOptions<AiOptions> options) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > MaxMessageChars)
                throw new ArgumentException($"The message must be between 1 and {MaxMessageChars} characters.");

            var provider = request.Provider ?? options.Value.Default.Provider;
            var model = request.Model ?? options.Value.Default.Model;
            var chosen = await catalog.RequireUsableAsync(provider, model, http.RequestAborted); // 400 before the stream starts

            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            var run = new AgentRequest(user.ToAgentIdentity(), chosen.Provider, chosen.Model, request.Message.Trim(),
                request.ConversationId, request.ViewStart, request.ViewEnd);

            await foreach (var ev in agent.RunAsync(run, http.RequestAborted))
            {
                await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(ev, EventJson)}\n\n", http.RequestAborted);
                await http.Response.Body.FlushAsync(http.RequestAborted);
            }
        }).RequireRateLimiting(Policies.ChatLimit);

        ai.MapDelete("/conversations/{id}", async (string id, ClaimsPrincipal user, IConversationStore store, CancellationToken ct) =>
        {
            await store.DeleteAsync(id, user.UserId(), ct);
            return Results.NoContent();
        });

        app.MapPost("/api/admin/policies/reindex", async (bool? force, PolicyIngestor ingestor, CancellationToken ct) =>
            Results.Ok(await ingestor.EnsureIndexedAsync(force ?? false, ct))).RequireAuthorization(Policies.Admin).WithTags("Admin");
    }
}

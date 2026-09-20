using Cx.Core.Domain;
using MongoDB.Driver;

namespace Cx.Core.Data;

public interface IConversationStore
{
    /// <summary>Returns the conversation only if it belongs to <paramref name="userId"/>.</summary>
    Task<Conversation?> GetAsync(string id, string userId, CancellationToken ct = default);
    Task SaveAsync(Conversation conversation, CancellationToken ct = default);
    Task DeleteAsync(string id, string userId, CancellationToken ct = default);
}

public sealed class ConversationStore(CxDatabase db) : IConversationStore
{
    public async Task<Conversation?> GetAsync(string id, string userId, CancellationToken ct = default) =>
        await db.Conversations.Find(c => c.Id == id && c.UserId == userId).FirstOrDefaultAsync(ct);

    public Task SaveAsync(Conversation conversation, CancellationToken ct = default) =>
        db.Conversations.ReplaceOneAsync(c => c.Id == conversation.Id, conversation, new ReplaceOptions { IsUpsert = true }, ct);

    public Task DeleteAsync(string id, string userId, CancellationToken ct = default) =>
        db.Conversations.DeleteOneAsync(c => c.Id == id && c.UserId == userId, ct);
}

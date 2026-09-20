using CommunityToolkit.VectorData.InMemory;
using Cx.Core.Data;
using Cx.Core.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using MongoDB.Driver;

namespace Cx.Core.Rag;

public sealed record PolicyHit(string Id, string Source, string Title, string Text, double Score);

/// <summary>Record shape held in the VectorData collection (hydrated from the vectors persisted in MongoDB).</summary>
public sealed class PolicyVectorRecord
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public ReadOnlyMemory<float> Embedding { get; set; }
}

/// <summary>
/// Semantic search over the policy chunks. MongoDB is the durable store (text + vectors); the vectors are loaded into a
/// Microsoft.Extensions.VectorData in-memory collection for cosine-similarity search. A local community mongod has no
/// $vectorSearch, and for a few dozen chunks an exact in-process search is both simpler and exact. The collection is
/// reloaded automatically when the persisted chunks change (e.g. after a re-index).
/// </summary>
public sealed class PolicySearch(
    CxDatabase db,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    IOptions<EmbeddingOptions> options) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VectorStoreCollection<string, PolicyVectorRecord>? _collection;
    private string _signature = "";

    public async Task<IReadOnlyList<PolicyHit>> SearchAsync(string query, int topK = 4, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("A search query is required.", nameof(query));
        var collection = await GetCollectionAsync(ct);

        var opt = options.Value;
        var queryInput = opt.QueryTemplate.Replace("{query}", query.Trim());
        var vector = (await embedder.GenerateAsync([queryInput], cancellationToken: ct))[0].Vector;

        var hits = new List<PolicyHit>();
        await foreach (var r in collection.SearchAsync(vector, Math.Clamp(topK, 1, 10), cancellationToken: ct))
            hits.Add(new PolicyHit(r.Record.Id, r.Record.Source, r.Record.Title, r.Record.Text, Math.Round(r.Score ?? 0, 4)));
        return hits;
    }

    private async Task<VectorStoreCollection<string, PolicyVectorRecord>> GetCollectionAsync(CancellationToken ct)
    {
        var modelKey = options.Value.ModelKey;
        var filter = Builders<PolicyChunk>.Filter.Eq(c => c.EmbeddingModel, modelKey);
        var latest = await db.PolicyChunks.Find(filter).SortByDescending(c => c.UpdatedAt).Limit(1)
            .Project(c => c.UpdatedAt).FirstOrDefaultAsync(ct);
        var count = await db.PolicyChunks.CountDocumentsAsync(filter, cancellationToken: ct);
        var signature = $"{modelKey}|{count}|{latest:O}";

        if (_collection is not null && signature == _signature) return _collection;

        await _gate.WaitAsync(ct);
        try
        {
            if (_collection is not null && signature == _signature) return _collection;

            var chunks = await db.PolicyChunks.Find(filter).ToListAsync(ct);
            if (chunks.Count == 0)
                throw new InvalidOperationException(
                    $"The policy index is empty for embedding model '{modelKey}'. Run the seeder (or POST /api/admin/policies/reindex) first.");

            var dimensions = chunks[0].Dimensions;
            var definition = new VectorStoreCollectionDefinition
            {
                Properties =
                [
                    new VectorStoreKeyProperty(nameof(PolicyVectorRecord.Id), typeof(string)),
                    new VectorStoreDataProperty(nameof(PolicyVectorRecord.Source), typeof(string)),
                    new VectorStoreDataProperty(nameof(PolicyVectorRecord.Title), typeof(string)),
                    new VectorStoreDataProperty(nameof(PolicyVectorRecord.Text), typeof(string)),
                    new VectorStoreVectorProperty(nameof(PolicyVectorRecord.Embedding), typeof(ReadOnlyMemory<float>), dimensions)
                        { DistanceFunction = DistanceFunction.CosineSimilarity },
                ],
            };

            var collection = new InMemoryVectorStore().GetCollection<string, PolicyVectorRecord>("policy", definition);
            await collection.EnsureCollectionExistsAsync(ct);
            await collection.UpsertAsync(chunks.Select(c => new PolicyVectorRecord
            {
                Id = c.Id, Source = c.Source, Title = c.Title, Text = c.Text, Embedding = c.Embedding,
            }), ct);

            _collection = collection;
            _signature = signature;
            return collection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

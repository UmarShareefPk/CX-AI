using Cx.Core.Data;
using Cx.Core.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Cx.Core.Rag;

public sealed record IngestResult(int Total, int Embedded, int Unchanged, int Removed, string EmbeddingModel, int Dimensions);

/// <summary>
/// Chunks the embedded policy documents, embeds new or changed chunks in batches and persists text + vector in
/// MongoDB. Idempotent: an unchanged chunk embedded with the same model is left alone, so restarts never re-embed.
/// </summary>
public sealed class PolicyIngestor(
    CxDatabase db,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    IOptions<EmbeddingOptions> options,
    TimeProvider clock,
    ILogger<PolicyIngestor> log)
{
    public static IReadOnlyList<PolicyPassage> LoadPassages()
    {
        var assembly = typeof(PolicyIngestor).Assembly;
        const string prefix = "Cx.Core.Policies.";
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .SelectMany(name =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
                return PolicyChunker.Split(name[prefix.Length..^3], reader.ReadToEnd());
            })
            .ToList();
    }

    public async Task<IngestResult> EnsureIndexedAsync(bool force = false, CancellationToken ct = default)
    {
        var opt = options.Value;
        var passages = LoadPassages();
        var existing = (await db.PolicyChunks.Find(FilterDefinition<PolicyChunk>.Empty).ToListAsync(ct)).ToDictionary(c => c.Id);

        // The hash covers the exact text sent to the embedding model, so a template change re-embeds too.
        var work = passages
            .Select(p => (Passage: p, Input: opt.DocumentTemplate.Replace("{title}", p.Title).Replace("{text}", p.Text)))
            .Select(x => (x.Passage, x.Input, Hash: PolicyChunker.Hash(x.Input)))
            .ToList();

        var stale = work.Where(w => force
            || !existing.TryGetValue(w.Passage.Id, out var c)
            || c.ContentHash != w.Hash || c.EmbeddingModel != opt.ModelKey || c.Embedding.Length == 0).ToList();

        var dimensions = 0;
        foreach (var batch in stale.Chunk(opt.BatchSize))
        {
            var embeddings = await embedder.GenerateAsync(batch.Select(b => b.Input).ToList(), cancellationToken: ct);
            if (embeddings.Count != batch.Length)
                throw new InvalidOperationException($"Embedding model returned {embeddings.Count} vectors for {batch.Length} inputs.");

            var writes = batch.Select((b, i) =>
            {
                var vector = embeddings[i].Vector.ToArray();
                dimensions = vector.Length;
                return new ReplaceOneModel<PolicyChunk>(
                    Builders<PolicyChunk>.Filter.Eq(c => c.Id, b.Passage.Id),
                    new PolicyChunk
                    {
                        Id = b.Passage.Id, Source = b.Passage.Source, Title = b.Passage.Title, Text = b.Passage.Text,
                        ContentHash = b.Hash, EmbeddingModel = opt.ModelKey, Embedding = vector, Dimensions = vector.Length,
                        UpdatedAt = clock.GetUtcNow().UtcDateTime,
                    }) { IsUpsert = true };
            }).ToList();
            await db.PolicyChunks.BulkWriteAsync(writes, cancellationToken: ct);
        }

        var currentIds = passages.Select(p => p.Id).ToHashSet();
        var removed = existing.Keys.Where(id => !currentIds.Contains(id)).ToList();
        if (removed.Count > 0)
            await db.PolicyChunks.DeleteManyAsync(Builders<PolicyChunk>.Filter.In(c => c.Id, removed), ct);

        if (dimensions == 0) dimensions = existing.Values.FirstOrDefault(c => c.EmbeddingModel == opt.ModelKey)?.Dimensions ?? 0;
        log.LogInformation("Policy index: {Total} chunks, {Embedded} embedded, {Removed} removed (model {Model}, {Dim} dims)",
            passages.Count, stale.Count, removed.Count, opt.ModelKey, dimensions);
        return new IngestResult(passages.Count, stale.Count, passages.Count - stale.Count, removed.Count, opt.ModelKey, dimensions);
    }
}

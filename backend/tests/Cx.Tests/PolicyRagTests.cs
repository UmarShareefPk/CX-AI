using Cx.Core.Data;
using Cx.Core.Domain;
using Cx.Core.Rag;
using Cx.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Cx.Tests;

public class PolicyRagTests(SeededDatabase seeded) : IClassFixture<SeededDatabase>
{
    private static EmbeddingOptions Options(string model = "hash-a") => new() { Provider = "test", Model = model };

    private PolicyIngestor Ingestor(HashEmbeddingGenerator embedder, EmbeddingOptions options) =>
        new(seeded.Db, embedder, Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System, NullLogger<PolicyIngestor>.Instance);

    private PolicySearch Search(HashEmbeddingGenerator embedder, EmbeddingOptions options) =>
        new(seeded.Db, embedder, Microsoft.Extensions.Options.Options.Create(options));

    [MongoFact]
    public async Task Chunks_and_their_vectors_are_persisted_in_mongodb()
    {
        var options = Options("persist");
        var result = await Ingestor(new HashEmbeddingGenerator(64), options).EnsureIndexedAsync();

        var stored = await seeded.Db.PolicyChunks.Find(c => c.EmbeddingModel == options.ModelKey).ToListAsync();
        Assert.Equal(result.Total, stored.Count);
        Assert.All(stored, c =>
        {
            Assert.Equal(64, c.Embedding.Length);
            Assert.Equal(64, c.Dimensions);
            Assert.NotEmpty(c.Text);
        });
    }

    [MongoFact]
    public async Task Re_indexing_unchanged_content_does_not_call_the_embedding_model_again()
    {
        var options = Options("idempotent");
        var embedder = new HashEmbeddingGenerator();
        var ingestor = Ingestor(embedder, options);

        var first = await ingestor.EnsureIndexedAsync();
        var callsAfterFirst = embedder.CallCount;
        var second = await ingestor.EnsureIndexedAsync();

        Assert.Equal(first.Total, first.Embedded);
        Assert.Equal(0, second.Embedded);
        Assert.Equal(second.Total, second.Unchanged);
        Assert.Equal(callsAfterFirst, embedder.CallCount);
    }

    [MongoFact]
    public async Task Changing_the_embedding_model_re_embeds_everything_because_vectors_are_never_mixed()
    {
        var embedder = new HashEmbeddingGenerator();
        await Ingestor(embedder, Options("model-one")).EnsureIndexedAsync();
        var switched = await Ingestor(embedder, Options("model-two")).EnsureIndexedAsync();

        Assert.Equal(switched.Total, switched.Embedded);
        var models = await seeded.Db.PolicyChunks.Distinct(c => c.EmbeddingModel, FilterDefinition<PolicyChunk>.Empty).ToListAsync();
        Assert.Equal(["test/model-two"], models); // each chunk holds exactly one vector, from the current model
    }

    [MongoFact]
    public async Task Search_returns_the_most_relevant_policy_passage_first()
    {
        var options = Options("search");
        var embedder = new HashEmbeddingGenerator();
        await Ingestor(embedder, options).EnsureIndexedAsync();
        using var search = Search(embedder, options);

        var inspection = await search.SearchAsync("pre-delivery inspection checklist: technician and sales manager must sign", 3);
        Assert.Contains("Pre-delivery inspection checklist", inspection[0].Title);

        var tie = await search.SearchAsync("what happens when two dealers have the same score, shared rank", 3);
        Assert.Contains("Ties in ranking", tie[0].Title);

        Assert.Equal(3, tie.Count);
        Assert.True(tie[0].Score >= tie[1].Score && tie[1].Score >= tie[2].Score);
    }

    [MongoFact]
    public async Task Searching_an_empty_index_explains_how_to_fix_it()
    {
        using var search = Search(new HashEmbeddingGenerator(), Options("never-indexed"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => search.SearchAsync("anything"));
        Assert.Contains("policy index is empty", ex.Message);
    }
}

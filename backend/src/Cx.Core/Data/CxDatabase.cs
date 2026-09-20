using System.ComponentModel.DataAnnotations;
using Cx.Core.Domain;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace Cx.Core.Data;

public sealed class MongoOptions
{
    public const string Section = "Mongo";

    [Required] public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    [Required] public string Database { get; set; } = "cx";
}

/// <summary>Typed access to the collections used by the platform.</summary>
public sealed class CxDatabase
{
    private static readonly object Gate = new();
    private static bool _registered;

    public CxDatabase(IMongoClient client, IOptions<MongoOptions> options)
    {
        RegisterConventions();
        Db = client.GetDatabase(options.Value.Database);
    }

    public IMongoDatabase Db { get; }
    public IMongoCollection<Dealer> Dealers => Db.GetCollection<Dealer>("dealers");
    public IMongoCollection<Customer> Customers => Db.GetCollection<Customer>("customers");
    public IMongoCollection<SurveyResponse> SurveyResponses => Db.GetCollection<SurveyResponse>("surveyResponses");
    public IMongoCollection<AppUser> Users => Db.GetCollection<AppUser>("users");
    public IMongoCollection<PolicyChunk> PolicyChunks => Db.GetCollection<PolicyChunk>("policyChunks");
    public IMongoCollection<Conversation> Conversations => Db.GetCollection<Conversation>("conversations");

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        await Db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
        return true;
    }

    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        await SurveyResponses.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<SurveyResponse>(Builders<SurveyResponse>.IndexKeys.Descending(r => r.SubmittedAt)),
            new CreateIndexModel<SurveyResponse>(Builders<SurveyResponse>.IndexKeys.Ascending(r => r.DealerId).Descending(r => r.SubmittedAt)),
        ], ct);
        await Customers.Indexes.CreateOneAsync(
            new CreateIndexModel<Customer>(Builders<Customer>.IndexKeys.Ascending(c => c.DealerId)), cancellationToken: ct);
        await Users.Indexes.CreateOneAsync(
            new CreateIndexModel<AppUser>(Builders<AppUser>.IndexKeys.Ascending(u => u.Username), new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);
        await PolicyChunks.Indexes.CreateOneAsync(
            new CreateIndexModel<PolicyChunk>(Builders<PolicyChunk>.IndexKeys.Ascending(c => c.EmbeddingModel)), cancellationToken: ct);
        await Conversations.Indexes.CreateOneAsync(
            new CreateIndexModel<Conversation>(Builders<Conversation>.IndexKeys.Ascending(c => c.UserId).Descending(c => c.UpdatedAt)),
            cancellationToken: ct);
    }

    /// <summary>Process-wide BSON conventions: camelCase names, enums as strings, UTC dates, tolerate unknown fields.</summary>
    private static void RegisterConventions()
    {
        lock (Gate)
        {
            if (_registered) return;
            ConventionRegistry.Register("cx", new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new EnumRepresentationConvention(BsonType.String),
                new IgnoreExtraElementsConvention(true),
            }, _ => true);
            BsonSerializer.TryRegisterSerializer(new DateTimeSerializer(DateTimeKind.Utc));
            _registered = true;
        }
    }
}

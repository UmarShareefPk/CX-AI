using Cx.Core.Data;
using Cx.Core.Seeding;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Cx.Tests.Support;

/// <summary>Skips (rather than fails) database tests when no local MongoDB is reachable.</summary>
public sealed class MongoFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            var client = new MongoClient(MongoSupport.ConnectionString);
            client.GetDatabase("admin").RunCommand<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
            return true;
        }
        catch
        {
            return false;
        }
    });

    public MongoFactAttribute()
    {
        if (!Available.Value) Skip = "MongoDB is not reachable at " + MongoSupport.ConnectionString;
    }
}

public static class MongoSupport
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("CX_TEST_MONGO") ?? "mongodb://localhost:27017/?serverSelectionTimeoutMS=2000";

    public static readonly DateTime SeedNow = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
}

/// <summary>A throw-away database (cx_test_&lt;guid&gt;) seeded with the deterministic demo data set, dropped afterwards.</summary>
public sealed class SeededDatabase : IAsyncLifetime
{
    public string Name { get; } = "cx_test_" + Guid.NewGuid().ToString("N");
    public MongoClient Client { get; } = new(MongoSupport.ConnectionString);
    public CxDatabase Db { get; private set; } = null!;
    public DemoData Data { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            Db = new CxDatabase(Client, Options.Create(new MongoOptions { Database = Name }));
            Data = DemoDataGenerator.Generate(seed: 42, nowUtc: MongoSupport.SeedNow);
            await Db.EnsureIndexesAsync();
            await Db.Dealers.InsertManyAsync(Data.Dealers);
            await Db.Customers.InsertManyAsync(Data.Customers);
            await Db.SurveyResponses.InsertManyAsync(Data.Responses);
            await Db.Users.InsertManyAsync(Data.Users);
        }
        catch (MongoException)
        {
            // MongoDB is not available; [MongoFact] tests are skipped, so nothing will use this fixture.
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await Client.DropDatabaseAsync(Name);
        }
        catch (MongoException)
        {
        }
    }
}

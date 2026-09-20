using Cx.Core.Data;
using Cx.Core.Domain;
using MongoDB.Driver;

namespace Cx.Core.Seeding;

public sealed class DemoDataSeeder(CxDatabase db, TimeProvider clock)
{
    public async Task<bool> HasDataAsync(CancellationToken ct = default) =>
        await db.Dealers.CountDocumentsAsync(FilterDefinition<Dealer>.Empty, new CountOptions { Limit = 1 }, ct) > 0;

    /// <summary>Drops the demo collections (policy chunks are kept: they are expensive to re-embed and independent of demo data).</summary>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        foreach (var name in new[] { "dealers", "customers", "surveyResponses", "users", "conversations" })
            await db.Db.DropCollectionAsync(name, ct);
    }

    public async Task<DemoData> SeedAsync(int seed = 20260920, CancellationToken ct = default)
    {
        await db.EnsureIndexesAsync(ct);
        var data = DemoDataGenerator.Generate(seed, clock.GetUtcNow().UtcDateTime);
        await db.Dealers.InsertManyAsync(data.Dealers, cancellationToken: ct);
        await db.Customers.InsertManyAsync(data.Customers, cancellationToken: ct);
        await db.SurveyResponses.InsertManyAsync(data.Responses, cancellationToken: ct);
        await db.Users.InsertManyAsync(data.Users, cancellationToken: ct);
        return data;
    }
}

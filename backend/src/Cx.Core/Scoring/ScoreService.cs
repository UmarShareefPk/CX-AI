using Cx.Core.Data;
using Cx.Core.Domain;
using Cx.Core.Security;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Cx.Core.Scoring;

public sealed record PerformanceSummary(
    DateRange Period,
    string? DealerId,
    string? DealerName,
    int SurveyCount,
    double? Score,
    double? PreviousScore,
    double? ScoreChange,
    int? Rank,
    int RankedDealerCount,
    double? TopScore,
    string? TopDealerName,
    double? AvgRecommendScore,
    double? AvgConditionScore,
    IReadOnlyDictionary<string, int> RecommendBreakdown,
    IReadOnlyDictionary<string, int> ConditionBreakdown);

public sealed record TrendPoint(DateOnly Date, double Score, int SurveyCount);

public sealed record SurveyRow(
    string Id, DateTime SubmittedAt, string DealerId, string DealerName, string CustomerName, string CustomerEmail,
    string Vehicle, RecommendAnswer Recommend, VehicleConditionAnswer VehicleCondition,
    int RecommendScore, int ConditionScore, double NetScore);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize);

public sealed record ResponseQuery(
    DateRange Range, string? DealerId = null, RecommendAnswer? Recommend = null, VehicleConditionAnswer? VehicleCondition = null,
    string SortBy = "submittedAt", bool Descending = true, int Page = 1, int PageSize = 25);

public sealed class ScoreService(CxDatabase db)
{
    /// <summary>Every dealer (including those without surveys) ranked for the period. Internal: callers must apply a scope.</summary>
    internal async Task<IReadOnlyList<DealerScore>> RankAllAsync(DateRange range, CancellationToken ct)
    {
        var (from, to) = range.UtcBounds();
        var pipeline = new[]
        {
            Match(from, to, null),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$dealerId" },
                { "total", new BsonDocument("$sum", "$netScore") },
                { "count", new BsonDocument("$sum", 1) },
            }),
        };

        var groups = (await db.SurveyResponses.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct))
            .ToDictionary(d => d["_id"].AsString, d => (Total: d["total"].ToDouble(), Count: d["count"].ToInt32()));
        var dealers = await db.Dealers.Find(FilterDefinition<Dealer>.Empty).ToListAsync(ct);

        return Ranking.Rank(dealers.Select(d =>
        {
            groups.TryGetValue(d.Id, out var g);
            return (d.Id, d.Name, g.Total, g.Count);
        }));
    }

    /// <summary>Full leaderboard. Administrators only: dealers must never see each other's scores.</summary>
    public async Task<IReadOnlyList<DealerScore>> GetLeaderboardAsync(DataScope scope, DateRange range, CancellationToken ct = default)
    {
        scope.RequireAdmin();
        return await RankAllAsync(range, ct);
    }

    public async Task<PerformanceSummary> GetPerformanceAsync(DataScope scope, string? requestedDealerId, DateRange range, CancellationToken ct = default)
    {
        var dealerId = scope.ResolveDealer(requestedDealerId);
        var (from, to) = range.UtcBounds();

        var current = await AggregateAsync(dealerId, from, to, ct);
        var previousRange = range.Previous();
        var (pFrom, pTo) = previousRange.UtcBounds();
        var previous = await AggregateAsync(dealerId, pFrom, pTo, ct);

        var ranking = await RankAllAsync(range, ct);
        var ranked = ranking.Where(r => r.Rank is not null).ToList();
        var top = ranked.FirstOrDefault();
        var topNames = string.Join(" & ", ranked.TakeWhile(r => r.Rank == 1).Select(r => r.DealerName)); // ties share rank 1
        var me = dealerId is null ? null : ranking.FirstOrDefault(r => r.DealerId == dealerId);
        var dealerName = dealerId is null ? null : me?.DealerName;

        var score = SurveyScoring.DealerScore(current.Total, current.Count);
        var previousScore = SurveyScoring.DealerScore(previous.Total, previous.Count);

        return new PerformanceSummary(
            range, dealerId, dealerName, current.Count, score, previousScore,
            score is not null && previousScore is not null ? Math.Round(score.Value - previousScore.Value, 2) : null,
            me?.Rank, ranked.Count, top?.Score,
            scope.IsAdmin && topNames.Length > 0 ? topNames : null, // a dealer sees the benchmark score, never who earned it
            current.Count == 0 ? null : Math.Round(current.RecommendTotal / current.Count, 2),
            current.Count == 0 ? null : Math.Round(current.ConditionTotal / current.Count, 2),
            Complete<RecommendAnswer>(current.RecommendCounts),
            Complete<VehicleConditionAnswer>(current.ConditionCounts));
    }

    public async Task<IReadOnlyList<TrendPoint>> GetTrendAsync(DataScope scope, string? requestedDealerId, DateRange range, CancellationToken ct = default)
    {
        var dealerId = scope.ResolveDealer(requestedDealerId);
        var (from, to) = range.UtcBounds();
        var unit = range.Days <= 45 ? "day" : "week";

        var pipeline = new[]
        {
            Match(from, to, dealerId),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument("$dateTrunc", new BsonDocument { { "date", "$submittedAt" }, { "unit", unit }, { "startOfWeek", "monday" } }) },
                { "total", new BsonDocument("$sum", "$netScore") },
                { "count", new BsonDocument("$sum", 1) },
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1)),
        };

        var rows = await db.SurveyResponses.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);
        return rows.Select(r =>
        {
            var count = r["count"].ToInt32();
            return new TrendPoint(DateOnly.FromDateTime(r["_id"].ToUniversalTime()), SurveyScoring.DealerScore(r["total"].ToDouble(), count)!.Value, count);
        }).ToList();
    }

    public async Task<PagedResult<SurveyRow>> GetResponsesAsync(DataScope scope, ResponseQuery query, CancellationToken ct = default)
    {
        var dealerId = scope.ResolveDealer(query.DealerId);
        var (from, to) = query.Range.UtcBounds();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var f = Builders<SurveyResponse>.Filter;
        var filters = new List<FilterDefinition<SurveyResponse>> { f.Gte(r => r.SubmittedAt, from), f.Lt(r => r.SubmittedAt, to) };
        if (dealerId is not null) filters.Add(f.Eq(r => r.DealerId, dealerId));
        if (query.Recommend is { } rec) filters.Add(f.Eq(r => r.Recommend, rec));
        if (query.VehicleCondition is { } vc) filters.Add(f.Eq(r => r.VehicleCondition, vc));
        var filter = f.And(filters);

        var sort = Builders<SurveyResponse>.Sort;
        var sortDef = (query.SortBy.ToLowerInvariant(), query.Descending) switch
        {
            ("netscore", true) => sort.Descending(r => r.NetScore).Descending(r => r.SubmittedAt),
            ("netscore", false) => sort.Ascending(r => r.NetScore).Descending(r => r.SubmittedAt),
            (_, true) => sort.Descending(r => r.SubmittedAt),
            (_, false) => sort.Ascending(r => r.SubmittedAt),
        };

        var total = await db.SurveyResponses.CountDocumentsAsync(filter, cancellationToken: ct);
        var responses = await db.SurveyResponses.Find(filter).Sort(sortDef).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(ct);

        var customerIds = responses.Select(r => r.CustomerId).Distinct().ToList();
        var customers = (await db.Customers.Find(Builders<Customer>.Filter.In(c => c.Id, customerIds)).ToListAsync(ct))
            .ToDictionary(c => c.Id);
        var dealers = (await db.Dealers.Find(FilterDefinition<Dealer>.Empty).ToListAsync(ct)).ToDictionary(d => d.Id);

        var items = responses.Select(r =>
        {
            customers.TryGetValue(r.CustomerId, out var c);
            return new SurveyRow(
                r.Id, r.SubmittedAt, r.DealerId, dealers.TryGetValue(r.DealerId, out var d) ? d.Name : r.DealerId,
                c?.FullName ?? "Unknown", c?.Email ?? "", c is null ? "" : $"{c.VehicleYear} {c.VehicleModel}",
                r.Recommend, r.VehicleCondition, r.RecommendScore, r.ConditionScore, r.NetScore);
        }).ToList();

        return new PagedResult<SurveyRow>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<Dealer>> GetDealersAsync(DataScope scope, CancellationToken ct = default)
    {
        var filter = scope.IsAdmin ? FilterDefinition<Dealer>.Empty : Builders<Dealer>.Filter.Eq(d => d.Id, scope.DealerId);
        return await db.Dealers.Find(filter).SortBy(d => d.Name).ToListAsync(ct);
    }

    private static BsonDocument Match(DateTime from, DateTime to, string? dealerId)
    {
        var match = new BsonDocument("submittedAt", new BsonDocument { { "$gte", from }, { "$lt", to } });
        if (dealerId is not null) match["dealerId"] = dealerId;
        return new BsonDocument("$match", match);
    }

    private sealed record Totals(
        int Count, double Total, double RecommendTotal, double ConditionTotal,
        Dictionary<string, int> RecommendCounts, Dictionary<string, int> ConditionCounts);

    private async Task<Totals> AggregateAsync(string? dealerId, DateTime from, DateTime to, CancellationToken ct)
    {
        var pipeline = new[]
        {
            Match(from, to, dealerId),
            new BsonDocument("$facet", new BsonDocument
            {
                { "totals", new BsonArray { new BsonDocument("$group", new BsonDocument
                    {
                        { "_id", BsonNull.Value },
                        { "count", new BsonDocument("$sum", 1) },
                        { "total", new BsonDocument("$sum", "$netScore") },
                        { "recommend", new BsonDocument("$sum", "$recommendScore") },
                        { "condition", new BsonDocument("$sum", "$conditionScore") },
                    }) } },
                { "recommend", new BsonArray { new BsonDocument("$group", new BsonDocument { { "_id", "$recommend" }, { "n", new BsonDocument("$sum", 1) } }) } },
                { "condition", new BsonArray { new BsonDocument("$group", new BsonDocument { { "_id", "$vehicleCondition" }, { "n", new BsonDocument("$sum", 1) } }) } },
            }),
        };

        var doc = await db.SurveyResponses.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).FirstAsync(ct);
        var t = doc["totals"].AsBsonArray.Count == 0 ? null : doc["totals"][0].AsBsonDocument;
        return new Totals(
            t?["count"].ToInt32() ?? 0, t?["total"].ToDouble() ?? 0, t?["recommend"].ToDouble() ?? 0, t?["condition"].ToDouble() ?? 0,
            doc["recommend"].AsBsonArray.ToDictionary(x => x["_id"].AsString, x => x["n"].ToInt32()),
            doc["condition"].AsBsonArray.ToDictionary(x => x["_id"].AsString, x => x["n"].ToInt32()));
    }

    /// <summary>Every enum member present (zero-filled), so consumers - including the LLM - never have to infer missing keys.</summary>
    private static Dictionary<string, int> Complete<TEnum>(Dictionary<string, int> counts) where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>().ToDictionary(n => n, n => counts.GetValueOrDefault(n));
}

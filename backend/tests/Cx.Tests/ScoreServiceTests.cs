using Cx.Core.Domain;
using Cx.Core.Scoring;
using Cx.Core.Security;
using Cx.Tests.Support;
using MongoDB.Driver;

namespace Cx.Tests;

public class ScoreServiceTests(SeededDatabase seeded) : IClassFixture<SeededDatabase>
{
    private static readonly DateRange SixMonths = new(new DateOnly(2026, 3, 1), new DateOnly(2026, 9, 20));
    private static readonly DateRange ThisMonth = new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 20));

    private ScoreService Service => new(seeded.Db);

    private IEnumerable<SurveyResponse> Expected(DateRange range, string? dealerId = null)
    {
        var (from, to) = range.UtcBounds();
        return seeded.Data.Responses.Where(r => r.SubmittedAt >= from && r.SubmittedAt < to && (dealerId is null || r.DealerId == dealerId));
    }

    [MongoFact]
    public async Task Dealer_score_matches_an_independent_calculation()
    {
        foreach (var dealer in seeded.Data.Dealers)
        {
            var mine = Expected(ThisMonth, dealer.Id).ToList();
            var summary = await Service.GetPerformanceAsync(DataScope.ForDealer(dealer.Id), null, ThisMonth);

            Assert.Equal(mine.Count, summary.SurveyCount);
            Assert.Equal(mine.Count == 0 ? null : Math.Round(mine.Sum(r => r.NetScore) / mine.Count, 2), summary.Score);
            Assert.Equal(mine.Count(r => r.Recommend == RecommendAnswer.HighlyRecommend), summary.RecommendBreakdown["HighlyRecommend"]);
            Assert.Equal(mine.Count, summary.RecommendBreakdown.Values.Sum());
            Assert.Equal(mine.Count, summary.ConditionBreakdown.Values.Sum());
        }
    }

    [MongoFact]
    public async Task Rank_is_consistent_with_scores_and_the_top_score_is_the_maximum()
    {
        var board = await Service.GetLeaderboardAsync(DataScope.Admin, ThisMonth);

        Assert.Equal(seeded.Data.Dealers.Count, board.Count);
        var ranked = board.Where(d => d.Rank is not null).ToList();
        Assert.Equal(1, ranked[0].Rank);
        Assert.Equal(ranked.OrderByDescending(d => d.Score).Select(d => d.Score), ranked.Select(d => d.Score));

        var dealerSummary = await Service.GetPerformanceAsync(DataScope.ForDealer(ranked[^1].DealerId), null, ThisMonth);
        Assert.Equal(ranked[^1].Rank, dealerSummary.Rank);
        Assert.Equal(ranked[0].Score, dealerSummary.TopScore);
        Assert.Null(dealerSummary.TopDealerName); // a dealer sees the benchmark, never who earned it

        var admin = await Service.GetPerformanceAsync(DataScope.Admin, null, ThisMonth);
        var firstPlace = ranked.TakeWhile(d => d.Rank == 1).Select(d => d.DealerName);
        Assert.Equal(string.Join(" & ", firstPlace), admin.TopDealerName); // tied dealers are all named
    }

    [MongoFact]
    public async Task Only_surveys_inside_the_selected_dates_are_counted_and_the_end_date_is_inclusive()
    {
        var day = new DateRange(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10));
        var summary = await Service.GetPerformanceAsync(DataScope.Admin, null, day);
        Assert.Equal(Expected(day).Count(), summary.SurveyCount);

        var wide = await Service.GetPerformanceAsync(DataScope.Admin, null, SixMonths);
        Assert.Equal(Expected(SixMonths).Count(), wide.SurveyCount);
        Assert.True(wide.SurveyCount > summary.SurveyCount);
    }

    [MongoFact]
    public async Task A_dealer_cannot_read_another_dealers_summary_trend_or_leaderboard()
    {
        var me = DataScope.ForDealer("HND-1003");

        await Assert.ThrowsAsync<ForbiddenException>(() => Service.GetPerformanceAsync(me, "HND-1001", ThisMonth));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service.GetTrendAsync(me, "HND-1001", ThisMonth));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service.GetLeaderboardAsync(me, ThisMonth));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service.GetResponsesAsync(me, new ResponseQuery(ThisMonth, "HND-1001")));
    }

    [MongoFact]
    public async Task A_dealers_response_list_contains_only_its_own_responses()
    {
        var page = await Service.GetResponsesAsync(DataScope.ForDealer("HND-1003"), new ResponseQuery(SixMonths, PageSize: 200));

        Assert.Equal(Expected(SixMonths, "HND-1003").Count(), page.Total);
        Assert.All(page.Items, r => Assert.Equal("HND-1003", r.DealerId));
        Assert.Equal(page.Items.OrderByDescending(r => r.SubmittedAt).Select(r => r.Id), page.Items.Select(r => r.Id));
    }

    [MongoFact]
    public async Task Response_paging_filtering_and_sorting_work()
    {
        var query = new ResponseQuery(SixMonths, VehicleCondition: VehicleConditionAnswer.Defect, SortBy: "netScore", Descending: false, Page: 2, PageSize: 5);
        var page = await Service.GetResponsesAsync(DataScope.Admin, query);

        Assert.Equal(Expected(SixMonths).Count(r => r.VehicleCondition == VehicleConditionAnswer.Defect), page.Total);
        Assert.Equal(5, page.Items.Count);
        Assert.All(page.Items, r => Assert.Equal(VehicleConditionAnswer.Defect, r.VehicleCondition));
        Assert.Equal(page.Items.OrderBy(r => r.NetScore).Select(r => r.NetScore), page.Items.Select(r => r.NetScore));
        Assert.All(page.Items, r => Assert.NotEqual("Unknown", r.CustomerName));
    }

    [MongoFact]
    public async Task The_trend_buckets_add_up_to_the_period_total()
    {
        var trend = await Service.GetTrendAsync(DataScope.ForDealer("HND-1001"), null, SixMonths);
        Assert.Equal(Expected(SixMonths, "HND-1001").Count(), trend.Sum(t => t.SurveyCount));
        Assert.Equal(trend.OrderBy(t => t.Date).Select(t => t.Date), trend.Select(t => t.Date));
    }

    [MongoFact]
    public async Task The_seeded_data_set_matches_the_requested_shape()
    {
        Assert.Equal(10, await seeded.Db.Dealers.CountDocumentsAsync(FilterDefinition<Dealer>.Empty));
        Assert.Equal(1000, await seeded.Db.Customers.CountDocumentsAsync(FilterDefinition<Customer>.Empty));
        Assert.Equal(1000, await seeded.Db.SurveyResponses.CountDocumentsAsync(FilterDefinition<SurveyResponse>.Empty));

        var users = seeded.Data.Users;
        Assert.Single(users, u => u.Role == UserRole.Admin);
        Assert.Equal(10, users.Count(u => u.Role == UserRole.Dealer));
        Assert.Equal(users.Count, users.Select(u => u.Username).Distinct().Count());

        var responses = seeded.Data.Responses;
        Assert.All(responses, r => Assert.InRange(r.SubmittedAt, MongoSupport.SeedNow.AddMonths(-6), MongoSupport.SeedNow));
        Assert.Equal(10, responses.Select(r => r.DealerId).Distinct().Count());
        Assert.All(responses, r => Assert.Equal((r.RecommendScore + r.ConditionScore) / 2.0, r.NetScore));
    }
}

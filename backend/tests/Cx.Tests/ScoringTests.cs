using Cx.Core.Domain;
using Cx.Core.Scoring;

namespace Cx.Tests;

public class ScoringTests
{
    [Theory]
    [InlineData(RecommendAnswer.HighlyRecommend, 100)]
    [InlineData(RecommendAnswer.Recommend, 50)]
    [InlineData(RecommendAnswer.MightRecommend, 25)]
    [InlineData(RecommendAnswer.NotRecommend, 0)]
    public void Recommend_answers_map_to_the_specified_scores(RecommendAnswer answer, int expected) =>
        Assert.Equal(expected, SurveyScoring.Score(answer));

    [Theory]
    [InlineData(VehicleConditionAnswer.AllGood, 100)]
    [InlineData(VehicleConditionAnswer.PartMissing, 50)]
    [InlineData(VehicleConditionAnswer.Defect, 50)]
    [InlineData(VehicleConditionAnswer.PartMissingAndDefect, 0)]
    public void Condition_answers_map_to_the_specified_scores(VehicleConditionAnswer answer, int expected) =>
        Assert.Equal(expected, SurveyScoring.Score(answer));

    [Theory]
    [InlineData(RecommendAnswer.HighlyRecommend, VehicleConditionAnswer.AllGood, 100)]
    [InlineData(RecommendAnswer.HighlyRecommend, VehicleConditionAnswer.Defect, 75)]
    [InlineData(RecommendAnswer.Recommend, VehicleConditionAnswer.AllGood, 75)]
    [InlineData(RecommendAnswer.MightRecommend, VehicleConditionAnswer.PartMissing, 37.5)]
    [InlineData(RecommendAnswer.NotRecommend, VehicleConditionAnswer.PartMissingAndDefect, 0)]
    public void Net_score_is_the_average_of_both_questions(RecommendAnswer q1, VehicleConditionAnswer q2, double expected)
    {
        var response = SurveyScoring.Apply(new SurveyResponse { Recommend = q1, VehicleCondition = q2 });
        Assert.Equal(expected, response.NetScore);
    }

    [Fact]
    public void Dealer_score_is_total_net_score_divided_by_survey_count()
    {
        // surveys with net scores 100, 75, 0 -> 175 / 3
        Assert.Equal(58.33, SurveyScoring.DealerScore(175, 3));
        Assert.Null(SurveyScoring.DealerScore(0, 0));
    }
}

public class RankingTests
{
    private static (string Id, string Name, double Total, int Count) D(string id, double total, int count) => (id, id, total, count);

    [Fact]
    public void Highest_score_ranks_first()
    {
        var ranked = Ranking.Rank([D("A", 80, 1), D("B", 90, 1), D("C", 70, 1)]);
        Assert.Equal(["B", "A", "C"], ranked.Select(r => r.DealerId));
        Assert.Equal([1, 2, 3], ranked.Select(r => r.Rank!.Value));
    }

    [Fact]
    public void Ties_share_a_rank_and_the_next_rank_is_skipped()
    {
        var ranked = Ranking.Rank([D("A", 90, 1), D("B", 80, 1), D("C", 80, 1), D("D", 70, 1)]);
        Assert.Equal([1, 2, 2, 4], ranked.Select(r => r.Rank!.Value));
    }

    [Fact]
    public void Score_is_an_average_so_dealers_with_more_surveys_are_not_favoured()
    {
        var ranked = Ranking.Rank([D("Big", 1000, 20), D("Small", 90, 1)]); // 50 vs 90
        Assert.Equal("Small", ranked[0].DealerId);
    }

    [Fact]
    public void Dealers_without_surveys_are_unranked_and_last()
    {
        var ranked = Ranking.Rank([D("Empty", 0, 0), D("A", 60, 1)]);
        Assert.Equal("A", ranked[0].DealerId);
        Assert.Equal(1, ranked[0].Rank);
        Assert.Null(ranked[1].Rank);
        Assert.Null(ranked[1].Score);
    }
}

public class PeriodResolverTests
{
    private static readonly DateOnly Today = new(2026, 9, 20);

    [Theory]
    [InlineData(null, "2026-09-01", "2026-09-20")]
    [InlineData("this_month", "2026-09-01", "2026-09-20")]
    [InlineData("last_month", "2026-08-01", "2026-08-31")]
    [InlineData("this_quarter", "2026-07-01", "2026-09-20")]
    [InlineData("last_quarter", "2026-04-01", "2026-06-30")]
    [InlineData("last_30_days", "2026-08-22", "2026-09-20")]
    [InlineData("this_year", "2026-01-01", "2026-09-20")]
    public void Named_periods_resolve_relative_to_today(string? period, string start, string end)
    {
        var range = PeriodResolver.Resolve(period, null, null, Today);
        Assert.Equal(DateOnly.Parse(start), range.Start);
        Assert.Equal(DateOnly.Parse(end), range.End);
    }

    [Fact]
    public void Quarter_math_handles_the_first_quarter_of_the_year()
    {
        var range = PeriodResolver.Resolve("last_quarter", null, null, new DateOnly(2026, 2, 10));
        Assert.Equal(new DateOnly(2025, 10, 1), range.Start);
        Assert.Equal(new DateOnly(2025, 12, 31), range.End);
    }

    [Fact]
    public void Explicit_dates_win_over_a_named_period()
    {
        var range = PeriodResolver.Resolve("last_month", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), Today);
        Assert.Equal(5, range.Days);
        Assert.Equal(new DateOnly(2026, 1, 5), range.Start);
    }

    [Fact]
    public void A_missing_end_date_defaults_to_today()
    {
        var range = PeriodResolver.Resolve(null, new DateOnly(2026, 9, 10), null, Today);
        Assert.Equal(Today, range.End);
    }

    [Fact]
    public void Previous_range_has_the_same_length_and_ends_the_day_before()
    {
        var range = new DateRange(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 20));
        var previous = range.Previous();
        Assert.Equal(range.Days, previous.Days);
        Assert.Equal(new DateOnly(2026, 8, 31), previous.End);
    }

    [Fact]
    public void Utc_bounds_are_half_open_so_the_end_date_is_inclusive()
    {
        var (from, to) = new DateRange(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 20)).UtcBounds();
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc), to);
    }

    [Fact]
    public void Invalid_input_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => PeriodResolver.Resolve(null, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 1), Today));
        Assert.Throws<ArgumentException>(() => PeriodResolver.Resolve("next_week", null, null, Today));
        Assert.Throws<ArgumentException>(() => PeriodResolver.Resolve(null, new DateOnly(2020, 1, 1), Today, Today));
    }
}

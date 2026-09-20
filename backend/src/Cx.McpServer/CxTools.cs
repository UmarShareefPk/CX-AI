using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cx.Core.Rag;
using Cx.Core.Scoring;
using Cx.Core.Security;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Cx.McpServer;

/// <summary>
/// The tools the agent can call. None of them takes a "who am I" argument: the data scope is fixed for the process (see
/// <see cref="ScopeOptions"/>). Dealer scopes cannot address another dealer; only an admin scope may pass a dealerCode.
/// </summary>
[McpServerToolType]
public sealed class CxTools(ScoreService scores, PolicySearch policy, DataScope scope, TimeProvider clock)
{
    private const string PeriodHelp =
        "Named period: this_month, last_month, this_quarter, last_quarter, last_30_days, last_90_days, last_6_months or this_year. Default this_month.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    [McpServerTool(Name = "get_dealer_score", ReadOnly = true)]
    [Description("Customer-satisfaction score (0-100) for the dealership over a period: the average net score of all surveys in the period. " +
        "Also returns the survey count, the change versus the previous period of equal length, the average of each of the two survey questions " +
        "and how many customers gave each answer (which shows whether recommend or vehicle condition loses more points).")]
    public Task<string> GetDealerScore(
        [Description(PeriodHelp)] string? period = null,
        [Description("Start date yyyy-MM-dd. Overrides period when given.")] string? startDate = null,
        [Description("End date yyyy-MM-dd (inclusive). Defaults to today.")] string? endDate = null,
        [Description("Administrators only: dealer code such as HND-1003. Omit for the whole network.")] string? dealerCode = null,
        CancellationToken ct = default) => Guard(async () =>
    {
        var range = ResolveRange(period, startDate, endDate);
        var p = await scores.GetPerformanceAsync(scope, dealerCode, range, ct);

        var weakest = (p.AvgRecommendScore, p.AvgConditionScore) switch
        {
            (null, _) or (_, null) => null,
            var (r, c) when r < c => "recommend (customers are not answering 'Highly recommend' often enough)",
            var (r, c) when c < r => "vehicleCondition (customers are reporting missing parts or defects)",
            _ => "both equal",
        };

        return Serialize(new
        {
            dealer = Describe(p.DealerId, p.DealerName),
            period = Period(range),
            surveyCount = p.SurveyCount,
            score = p.Score,
            previousPeriodScore = p.PreviousScore,
            changeVsPreviousPeriod = p.ScoreChange,
            averageRecommendScore = p.AvgRecommendScore,
            averageVehicleConditionScore = p.AvgConditionScore,
            weakestArea = weakest,
            recommendAnswers = p.RecommendBreakdown,
            vehicleConditionAnswers = p.ConditionBreakdown,
            note = p.SurveyCount == 0 ? "No surveys were submitted in this period, so there is no score." : null,
        });
    });

    [McpServerTool(Name = "get_dealer_rank", ReadOnly = true)]
    [Description("Rank of the dealership among all dealers for a period (1 = best, ranked by score), how many dealers are ranked, " +
        "the top dealer's score as a benchmark and the gap to it.")]
    public Task<string> GetDealerRank(
        [Description(PeriodHelp)] string? period = null,
        [Description("Start date yyyy-MM-dd. Overrides period when given.")] string? startDate = null,
        [Description("End date yyyy-MM-dd (inclusive). Defaults to today.")] string? endDate = null,
        [Description("Administrators only: dealer code such as HND-1003 (required for administrators).")] string? dealerCode = null,
        CancellationToken ct = default) => Guard(async () =>
    {
        var range = ResolveRange(period, startDate, endDate);
        var id = scope.ResolveDealer(dealerCode) ?? throw new McpException("Administrators must pass dealerCode to get a dealer's rank.");
        var p = await scores.GetPerformanceAsync(scope, id, range, ct);

        return Serialize(new
        {
            dealer = Describe(p.DealerId, p.DealerName),
            period = Period(range),
            rank = p.Rank,
            rankedDealers = p.RankedDealerCount,
            score = p.Score,
            topScore = p.TopScore,
            gapToTop = p.Score is not null && p.TopScore is not null ? Math.Round(p.TopScore.Value - p.Score.Value, 2) : (double?)null,
            isTopRanked = p.Rank == 1,
            surveyCount = p.SurveyCount,
            note = p.Rank is null ? "The dealer has no surveys in this period, so it is not ranked." : null,
        });
    });

    [McpServerTool(Name = "get_top_ranked_score", ReadOnly = true)]
    [Description("The score of the highest ranked dealer (rank 1) in the network for a period, as a benchmark. " +
        "Dealers get the score only; the dealer's identity is shown to administrators only.")]
    public Task<string> GetTopRankedScore(
        [Description(PeriodHelp)] string? period = null,
        [Description("Start date yyyy-MM-dd. Overrides period when given.")] string? startDate = null,
        [Description("End date yyyy-MM-dd (inclusive). Defaults to today.")] string? endDate = null,
        CancellationToken ct = default) => Guard(async () =>
    {
        var range = ResolveRange(period, startDate, endDate);
        var p = await scores.GetPerformanceAsync(scope, null, range, ct);
        return Serialize(new
        {
            period = Period(range),
            topScore = p.TopScore,
            topDealer = p.TopDealerName, // null (omitted) for dealers; tied dealers are joined with " & "
            rankedDealers = p.RankedDealerCount,
            note = p.TopScore is null ? "No dealer has surveys in this period." : null,
        });
    });

    [McpServerTool(Name = "get_leaderboard", ReadOnly = true)]
    [Description("ADMINISTRATORS ONLY. Every dealer ranked by score for a period, with dealer codes, names and survey counts.")]
    public Task<string> GetLeaderboard(
        [Description(PeriodHelp)] string? period = null,
        [Description("Start date yyyy-MM-dd. Overrides period when given.")] string? startDate = null,
        [Description("End date yyyy-MM-dd (inclusive). Defaults to today.")] string? endDate = null,
        CancellationToken ct = default) => Guard(async () =>
    {
        var range = ResolveRange(period, startDate, endDate);
        var board = await scores.GetLeaderboardAsync(scope, range, ct);
        return Serialize(new
        {
            period = Period(range),
            dealers = board.Select(d => new { rank = d.Rank, dealerCode = d.DealerId, name = d.DealerName, score = d.Score, surveyCount = d.SurveyCount }),
        });
    });

    [McpServerTool(Name = "get_score_trend", ReadOnly = true)]
    [Description("How the score moved over the period: one point per day (ranges up to 45 days) or per week, each with the number of surveys.")]
    public Task<string> GetScoreTrend(
        [Description(PeriodHelp)] string? period = null,
        [Description("Start date yyyy-MM-dd. Overrides period when given.")] string? startDate = null,
        [Description("End date yyyy-MM-dd (inclusive). Defaults to today.")] string? endDate = null,
        [Description("Administrators only: dealer code such as HND-1003. Omit for the whole network.")] string? dealerCode = null,
        CancellationToken ct = default) => Guard(async () =>
    {
        var range = ResolveRange(period, startDate, endDate);
        var trend = await scores.GetTrendAsync(scope, dealerCode, range, ct);
        return Serialize(new
        {
            period = Period(range),
            granularity = range.Days <= 45 ? "day" : "week (week start, Monday)",
            points = trend.Select(t => new { date = t.Date.ToString("yyyy-MM-dd"), score = t.Score, surveys = t.SurveyCount }),
        });
    });

    [McpServerTool(Name = "search_policy", ReadOnly = true)]
    [Description("Semantic search over the platform's policy documents: how scores and ranks are calculated, reporting periods, " +
        "how to improve satisfaction, defects and missing parts, survey rules and data privacy. Use it for any 'how' or 'why' or policy question.")]
    public Task<string> SearchPolicy(
        [Description("What to look up, phrased as a natural-language question or topic.")] string query,
        [Description("How many passages to return (1-6). Default 4.")] int topK = 4,
        CancellationToken ct = default) => Guard(async () =>
    {
        var hits = await policy.SearchAsync(query, Math.Clamp(topK, 1, 6), ct);
        return Serialize(new
        {
            query,
            passages = hits.Select(h => new { title = h.Title, relevance = h.Score, text = h.Text }),
        });
    });

    private DateRange ResolveRange(string? period, string? startDate, string? endDate) =>
        PeriodResolver.Resolve(period, ParseDate(startDate, nameof(startDate)), ParseDate(endDate, nameof(endDate)),
            DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

    private static DateOnly? ParseDate(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new McpException($"{name} must be a date formatted yyyy-MM-dd, got '{value}'.");
    }

    private static object Period(DateRange r) => new { start = r.Start.ToString("yyyy-MM-dd"), end = r.End.ToString("yyyy-MM-dd"), days = r.Days };

    private static string Describe(string? id, string? name) => id is null ? "All dealers (network-wide)" : $"{name} ({id})";

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    /// <summary>Only McpException messages reach the model; everything else would be reported as an opaque failure.</summary>
    private static async Task<string> Guard(Func<Task<string>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is ForbiddenException or ArgumentException)
        {
            throw new McpException(ex.Message, ex);
        }
    }
}

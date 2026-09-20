using System.Security.Claims;
using Cx.Api.Infrastructure;
using Cx.Core.Domain;
using Cx.Core.Scoring;

namespace Cx.Api.Endpoints;

/// <param name="Start">Inclusive start date (yyyy-MM-dd). Omit both dates for the current month to date.</param>
/// <param name="End">Inclusive end date (yyyy-MM-dd).</param>
/// <param name="DealerId">Administrators only: narrow to one dealer. Dealers may only pass their own id.</param>
public sealed record DateQuery(DateOnly? Start, DateOnly? End, string? DealerId);

public sealed record DealerDto(string Id, string Name, string City, string State, string Region);

public static class DataEndpoints
{
    public static void MapData(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Data");

        api.MapGet("/dashboard/summary", async ([AsParameters] DateQuery q, ClaimsPrincipal user, ScoreService scores, TimeProvider clock, CancellationToken ct) =>
            Results.Ok(await scores.GetPerformanceAsync(user.ToScope(), q.DealerId, Range(q, clock), ct)));

        api.MapGet("/dashboard/trend", async ([AsParameters] DateQuery q, ClaimsPrincipal user, ScoreService scores, TimeProvider clock, CancellationToken ct) =>
            Results.Ok(await scores.GetTrendAsync(user.ToScope(), q.DealerId, Range(q, clock), ct)));

        api.MapGet("/leaderboard", async ([AsParameters] DateQuery q, ClaimsPrincipal user, ScoreService scores, TimeProvider clock, CancellationToken ct) =>
        {
            var range = Range(q, clock);
            return Results.Ok(new { period = range, dealers = await scores.GetLeaderboardAsync(user.ToScope(), range, ct) });
        }).RequireAuthorization(Policies.Admin);

        api.MapGet("/dealers", async (ClaimsPrincipal user, ScoreService scores, CancellationToken ct) =>
            Results.Ok((await scores.GetDealersAsync(user.ToScope(), ct)).Select(d => new DealerDto(d.Id, d.Name, d.City, d.State, d.Region))));

        api.MapGet("/responses", async (
            [AsParameters] DateQuery q, RecommendAnswer? recommend, VehicleConditionAnswer? condition, string? sortBy, bool? desc,
            int? page, int? pageSize, ClaimsPrincipal user, ScoreService scores, TimeProvider clock, CancellationToken ct) =>
        {
            var query = new ResponseQuery(Range(q, clock), q.DealerId, recommend, condition, sortBy ?? "submittedAt", desc ?? true, page ?? 1, pageSize ?? 25);
            return Results.Ok(await scores.GetResponsesAsync(user.ToScope(), query, ct));
        });
    }

    private static DateRange Range(DateQuery q, TimeProvider clock) =>
        PeriodResolver.Resolve(null, q.Start, q.End, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
}

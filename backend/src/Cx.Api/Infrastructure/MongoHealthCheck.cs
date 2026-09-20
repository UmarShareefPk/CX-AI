using Cx.Core.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cx.Api.Infrastructure;

public sealed class MongoHealthCheck(CxDatabase db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await db.PingAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB is not reachable.", ex);
        }
    }
}

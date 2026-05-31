using Microsoft.Extensions.Diagnostics.HealthChecks;
using HRNexus.Infrastructure.Persistence;

namespace HRNexus.Api.Health;

/// <summary>
/// Readiness probe — verifies the database is reachable. Tagged "ready" so it's included in
/// <c>/health/ready</c> (orchestration readiness gating) but excluded from the dependency-free
/// <c>/health/live</c> liveness probe. Distinct from the existing <c>/api/health</c> endpoint,
/// which the maintenance-mode flow polls and must stay unchanged.
/// </summary>
public sealed class DbReadyHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy("Database reachable.")
                : HealthCheckResult.Unhealthy("Database unreachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database probe threw.", ex);
        }
    }
}

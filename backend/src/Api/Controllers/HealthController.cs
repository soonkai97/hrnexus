using HRNexus.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController(IAppDbContext db, IConfiguration config) : ControllerBase
{
    // Build stamp injected as env at image build time (see backend/Dockerfile).
    private string Version => config["Build:Version"] ?? "dev";

    /// <summary>
    /// Liveness + readiness probe.
    /// Returns 200 only when the API can reach the database.
    /// Returns 503 when the database is unreachable so that monitoring tools
    /// and Docker health-checks can stop routing traffic to this instance.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try
        {
            // Cheapest possible round-trip — just checks the connection is alive.
            await db.Staff.AnyAsync(ct);
            return Ok(new { status = "healthy", utc = DateTime.UtcNow, version = Version });
        }
        catch
        {
            return StatusCode(503, new { status = "unhealthy", utc = DateTime.UtcNow, version = Version });
        }
    }
}

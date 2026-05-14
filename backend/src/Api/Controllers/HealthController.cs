using Microsoft.AspNetCore.Mvc;

namespace NightclubWorkforce.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "healthy", utc = DateTime.UtcNow });
}

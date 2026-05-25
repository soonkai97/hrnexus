using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HRNexus.Application.FeatureFlags.Queries;

namespace HRNexus.Api.Controllers;

[ApiController]
[Route("api/feature-flags")]
[Authorize]
public class FeatureFlagsController(IMediator mediator) : ControllerBase
{
    /// <summary>Returns all feature flags. Any authenticated user can read them.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(await mediator.Send(new GetFeatureFlagsQuery(), ct));
}

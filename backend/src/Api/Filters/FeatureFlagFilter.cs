using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Api.Filters;

/// <summary>
/// Global action filter that enforces server-side feature flag checks.
/// Only runs on actions/controllers decorated with <see cref="FeatureRequiredAttribute"/>.
/// Blocks the request with 403 Forbidden if none of the required flags are enabled.
/// </summary>
public class FeatureFlagFilter(IAppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var keys = ctx.ActionDescriptor.EndpointMetadata
            .OfType<FeatureRequiredAttribute>()
            .SelectMany(a => a.Keys)
            .Distinct()
            .ToArray();

        if (keys.Length == 0)
        {
            await next();
            return;
        }

        var ct = ctx.HttpContext.RequestAborted;
        var anyEnabled = await db.FeatureFlags
            .Where(f => keys.Contains(f.Key) && f.IsEnabled)
            .AnyAsync(ct);

        if (!anyEnabled)
        {
            ctx.Result = new ObjectResult(new { error = "This feature is not available." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}

using System.Text.Json;
using NightclubWorkforce.Application.Common;

namespace NightclubWorkforce.Api.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            ctx.Response.StatusCode = ex switch
            {
                UnauthorizedAccessException => 401,
                TooManyRequestsException    => 429,
                _                           => 500,
            };
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }
}

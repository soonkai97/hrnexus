using System.Text.Json;
using HRNexus.Application.Common;

namespace HRNexus.Api.Middleware;

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

            // Known/intentional exceptions carry curated, user-safe messages — surface them
            // with the right status. Anything else is an unexpected fault: return a generic
            // message so we never leak raw exception text or DB internals to the client.
            // (The full exception is always logged above for diagnostics.)
            var (status, message) = ex switch
            {
                UnauthorizedAccessException => (401, ex.Message),
                TooManyRequestsException    => (429, ex.Message),
                ArgumentException           => (400, ex.Message),
                KeyNotFoundException        => (404, ex.Message),
                InvalidOperationException   => (409, ex.Message),
                _                           => (500, "An unexpected error occurred. Please try again."),
            };

            ctx.Response.StatusCode  = status;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }));
        }
    }
}

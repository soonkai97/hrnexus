using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HRNexus.Application.Common.Behaviors;

/// <summary>
/// MediatR pipeline behavior that logs every request's name and elapsed time, and logs a warning
/// (with the exception) when a handler throws. One central place for request-level observability —
/// the natural home for future metrics/tracing — without scattering Stopwatch/try-catch into handlers.
/// </summary>
public class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            stopwatch.Stop();
            logger.LogInformation("{Request} handled in {ElapsedMs} ms", requestName, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "{Request} failed after {ElapsedMs} ms: {Message}",
                requestName, stopwatch.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }
}

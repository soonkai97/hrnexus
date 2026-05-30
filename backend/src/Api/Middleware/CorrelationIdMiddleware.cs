namespace HRNexus.Api.Middleware;

/// <summary>
/// Assigns a correlation id to every request so a single user action can be traced across
/// logs (and, in a later phase, the frontend → API → log line).
/// - Reuses an incoming <c>X-Correlation-Id</c> header if the caller supplied one, otherwise
///   generates a new id.
/// - Stashes it in <see cref="HttpContext.Items"/> so <c>HttpContextEnricher</c> can stamp it on
///   every log event for the request.
/// - Echoes it back in the <c>X-Correlation-Id</c> response header (even on error responses) so
///   the client can surface/report it.
/// Registered first in the pipeline so the id is available before anything else logs.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey    = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId =
            context.Request.Headers.TryGetValue(HeaderName, out var incoming) &&
            !string.IsNullOrWhiteSpace(incoming)
                ? incoming.ToString()
                : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;

        // OnStarting so the header is written even when a downstream middleware (e.g. the
        // exception handler) produces the response.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await next(context);
    }
}

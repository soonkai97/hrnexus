using HRNexus.Api.Middleware;
using Serilog.Core;
using Serilog.Events;

namespace HRNexus.Api.Logging;

/// <summary>
/// Stamps every log event emitted during a request with its <c>CorrelationId</c> (set by
/// <see cref="CorrelationIdMiddleware"/>) and, once authenticated, the caller's <c>StaffId</c>.
/// <para>
/// Reads from an <see cref="HttpContextAccessor"/> instance — its backing store is a static
/// <c>AsyncLocal</c>, so a freshly-constructed accessor still sees the current request's context.
/// That lets this enricher be added to the bootstrap <c>Log.Logger</c> via
/// <c>.Enrich.With&lt;HttpContextEnricher&gt;()</c> without DI plumbing. Requires
/// <c>AddHttpContextAccessor()</c> so the framework populates the AsyncLocal per request.
/// </para>
/// Because it reads lazily at log time, events written after authentication include the StaffId
/// even though the correlation id was assigned earlier in the pipeline.
/// </summary>
public sealed class HttpContextEnricher : ILogEventEnricher
{
    private readonly HttpContextAccessor _accessor = new();

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var context = _accessor.HttpContext;
        if (context is null) return;

        if (context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var raw) &&
            raw is string correlationId && correlationId.Length > 0)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", correlationId));
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var staffId = context.User.FindFirst("staffId")?.Value;
            if (!string.IsNullOrEmpty(staffId))
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("StaffId", staffId));
        }
    }
}

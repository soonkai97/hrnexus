using Microsoft.Extensions.Options;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Infrastructure.Services;

public class DateTimeProvider : IDateTimeProvider
{
    private readonly TimeZoneInfo _tz;

    public DateTimeProvider(IOptions<VenueOptions> opts)
    {
        // FindSystemTimeZoneById accepts IANA IDs on Linux/macOS and Windows IDs on Windows.
        // We store IANA IDs in config; .NET 6+ handles both on all platforms.
        _tz = TimeZoneInfo.FindSystemTimeZoneById(opts.Value.TimeZoneId);
    }

    // ── UTC ──────────────────────────────────────────────────────────────────
    public DateTime UtcNow   => DateTime.UtcNow;
    public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);

    // ── Venue-local ──────────────────────────────────────────────────────────
    public TimeZoneInfo VenueTimeZone => _tz;

    public DateTime LocalNow =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);
}

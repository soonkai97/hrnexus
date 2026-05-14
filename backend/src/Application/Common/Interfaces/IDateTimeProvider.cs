namespace NightclubWorkforce.Application.Common.Interfaces;

public interface IDateTimeProvider
{
    // ── UTC (raw) ────────────────────────────────────────────────────────────
    DateTime UtcNow   { get; }
    DateOnly TodayUtc { get; }

    // ── Venue-local helpers ──────────────────────────────────────────────────

    /// <summary>The configured venue timezone (e.g. Asia/Kuala_Lumpur).</summary>
    TimeZoneInfo VenueTimeZone { get; }

    /// <summary>Current wall-clock time at the venue.</summary>
    DateTime LocalNow { get; }
}

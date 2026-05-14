namespace NightclubWorkforce.Application.Common;

/// <summary>
/// Helpers to avoid DateTime Kind=Unspecified errors with PostgreSQL timestamptz columns.
/// </summary>
public static class DateTimeExtensions
{
    /// <summary>
    /// Returns the date portion of <paramref name="dt"/> as a UTC midnight DateTime.
    /// Use whenever calling .Date on a value that will be compared in an EF Core query
    /// against a PostgreSQL 'timestamp with time zone' column.
    /// </summary>
    public static DateTime AsUtcDate(this DateTime dt)
        => DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
}

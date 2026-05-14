namespace NightclubWorkforce.Application.Common;

public class VenueOptions
{
    public const string SectionName = "Venue";

    /// <summary>
    /// IANA timezone identifier for the venue (e.g. "Asia/Kuala_Lumpur").
    /// Used to determine "today" and to convert HH:mm shift times to UTC.
    /// Must be a valid IANA ID — these work on both Linux (Docker) and Windows.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";
}

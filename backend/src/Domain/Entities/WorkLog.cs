using HRNexus.Domain.Common;
using HRNexus.Domain.Constants;

namespace HRNexus.Domain.Entities;

public class WorkLog : IHasAudit, ISoftDelete
{
    public Guid      Id               { get; private set; }
    public Guid      EmployeeId       { get; private set; }
    public string    RoleName         { get; private set; } = default!;
    public string    LocationId       { get; private set; } = default!;
    public string    LocationName     { get; private set; } = default!;
    public DateOnly  WorkDate         { get; private set; }
    public string    StartTime        { get; private set; } = default!; // HH:mm
    public DateOnly? EndDate          { get; private set; }             // null = active; set when ended (may differ from WorkDate for overnight / multi-day)
    public string?   EndTime          { get; private set; }             // null = active live shift
    public bool      IsManualEntry    { get; private set; }
    public string?   Notes            { get; private set; }
    // GPS captured from the staff member's device when they clocked in live (null if denied/unavailable
    // or for manual/back-dated entries). Used to show where the staff member physically started the shift.
    public double?   StartLatitude    { get; private set; }
    public double?   StartLongitude   { get; private set; }
    // Latest GPS the staff member pushed via "Update location" during the shift (null until they do).
    // The dashboard shows this in preference to the clock-in position so admins see where they are now.
    public double?   LastLatitude     { get; private set; }
    public double?   LastLongitude    { get; private set; }
    public DateTime? LastLocationAt   { get; private set; }
    public decimal   HourlyRateSnapshot { get; private set; }
    /// <summary>The role's pay type captured at clock-in ("hourly" | "fixed"), so the pay model used
    /// for this shift is fixed in history even if the job's pay type changes later.</summary>
    public string    PayTypeSnapshot  { get; private set; } = AppConstants.PayType.Hourly;
    public bool      IsEdited         { get; private set; }
    public DateTime? EditedAt         { get; private set; }
    public DateTime  CreatedAt        { get; private set; }
    public string    CreatedBy        { get; private set; } = string.Empty;
    public DateTime? ModifiedAt       { get; private set; }
    public string?   ModifiedBy       { get; private set; }
    public bool      IsDeleted        { get; private set; }
    public DateTime? DeletedAt        { get; private set; }
    public string?   DeletedBy        { get; private set; }
    public Staff?    Employee         { get; private set; }

    private WorkLog() { }

    public static WorkLog StartLive(Guid employeeId, string roleName, string locationId, string locationName,
        DateOnly workDate, string startTime, decimal rateSnapshot, string? notes = null,
        double? latitude = null, double? longitude = null, string payType = AppConstants.PayType.Hourly) => new()
    {
        Id                 = Guid.NewGuid(),
        EmployeeId         = employeeId,
        RoleName           = roleName.Trim().ToUpperInvariant(),
        LocationId         = locationId,
        LocationName       = locationName,
        WorkDate           = workDate,
        StartTime          = startTime,
        EndTime            = null,
        IsManualEntry      = false,
        Notes              = notes,
        StartLatitude      = latitude,
        StartLongitude     = longitude,
        HourlyRateSnapshot = rateSnapshot,
        PayTypeSnapshot    = payType,
        CreatedAt          = DateTime.UtcNow,
    };

    public static WorkLog CreateManual(Guid employeeId, string roleName, string locationId, string locationName, DateOnly workDate, string startTime, DateOnly endDate, string endTime, decimal rateSnapshot, string? notes = null, string payType = AppConstants.PayType.Hourly) => new()
    {
        Id                 = Guid.NewGuid(),
        EmployeeId         = employeeId,
        RoleName           = roleName.Trim().ToUpperInvariant(),
        LocationId         = locationId,
        LocationName       = locationName,
        WorkDate           = workDate,
        StartTime          = startTime,
        EndDate            = endDate,
        EndTime            = endTime,
        IsManualEntry      = true,
        Notes              = notes,
        HourlyRateSnapshot = rateSnapshot,
        PayTypeSnapshot    = payType,
        CreatedAt          = DateTime.UtcNow,
    };

    /// <summary>Overload for callers that don't track an explicit end date — the end date is inferred
    /// as the next day when the end time is at/before the start (single overnight, ≤24h).</summary>
    public static WorkLog CreateManual(Guid employeeId, string roleName, string locationId, string locationName,
        DateOnly workDate, string startTime, string endTime, decimal rateSnapshot, string? notes = null,
        string payType = AppConstants.PayType.Hourly) =>
        CreateManual(employeeId, roleName, locationId, locationName, workDate, startTime,
            InferEndDate(workDate, startTime, endTime), endTime, rateSnapshot, notes, payType);

    /// <summary>The next day if the end time is at/before the start (overnight), else the same day.</summary>
    private static DateOnly InferEndDate(DateOnly workDate, string startTime, string endTime) =>
        TimeOnly.Parse(endTime) <= TimeOnly.Parse(startTime) ? workDate.AddDays(1) : workDate;

    /// <summary>Records a fresh GPS fix the staff member pushed during the shift (on-demand "Update location").</summary>
    public void UpdateLocation(double latitude, double longitude, DateTime at)
    {
        LastLatitude   = latitude;
        LastLongitude  = longitude;
        LastLocationAt = at;
    }

    /// <summary>Re-snapshot the pay type — used when an admin changes a shift's role to one with a
    /// different pay model.</summary>
    public void SetPayTypeSnapshot(string payType) => PayTypeSnapshot = payType;

    public void End(DateOnly endDate, string endTime) { EndDate = endDate; EndTime = endTime; }

    /// <summary>Overload that infers the end date (overnight) from the shift's own start.</summary>
    public void End(string endTime) => End(InferEndDate(WorkDate, StartTime, endTime), endTime);

    /// <summary>Overload that infers the end date (overnight) from workDate + times.</summary>
    public void Update(string roleName, string locationId, string locationName,
        DateOnly workDate, string startTime, string endTime, string? notes, decimal? rateSnapshot = null) =>
        Update(roleName, locationId, locationName, workDate, startTime,
            InferEndDate(workDate, startTime, endTime), endTime, notes, rateSnapshot);

    public void Update(string roleName, string locationId, string locationName,
        DateOnly workDate, string startTime, DateOnly endDate, string endTime, string? notes,
        decimal? rateSnapshot = null)
    {
        RoleName     = roleName.Trim().ToUpperInvariant();
        LocationId   = locationId;
        LocationName = locationName;
        WorkDate     = workDate;
        StartTime    = startTime;
        EndDate      = endDate;
        EndTime      = endTime;
        Notes        = notes;
        // Rate is system-derived, never hand-edited: the caller passes the new role's current rate
        // ONLY when the role actually changed. A time/notes-only edit passes null so an unrelated
        // edit never silently re-prices a historical shift.
        if (rateSnapshot.HasValue) HourlyRateSnapshot = rateSnapshot.Value;
        IsEdited     = true;
        EditedAt     = DateTime.UtcNow;
    }
}

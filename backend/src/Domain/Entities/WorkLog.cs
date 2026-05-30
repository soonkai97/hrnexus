using HRNexus.Domain.Common;

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
    public string?   EndTime          { get; private set; }             // null = active live shift
    public bool      IsManualEntry    { get; private set; }
    public string?   Notes            { get; private set; }
    public decimal   HourlyRateSnapshot { get; private set; }
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
        DateOnly workDate, string startTime, decimal rateSnapshot, string? notes = null) => new()
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
        HourlyRateSnapshot = rateSnapshot,
        CreatedAt          = DateTime.UtcNow,
    };

    public static WorkLog CreateManual(Guid employeeId, string roleName, string locationId, string locationName, DateOnly workDate, string startTime, string endTime, decimal rateSnapshot, string? notes = null) => new()
    {
        Id                 = Guid.NewGuid(),
        EmployeeId         = employeeId,
        RoleName           = roleName.Trim().ToUpperInvariant(),
        LocationId         = locationId,
        LocationName       = locationName,
        WorkDate           = workDate,
        StartTime          = startTime,
        EndTime            = endTime,
        IsManualEntry      = true,
        Notes              = notes,
        HourlyRateSnapshot = rateSnapshot,
        CreatedAt          = DateTime.UtcNow,
    };

    public void End(string endTime) => EndTime = endTime;

    public void Update(string roleName, string locationId, string locationName,
        DateOnly workDate, string startTime, string endTime, string? notes,
        decimal? rateSnapshot = null)
    {
        RoleName     = roleName.Trim().ToUpperInvariant();
        LocationId   = locationId;
        LocationName = locationName;
        WorkDate     = workDate;
        StartTime    = startTime;
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

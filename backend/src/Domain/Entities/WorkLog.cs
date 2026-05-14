namespace NightclubWorkforce.Domain.Entities;

public class WorkLog
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
    public DateTime  CreatedAt        { get; private set; }
    public Staff?    Employee         { get; private set; }

    private WorkLog() { }

    public static WorkLog StartLive(Guid employeeId, string roleName, string locationId, string locationName,
        DateOnly workDate, string startTime, decimal rateSnapshot, string? notes = null) => new()
    {
        Id                 = Guid.NewGuid(),
        EmployeeId         = employeeId,
        RoleName           = roleName.ToLowerInvariant(),
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
        RoleName           = roleName.ToLowerInvariant(),
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
    public void UpdateNotes(string? notes) => Notes = notes;
}

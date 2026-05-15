using NightclubWorkforce.Domain.Constants;

namespace NightclubWorkforce.Domain.Entities;

public class LeaveRequest
{
    public Guid     Id         { get; private set; }
    public Guid     EmployeeId { get; private set; }
    public string   LeaveType  { get; private set; } = default!; // sick | annual | unpaid | no-show
    public DateTime StartDate  { get; private set; }
    public DateTime EndDate    { get; private set; }
    public string?  Notes           { get; private set; }
    public string   Status          { get; private set; } = default!; // pending | approved | rejected
    public DateTime CreatedAt       { get; private set; }

    public Staff Employee { get; private set; } = default!;

    private LeaveRequest() { }

    public static LeaveRequest Create(Guid employeeId, string leaveType,
        DateTime startDate, DateTime endDate, string? notes) =>
        new()
        {
            Id         = Guid.NewGuid(),
            EmployeeId = employeeId,
            LeaveType  = leaveType.ToLower(),
            StartDate  = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc),
            EndDate    = DateTime.SpecifyKind(endDate.Date,   DateTimeKind.Utc),
            Notes      = notes,
            Status     = AppConstants.Leave.Pending,
            CreatedAt  = DateTime.UtcNow,
        };

    public void Update(string leaveType, DateTime startDate, DateTime endDate,
        string? notes, string status)
    {
        LeaveType = leaveType.ToLower();
        StartDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        EndDate   = DateTime.SpecifyKind(endDate.Date,   DateTimeKind.Utc);
        Notes     = notes;
        Status    = status.ToLower();
    }

    public void SetStatus(string status) => Status = status.ToLower();

    public void Cancel() => Status = AppConstants.Leave.Cancelled;

    /// <summary>Auto-reject this leave and stamp a system note onto it.</summary>
    public void AutoReject(string systemNote)
    {
        Status = AppConstants.Leave.Rejected;
        Notes  = string.IsNullOrWhiteSpace(Notes)
            ? systemNote
            : $"{Notes} | {systemNote}";
    }
}

using HRNexus.Domain.Constants;

namespace HRNexus.Domain.Entities;

public class LeaveRequest
{
    public Guid     Id         { get; private set; }
    public Guid     EmployeeId { get; private set; }
    public string   LeaveType  { get; private set; } = default!; // sick | annual | unpaid | no-show
    public DateTime StartDate  { get; private set; }
    public DateTime EndDate    { get; private set; }
    public string?  StartTime  { get; private set; }  // HH:mm, nullable
    public string?  EndTime    { get; private set; }  // HH:mm, nullable
    public decimal? Hours      { get; private set; }  // explicit hours, null for pre-migration records
    public string?  Notes            { get; private set; }
    public string?  RejectionReason  { get; private set; }  // set by manager when rejecting
    public string   Status           { get; private set; } = default!; // pending | approved | rejected | cancelled
    public DateTime CreatedAt        { get; private set; }

    public Staff Employee { get; private set; } = default!;

    private LeaveRequest() { }

    public static LeaveRequest Create(Guid employeeId, string leaveType,
        DateTime startDate, DateTime endDate,
        string? startTime, string? endTime, decimal? hours,
        string? notes) =>
        new()
        {
            Id         = Guid.NewGuid(),
            EmployeeId = employeeId,
            LeaveType  = leaveType.ToLower(),
            StartDate  = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc),
            EndDate    = DateTime.SpecifyKind(endDate.Date,   DateTimeKind.Utc),
            StartTime  = startTime,
            EndTime    = endTime,
            Hours      = hours,
            Notes      = notes,
            Status     = AppConstants.Leave.Pending,
            CreatedAt  = DateTime.UtcNow,
        };

    public void Update(string leaveType, DateTime startDate, DateTime endDate,
        string? startTime, string? endTime, decimal? hours,
        string? notes, string status, string? rejectionReason = null)
    {
        LeaveType       = leaveType.ToLower();
        StartDate       = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        EndDate         = DateTime.SpecifyKind(endDate.Date,   DateTimeKind.Utc);
        StartTime       = startTime;
        EndTime         = endTime;
        Hours           = hours;
        Notes           = notes;
        Status          = status.ToLower();
        RejectionReason = status.Equals(AppConstants.Leave.Rejected, StringComparison.OrdinalIgnoreCase)
            ? rejectionReason
            : null;  // clear rejection reason when status is no longer rejected
    }

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

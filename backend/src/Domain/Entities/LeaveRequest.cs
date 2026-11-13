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
        string? notes, Guid? id = null) =>
        new()
        {
            // Caller may supply a stable Id so a retried create (EnableRetryOnFailure) can detect
            // its own already-committed row and stay idempotent. Defaults to a fresh Guid.
            Id         = id ?? Guid.NewGuid(),
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
        if (Status is AppConstants.Leave.Rejected or AppConstants.Leave.Cancelled)
            throw new InvalidOperationException($"A {Status} leave request cannot be edited.");
        EnsureCanBecome(status);
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

    public void Cancel()
    {
        // Unlike Update, cancelling twice is an error rather than a no-op.
        if (Status is not (AppConstants.Leave.Pending or AppConstants.Leave.Approved))
            throw new InvalidOperationException($"A {Status} leave request cannot be cancelled.");
        Status = AppConstants.Leave.Cancelled;
    }

    /// <summary>
    /// Status state machine: pending moves to approved, rejected or cancelled; approved moves to
    /// rejected or cancelled; rejected and cancelled are final (apply again instead). Staying in the
    /// same status is allowed so notes, dates and hours can still be edited.
    /// </summary>
    private void EnsureCanBecome(string next)
    {
        next = next.ToLowerInvariant();
        if (next == Status) return;
        var allowed = Status switch
        {
            AppConstants.Leave.Pending  => next is AppConstants.Leave.Approved or AppConstants.Leave.Rejected or AppConstants.Leave.Cancelled,
            AppConstants.Leave.Approved => next is AppConstants.Leave.Rejected or AppConstants.Leave.Cancelled,
            _                           => false,
        };
        if (!allowed)
            throw new InvalidOperationException($"A {Status} leave request cannot be changed to {next}.");
    }

    /// <summary>Auto-reject this leave and stamp a system note onto it.</summary>
    public void AutoReject(string systemNote)
    {
        EnsureCanBecome(AppConstants.Leave.Rejected);
        Status = AppConstants.Leave.Rejected;
        Notes  = string.IsNullOrWhiteSpace(Notes)
            ? systemNote
            : $"{Notes} | {systemNote}";
    }
}

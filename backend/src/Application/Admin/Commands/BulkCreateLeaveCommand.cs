using MediatR;

namespace HRNexus.Application.Admin.Commands;

public record BulkLeaveResult(int Created, int Failed, List<string> Errors);

/// <summary>Record the SAME leave (e.g. a public holiday) for many staff at once. Delegates each
/// employee to <see cref="UpsertLeaveCommand"/> so the full validation — overlap, balance,
/// notifications — runs per person. PARTIAL SUCCESS: a rejected employee is reported and skipped,
/// the rest still commit.</summary>
public record BulkCreateLeaveCommand(
    List<Guid> EmployeeIds,
    string     LeaveType,
    DateTime   StartDate,
    DateTime   EndDate,
    string?    StartTime,
    string?    EndTime,
    decimal?   Hours,
    string?    Notes,
    string     Status) : IRequest<BulkLeaveResult>;

public class BulkCreateLeaveCommandHandler(ISender sender) : IRequestHandler<BulkCreateLeaveCommand, BulkLeaveResult>
{
    public async Task<BulkLeaveResult> Handle(BulkCreateLeaveCommand req, CancellationToken ct)
    {
        var ids = req.EmployeeIds.Distinct().ToList();
        if (ids.Count == 0) return new BulkLeaveResult(0, 0, []);
        if (ids.Count > 100)
            throw new ArgumentException("Cannot create leave for more than 100 staff at once.");

        var created = 0;
        var errors  = new List<string>();
        foreach (var empId in ids)
        {
            try
            {
                await sender.Send(new UpsertLeaveCommand(
                    Id: null, EmployeeId: empId, LeaveType: req.LeaveType,
                    StartDate: req.StartDate, EndDate: req.EndDate,
                    StartTime: req.StartTime, EndTime: req.EndTime, Hours: req.Hours,
                    Notes: req.Notes, Status: req.Status, RejectionReason: null,
                    SkipAdvanceNotice: true), ct);
                created++;
            }
            catch (Exception ex)
            {
                errors.Add($"{empId}: {ex.Message}");
            }
        }
        return new BulkLeaveResult(created, errors.Count, errors);
    }
}

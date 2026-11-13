using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Admin.Commands;

/// <summary>
/// Bulk-convert a list of leave requests to the "no-show" type (the staff member was rostered but
/// did not turn up). Only the leave TYPE changes; the existing status is preserved. Already-no-show
/// records are skipped. Returns the number of records actually changed.
/// </summary>
public record BulkMarkLeaveNoShowCommand(List<Guid> Ids) : IRequest<int>;

public class BulkMarkLeaveNoShowCommandHandler(IAppDbContext db, IAuditService audit)
    : IRequestHandler<BulkMarkLeaveNoShowCommand, int>
{
    public async Task<int> Handle(BulkMarkLeaveNoShowCommand req, CancellationToken ct)
    {
        if (req.Ids.Count == 0) return 0;
        if (req.Ids.Count > 100)
            throw new ArgumentException("Cannot bulk-update more than 100 leave requests at once.");

        var leaves = await db.LeaveRequests.Where(l => req.Ids.Contains(l.Id)).ToListAsync(ct);

        var count = 0;
        foreach (var leave in leaves)
        {
            if (leave.LeaveType.Equals(AppConstants.Leave.NoShow, StringComparison.OrdinalIgnoreCase))
                continue; // already a no-show

            leave.Update(AppConstants.Leave.NoShow, leave.StartDate, leave.EndDate,
                leave.StartTime, leave.EndTime, leave.Hours, leave.Notes, leave.Status, null);
            await audit.LogAsync("LeaveRequest", leave.Id.ToString(), "BulkMarkNoShow",
                null, "Admin", "Type → no-show", ct);
            count++;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // xmin token mismatch: another admin (or the auto-reject job) touched one of these
            // records between our read and save. Surface a clean refresh-and-retry message.
            throw new InvalidOperationException(
                "One or more leave requests were modified concurrently. Please refresh and try again.");
        }

        return count;
    }
}

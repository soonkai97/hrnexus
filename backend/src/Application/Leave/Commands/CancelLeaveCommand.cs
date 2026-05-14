using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Application.Leave.Commands;

public record CancelLeaveCommand(Guid LeaveId, Guid EmployeeId) : IRequest;

public class CancelLeaveCommandHandler(IAppDbContext db, IDateTimeProvider dt) : IRequestHandler<CancelLeaveCommand>
{
    public async Task Handle(CancelLeaveCommand req, CancellationToken ct)
    {
        var leave = await db.LeaveRequests
            .FirstOrDefaultAsync(l => l.Id == req.LeaveId && l.EmployeeId == req.EmployeeId, ct)
            ?? throw new KeyNotFoundException("Leave request not found.");
        if (leave.Status != "pending" && leave.Status != "approved")
            throw new InvalidOperationException("Only pending or approved leaves can be cancelled.");
        if (leave.EndDate.Date < dt.LocalNow.Date)
            throw new InvalidOperationException("Cannot cancel a leave that has already ended.");

        // Look up employee name for audit
        var employee = await db.Staff
            .Where(s => s.Id == req.EmployeeId)
            .Select(s => new { s.FullName })
            .FirstOrDefaultAsync(ct);

        var wasApproved = leave.Status == "approved";
        var changes = $"Type={leave.LeaveType},Start={leave.StartDate:yyyy-MM-dd},End={leave.EndDate:yyyy-MM-dd},PreviousStatus={leave.Status}";

        db.AuditLogs.Add(AuditLog.Create(
            entityType:      "LeaveRequest",
            entityId:        leave.Id.ToString(),
            action:          wasApproved ? "WithdrawnApproved" : "Cancelled",
            performedById:   req.EmployeeId,
            performedByName: employee?.FullName ?? "Unknown",
            changes:         changes));

        leave.Cancel();
        await db.SaveChangesAsync(ct);
    }
}

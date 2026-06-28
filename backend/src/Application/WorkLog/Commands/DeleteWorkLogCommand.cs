using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.WorkLog.Commands;

public record DeleteWorkLogCommand(Guid Id, Guid EmployeeId, bool IsPrivileged = false) : IRequest;

public class DeleteWorkLogCommandHandler(IAppDbContext db) : IRequestHandler<DeleteWorkLogCommand>
{
    public async Task Handle(DeleteWorkLogCommand req, CancellationToken ct)
    {
        var log = await db.WorkLogs.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException("Work log not found.");
        // Owner can delete their own; an admin/manager (privileged) can delete anyone's.
        if (log.EmployeeId != req.EmployeeId && !req.IsPrivileged) throw new UnauthorizedAccessException("Not your shift.");
        if (log.EndTime == null) throw new InvalidOperationException("Cannot delete an active shift. End it first.");

        // Can't delete a shift that falls in a locked payroll period.
        await PayrollLockGuard.EnsureDateOpenAsync(db, log.WorkDate, ct);

        // Look up the employee name for the audit trail
        var employee = await db.Staff
            .Where(s => s.Id == req.EmployeeId)
            .Select(s => new { s.FullName })
            .FirstOrDefaultAsync(ct);

        // Audit before delete (so we retain the log details)
        var changes = $"Date={log.WorkDate},Start={log.StartTime},End={log.EndTime ?? "active"},Role={log.RoleName},Location={log.LocationName}";
        db.AuditLogs.Add(AuditLog.Create(
            entityType:      "WorkLog",
            entityId:        log.Id.ToString(),
            action:          "Deleted",
            performedById:   req.EmployeeId,
            performedByName: employee?.FullName ?? "Unknown",
            changes:         changes));

        db.WorkLogs.Remove(log);
        await db.SaveChangesAsync(ct);
    }
}

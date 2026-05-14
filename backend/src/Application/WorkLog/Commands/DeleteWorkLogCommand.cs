using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Application.WorkLog.Commands;

public record DeleteWorkLogCommand(Guid Id, Guid EmployeeId) : IRequest;

public class DeleteWorkLogCommandHandler(IAppDbContext db) : IRequestHandler<DeleteWorkLogCommand>
{
    public async Task Handle(DeleteWorkLogCommand req, CancellationToken ct)
    {
        var log = await db.WorkLogs.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException("Work log not found.");
        if (log.EmployeeId != req.EmployeeId) throw new UnauthorizedAccessException("Not your shift.");
        if (log.EndTime == null) throw new InvalidOperationException("Cannot delete an active shift. End it first.");

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

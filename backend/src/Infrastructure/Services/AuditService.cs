using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Infrastructure.Services;

public class AuditService(IAppDbContext db, ICurrentUserService currentUser) : IAuditService
{
    public async Task LogAsync(string entityType, string entityId, string action,
        Guid? performedById, string performedByName, string? changes = null, CancellationToken ct = default)
    {
        // When a command doesn't supply a performer (many admin/bulk commands historically passed
        // null + a placeholder like "Admin"), attribute the entry to the authenticated caller so the
        // audit trail records WHICH admin acted, not a placeholder. Commands that already pass a real
        // performer (e.g. payroll lock/reopen) keep theirs.
        var id   = performedById ?? currentUser.UserId;
        var name = performedById is null && !string.IsNullOrWhiteSpace(currentUser.UserName)
            ? currentUser.UserName!
            : performedByName;
        db.AuditLogs.Add(AuditLog.Create(entityType, entityId, action, id, name, changes));
        await db.SaveChangesAsync(ct);
    }
}

using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Services;

public class AuditService(IAppDbContext db) : IAuditService
{
    public async Task LogAsync(string entityType, string entityId, string action,
        Guid? performedById, string performedByName, string? changes = null, CancellationToken ct = default)
    {
        db.AuditLogs.Add(AuditLog.Create(entityType, entityId, action, performedById, performedByName, changes));
        await db.SaveChangesAsync(ct);
    }
}

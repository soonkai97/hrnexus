namespace HRNexus.Application.Common.Interfaces;

public interface IAuditService
{
    Task LogAsync(string entityType, string entityId, string action,
        Guid? performedById, string performedByName, string? changes = null,
        CancellationToken ct = default);
}

namespace HRNexus.Domain.Common;

/// <summary>
/// Marker interface. Entities that implement this have CreatedAt, CreatedBy,
/// ModifiedAt, ModifiedBy columns auto-populated by AppDbContext.SaveChangesAsync.
/// </summary>
public interface IHasAudit { }

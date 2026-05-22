namespace NightclubWorkforce.Domain.Common;

/// <summary>
/// Marker interface. Entities that implement this use soft-delete:
/// AppDbContext converts Remove() calls into IsDeleted=true + DeletedAt + DeletedBy.
/// A global EF Core query filter automatically excludes soft-deleted rows.
/// </summary>
public interface ISoftDelete { }

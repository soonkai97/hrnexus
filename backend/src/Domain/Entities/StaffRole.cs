namespace HRNexus.Domain.Entities;

/// <summary>
/// One row per role assignment for an employee.
/// Composite PK: (EmployeeId, RoleId) — an employee cannot hold the same role twice.
/// The <see cref="Role"/> navigation is AutoInclude()d so a person's role names can be listed without a second query.
/// </summary>
public class StaffRole
{
    public Guid EmployeeId { get; set; }
    public Guid RoleId     { get; set; }

    /// <summary>Navigation to the assigned role — loaded automatically via AutoInclude.</summary>
    public Role Role { get; set; } = default!;
}

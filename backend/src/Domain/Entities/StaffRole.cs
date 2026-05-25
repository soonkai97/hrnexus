namespace HRNexus.Domain.Entities;

/// <summary>
/// One row per role assignment for an employee.
/// Composite PK: (EmployeeId, Role) — an employee cannot hold the same role twice.
/// </summary>
public class StaffRole
{
    public Guid   EmployeeId { get; set; }
    public string Role       { get; set; } = default!;
}

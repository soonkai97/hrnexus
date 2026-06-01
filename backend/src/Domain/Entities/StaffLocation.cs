namespace HRNexus.Domain.Entities;

/// <summary>
/// One row per location a staff member is assigned to work at.
/// Composite PK: (EmployeeId, LocationId) — a staff member cannot be assigned the same location twice.
/// Staff may only log shifts at a location they are assigned to.
/// </summary>
public class StaffLocation
{
    public Guid   EmployeeId { get; set; }
    public string LocationId { get; set; } = default!;
}

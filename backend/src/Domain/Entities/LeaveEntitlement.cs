namespace NightclubWorkforce.Domain.Entities;

/// <summary>Per-employee annual leave entitlement for a given calendar year (values in hours).</summary>
public class LeaveEntitlement
{
    public Guid    Id         { get; private set; }
    public Guid    EmployeeId { get; private set; }
    public int     Year       { get; private set; }
    public decimal AnnualHours   { get; private set; }  // entitlement in hours; 0 = not yet set
    public decimal SickHours     { get; private set; }  // entitlement in hours; 0 = not yet set
    public decimal CarryOverHours { get; private set; } // hours added from previous year's carry-over

    public Staff Employee { get; private set; } = default!;

    private LeaveEntitlement() { }

    public static LeaveEntitlement Create(Guid employeeId, int year,
        decimal annualHours = 0, decimal sickHours = 0) =>
        new()
        {
            Id = Guid.NewGuid(), EmployeeId = employeeId,
            Year = year, AnnualHours = annualHours, SickHours = sickHours,
        };

    public void Update(decimal annualHours, decimal sickHours)
    {
        AnnualHours = annualHours;
        SickHours   = sickHours;
        // CarryOverHours is intentionally NOT reset here — it is only set by AddCarryOver().
    }

    /// <summary>Add carry-over hours (accumulated from a prior-year rollover operation).</summary>
    public void AddCarryOver(decimal hours)
    {
        AnnualHours   += hours;
        CarryOverHours += hours;
    }
}

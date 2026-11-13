namespace HRNexus.Domain.Entities;

/// <summary>Per-employee annual leave entitlement for a given calendar year (values in hours).</summary>
public class LeaveEntitlement
{
    public Guid    Id         { get; private set; }
    public Guid    EmployeeId { get; private set; }
    public int     Year       { get; private set; }
    public decimal AnnualHours   { get; private set; }  // BASE yearly entitlement in hours (excludes carry-over); 0 = not yet set
    public decimal SickHours     { get; private set; }  // entitlement in hours; 0 = not yet set
    public decimal CarryOverHours { get; private set; } // hours carried over from the previous year (kept apart from AnnualHours)

    /// <summary>Usable annual pool: the base entitlement plus hours carried over from last year.</summary>
    public decimal TotalAnnualHours => AnnualHours + CarryOverHours;

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
        // CarryOverHours is kept: it is a separate figure, so editing the base can't delete it.
    }

    /// <summary>Add carry-over hours (from a prior-year rollover). They raise the usable pool
    /// (<see cref="TotalAnnualHours"/>) but never the base entitlement.</summary>
    public void AddCarryOver(decimal hours) => CarryOverHours += hours;
}

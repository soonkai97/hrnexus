namespace NightclubWorkforce.Domain.Entities;

/// <summary>Per-employee annual leave entitlement for a given calendar year.</summary>
public class LeaveEntitlement
{
    public Guid Id         { get; private set; }
    public Guid EmployeeId { get; private set; }
    public int  Year       { get; private set; }
    public int  AnnualDays { get; private set; }  // default 14
    public int  SickDays   { get; private set; }  // default 14

    public Staff Employee { get; private set; } = default!;

    private LeaveEntitlement() { }

    public static LeaveEntitlement Create(Guid employeeId, int year,
        int annualDays = 14, int sickDays = 14) =>
        new()
        {
            Id = Guid.NewGuid(), EmployeeId = employeeId,
            Year = year, AnnualDays = annualDays, SickDays = sickDays,
        };

    public void Update(int annualDays, int sickDays)
    {
        AnnualDays = annualDays;
        SickDays   = sickDays;
    }
}

namespace NightclubWorkforce.Domain.Entities;

/// <summary>
/// Persists the individual tip amount each staff member received from a TipsPool distribution.
/// Provides an auditable record that can be reviewed or disputed after the fact.
/// </summary>
public class TipsAllocationRecord
{
    public Guid     Id          { get; private set; }
    public Guid     PoolId      { get; private set; }   // FK → TipsPool
    public Guid     EmployeeId  { get; private set; }
    public string   FullName    { get; private set; } = string.Empty; // snapshot at distribution time
    public decimal  Amount      { get; private set; }
    public decimal  HoursWorked { get; private set; }   // 0 for equal-split distributions
    public DateTime CreatedAt   { get; private set; }

    private TipsAllocationRecord() { }

    public static TipsAllocationRecord Create(
        Guid poolId, Guid employeeId, string fullName,
        decimal amount, decimal hoursWorked) =>
        new()
        {
            Id          = Guid.NewGuid(),
            PoolId      = poolId,
            EmployeeId  = employeeId,
            FullName    = fullName,
            Amount      = Math.Round(amount, 2),
            HoursWorked = Math.Round(hoursWorked, 2),
            CreatedAt   = DateTime.UtcNow,
        };
}

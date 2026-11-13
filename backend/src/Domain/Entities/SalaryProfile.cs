namespace HRNexus.Domain.Entities;

/// <summary>
/// One row of a person's pay history: the monthly basic salary that applies from
/// <see cref="EffectiveFrom"/> until a later row takes over. Rows are append-only (a raise adds a
/// row; nothing is edited), so any past month can be recalculated with the salary it really had.
/// Salary, account number and holder are encrypted at rest (see SalaryProfileConfiguration).
/// </summary>
public class SalaryProfile
{
    public Guid     Id            { get; private set; }
    public Guid     EmployeeId    { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public decimal  BasicSalary   { get; private set; }   // monthly, MYR
    public string   PayFrequency  { get; private set; } = default!;
    public string?  BankName      { get; private set; }
    public string?  BankAccount   { get; private set; }
    public string?  BankHolder    { get; private set; }
    public DateTime CreatedAt     { get; private set; }
    public string?  CreatedBy     { get; private set; }

    private SalaryProfile() { }

    public static SalaryProfile Create(Guid employeeId, DateOnly effectiveFrom, decimal basicSalary,
        string payFrequency, string? bankName, string? bankAccount, string? bankHolder, string? createdBy = null) => new()
    {
        Id            = Guid.NewGuid(),
        EmployeeId    = employeeId,
        EffectiveFrom = effectiveFrom,
        BasicSalary   = basicSalary,
        PayFrequency  = payFrequency,
        BankName      = bankName,
        BankAccount   = bankAccount,
        BankHolder    = bankHolder,
        CreatedAt     = DateTime.UtcNow,
        CreatedBy     = createdBy,
    };
}

namespace HRNexus.Domain.Entities;

/// <summary>
/// Marks one payroll period (a single calendar month) as locked. While a lock row exists for a
/// (Year, Month), the period counts as paid out and payroll records dated in that month are to be frozen
/// (enforced by <c>PayrollLockGuard</c> once payroll runs exist). Reopening simply deletes the row.
/// </summary>
public sealed class PayrollLock
{
    public Guid     Id       { get; private set; }
    public int      Year     { get; private set; }
    public int      Month    { get; private set; }   // 1-12
    public DateTime LockedAt { get; private set; }    // UTC — when the period was locked
    public string   LockedBy { get; private set; } = string.Empty;
    public string?  Note     { get; private set; }

    private PayrollLock() { }

    public static PayrollLock Create(int year, int month, string lockedBy, string? note = null)
    {
        if (month is < 1 or > 12)
            throw new ArgumentException("Month must be between 1 and 12.");
        if (year is < 2000 or > 2200)
            throw new ArgumentException("Year is out of the supported range.");

        return new PayrollLock
        {
            Id       = Guid.NewGuid(),
            Year     = year,
            Month    = month,
            LockedAt = DateTime.UtcNow,
            LockedBy = lockedBy,
            Note     = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
    }
}

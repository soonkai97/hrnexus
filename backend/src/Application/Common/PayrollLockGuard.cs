using System.Globalization;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Common;

/// <summary>
/// Shared guard for the month-lock feature. Commands that create/edit/delete a dated record
/// (shift logs, ledger entries/expenses/loans) call this before mutating, so any change to a
/// locked payroll period is rejected with a clean, user-facing message.
/// </summary>
public static class PayrollLockGuard
{
    public static async Task EnsureMonthOpenAsync(IAppDbContext db, int year, int month, CancellationToken ct)
    {
        var locked = await db.PayrollLocks.AnyAsync(p => p.Year == year && p.Month == month, ct);
        if (locked)
            throw new InvalidOperationException(
                $"The {MonthName(month)} {year} payroll period is locked and can no longer be modified. " +
                "Ask a manager to reopen it first.");
    }

    public static Task EnsureDateOpenAsync(IAppDbContext db, DateTime date, CancellationToken ct)
        => EnsureMonthOpenAsync(db, date.Year, date.Month, ct);

    public static Task EnsureDateOpenAsync(IAppDbContext db, DateOnly date, CancellationToken ct)
        => EnsureMonthOpenAsync(db, date.Year, date.Month, ct);

    private static string MonthName(int month) =>
        month is >= 1 and <= 12
            ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)
            : month.ToString(CultureInfo.InvariantCulture);
}

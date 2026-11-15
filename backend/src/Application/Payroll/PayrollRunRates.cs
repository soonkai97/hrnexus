using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Payroll;

/// <summary>Which statutory rates a run's frozen figures rest on, and which of them the accountant has not verified yet.</summary>
public static class PayrollRunRates
{
    /// <summary>
    /// The rate rows the run's included lines really used (their stored rate ids, not whatever is in force today) that are
    /// not verified, as "EPF (citizen)" style labels: scheme and class only, never a figure.
    /// </summary>
    public static async Task<List<string>> UnverifiedUsedAsync(IAppDbContext db, PayrollRun run, CancellationToken ct)
    {
        var used = run.Lines.Where(l => !l.Excluded).SelectMany(l => l.Items)
            .Where(i => i.RateRuleId != null).Select(i => i.RateRuleId!.Value).Distinct().ToList();
        var rows = await db.StatutoryRateTables.AsNoTracking().Where(t => used.Contains(t.Id) && !t.Verified)
            .Select(t => new { t.Scheme, t.CitizenshipClass }).ToListAsync(ct);
        return rows.Select(u => $"{u.Scheme.ToUpperInvariant()} ({u.CitizenshipClass})").Distinct().Order().ToList();
    }
}

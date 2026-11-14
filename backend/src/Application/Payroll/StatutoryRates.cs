using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Payroll;

/// <summary>An unverified rate row a month's calculation would use.</summary>
public sealed record UnverifiedRate(Guid Id, string Scheme, string CitizenshipClass, DateOnly EffectiveFrom);

/// <summary>
/// Loads the rate rows for the payroll calculator and answers the one question the approval guard asks: which of
/// the rows this month would really use have not been verified by the accountant?
/// </summary>
public static class StatutoryRates
{
    /// <summary>Every rate row as the calculator's plain input (bands included).</summary>
    public static async Task<List<RateRule>> LoadRulesAsync(IAppDbContext db, CancellationToken ct) =>
        (await db.StatutoryRateTables.ToListAsync(ct)).Select(t => t.ToRule()).ToList();

    /// <summary>
    /// The unverified rows that a calculation for these people in this month would use: EPF, SOCSO, EIS and the
    /// HRD Corp levy row that is switched on, resolved the same way the calculator resolves them (latest row on or
    /// before the first of the month, for the person's class and age). Approval (Phase 8c) is refused while the
    /// list is not empty. The minimum wage is only ever a warning, so it is not part of the gate.
    /// </summary>
    public static async Task<List<UnverifiedRate>> UnverifiedAsync(IAppDbContext db, int year, int month,
        IEnumerable<(string? CitizenshipClass, int Age)> people, CancellationToken ct)
    {
        var rules = await LoadRulesAsync(db, ct);
        var first = new DateOnly(year, month, 1);
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var found = new Dictionary<Guid, UnverifiedRate>();

        foreach (var (klass, age) in people.Where(p => p.CitizenshipClass is "citizen" or "permanent-resident").Distinct())
        {
            foreach (var scheme in new[] { StatutoryScheme.Epf, StatutoryScheme.Socso, StatutoryScheme.Eis, StatutoryScheme.HrdCompulsory, StatutoryScheme.HrdOptional })
            {
                var rule = RateResolver.Resolve(rules, scheme, klass!, age, first, last).Rule;
                if (rule is null || rule.Verified) continue;
                var isHrd = scheme is StatutoryScheme.HrdCompulsory or StatutoryScheme.HrdOptional;
                if (isHrd && !rule.Active) continue;   // a switched-off levy row is not used
                found[rule.Id] = new UnverifiedRate(rule.Id, rule.Scheme, rule.CitizenshipClass, rule.EffectiveFrom);
            }
        }
        return found.Values.OrderBy(u => u.Scheme).ThenBy(u => u.CitizenshipClass).ToList();
    }
}

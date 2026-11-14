using HRNexus.Domain.Entities;

namespace HRNexus.Domain.Payroll;

/// <summary>
/// The rate rows the product ships with, taken from PRD 6.2 (researched 13 Nov 2026). EVERY row is unverified:
/// the percentage rows are placeholders (the official EPF, SOCSO and EIS schedules are believed to be band
/// tables) and the rounding rules are assumptions. A person cannot approve a payroll run on them until the
/// accountant verifies them (docs/process/payroll-accountant-questions.md).
/// </summary>
public static class StatutoryRateSeed
{
    public const string Source = "PRD 6.2 (13 Nov 2026), unverified";
    private const string Placeholder = "Percentage placeholder: the official schedule is believed to be a band table. Rounding is an assumption. Not verified.";

    /// <summary>The first day the SOCSO and EIS ceiling of RM6,000 applies.</summary>
    private static readonly DateOnly CeilingFrom = new(2024, 10, 1);
    private static readonly DateOnly MinimumWageFrom = new(2025, 2, 1);
    private static readonly DateOnly Origin = new(2020, 1, 1);

    public static IReadOnlyList<StatutoryRateTable> Rows()
    {
        var rows = new List<StatutoryRateTable>();
        foreach (var klass in new[] { "citizen", "permanent-resident" })
        {
            rows.Add(StatutoryRateTable.CreatePercentage(StatutoryScheme.Epf, klass, Origin, 0.11m, 0.13m, RoundingRule.UpToNextRinggit, Source,
                mode: EmployerRateMode.WholeWageByThreshold, threshold: 5000m, above: 0.12m, note: Placeholder));
            rows.Add(StatutoryRateTable.CreatePercentage(StatutoryScheme.Socso, klass, CeilingFrom, 0.005m, 0.0175m, RoundingRule.Nearest5Sen, Source,
                ceiling: 6000m, note: "Category 1. " + Placeholder));
            rows.Add(StatutoryRateTable.CreatePercentage(StatutoryScheme.Eis, klass, CeilingFrom, 0.002m, 0.002m, RoundingRule.Nearest5Sen, Source,
                ceiling: 6000m, note: Placeholder));
            // The owner answered (14 Nov 2026) that the company has 10 or more Malaysian employees: the 1% row is on.
            rows.Add(StatutoryRateTable.CreatePercentage(StatutoryScheme.HrdCompulsory, klass, Origin, 0m, 0.01m, RoundingRule.NearestCent, Source,
                active: true, note: "Compulsory with 10 or more Malaysian employees."));
            rows.Add(StatutoryRateTable.CreatePercentage(StatutoryScheme.HrdOptional, klass, Origin, 0m, 0.005m, RoundingRule.NearestCent, Source,
                active: false, note: "Optional for 5 to 9 Malaysian employees; leave off unless the company registers."));
            rows.Add(StatutoryRateTable.CreateMinimumWage(klass, MinimumWageFrom, 1700m, Source,
                note: "A rise to RM2,000 from June 2027 is only a press report: add a row when confirmed."));
        }
        return rows;
    }
}

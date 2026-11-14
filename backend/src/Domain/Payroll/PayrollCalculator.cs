namespace HRNexus.Domain.Payroll;

/// <summary>
/// Pure payroll maths for one person and one month, in the fixed order of PRD 6.2. A domain service: no
/// database, no clock, no floating point; the same input always gives the same output. It applies the rate
/// rows it is handed; whether those rows match the law is the accountant's question, which is why every row
/// carries a Verified flag and an unverified row raises a blocking warning.
/// </summary>
public static class PayrollCalculator
{
    public const string Version = "1";

    private static readonly string[] Classes = ["citizen", "permanent-resident"];

    public static PayrollResult Calculate(PayrollInput input)
    {
        Validate(input);

        var warnings = new List<PayrollWarning>();
        var lines = new List<PayLine>();
        var usedRules = new List<Guid>();
        void Warn(string code, string message, bool blocking) => warnings.Add(new PayrollWarning(code, message, blocking));

        var first = new DateOnly(input.Year, input.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(input.Year, input.Month);
        var last = new DateOnly(input.Year, input.Month, daysInMonth);

        // ── 1. Prorated basic, per salary segment ────────────────────────────────
        var start = Max(first, input.EmploymentStart ?? first);
        var end = Min(last, input.EmploymentEnd ?? last);
        var employedDays = end >= start ? end.DayNumber - start.DayNumber + 1 : 0;
        if (employedDays == 0)
            Warn("employment-outside-month", "The person was not employed on any day of this month.", blocking: true);

        var clipped = input.Segments
            .Select(s => new SalarySegment(Max(s.From, start), Min(s.To, end), s.Basic, s.ProfileId))
            .Where(s => s.From <= s.To)
            .ToList();
        var coveredDays = clipped.Sum(s => s.To.DayNumber - s.From.DayNumber + 1);
        if (coveredDays < employedDays)
            Warn("no-salary-for-days", $"{employedDays - coveredDays} employed day(s) have no salary on record.", blocking: true);

        var proratedRaw = clipped.Sum(s => s.Basic * (s.To.DayNumber - s.From.DayNumber + 1) / daysInMonth);
        var prorated = MoneyRounding.Round(proratedRaw, RoundingRule.NearestCent);
        lines.Add(new PayLine("basic", LineSection.Earning, "Basic salary (prorated)", prorated));

        // ── 2. Unpaid-leave deduction, never more than the prorated basic ────────
        decimal unpaidRaw = 0m;
        foreach (var day in input.UnpaidLeave)
        {
            var seg = clipped.FirstOrDefault(s => s.From <= day.Date && day.Date <= s.To);
            if (seg is null)
            {
                Warn("unpaid-day-outside-employment", $"Unpaid leave on {day.Date:yyyy-MM-dd} falls outside employment or salary cover and was ignored.", blocking: false);
                continue;
            }
            unpaidRaw += seg.Basic / input.Divisor * day.Days;
        }
        var unpaid = MoneyRounding.Round(unpaidRaw, RoundingRule.NearestCent);
        if (unpaid > prorated)
        {
            unpaid = prorated;
            Warn("unpaid-leave-capped", "The unpaid-leave deduction was capped at the prorated basic.", blocking: false);
        }

        // ── 3. Gross ─────────────────────────────────────────────────────────────
        foreach (var e in input.Earnings)
            lines.Add(new PayLine(e.Type, LineSection.Earning, e.Reason, e.Amount));
        if (unpaid > 0m)
            lines.Add(new PayLine("unpaid-leave", LineSection.GrossDeduction, "Unpaid leave", unpaid));

        var earningsTotal = input.Earnings.Sum(e => e.Amount);
        var gross = prorated + earningsTotal - unpaid;

        var baseCommon = prorated - unpaid;
        var epfBase = baseCommon + input.Earnings.Where(e => e.CountsForEpf).Sum(e => e.Amount);
        var socsoEisBase = baseCommon + input.Earnings.Where(e => e.CountsForSocsoEis).Sum(e => e.Amount);

        // ── 4. Statutory contributions, each on its own wage base ────────────────
        decimal eeStatutory = 0m, erStatutory = 0m, levy = 0m;
        var klass = input.CitizenshipClass;
        if (klass is null || !Classes.Contains(klass))
        {
            Warn("no-citizenship-class", "The person's citizenship status is not set to citizen or permanent resident, so statutory contributions cannot be worked out.", blocking: true);
        }
        else
        {
            foreach (var (scheme, bas, prefix) in new[]
                     {
                         (StatutoryScheme.Epf, epfBase, "epf"),
                         (StatutoryScheme.Socso, socsoEisBase, "socso"),
                         (StatutoryScheme.Eis, socsoEisBase, "eis"),
                     })
            {
                var rule = ResolveOrWarn(input, scheme, klass, first, last, Warn);
                if (rule is null) continue;
                NoteUsed(rule, usedRules, Warn);

                var (ee, er) = rule.Kind == RateKind.BandTable ? Band(rule, bas, Warn) : Percentage(rule, bas);
                lines.Add(new PayLine($"{prefix}-employee", LineSection.EmployeeStatutory, $"{prefix.ToUpperInvariant()} employee", ee, rule.Id));
                lines.Add(new PayLine($"{prefix}-employer", LineSection.EmployerContribution, $"{prefix.ToUpperInvariant()} employer", er, rule.Id));
                eeStatutory += ee;
                erStatutory += er;
            }

            // HRD Corp levy: employer only, and only if exactly one of its rows is switched on.
            var hrd = new[] { StatutoryScheme.HrdCompulsory, StatutoryScheme.HrdOptional }
                .Select(s => RateResolver.Resolve(input.Rates, s, klass, input.AgeAtMonthEnd, first, last).Rule)
                .Where(r => r is { Active: true })
                .Cast<RateRule>()
                .ToList();
            if (hrd.Count > 1)
                Warn("two-hrd-rows-active", "Both HRD Corp levy rows are switched on; switch off one.", blocking: true);
            else if (hrd.Count == 1)
            {
                var rule = hrd[0];
                NoteUsed(rule, usedRules, Warn);
                var baseForLevy = rule.WageCeiling is { } cap ? Math.Min(epfBase, cap) : epfBase;
                levy = baseForLevy > 0m ? MoneyRounding.Round(baseForLevy * rule.EmployerRate, rule.Rounding) : 0m;
                lines.Add(new PayLine("hrd-levy", LineSection.EmployerContribution, "HRD Corp levy", levy, rule.Id));
            }
        }

        // ── 5. PCB: typed in by the preparer, applied as given ───────────────────
        decimal pcb = 0m;
        if (input.PcbAmount is null)
        {
            Warn("no-pcb-entered", "No PCB amount was entered for this person (treated as 0).", blocking: false);
        }
        else
        {
            pcb = Math.Min(input.PcbAmount.Value, Math.Max(0m, gross - eeStatutory));
            if (pcb < input.PcbAmount.Value)
                Warn("pcb-reduced", "The PCB amount was reduced to fit what is left of net pay.", blocking: false);
            lines.Add(new PayLine("pcb", LineSection.Tax, "Monthly tax deduction (PCB)", pcb));
        }

        // ── 6. Other deductions last: priority order, capped, never below zero net ─
        var netBefore = gross - eeStatutory - pcb;
        if (netBefore < 0m)
            Warn("negative-net", "Statutory contributions and tax exceed the person's gross pay, so net pay is below zero.", blocking: true);

        var capRemaining = MoneyRounding.Round(gross * input.DeductionCapRate, RoundingRule.NearestCent);
        var available = Math.Max(0m, netBefore);
        decimal appliedTotal = 0m, unapplied = 0m;
        foreach (var d in input.Deductions.Select((d, i) => (d, i)).OrderBy(x => x.d.Priority).ThenBy(x => x.i).Select(x => x.d))
        {
            var applied = Math.Min(d.Amount, Math.Min(capRemaining, available));
            if (applied > 0m)
                lines.Add(new PayLine("deduction", LineSection.OtherDeduction, d.Reason, applied));
            capRemaining -= applied;
            available -= applied;
            appliedTotal += applied;
            unapplied += d.Amount - applied;
        }
        if (unapplied > 0m)
            Warn("deductions-unapplied", $"{unapplied:0.00} of other deductions could not be applied this month and carries to the next run.", blocking: false);

        // ── 7. Net and employer cost (PRD formulas) ──────────────────────────────
        var net = netBefore - appliedTotal;
        var employerCost = gross + erStatutory + levy;

        WarnIfBelowMinimumWage(input, klass, first, last, clipped, Warn);

        var basis = new PayrollBasis(Version, input.Divisor, daysInMonth, employedDays, input.AgeAtMonthEnd,
            usedRules.Distinct().ToList(), input.Segments);
        return new PayrollResult(lines, prorated, unpaid, gross, net, employerCost, unapplied, basis, warnings);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static RateRule? ResolveOrWarn(PayrollInput input, string scheme, string klass, DateOnly first, DateOnly last, Action<string, string, bool> warn)
    {
        var res = RateResolver.Resolve(input.Rates, scheme, klass, input.AgeAtMonthEnd, first, last);
        if (res.Rule is null)
            warn(res.WarningCode!, $"No usable {scheme.ToUpperInvariant()} rate for a {klass} aged {input.AgeAtMonthEnd} in {first:yyyy-MM} ({res.WarningCode}).", true);
        return res.Rule;
    }

    private static void NoteUsed(RateRule rule, List<Guid> used, Action<string, string, bool> warn)
    {
        used.Add(rule.Id);
        if (!rule.Verified)
            warn("unverified-rate", $"The {rule.Scheme.ToUpperInvariant()} rate row used has not been verified by the accountant.", true);
    }

    private static (decimal Employee, decimal Employer) Percentage(RateRule rule, decimal wageBase)
    {
        var b = rule.WageCeiling is { } cap ? Math.Min(wageBase, cap) : wageBase;
        if (b <= 0m) return (0m, 0m);
        var employerRate = rule.EmployerRateMode == EmployerRateMode.WholeWageByThreshold
            ? (b > rule.EmployerRateThreshold ? rule.EmployerRateAbove ?? rule.EmployerRate : rule.EmployerRate)
            : rule.EmployerRate;
        return (MoneyRounding.Round(b * rule.EmployeeRate, rule.Rounding), MoneyRounding.Round(b * employerRate, rule.Rounding));
    }

    private static (decimal Employee, decimal Employer) Band(RateRule rule, decimal wageBase, Action<string, string, bool> warn)
    {
        var b = rule.WageCeiling is { } cap ? Math.Min(wageBase, cap) : wageBase;
        if (b <= 0m || rule.Bands.Count == 0) return (0m, 0m);
        var bands = rule.Bands.OrderBy(x => x.WageFrom).ToList();
        var hit = bands.FirstOrDefault(x => x.WageFrom <= b && b < x.WageTo);
        if (hit is null)
        {
            if (b >= bands[^1].WageTo) hit = bands[^1];          // above the top band: the top band applies
            else if (b < bands[0].WageFrom) return (0m, 0m);     // below the lowest band: nothing
            else
            {
                warn("band-gap", $"The {rule.Scheme.ToUpperInvariant()} band table has no band for a wage of {b:0.00}.", true);
                return (0m, 0m);
            }
        }
        return (MoneyRounding.Round(hit.EmployeeAmount, rule.Rounding), MoneyRounding.Round(hit.EmployerAmount, rule.Rounding));
    }

    private static void WarnIfBelowMinimumWage(PayrollInput input, string? klass, DateOnly first, DateOnly last,
        IReadOnlyList<SalarySegment> clipped, Action<string, string, bool> warn)
    {
        if (klass is null || !Classes.Contains(klass)) return;
        var rule = RateResolver.Resolve(input.Rates, StatutoryScheme.MinimumWage, klass, input.AgeAtMonthEnd, first, last).Rule;
        if (rule?.Amount is not { } minimum) return;
        if (clipped.Any(s => s.Basic < minimum))
            warn("below-minimum-wage", $"A monthly basic is below the minimum wage of {minimum:0.00}.", false);
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a <= b ? a : b;

    private static void Validate(PayrollInput i)
    {
        if (i.Month is < 1 or > 12 || i.Year is < 2000 or > 2200)
            throw new ArgumentException("The payroll month is not a valid month.");
        if (i.Divisor <= 0m)
            throw new ArgumentException("The unpaid-leave divisor must be more than zero.");
        if (i.DeductionCapRate is < 0m or > 1m)
            throw new ArgumentException("The deduction cap rate must be between 0 and 1.");
        if (i.Earnings.Any(e => e.Amount < 0m))
            throw new ArgumentException("An earning cannot be negative.");
        if (i.Deductions.Any(d => d.Amount < 0m))
            throw new ArgumentException("A deduction cannot be negative.");
        if (i.PcbAmount is < 0m)
            throw new ArgumentException("The PCB amount cannot be negative.");
        if (i.UnpaidLeave.Any(u => u.Days < 0m))
            throw new ArgumentException("Unpaid leave days cannot be negative.");
        if (i.Segments.Any(s => s.To < s.From || s.Basic < 0m))
            throw new ArgumentException("A salary segment must end on or after it starts and cannot have a negative basic.");
        var ordered = i.Segments.OrderBy(s => s.From).ToList();
        for (var k = 1; k < ordered.Count; k++)
            if (ordered[k].From <= ordered[k - 1].To)
                throw new ArgumentException("Salary segments must not overlap.");
    }
}

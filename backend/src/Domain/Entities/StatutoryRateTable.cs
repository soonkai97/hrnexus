using HRNexus.Domain.Payroll;

namespace HRNexus.Domain.Entities;

/// <summary>
/// One statutory rate row: a scheme (EPF, SOCSO, EIS, HRD Corp levy, minimum wage), the citizenship class and age
/// range it applies to, and the rate or band table in force from <see cref="EffectiveFrom"/>.
///
/// An aggregate root: it owns its bands and protects its own rules. After creation **nothing about the rates can
/// change**: a new rate, a corrected band table or a different rounding rule is a NEW row with a later effective
/// date, which starts unverified. Only the accountant's verification and the HRD "active" switch can change, so a
/// verified row can never be altered under a past month's payslips.
/// </summary>
public class StatutoryRateTable
{
    private readonly List<StatutoryRateBand> _bands = [];

    public Guid             Id                    { get; private set; }
    public string           Scheme                { get; private set; } = default!;
    public string           CitizenshipClass      { get; private set; } = default!;
    public int              AgeFrom               { get; private set; }
    public int?             AgeTo                 { get; private set; }
    public DateOnly         EffectiveFrom         { get; private set; }
    public RateKind         Kind                  { get; private set; }
    public decimal?         WageCeiling           { get; private set; }
    public decimal          EmployeeRate          { get; private set; }
    public decimal          EmployerRate          { get; private set; }
    public EmployerRateMode EmployerRateMode      { get; private set; }
    public decimal?         EmployerRateThreshold { get; private set; }
    public decimal?         EmployerRateAbove     { get; private set; }
    public RoundingRule     Rounding              { get; private set; }
    /// <summary>The monthly amount, for the minimum-wage row only.</summary>
    public decimal?         Amount                { get; private set; }
    /// <summary>Switch for the optional rows (the HRD Corp levy). Everything else is always in force.</summary>
    public bool             Active                { get; private set; }
    public bool             Verified              { get; private set; }
    public string?          VerifiedBy            { get; private set; }
    public DateTime?        VerifiedAt            { get; private set; }
    public string?          VerificationNote      { get; private set; }
    public string           Source                { get; private set; } = default!;
    public string?          Note                  { get; private set; }
    public DateTime         CreatedAt             { get; private set; }

    public IReadOnlyList<StatutoryRateBand> Bands => _bands;

    private StatutoryRateTable() { }

    private static readonly string[] Classes = ["citizen", "permanent-resident"];

    public static StatutoryRateTable CreatePercentage(string scheme, string citizenshipClass, DateOnly effectiveFrom,
        decimal employeeRate, decimal employerRate, RoundingRule rounding, string source,
        int ageFrom = 0, int? ageTo = null, decimal? ceiling = null,
        EmployerRateMode mode = EmployerRateMode.Flat, decimal? threshold = null, decimal? above = null,
        bool active = true, string? note = null)
    {
        var t = Base(scheme, citizenshipClass, effectiveFrom, RateKind.Percentage, rounding, source, ageFrom, ageTo, ceiling, active, note);
        if (scheme == StatutoryScheme.MinimumWage)
            throw new ArgumentException("The minimum wage is an amount: use CreateMinimumWage.");
        if (employeeRate is < 0m or > 1m || employerRate is < 0m or > 1m)
            throw new ArgumentException("A rate must be between 0 and 1 (0% to 100%).");
        if (mode == EmployerRateMode.WholeWageByThreshold)
        {
            if (threshold is not > 0m) throw new ArgumentException("A threshold rule needs a threshold above zero.");
            if (above is null or < 0m or > 1m) throw new ArgumentException("A threshold rule needs the employer rate above the threshold, between 0 and 1.");
        }
        t.EmployeeRate = employeeRate;
        t.EmployerRate = employerRate;
        t.EmployerRateMode = mode;
        t.EmployerRateThreshold = threshold;
        t.EmployerRateAbove = above;
        return t;
    }

    public static StatutoryRateTable CreateBandTable(string scheme, string citizenshipClass, DateOnly effectiveFrom,
        IReadOnlyList<RateBandRule> bands, RoundingRule rounding, string source,
        int ageFrom = 0, int? ageTo = null, decimal? ceiling = null, bool active = true, string? note = null)
    {
        var t = Base(scheme, citizenshipClass, effectiveFrom, RateKind.BandTable, rounding, source, ageFrom, ageTo, ceiling, active, note);
        if (bands.Count == 0) throw new ArgumentException("A band table needs at least one band.");
        var ordered = bands.OrderBy(b => b.WageFrom).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var b = ordered[i];
            if (b.WageFrom < 0m || b.WageTo <= b.WageFrom) throw new ArgumentException("Each band must start at zero or more and end above where it starts.");
            if (b.EmployeeAmount < 0m || b.EmployerAmount < 0m) throw new ArgumentException("A band amount cannot be negative.");
            if (i > 0 && b.WageFrom < ordered[i - 1].WageTo) throw new ArgumentException("Bands must not overlap.");
            t._bands.Add(new StatutoryRateBand(t.Id, b.WageFrom, b.WageTo, b.EmployeeAmount, b.EmployerAmount));
        }
        return t;
    }

    public static StatutoryRateTable CreateMinimumWage(string citizenshipClass, DateOnly effectiveFrom, decimal amount, string source,
        int ageFrom = 0, int? ageTo = null, string? note = null)
    {
        var t = Base(StatutoryScheme.MinimumWage, citizenshipClass, effectiveFrom, RateKind.Percentage, RoundingRule.None, source, ageFrom, ageTo, null, true, note);
        if (amount <= 0m) throw new ArgumentException("The minimum wage must be more than zero.");
        t.Amount = amount;
        return t;
    }

    private static StatutoryRateTable Base(string scheme, string citizenshipClass, DateOnly effectiveFrom, RateKind kind,
        RoundingRule rounding, string source, int ageFrom, int? ageTo, decimal? ceiling, bool active, string? note)
    {
        if (!StatutoryScheme.All.Contains(scheme)) throw new ArgumentException($"'{scheme}' is not a statutory scheme.");
        if (!Classes.Contains(citizenshipClass)) throw new ArgumentException("A rate row is for a citizen or a permanent resident.");
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("A rate row must say where its figures came from.");
        if (ageFrom < 0 || (ageTo is { } to && to < ageFrom)) throw new ArgumentException("The age range is not valid.");
        if (ceiling is <= 0m) throw new ArgumentException("A wage ceiling must be more than zero.");
        return new StatutoryRateTable
        {
            Id = Guid.NewGuid(),
            Scheme = scheme,
            CitizenshipClass = citizenshipClass,
            AgeFrom = ageFrom,
            AgeTo = ageTo,
            EffectiveFrom = effectiveFrom,
            Kind = kind,
            WageCeiling = ceiling,
            Rounding = rounding,
            Source = source.Trim(),
            Active = active,
            Note = note,
            CreatedAt = DateTime.UtcNow,
        };
    }

    /// <summary>The accountant confirms this row against the official table. A row can be verified once.</summary>
    public void Verify(string by, DateTime at, string note)
    {
        if (string.IsNullOrWhiteSpace(by)) throw new ArgumentException("Verifying needs to say who verified.");
        if (string.IsNullOrWhiteSpace(note)) throw new ArgumentException("Verifying needs a note saying what it was checked against.");
        if (Verified) throw new InvalidOperationException("This rate row is already verified.");
        Verified = true;
        VerifiedBy = by.Trim();
        VerifiedAt = at;
        VerificationNote = note.Trim();
    }

    public void SetActive(bool active) => Active = active;

    /// <summary>The plain, database-free view of this row that the payroll calculator takes.</summary>
    public RateRule ToRule() => new(Id, Scheme, CitizenshipClass, AgeFrom, AgeTo, EffectiveFrom, Kind, WageCeiling,
        EmployeeRate, EmployerRate, EmployerRateMode, EmployerRateThreshold, EmployerRateAbove, Rounding, Amount, Active, Verified,
        // A database does not promise to load child rows in any order: hand the calculator bands in wage order.
        _bands.OrderBy(b => b.WageFrom).Select(b => new RateBandRule(b.WageFrom, b.WageTo, b.EmployeeAmount, b.EmployerAmount)).ToList());
}

/// <summary>One wage band of a band-table row (inclusive from, exclusive to). Owned by its table.</summary>
public class StatutoryRateBand
{
    public Guid    Id             { get; private set; }
    public Guid    RateTableId    { get; private set; }
    public decimal WageFrom       { get; private set; }
    public decimal WageTo         { get; private set; }
    public decimal EmployeeAmount { get; private set; }
    public decimal EmployerAmount { get; private set; }

    private StatutoryRateBand() { }

    internal StatutoryRateBand(Guid rateTableId, decimal wageFrom, decimal wageTo, decimal employeeAmount, decimal employerAmount)
    {
        Id = Guid.NewGuid();
        RateTableId = rateTableId;
        WageFrom = wageFrom;
        WageTo = wageTo;
        EmployeeAmount = employeeAmount;
        EmployerAmount = employerAmount;
    }
}

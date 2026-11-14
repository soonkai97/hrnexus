namespace HRNexus.Domain.Payroll;

/// <summary>How a scheme's contribution is worked out.</summary>
public enum RateKind
{
    /// <summary>A percentage of the wage base (placeholders until the accountant supplies the official tables).</summary>
    Percentage = 0,

    /// <summary>A fixed employee and employer amount per wage band (the shape of the official EPF, SOCSO and EIS schedules).</summary>
    BandTable = 1,
}

/// <summary>How the employer rate is chosen.</summary>
public enum EmployerRateMode
{
    /// <summary>One employer rate for every wage.</summary>
    Flat = 0,

    /// <summary>EPF: the rate is chosen by whether the wage base is above the threshold, then applied to the WHOLE wage base (assumed, not marginal).</summary>
    WholeWageByThreshold = 1,
}

/// <summary>The statutory schemes and tables payroll uses (rate-table <c>Scheme</c> values).</summary>
public static class StatutoryScheme
{
    public const string Epf = "epf";
    public const string Socso = "socso";
    public const string Eis = "eis";
    public const string HrdCompulsory = "hrd-compulsory";
    public const string HrdOptional = "hrd-optional";
    public const string MinimumWage = "minimum-wage";

    public static readonly IReadOnlyList<string> All = [Epf, Socso, Eis, HrdCompulsory, HrdOptional, MinimumWage];
}

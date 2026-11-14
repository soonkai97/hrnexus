namespace HRNexus.Domain.Payroll;

/// <summary>How a statutory amount is rounded. Declared per scheme in its rate table, never in code.</summary>
public enum RoundingRule
{
    /// <summary>Keep every decimal.</summary>
    None = 0,

    /// <summary>To the nearest sen; a midpoint goes away from zero.</summary>
    NearestCent = 1,

    /// <summary>To the nearest 5 sen; a midpoint goes up.</summary>
    Nearest5Sen = 2,

    /// <summary>Up to the next whole ringgit; a whole ringgit is unchanged.</summary>
    UpToNextRinggit = 3,
}

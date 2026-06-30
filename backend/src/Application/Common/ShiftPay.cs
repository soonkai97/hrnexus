using HRNexus.Domain.Constants;

namespace HRNexus.Application.Common;

public static class ShiftPay
{
    /// <summary>
    /// Gross pay for a single shift given its captured pay type, worked hours, and rate snapshot.
    /// "fixed" pays the flat <paramref name="rate"/> per shift regardless of hours (e.g. an overnight
    /// package); "hourly" (the default) pays hours × rate. Callers pass the WorkLog's
    /// <c>PayTypeSnapshot</c> + <c>HourlyRateSnapshot</c> so historical shifts keep the pricing model
    /// they were logged under.
    /// </summary>
    public static decimal Calc(string payType, decimal hours, decimal rate) =>
        string.Equals(payType, AppConstants.PayType.Fixed, StringComparison.OrdinalIgnoreCase)
            ? rate
            : hours * rate;
}

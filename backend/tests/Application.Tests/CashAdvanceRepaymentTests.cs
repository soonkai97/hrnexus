using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// WHY these tests matter: an advance is settled exactly when what was really deducted reaches what was lent.
/// A repayment of zero or below, or a monthly schedule larger than the advance, would let the status drift away
/// from the money, and nothing in the system moves real money to correct it.
/// </summary>
public class CashAdvanceRepaymentTests
{
    private static E.CashAdvance Advance(decimal amount = 500m) =>
        E.CashAdvance.Create(Guid.NewGuid(), amount, new DateOnly(2026, 10, 2), "Rent", "admin");

    [Fact]
    public void A_repayment_records_the_run_the_advance_and_the_amount_really_deducted()
    {
        var advanceId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        var r = E.CashAdvanceRepayment.Create(advanceId, runId, 120.50m, new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(advanceId, r.CashAdvanceId);
        Assert.Equal(runId, r.RunId);
        Assert.Equal(120.50m, r.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_repayment_must_be_more_than_zero(int amount) =>
        Assert.Throws<ArgumentException>(() => E.CashAdvanceRepayment.Create(Guid.NewGuid(), Guid.NewGuid(), amount, DateTime.UtcNow));

    [Fact]
    public void An_advance_has_no_monthly_schedule_until_one_is_set()
    {
        Assert.Null(Advance().RepaymentPerMonth);
    }

    [Fact]
    public void A_monthly_repayment_can_be_set_and_cleared()
    {
        var advance = Advance(500m);

        advance.SetRepaymentPerMonth(100m);
        Assert.Equal(100m, advance.RepaymentPerMonth);

        advance.SetRepaymentPerMonth(null);
        Assert.Null(advance.RepaymentPerMonth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(501)]
    public void A_monthly_repayment_must_be_more_than_zero_and_no_more_than_the_advance(int perMonth) =>
        Assert.Throws<ArgumentException>(() => Advance(500m).SetRepaymentPerMonth(perMonth));
}

using HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// A pay item is a recurring allowance or deduction on one person's pay (a transport allowance, a parking fee).
///
/// WHY these tests matter: the amount feeds every payslip until the item ends, and it counts towards EPF and
/// SOCSO only when the flags say so. A negative amount, an item that ends before it starts, or a deduction that
/// claims to count towards EPF would silently misprice pay for months.
/// </summary>
public class PayItemTests
{
    private static readonly Guid Employee = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 3, 1);

    private static PayItem Allowance(decimal amount = 150m, DateOnly? to = null, bool epf = true, bool socso = true) =>
        PayItem.Create(Employee, PayItemKind.Allowance, "Transport", amount, epf, socso, 0, From, to, "admin");

    [Fact]
    public void An_allowance_keeps_what_it_was_created_with()
    {
        var item = Allowance(150m, new DateOnly(2026, 12, 31), epf: true, socso: false);

        Assert.Equal(Employee, item.EmployeeId);
        Assert.Equal("allowance", item.Kind);
        Assert.Equal("Transport", item.Reason);
        Assert.Equal(150m, item.Amount);
        Assert.True(item.CountsForEpf);
        Assert.False(item.CountsForSocsoEis);
        Assert.Equal(From, item.From);
        Assert.Equal(new DateOnly(2026, 12, 31), item.To);
    }

    /// <summary>A deduction takes pay away; whether it "counts for EPF" is meaningless, so it can never be set.</summary>
    [Fact]
    public void A_deduction_never_counts_towards_contributions_and_keeps_its_priority()
    {
        var item = PayItem.Create(Employee, PayItemKind.Deduction, "Parking", 50m, true, true, 3, From, null, "admin");

        Assert.False(item.CountsForEpf);
        Assert.False(item.CountsForSocsoEis);
        Assert.Equal(3, item.Priority);
    }

    [Fact]
    public void An_allowance_has_no_priority()
    {
        var item = PayItem.Create(Employee, PayItemKind.Allowance, "Transport", 50m, true, true, 9, From, null, "admin");

        Assert.Equal(0, item.Priority);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1_000_001)]
    public void An_amount_that_is_not_positive_or_is_absurd_is_refused(int amount) =>
        Assert.Throws<ArgumentException>(() => Allowance(amount));

    [Fact]
    public void An_item_needs_a_reason_and_a_known_kind()
    {
        Assert.Throws<ArgumentException>(() => PayItem.Create(Employee, PayItemKind.Allowance, " ", 10m, true, true, 0, From, null, "a"));
        Assert.Throws<ArgumentException>(() => PayItem.Create(Employee, "bonus", "x", 10m, true, true, 0, From, null, "a"));
        Assert.Throws<ArgumentException>(() => PayItem.Create(Employee, PayItemKind.Allowance, new string('x', 201), 10m, true, true, 0, From, null, "a"));
    }

    [Fact]
    public void An_item_cannot_end_before_it_starts() =>
        Assert.Throws<ArgumentException>(() => Allowance(to: From.AddDays(-1)));

    [Theory]
    [InlineData(2026, 2, false)]    // before it starts
    [InlineData(2026, 3, true)]     // the month it starts
    [InlineData(2026, 8, true)]     // open-ended
    public void An_open_ended_item_applies_from_the_month_it_starts(int year, int month, bool applies) =>
        Assert.Equal(applies, Allowance().AppliesInMonth(year, month));

    [Theory]
    [InlineData(2026, 4, true)]     // the month it ends in still counts
    [InlineData(2026, 5, false)]    // after it ends
    public void An_item_applies_up_to_and_including_the_month_it_ends_in(int year, int month, bool applies) =>
        Assert.Equal(applies, Allowance(to: new DateOnly(2026, 4, 10)).AppliesInMonth(year, month));
}

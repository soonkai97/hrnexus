using NightclubWorkforce.Domain.Entities;
using Xunit;

namespace NightclubWorkforce.Application.Tests;

/// <summary>
/// Domain entity tests for staff ledger entries, expenses, and loans.
/// These rules matter because the ledger is the financial source of truth for staff payouts.
/// </summary>
public class StaffLedgerDomainTests
{
    private static Guid SomeStaff() => Guid.NewGuid();

    // ── StaffEntry ─────────────────────────────────────────────────────────────

    [Fact]
    public void StaffEntry_Create_SetsAllFields()
    {
        var staffId   = SomeStaff();
        var entryDate = new DateOnly(2025, 6, 15);
        var entry     = StaffEntry.Create(staffId, entryDate, "VENUECLOCK", 3, 50m, 150m, 0m, 0m, "PENDING", null);

        Assert.Equal(staffId,   entry.StaffId);
        Assert.Equal(entryDate, entry.EntryDate);
        Assert.Equal("VENUECLOCK", entry.WorkType);
        Assert.Equal(3,   entry.VenueUnits);
        Assert.Equal(50m, entry.VenueRate);
        Assert.Equal(150m, entry.VenueEarnings);
        Assert.Equal("PENDING", entry.Settlement);
    }

    [Fact]
    public void StaffEntry_Create_WorkTypeIsUppercased()
    {
        var entry = StaffEntry.Create(SomeStaff(), DateOnly.FromDateTime(DateTime.Today), "venueclock", 1, 50m, 50m, 0m, 0m, "PENDING", null);
        Assert.Equal("VENUECLOCK", entry.WorkType);
    }

    [Fact]
    public void StaffEntry_Create_SettlementIsUppercased()
    {
        var entry = StaffEntry.Create(SomeStaff(), DateOnly.FromDateTime(DateTime.Today), "K", 1, 50m, 50m, 0m, 0m, "pending", null);
        Assert.Equal("PENDING", entry.Settlement);
    }

    [Fact]
    public void StaffEntry_Update_ChangesSettlement()
    {
        var entry = StaffEntry.Create(SomeStaff(), DateOnly.FromDateTime(DateTime.Today), "K", 1, 50m, 50m, 0m, 0m, "PENDING", null);
        entry.Update(DateOnly.FromDateTime(DateTime.Today), "K", 1, 50m, 50m, 0m, 0m, "SETTLED", "note");
        Assert.Equal("SETTLED", entry.Settlement);
        Assert.Equal("note", entry.Notes);
    }

    // ── StaffExpense ───────────────────────────────────────────────────────────

    [Fact]
    public void StaffExpense_Create_SetsFields()
    {
        var staffId = SomeStaff();
        var expense = StaffExpense.Create(staffId, 2025, 6, "MEAL", 50m, "Dinner");

        Assert.Equal(staffId, expense.StaffId);
        Assert.Equal(2025, expense.Year);
        Assert.Equal(6,    expense.Month);
        Assert.Equal("MEAL", expense.Category);
        Assert.Equal(50m, expense.Amount);
        Assert.Equal("Dinner", expense.Notes);
    }

    [Fact]
    public void StaffExpense_Create_CategoryIsUppercased()
    {
        var expense = StaffExpense.Create(SomeStaff(), 2025, 6, "meal", 10m, null);
        Assert.Equal("MEAL", expense.Category);
    }

    [Fact]
    public void StaffExpense_Update_ChangesAmount()
    {
        var expense = StaffExpense.Create(SomeStaff(), 2025, 6, "MEAL", 50m, null);
        expense.Update("MEAL", 75m, "Updated");
        Assert.Equal(75m, expense.Amount);
        Assert.Equal("Updated", expense.Notes);
    }

    // ── StaffLoan ──────────────────────────────────────────────────────────────

    [Fact]
    public void StaffLoan_Create_SetsAllFields()
    {
        var staffId  = SomeStaff();
        var loanDate = new DateOnly(2025, 6, 1);
        var loan     = StaffLoan.Create(staffId, loanDate, "ADVANCE", 500m, "Emergency");

        Assert.Equal(staffId,  loan.StaffId);
        Assert.Equal(loanDate, loan.LoanDate);
        Assert.Equal("ADVANCE", loan.LoanType);
        Assert.Equal(500m, loan.Amount);
        Assert.Equal("Emergency", loan.Purpose);
    }

    [Fact]
    public void StaffLoan_Create_LoanTypeIsUppercased()
    {
        var loan = StaffLoan.Create(SomeStaff(), DateOnly.FromDateTime(DateTime.Today), "advance", 100m, null);
        Assert.Equal("ADVANCE", loan.LoanType);
    }

    [Fact]
    public void StaffLoan_Create_NullPurposeIsAllowed()
    {
        var loan = StaffLoan.Create(SomeStaff(), DateOnly.FromDateTime(DateTime.Today), "REPAYMENT", 200m, null);
        Assert.Null(loan.Purpose);
    }

    [Theory]
    [InlineData("ADVANCE",   500)]
    [InlineData("REPAYMENT", 200)]
    [InlineData("CUSTOM",    999)]
    public void StaffLoan_Create_AcceptsAnyLoanTypeString(string loanType, decimal amount)
    {
        // LoanType is stored as-is — validation of valid types is done at the API layer
        var loan = StaffLoan.Create(SomeStaff(), DateOnly.FromDateTime(DateTime.Today), loanType, amount, null);
        Assert.Equal(loanType, loan.LoanType);
        Assert.Equal(amount, loan.Amount);
    }
}

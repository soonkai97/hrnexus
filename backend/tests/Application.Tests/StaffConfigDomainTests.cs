using HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Domain entity tests for staff configuration types.
/// These rules matter because incorrect defaults could cause silent data corruption
/// (e.g. a work type marked inactive still appearing in dropdowns).
/// </summary>
public class StaffConfigDomainTests
{
    // ── StaffWorkType ──────────────────────────────────────────────────────────

    [Fact]
    public void WorkType_Create_KeyIsUppercased()
    {
        var wt = StaffWorkType.Create("venueclock", "写钟", false, false, 0);
        Assert.Equal("VENUECLOCK", wt.Key);
    }

    [Fact]
    public void WorkType_Create_DefaultIsActiveTrue()
    {
        var wt = StaffWorkType.Create("K", "Label", false, false, 0);
        Assert.True(wt.IsActive);
    }

    [Fact]
    public void WorkType_Create_StoresToggles()
    {
        var wt = StaffWorkType.Create("K", "Label", autoCalc: true, hasPrivate: true, displayOrder: 5);
        Assert.True(wt.AutoCalc);
        Assert.True(wt.HasPrivate);
        Assert.Equal(5, wt.DisplayOrder);
    }

    [Fact]
    public void WorkType_Update_ChangesLabelOrderActive()
    {
        var wt = StaffWorkType.Create("K", "Old", false, false, 0);
        wt.Update("New Label", false, false, 3, false);
        Assert.Equal("New Label", wt.Label);
        Assert.Equal(3, wt.DisplayOrder);
        Assert.False(wt.IsActive);
    }

    [Fact]
    public void WorkType_Create_KeyIsTrimmed()
    {
        var wt = StaffWorkType.Create("  TABLERUN  ", "跑台", false, false, 0);
        Assert.Equal("TABLERUN", wt.Key);
    }

    // ── StaffSettlement ────────────────────────────────────────────────────────

    [Fact]
    public void Settlement_Create_KeyIsUppercased()
    {
        var st = StaffSettlement.Create("settled", "清", "success", 0);
        Assert.Equal("SETTLED", st.Key);
    }

    [Fact]
    public void Settlement_Create_DefaultIsActiveTrue()
    {
        var st = StaffSettlement.Create("K", "Label", "secondary", 0);
        Assert.True(st.IsActive);
    }

    [Fact]
    public void Settlement_Update_ChangesSeverity()
    {
        var st = StaffSettlement.Create("K", "Label", "secondary", 0);
        st.Update("New Label", "danger", 2, false);
        Assert.Equal("danger", st.Severity);
        Assert.Equal("New Label", st.Label);
        Assert.False(st.IsActive);
    }

    // ── StaffExpenseCategory ──────────────────────────────────────────────────

    [Fact]
    public void ExpenseCategory_Create_KeyIsUppercased()
    {
        var ec = StaffExpenseCategory.Create("meal", "Meal Subsidy", 0);
        Assert.Equal("MEAL", ec.Key);
    }

    [Fact]
    public void ExpenseCategory_Create_DefaultIsActiveTrue()
    {
        var ec = StaffExpenseCategory.Create("K", "Label", 0);
        Assert.True(ec.IsActive);
    }

    [Fact]
    public void ExpenseCategory_Update_ChangesLabel()
    {
        var ec = StaffExpenseCategory.Create("MEAL", "Old", 0);
        ec.Update("New Label", 5, false);
        Assert.Equal("New Label", ec.Label);
        Assert.Equal(5, ec.DisplayOrder);
        Assert.False(ec.IsActive);
    }

    // ── StaffLoanType ──────────────────────────────────────────────────────────

    [Fact]
    public void LoanType_Create_KeyIsUppercased()
    {
        var lt = StaffLoanType.Create("advance", "Advance", true, 0);
        Assert.Equal("ADVANCE", lt.Key);
    }

    [Fact]
    public void LoanType_Create_SystemFlag_IsFalseByDefault()
    {
        var lt = StaffLoanType.Create("CUSTOM", "Custom", true, 0);
        Assert.False(lt.IsSystem);
    }

    [Fact]
    public void LoanType_Create_WithSystemFlag_IsSystemTrue()
    {
        var lt = StaffLoanType.Create("ADVANCE", "Advance", true, 0, isSystem: true);
        Assert.True(lt.IsSystem);
    }

    [Fact]
    public void LoanType_Create_IsAdvanceFalse_RepresentsCashIn()
    {
        // REPAYMENT: money coming in reduces the outstanding loan balance
        var lt = StaffLoanType.Create("REPAYMENT", "Repayment", isAdvance: false, 1);
        Assert.False(lt.IsAdvance);
    }

    [Fact]
    public void LoanType_Update_DoesNotChangeIsAdvanceOrIsSystem()
    {
        // IsAdvance and IsSystem are structural — changing them would corrupt existing loan records
        var lt = StaffLoanType.Create("ADVANCE", "Old Label", isAdvance: true, 0, isSystem: true);
        lt.Update("New Label", 5, false);
        Assert.True(lt.IsAdvance);   // unchanged
        Assert.True(lt.IsSystem);    // unchanged
        Assert.Equal("New Label", lt.Label);
        Assert.Equal(5, lt.DisplayOrder);
        Assert.False(lt.IsActive);
    }

    [Fact]
    public void LoanType_Update_LabelIsTrimmed()
    {
        var lt = StaffLoanType.Create("K", "Old", true, 0);
        lt.Update("  Trimmed  ", 0, true);
        Assert.Equal("Trimmed", lt.Label);
    }

    [Theory]
    [InlineData(true,  "Cash Out — increases balance")]
    [InlineData(false, "Cash In — decreases balance")]
    public void LoanType_Create_IsAdvanceSetCorrectly(bool isAdvance, string description)
    {
        var lt = StaffLoanType.Create("K", description, isAdvance, 0);
        Assert.Equal(isAdvance, lt.IsAdvance);
    }
}

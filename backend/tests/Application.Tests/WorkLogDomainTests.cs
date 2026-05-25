using E = HRNexus.Domain.Entities;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Domain entity tests for WorkLog — live clock-in, manual entry, and edit operations.
/// These rules matter because shift logs are the attendance and compliance record.
/// </summary>
public class WorkLogDomainTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Guid   SomeEmployee()  => Guid.NewGuid();
    private static DateOnly Today()       => DateOnly.FromDateTime(DateTime.UtcNow);

    // ── StartLive ─────────────────────────────────────────────────────────────

    [Fact]
    public void StartLive_SetsAllFields()
    {
        var employeeId   = SomeEmployee();
        var workDate     = Today();
        var log = E.WorkLog.StartLive(employeeId, "bartender", "LOC-1", "Main Bar",
            workDate, "20:00", 15m, "Opening shift");

        Assert.Equal(employeeId,    log.EmployeeId);
        Assert.Equal("Main Bar",    log.LocationName);
        Assert.Equal("LOC-1",       log.LocationId);
        Assert.Equal(workDate,      log.WorkDate);
        Assert.Equal("20:00",       log.StartTime);
        Assert.Equal(15m,           log.HourlyRateSnapshot);
        Assert.Equal("Opening shift", log.Notes);
    }

    [Fact]
    public void StartLive_EndTime_IsNull()
    {
        // An active live shift must have no end time — it is still in progress.
        var log = E.WorkLog.StartLive(SomeEmployee(), "BARTENDER", "L1", "Bar",
            Today(), "21:00", 15m);

        Assert.Null(log.EndTime);
    }

    [Fact]
    public void StartLive_IsManualEntry_False()
    {
        // Live clock-ins are never manual entries; they are real-time attendance records.
        var log = E.WorkLog.StartLive(SomeEmployee(), "BARTENDER", "L1", "Bar",
            Today(), "21:00", 15m);

        Assert.False(log.IsManualEntry);
    }

    [Fact]
    public void StartLive_RoleName_IsUppercased()
    {
        // Role names are normalised to uppercase so comparisons are case-insensitive at rest.
        var log = E.WorkLog.StartLive(SomeEmployee(), "bartender", "L1", "Bar",
            Today(), "21:00", 15m);

        Assert.Equal("BARTENDER", log.RoleName);
    }

    [Fact]
    public void StartLive_AssignsNewId()
    {
        // Every new shift must have a unique non-empty identifier.
        var log = E.WorkLog.StartLive(SomeEmployee(), "BARTENDER", "L1", "Bar",
            Today(), "21:00", 15m);

        Assert.NotEqual(Guid.Empty, log.Id);
    }

    [Fact]
    public void StartLive_IsEdited_False()
    {
        // A freshly created shift has never been edited.
        var log = E.WorkLog.StartLive(SomeEmployee(), "BARTENDER", "L1", "Bar",
            Today(), "21:00", 15m);

        Assert.False(log.IsEdited);
    }

    [Fact]
    public void StartLive_IsDeleted_False()
    {
        // Shifts must be soft-deleted explicitly — they should not start deleted.
        var log = E.WorkLog.StartLive(SomeEmployee(), "BARTENDER", "L1", "Bar",
            Today(), "21:00", 15m);

        Assert.False(log.IsDeleted);
    }

    // ── CreateManual ──────────────────────────────────────────────────────────

    [Fact]
    public void CreateManual_IsManualEntry_True()
    {
        // Manual entries must be flagged so managers can distinguish back-fills from live logs.
        var log = E.WorkLog.CreateManual(SomeEmployee(), "WAITER", "L2", "VIP Room",
            Today(), "19:00", "23:00", 12m);

        Assert.True(log.IsManualEntry);
    }

    [Fact]
    public void CreateManual_SetsEndTime()
    {
        // Manual entries must always record the end time provided by the manager.
        var log = E.WorkLog.CreateManual(SomeEmployee(), "WAITER", "L2", "VIP Room",
            Today(), "19:00", "23:00", 12m);

        Assert.Equal("23:00", log.EndTime);
    }

    [Fact]
    public void CreateManual_RoleName_IsUppercased()
    {
        var log = E.WorkLog.CreateManual(SomeEmployee(), "waiter", "L2", "VIP Room",
            Today(), "19:00", "23:00", 12m);

        Assert.Equal("WAITER", log.RoleName);
    }

    // ── End ───────────────────────────────────────────────────────────────────

    [Fact]
    public void End_SetsEndTime()
    {
        // Clocking out must record the exact end time so duration can be calculated.
        var log = E.WorkLog.StartLive(SomeEmployee(), "BARTENDER", "L1", "Bar",
            Today(), "20:00", 15m);

        log.End("23:00");

        Assert.Equal("23:00", log.EndTime);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_SetsIsEdited_True()
    {
        // Any manager correction must be flagged so auditors can identify altered records.
        var log = E.WorkLog.CreateManual(SomeEmployee(), "BARTENDER", "L1", "Bar",
            Today(), "20:00", "23:00", 15m);

        log.Update("BARTENDER", "L1", "Bar", Today(), "20:00", "23:30", null);

        Assert.True(log.IsEdited);
    }

    [Fact]
    public void Update_SetsEditedAt_NotNull()
    {
        // EditedAt must be recorded so the audit trail shows when the change occurred.
        var log = E.WorkLog.CreateManual(SomeEmployee(), "BARTENDER", "L1", "Bar",
            Today(), "20:00", "23:00", 15m);

        log.Update("BARTENDER", "L1", "Bar", Today(), "20:00", "23:30", null);

        Assert.NotNull(log.EditedAt);
    }

    [Fact]
    public void Update_ChangesRoleAndLocation()
    {
        // Update must persist the new role and location supplied by the manager.
        var log = E.WorkLog.CreateManual(SomeEmployee(), "BARTENDER", "L1", "Main Bar",
            Today(), "20:00", "23:00", 15m);

        log.Update("WAITER", "L2", "VIP Room", Today(), "20:00", "23:00", null);

        Assert.Equal("WAITER",    log.RoleName);
        Assert.Equal("VIP Room",  log.LocationName);
    }

    [Fact]
    public void Update_PreservesEmployeeId()
    {
        // Update must never reassign a shift to a different employee.
        var employeeId = SomeEmployee();
        var log = E.WorkLog.CreateManual(employeeId, "BARTENDER", "L1", "Bar",
            Today(), "20:00", "23:00", 15m);

        log.Update("WAITER", "L2", "VIP Room", Today(), "20:00", "23:00", null);

        Assert.Equal(employeeId, log.EmployeeId);
    }
}

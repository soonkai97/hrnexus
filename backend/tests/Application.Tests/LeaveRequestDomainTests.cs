using NightclubWorkforce.Domain.Entities;
using Xunit;

namespace NightclubWorkforce.Application.Tests;

/// <summary>
/// Unit tests for LeaveRequest domain entity methods.
/// No DB / infrastructure needed — pure domain logic.
/// </summary>
public class LeaveRequestDomainTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static LeaveRequest MakeLeave(string? notes = null) =>
        LeaveRequest.Create(
            Guid.NewGuid(), "annual",
            DateTime.UtcNow.AddDays(5),
            DateTime.UtcNow.AddDays(7),
            null, null, null, notes);

    // ── Create defaults ───────────────────────────────────────────────────────

    [Fact]
    public void Create_DefaultStatus_IsPending()
    {
        var leave = MakeLeave();
        Assert.Equal("pending", leave.Status);
    }

    [Fact]
    public void Create_LeaveType_IsLowerCased()
    {
        var leave = LeaveRequest.Create(
            Guid.NewGuid(), "ANNUAL",
            DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2), null, null, null, null);
        Assert.Equal("annual", leave.LeaveType);
    }

    [Fact]
    public void Create_AssignsNewId()
    {
        var a = MakeLeave();
        var b = MakeLeave();
        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.NotEqual(a.Id, b.Id);
    }

    // ── Update status ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("APPROVED", "approved")]
    [InlineData("Rejected", "rejected")]
    [InlineData("PENDING",  "pending")]
    public void Update_Status_AlwaysLowerCases(string input, string expected)
    {
        var leave = MakeLeave();
        leave.Update(leave.LeaveType, leave.StartDate, leave.EndDate, leave.StartTime, leave.EndTime, leave.Hours, leave.Notes, input);
        Assert.Equal(expected, leave.Status);
    }

    // ── AutoReject ────────────────────────────────────────────────────────────

    [Fact]
    public void AutoReject_SetsStatusToRejected()
    {
        var leave = MakeLeave();
        leave.AutoReject("Auto-rejected: overdue.");
        Assert.Equal("rejected", leave.Status);
    }

    [Fact]
    public void AutoReject_SetsNote_WhenNotesNull()
    {
        var leave = MakeLeave(notes: null);
        leave.AutoReject("System note");
        Assert.Equal("System note", leave.Notes);
    }

    [Fact]
    public void AutoReject_SetsNote_WhenNotesWhitespace()
    {
        var leave = MakeLeave(notes: "   ");

        // Notes is set via Create → null/whitespace are stored as-is.
        // AutoReject uses IsNullOrWhiteSpace, so it replaces whitespace too.
        // We verify the note becomes just the system note (no pipe prefix).
        leave.AutoReject("System note");
        Assert.Equal("System note", leave.Notes);
    }

    [Fact]
    public void AutoReject_AppendsNote_WhenNotesExist()
    {
        var leave = MakeLeave(notes: "Staff request");
        leave.AutoReject("Auto-rejected");
        Assert.Equal("Staff request | Auto-rejected", leave.Notes);
    }

    [Fact]
    public void AutoReject_CalledTwice_AppendsBothNotes()
    {
        // Second auto-reject (edge case) appends again
        var leave = MakeLeave(notes: null);
        leave.AutoReject("First");
        leave.AutoReject("Second");
        Assert.Equal("First | Second", leave.Notes);
        Assert.Equal("rejected", leave.Status);
    }

    [Fact]
    public void AutoReject_CanBeCalledOnApprovedLeave()
    {
        var leave = MakeLeave();
        leave.Update(leave.LeaveType, leave.StartDate, leave.EndDate, leave.StartTime, leave.EndTime, leave.Hours, leave.Notes, "approved");
        leave.AutoReject("No-show override");
        Assert.Equal("rejected", leave.Status);
    }
}

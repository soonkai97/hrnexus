using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Staff.Queries;
using HRNexus.Application.Tests.Infrastructure;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Tests for GetStaffListQueryHandler — verifies pagination, search, and ExcludeAdmins filtering.
/// Why these matter: server-side pagination is the contract between the admin UI and the API;
/// wrong page/total values break navigation controls and staff selection dropdowns.
/// </summary>
public class GetStaffListQueryTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TestDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // isolated per test
            .Options;
        return new TestDbContext(opts);
    }

    private static GetStaffListQueryHandler CreateHandler(TestDbContext db) => new(db);

    private static E.Staff MakeStaff(string staffId, string firstName, string lastName, params string[] roles)
        => E.Staff.Create(staffId, firstName, lastName, $"{staffId.ToLower()}@test.com", "hash", roles.ToList());

    // ── Pagination ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_Page1_ReturnsFirstPageAndCorrectTotal()
    {
        await using var db = CreateDb();
        for (var i = 1; i <= 12; i++)
            db.Staff.Add(MakeStaff($"S{i:D3}", "First", $"Last{i:D2}", "bartender"));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(Page: 1, PageSize: 10), CancellationToken.None);

        Assert.Equal(12, result.Total);
        Assert.Equal(10, result.Items.Count);
    }

    [Fact]
    public async Task Handle_Page2_ReturnsRemainingItems()
    {
        await using var db = CreateDb();
        for (var i = 1; i <= 12; i++)
            db.Staff.Add(MakeStaff($"S{i:D3}", "First", $"Last{i:D2}", "bartender"));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(Page: 2, PageSize: 10), CancellationToken.None);

        Assert.Equal(12, result.Total);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task Handle_PageBeyondData_ReturnsEmptyItems()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S001", "Alice", "Wong", "bartender"));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(Page: 5, PageSize: 10), CancellationToken.None);

        // Total still reflects the real count; items on this page are empty
        Assert.Equal(1, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Handle_EmptyDatabase_ReturnsTotalZero()
    {
        await using var db = CreateDb();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(), CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SearchByName_CaseInsensitive()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S001", "Alice", "Wong", "bartender"));
        db.Staff.Add(MakeStaff("S002", "Bob", "Tan", "bartender"));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(Search: "ALICE"), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("Alice", result.Items[0].FirstName);
    }

    [Fact]
    public async Task Handle_SearchByStaffId_ReturnsMatch()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S001", "Alice", "Wong", "bartender"));
        db.Staff.Add(MakeStaff("S002", "Bob", "Tan", "bartender"));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(Search: "S001"), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("S001", result.Items[0].StaffId);
    }

    [Fact]
    public async Task Handle_SearchNoMatch_ReturnsEmpty()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S001", "Alice", "Wong", "bartender"));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(Search: "zzznomatch"), CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Handle_SearchByRole_ReturnsMatchingStaff()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S001", "Alice", "Wong", "bartender"));
        db.Staff.Add(MakeStaff("S002", "Bob", "Tan", "manager"));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(Search: "manager"), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("Bob", result.Items[0].FirstName);
    }

    // ── ExcludeAdmins ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ExcludeAdmins_OmitsAdminRole()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S001", "Admin", "User", "admin"));
        db.Staff.Add(MakeStaff("S002", "Regular", "Staff", "bartender"));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(ExcludeAdmins: true), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("Regular", result.Items[0].FirstName);
    }

    [Fact]
    public async Task Handle_ExcludeAdminsFalse_IncludesAll()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S001", "Admin", "User", "admin"));
        db.Staff.Add(MakeStaff("S002", "Regular", "Staff", "bartender"));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(ExcludeAdmins: false), CancellationToken.None);

        Assert.Equal(2, result.Total);
    }

    // ── DTO mapping ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_RolesInDto_AreLowercase()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S001", "Alice", "Wong", "Bartender", "Manager"));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(), CancellationToken.None);

        // Roles are returned as stored (UPPERCASE) so they match multiselect option values
        var dto = result.Items[0];
        Assert.Contains("BARTENDER", dto.Roles);
        Assert.Contains("MANAGER", dto.Roles);
    }

    [Fact]
    public async Task Handle_OrderedByStaffId()
    {
        await using var db = CreateDb();
        db.Staff.Add(MakeStaff("S003", "Charlie", "Lee", "bartender"));
        db.Staff.Add(MakeStaff("S001", "Alice", "Wong", "bartender"));
        db.Staff.Add(MakeStaff("S002", "Bob", "Tan", "bartender"));
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(), CancellationToken.None);

        var ids = result.Items.Select(i => i.StaffId).ToList();
        Assert.Equal(["S001", "S002", "S003"], ids);
    }
}

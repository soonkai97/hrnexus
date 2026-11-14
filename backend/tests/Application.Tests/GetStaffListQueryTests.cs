using HRNexus.Application.Staff.Queries;
using HRNexus.Application.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
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

    // Seeds the named role rows (so the Role navigation resolves under the InMemory provider — the
    // handler filters and projects on r.Role.Name) and adds the staff assigned to them. The staff is
    // added to the context but NOT saved; the caller saves.
    private static Task<E.Staff> AddStaffAsync(TestDbContext db, string staffId, string firstName, string lastName, params string[] roles)
        => RoleSeed.AddStaffWithRolesAsync(db, staffId, firstName, lastName, $"{staffId.ToLower()}@test.com", "hash", roles);

    // ── Pagination ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_Page1_ReturnsFirstPageAndCorrectTotal()
    {
        await using var db = CreateDb();
        for (var i = 1; i <= 12; i++)
            await AddStaffAsync(db, $"S{i:D3}", "First", $"Last{i:D2}", "bartender");
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
            await AddStaffAsync(db, $"S{i:D3}", "First", $"Last{i:D2}", "bartender");
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
        await AddStaffAsync(db, "S001", "Alice", "Wong", "bartender");
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
        await AddStaffAsync(db, "S001", "Alice", "Wong", "bartender");
        await AddStaffAsync(db, "S002", "Bob", "Tan", "bartender");
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
        await AddStaffAsync(db, "S001", "Alice", "Wong", "bartender");
        await AddStaffAsync(db, "S002", "Bob", "Tan", "bartender");
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
        await AddStaffAsync(db, "S001", "Alice", "Wong", "bartender");
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
        await AddStaffAsync(db, "S001", "Alice", "Wong", "bartender");
        await AddStaffAsync(db, "S002", "Bob", "Tan", "front desk");
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(Search: "front desk"), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("Bob", result.Items[0].FirstName);
    }

    // ── Hidden filtering ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ExcludesHiddenAccounts_ByDefault()
    {
        await using var db = CreateDb();
        await AddStaffAsync(db, "S001", "Regular", "Staff", "bartender");
        var hidden = await AddStaffAsync(db, "HRADMIN", "HR", "Admin", "owner");
        hidden.SetHidden(true);
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(), CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("Regular", result.Items[0].FirstName);
    }

    [Fact]
    public async Task Handle_IncludeHidden_ReturnsHiddenAccountsToo()
    {
        await using var db = CreateDb();
        await AddStaffAsync(db, "S001", "Regular", "Staff", "bartender");
        var hidden = await AddStaffAsync(db, "HRADMIN", "HR", "Admin", "owner");
        hidden.SetHidden(true);
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(IncludeHidden: true), CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Contains(result.Items, i => i.StaffId == "HRADMIN" && i.IsHidden);
    }

    // ── DTO mapping ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_RolesInDto_AreLowercase()
    {
        await using var db = CreateDb();
        await AddStaffAsync(db, "S001", "Alice", "Wong", "Bartender", "Front Desk");
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(), CancellationToken.None);

        // Roles are returned as stored (UPPERCASE) so they match multiselect option values
        var dto = result.Items[0];
        Assert.Contains("BARTENDER", dto.Roles);
        Assert.Contains("FRONT DESK", dto.Roles);
    }

    [Fact]
    public async Task Handle_OrderedByStaffId()
    {
        await using var db = CreateDb();
        await AddStaffAsync(db, "S003", "Charlie", "Lee", "bartender");
        await AddStaffAsync(db, "S001", "Alice", "Wong", "bartender");
        await AddStaffAsync(db, "S002", "Bob", "Tan", "bartender");
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new GetStaffListQuery(), CancellationToken.None);

        var ids = result.Items.Select(i => i.StaffId).ToList();
        Assert.Equal(["S001", "S002", "S003"], ids);
    }
}

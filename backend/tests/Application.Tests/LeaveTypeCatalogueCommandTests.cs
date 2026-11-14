using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave;
using HRNexus.Application.Leave.Commands;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Leave;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Saving the company's leave types (Phase 7q): one save replaces the whole catalogue under one version token.
///
/// WHY these tests matter: the catalogue decides who is charged what, so a save must be all-or-nothing (a
/// half-applied catalogue could strand leave), must refuse a stale copy (two administrators must not overwrite
/// each other), and must never drop or rewire a type that already has leave (that would rewrite history).
/// </summary>
public class LeaveTypeCatalogueCommandTests
{
    private sealed class StubUser(string? name) : ICurrentUserService
    {
        public Guid? UserId => null;
        public string? UserName => name;
    }

    private sealed class RecordingAudit(IAppDbContext db) : IAuditService
    {
        public List<string> Rows { get; } = [];
        public async Task LogAsync(string et, string ei, string a, Guid? pid, string pn, string? c = null, CancellationToken ct = default)
        {
            Rows.Add($"{et}|{ei}|{a}|{c}");
            db.AuditLogs.Add(E.AuditLog.Create(et, ei, a, pid, pn, c));
            await db.SaveChangesAsync(ct);
        }
    }

    private static TestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SaveLeaveTypesCommandHandler Saver(TestDbContext db, RecordingAudit? audit = null, string? user = "admin.lim") =>
        new(db, audit ?? new RecordingAudit(db), new StubUser(user));

    private static LeaveTypeSpec ToSpec(E.LeaveTypeDefinition t) => new(t.Key, t.Label, t.Colour, t.SortOrder, t.Active, t.Pool,
        t.AllowanceDays, t.MaxDaysPerRequest, t.AutoApprove, t.AdvanceNoticeDays, t.AdvanceNoticeFromPolicy,
        t.DeductsPay, t.AdminOnly, t.CountsCalendarDays, t.Sensitive);

    private static List<LeaveTypeSpec> Defaults() => LeaveTypeDefaults.All().Select(ToSpec).ToList();

    private static LeaveTypeSpec Study(bool active = true) =>
        new("study", "Study", "info", 100, active, "none", null, 5m, false, null, false, false, false, false, false);

    private static async Task<int> VersionOf(TestDbContext db) =>
        (await new GetLeaveTypesQueryHandler(db).Handle(new GetLeaveTypesQuery(IncludeInactive: true), CancellationToken.None)).Version;

    // ── first save ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_first_save_writes_the_whole_catalogue_and_starts_the_version_at_1()
    {
        await using var db = CreateDb();
        Assert.Equal(0, await VersionOf(db));   // nothing saved: the code defaults apply

        var version = await Saver(db).Handle(new SaveLeaveTypesCommand(0, [.. Defaults(), Study()]), CancellationToken.None);

        Assert.Equal(1, version);
        Assert.Equal(10, await db.LeaveTypes.CountAsync());
        Assert.All(await db.LeaveTypes.AsNoTracking().ToListAsync(), t => Assert.Equal(1, t.CatalogVersion));
        Assert.Equal(1, await VersionOf(db));
    }

    [Fact]
    public async Task A_save_can_edit_deactivate_and_add_types_in_one_go()
    {
        await using var db = CreateDb();
        var specs = Defaults();
        var i = specs.FindIndex(s => s.Key == "paternity");
        specs[i] = specs[i] with { Active = false };
        var j = specs.FindIndex(s => s.Key == "unpaid");
        specs[j] = specs[j] with { Label = "Leave without pay" };

        await Saver(db).Handle(new SaveLeaveTypesCommand(0, [.. specs, Study()]), CancellationToken.None);

        var catalog = await LeaveCatalogReader.GetAsync(db, CancellationToken.None);
        Assert.False(catalog.Find("paternity")!.Active);
        Assert.Equal("Leave without pay", catalog.Find("unpaid")!.Label);
        Assert.Equal(5m, catalog.Find("study")!.MaxDaysPerRequest);
        Assert.True(catalog.IsSaved);
    }

    // ── version token ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Each_save_moves_the_version_on_by_one()
    {
        await using var db = CreateDb();
        await Saver(db).Handle(new SaveLeaveTypesCommand(0, Defaults()), CancellationToken.None);

        var second = await Saver(db).Handle(new SaveLeaveTypesCommand(1, [.. Defaults(), Study()]), CancellationToken.None);

        Assert.Equal(2, second);
        Assert.All(await db.LeaveTypes.AsNoTracking().ToListAsync(), t => Assert.Equal(2, t.CatalogVersion));
    }

    [Fact]
    public async Task A_save_made_from_a_stale_copy_is_refused_and_changes_nothing()
    {
        await using var db = CreateDb();
        await Saver(db).Handle(new SaveLeaveTypesCommand(0, Defaults()), CancellationToken.None);

        // A second administrator still holds version 0 and tries to add a type.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Saver(db).Handle(new SaveLeaveTypesCommand(0, [.. Defaults(), Study()]), CancellationToken.None));

        Assert.Contains("someone else", ex.Message);
        Assert.Equal(9, await db.LeaveTypes.CountAsync());
        Assert.Equal(1, await VersionOf(db));
    }

    // ── history is protected ─────────────────────────────────────────────────────

    private static async Task<Guid> AddLeaveOfType(TestDbContext db, string type)
    {
        var roleId = await RoleSeed.EnsureRoleAsync(db, "BARTENDER");
        var staff = E.Staff.Create("A1", "Test", "A1", "a1@t.com", "hash", new List<Guid> { roleId });
        db.Staff.Add(staff);
        db.LeaveRequests.Add(E.LeaveRequest.Create(staff.Id, type, new DateTime(2026, 11, 9), new DateTime(2026, 11, 9), null, null, 8m, null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return staff.Id;
    }

    [Fact]
    public async Task A_type_that_has_leave_cannot_be_dropped_from_the_catalogue()
    {
        await using var db = CreateDb();
        await AddLeaveOfType(db, "unpaid");

        var without = Defaults().Where(s => s.Key != "unpaid").ToList();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Saver(db).Handle(new SaveLeaveTypesCommand(0, without), CancellationToken.None));

        Assert.Contains("deactivate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.LeaveTypes.CountAsync());   // all or nothing
    }

    [Fact]
    public async Task A_type_without_leave_can_be_dropped_once_a_catalogue_exists()
    {
        await using var db = CreateDb();
        await Saver(db).Handle(new SaveLeaveTypesCommand(0, [.. Defaults(), Study()]), CancellationToken.None);

        await Saver(db).Handle(new SaveLeaveTypesCommand(1, Defaults()), CancellationToken.None);

        Assert.Null((await LeaveCatalogReader.GetAsync(db, CancellationToken.None)).Find("study"));
    }

    [Fact]
    public async Task Where_a_type_is_charged_cannot_change_once_it_has_leave()
    {
        await using var db = CreateDb();
        await AddLeaveOfType(db, "unpaid");
        var specs = Defaults();
        var i = specs.FindIndex(s => s.Key == "unpaid");
        specs[i] = specs[i] with { Pool = "own", AllowanceDays = 10m };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Saver(db).Handle(new SaveLeaveTypesCommand(0, specs), CancellationToken.None));
        Assert.Equal(0, await db.LeaveTypes.CountAsync());
    }

    [Fact]
    public async Task A_type_that_has_leave_can_still_be_renamed_and_deactivated()
    {
        await using var db = CreateDb();
        await AddLeaveOfType(db, "unpaid");
        var specs = Defaults();
        var i = specs.FindIndex(s => s.Key == "unpaid");
        specs[i] = specs[i] with { Label = "No pay", Active = false };

        await Saver(db).Handle(new SaveLeaveTypesCommand(0, specs), CancellationToken.None);

        var unpaid = (await LeaveCatalogReader.GetAsync(db, CancellationToken.None)).Find("unpaid")!;
        Assert.Equal("No pay", unpaid.Label);
        Assert.False(unpaid.Active);
    }

    [Theory]
    [InlineData("sick")]
    [InlineData("no-show")]
    public async Task A_system_type_cannot_be_dropped_or_deactivated(string key)
    {
        await using var db = CreateDb();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Saver(db).Handle(new SaveLeaveTypesCommand(0, Defaults().Where(s => s.Key != key).ToList()), CancellationToken.None));

        var specs = Defaults();
        var i = specs.FindIndex(s => s.Key == key);
        specs[i] = specs[i] with { Active = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Saver(db).Handle(new SaveLeaveTypesCommand(0, specs), CancellationToken.None));
        Assert.Equal(0, await db.LeaveTypes.CountAsync());
    }

    // ── validation and atomicity ─────────────────────────────────────────────────

    [Fact]
    public async Task A_repeated_key_or_an_invalid_type_refuses_the_whole_save()
    {
        await using var db = CreateDb();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Saver(db).Handle(new SaveLeaveTypesCommand(0, [.. Defaults(), Study(), Study()]), CancellationToken.None));
        var bad = Study() with { Pool = "own", AllowanceDays = null };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Saver(db).Handle(new SaveLeaveTypesCommand(0, [.. Defaults(), bad]), CancellationToken.None));

        Assert.Equal(0, await db.LeaveTypes.CountAsync());
    }

    [Fact]
    public async Task The_new_key_of_a_type_is_taken_exactly_so_it_matches_what_leave_stores()
    {
        await using var db = CreateDb();
        await Saver(db).Handle(new SaveLeaveTypesCommand(0, [.. Defaults(), Study() with { Key = " study " }]), CancellationToken.None);

        Assert.NotNull((await LeaveCatalogReader.GetAsync(db, CancellationToken.None)).Find("study"));
    }

    // ── audit ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_save_is_audited_with_who_and_which_types_and_no_limits_or_figures()
    {
        await using var db = CreateDb();
        var audit = new RecordingAudit(db);

        await Saver(db, audit).Handle(new SaveLeaveTypesCommand(0, [.. Defaults(), Study()]), CancellationToken.None);

        var row = Assert.Single(audit.Rows);
        Assert.StartsWith("LeaveTypes|", row);
        Assert.Contains("study", row);
        Assert.DoesNotContain("98", row);   // the limits are not copied into the trail
        Assert.Equal("admin.lim", (await db.LeaveTypes.AsNoTracking().FirstAsync()).UpdatedBy);
    }

    // ── reading ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Staff_see_only_active_types_and_the_settings_page_sees_all_with_which_have_leave()
    {
        await using var db = CreateDb();
        await AddLeaveOfType(db, "unpaid");
        var specs = Defaults();
        var i = specs.FindIndex(s => s.Key == "paternity");
        specs[i] = specs[i] with { Active = false };
        await Saver(db).Handle(new SaveLeaveTypesCommand(0, specs), CancellationToken.None);
        var query = new GetLeaveTypesQueryHandler(db);

        var staffView = await query.Handle(new GetLeaveTypesQuery(), CancellationToken.None);
        var settingsView = await query.Handle(new GetLeaveTypesQuery(IncludeInactive: true, WithUsage: true), CancellationToken.None);

        Assert.DoesNotContain(staffView.Types, t => t.Key == "paternity");
        Assert.Contains(settingsView.Types, t => t.Key == "paternity" && !t.Active);
        Assert.True(settingsView.Types.Single(t => t.Key == "unpaid").HasLeave);
        Assert.False(settingsView.Types.Single(t => t.Key == "annual").HasLeave);
        Assert.DoesNotContain(staffView.Types, t => t.HasLeave);   // usage is only worked out for the settings page
    }
}

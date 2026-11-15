using HRNexus.Application.Common;
using HRNexus.Application.Staff.Commands;
using HRNexus.Application.Staff.Queries;
using HRNexus.Application.Tests.Infrastructure;
using HRNexus.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

/// <summary>
/// Who reports to whom.
///
/// WHY these tests matter: the reporting line decides whose pay a manager may read and sign off. A loop would let two people
/// sign each other's pay and leave nobody at the top; an inactive or deleted manager would leave their reports waiting for a
/// signature that can never come; and anyone able to edit the line could read everyone's pay by making themselves everyone's
/// manager, so the right to edit it is its own permission and every change is audited.
/// </summary>
public class ReportsToCommandTests
{
    private static async Task<(PayrollRunKit K, E.Staff Admin)> SceneAsync()
    {
        var k = new PayrollRunKit();
        var admin = await k.PersonAsync("ADMIN", role: "STAFF ADMIN", keys: [AppConstants.Permissions.StaffManage], salary: false);
        return (k, admin);
    }

    private static Task Set(PayrollRunKit k, E.Staff by, E.Staff person, Guid? manager) =>
        new SetReportsToCommandHandler(k.Db, k.Audit).Handle(new SetReportsToCommand(person.Id, manager, by.Id, by.FullName), CancellationToken.None);

    private static async Task<Guid?> Boss(PayrollRunKit k, E.Staff person) => (await k.Db.Staff.AsNoTracking().SingleAsync(s => s.Id == person.Id)).ReportsToId;

    [Fact]
    public async Task A_person_is_given_a_reporting_manager_and_it_can_be_cleared()
    {
        var (k, admin) = await SceneAsync();
        var boss = await k.PersonAsync("BOSS", salary: false);
        var worker = await k.PersonAsync("WORKER", salary: false);

        await Set(k, admin, worker, boss.Id);
        Assert.Equal(boss.Id, await Boss(k, worker));

        await Set(k, admin, worker, null);
        Assert.Null(await Boss(k, worker));
    }

    [Fact]
    public async Task A_change_is_audited_with_the_staff_codes()
    {
        var (k, admin) = await SceneAsync();
        var boss = await k.PersonAsync("BOSS", salary: false);
        var worker = await k.PersonAsync("WORKER", salary: false);

        await Set(k, admin, worker, boss.Id);
        await Set(k, admin, worker, null);

        Assert.Equal(["WORKER: none -> BOSS", "WORKER: BOSS -> none"], k.Audit.Entries.Where(e => e.Action == "ReportsToChanged").Select(e => e.Changes));
        Assert.All(k.Audit.Entries, e => Assert.Equal(admin.Id, e.By));
    }

    [Fact]
    public async Task Nobody_reports_to_themselves()
    {
        var (k, admin) = await SceneAsync();
        var worker = await k.PersonAsync("WORKER", salary: false);

        await Assert.ThrowsAsync<ArgumentException>(() => Set(k, admin, worker, worker.Id));
    }

    /// <summary>WHY: A reports to B and B reports to A would sign each other's pay and leave nobody at the top.</summary>
    [Fact]
    public async Task A_loop_is_refused_however_many_levels_it_goes_through()
    {
        var (k, admin) = await SceneAsync();
        var ceo = await k.PersonAsync("CEO", salary: false);
        var hod = await k.PersonAsync("HOD", salary: false);
        var mgr = await k.PersonAsync("MGR", salary: false);
        await Set(k, admin, mgr, hod.Id);
        await Set(k, admin, hod, ceo.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Set(k, admin, ceo, mgr.Id));

        Assert.Contains("above themselves", ex.Message);
        Assert.Null(await Boss(k, ceo));
    }

    [Fact]
    public async Task An_inactive_manager_is_refused()
    {
        var (k, admin) = await SceneAsync();
        var boss = await k.PersonAsync("BOSS", salary: false);
        var worker = await k.PersonAsync("WORKER", salary: false);
        (await k.Db.Staff.SingleAsync(s => s.Id == boss.Id)).Deactivate();
        await k.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Set(k, admin, worker, boss.Id));
    }

    [Fact]
    public async Task A_manager_or_a_person_who_does_not_exist_is_not_found()
    {
        var (k, admin) = await SceneAsync();
        var worker = await k.PersonAsync("WORKER", salary: false);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Set(k, admin, worker, Guid.NewGuid()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new SetReportsToCommandHandler(k.Db, k.Audit)
            .Handle(new SetReportsToCommand(Guid.NewGuid(), null, admin.Id, admin.FullName), CancellationToken.None));
    }

    /// <summary>WHY: whoever can edit the reporting line can decide who reads whose pay, so it is staff.manage, checked in the command.</summary>
    [Fact]
    public async Task Changing_the_reporting_line_needs_staff_manage_even_for_a_payroll_signer()
    {
        var (k, _) = await SceneAsync();
        var signer = await k.ManagerAsync("SIGNER", salary: false);   // holds payroll.signoff only
        var worker = await k.PersonAsync("WORKER", salary: false);

        await Assert.ThrowsAsync<ForbiddenException>(() => Set(k, signer, worker, signer.Id));   // making themselves the manager
        Assert.Null(await Boss(k, worker));
    }

    /// <summary>
    /// WHY: someone with staff.manage and payroll.signoff could otherwise make themselves everyone's manager and read and sign
    /// everyone's pay through the team view, which is exactly what payroll.view exists to prevent.
    /// </summary>
    [Fact]
    public async Task Someone_who_is_not_a_head_of_hr_cannot_make_themselves_anyones_manager_or_pick_their_own_manager()
    {
        var (k, _) = await SceneAsync();
        var risky = await k.PersonAsync("RISKY", role: "SIGNER ADMIN", keys: [AppConstants.Permissions.StaffManage, AppConstants.Permissions.PayrollSignoff], salary: false);
        var worker = await k.PersonAsync("WORKER", salary: false);
        var friend = await k.PersonAsync("FRIEND", salary: false);

        await Assert.ThrowsAsync<ForbiddenException>(() => Set(k, risky, worker, risky.Id));   // making themselves the manager
        await Assert.ThrowsAsync<ForbiddenException>(() => Set(k, risky, risky, friend.Id));   // choosing who signs their own pay
        Assert.Null(await Boss(k, worker));
        Assert.Null(await Boss(k, risky));
    }

    [Fact]
    public async Task A_head_of_hr_who_also_manages_staff_may_place_people_under_themselves()
    {
        var (k, _) = await SceneAsync();
        var head = await k.PersonAsync("HEAD", role: "HR HEAD", keys: [AppConstants.Permissions.StaffManage, AppConstants.Permissions.PayrollApprove], salary: false);
        var worker = await k.PersonAsync("WORKER", salary: false);

        await Set(k, head, worker, head.Id);

        Assert.Equal(head.Id, await Boss(k, worker));
    }

    /// <summary>WHY: a manager re-pointed mid-review could read and sign a line that was already waiting for someone else.</summary>
    [Fact]
    public async Task Someone_whose_pay_is_in_a_run_being_reviewed_keeps_their_reviewer_until_it_is_returned_or_locked()
    {
        var (k, admin) = await SceneAsync();
        await k.SeedAndVerifyRatesAsync(true);
        var prep = await k.PreparerAsync();
        var boss = await k.PersonAsync("BOSS", salary: false);
        var other = await k.PersonAsync("OTHER", salary: false);
        var worker = await k.PersonAsync("WORKER", 3000m, reportsTo: boss.Id);
        var runId = await new HRNexus.Application.Payroll.Commands.Run.StartPayrollRunCommandHandler(k.Db, k.Audit, k.Clock)
            .Handle(new HRNexus.Application.Payroll.Commands.Run.StartPayrollRunCommand(2026, 10, PayrollRunKit.Act(prep)), CancellationToken.None);

        await Set(k, admin, worker, other.Id);   // a draft run does not freeze the line
        Assert.Equal(other.Id, await Boss(k, worker));

        var version = k.Db.PayrollRuns.AsNoTracking().Single(r => r.Id == runId).Version;
        await new HRNexus.Application.Payroll.Commands.Run.SubmitPayrollRunCommandHandler(k.Db, k.Audit, k.Clock)
            .Handle(new HRNexus.Application.Payroll.Commands.Run.SubmitPayrollRunCommand(runId, version, PayrollRunKit.Act(prep)), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Set(k, admin, worker, boss.Id));
        Assert.Equal(other.Id, await Boss(k, worker));
    }

    [Fact]
    public async Task The_staff_list_shows_who_each_person_reports_to()
    {
        var (k, admin) = await SceneAsync();
        var boss = await k.PersonAsync("BOSS", salary: false);
        var worker = await k.PersonAsync("WORKER", salary: false);
        await Set(k, admin, worker, boss.Id);

        var page = await new GetStaffListQueryHandler(k.Db).Handle(new GetStaffListQuery(IncludeHidden: true, PageSize: 50), CancellationToken.None);

        var row = page.Items.Single(s => s.StaffId == "WORKER");
        Assert.Equal((boss.Id, "First BOSS"), (row.ReportsToId, row.ReportsToName));
        Assert.Null(page.Items.Single(s => s.StaffId == "BOSS").ReportsToId);
    }

    [Fact]
    public async Task The_staff_detail_shows_who_the_person_reports_to()
    {
        var (k, admin) = await SceneAsync();
        var boss = await k.PersonAsync("BOSS", salary: false);
        var worker = await k.PersonAsync("WORKER", salary: false);
        await Set(k, admin, worker, boss.Id);

        var detail = await new GetStaffByIdQueryHandler(k.Db).Handle(new GetStaffByIdQuery(worker.Id), CancellationToken.None);
        var top = await new GetStaffByIdQueryHandler(k.Db).Handle(new GetStaffByIdQuery(boss.Id), CancellationToken.None);

        Assert.Equal((boss.Id, "First BOSS"), (detail.ReportsToId, detail.ReportsToName));
        Assert.Null(top.ReportsToId);
    }
}

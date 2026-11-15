using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Payroll;
using HRNexus.Application.Permissions;
using HRNexus.Domain.Common;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Staff.Commands;

/// <summary>
/// Sets who a person reports to (null = nobody). This decides whose payroll line a manager may read and sign off, so it needs
/// staff.manage (checked here as well as at the endpoint), runs in one Serializable transaction (two people changing the line at
/// once cannot each pass the loop check), refuses a manager who is deleted or inactive, refuses a loop, refuses anyone but a head
/// of HR placing themselves in the line, refuses a change for someone whose pay is being reviewed, and is audited with the staff
/// codes (previous and new manager).
/// </summary>
public record SetReportsToCommand(Guid StaffId, Guid? ManagerId, Guid By, string ByName) : IRequest;

public class SetReportsToCommandHandler(IAppDbContext db, IAuditService audit) : IRequestHandler<SetReportsToCommand>
{
    public async Task Handle(SetReportsToCommand req, CancellationToken ct) =>
        await SerializableWork.RunAsync(db, async c =>
        {
            await PayrollAccess.RequireAsync(db, AppConstants.Permissions.StaffManage, req.By, c, "You are not allowed to change who reports to whom.");
            var staff = await db.Staff.FirstOrDefaultAsync(s => s.Id == req.StaffId, c)
                ?? throw new KeyNotFoundException($"Staff {req.StaffId} not found.");

            // The line decides whose pay a manager can read and sign. Someone who is not a head of HR therefore cannot place
            // themselves above anyone, or place themselves under someone of their choosing (that would pick who signs their pay).
            if ((req.ManagerId == req.By || staff.Id == req.By)
                && !(await PermissionHolders.GetIdsAsync(db, AppConstants.Permissions.PayrollApprove, c)).Contains(req.By))
                throw new ForbiddenException("You cannot change your own place in the reporting line, or make yourself anyone's manager.");

            // A person whose pay is being reviewed keeps their reviewer until the run is returned or locked.
            var inReview = await db.PayrollRuns.Where(r => r.State == PayrollRunState.Submitted || r.State == PayrollRunState.Approved)
                .AnyAsync(r => r.Lines.Any(l => l.EmployeeId == staff.Id && !l.Excluded), c);
            if (inReview)
                throw new InvalidOperationException("A payroll run that is being reviewed includes this person, so who they report to cannot change until the run is returned or locked.");

            string? managerCode = null;
            if (req.ManagerId is { } managerId)
            {
                if (managerId == staff.Id) throw new ArgumentException("A person cannot report to themselves.");
                var manager = await db.Staff.FirstOrDefaultAsync(s => s.Id == managerId, c)
                    ?? throw new KeyNotFoundException("That manager was not found.");
                if (!manager.IsActive) throw new InvalidOperationException("An inactive person cannot be a reporting manager.");
                var chain = await db.Staff.AsNoTracking().IgnoreAutoIncludes().Select(s => new { s.Id, s.ReportsToId }).ToListAsync(c);
                if (ReportingLine.WouldLoop(staff.Id, managerId, chain.ToDictionary(s => s.Id, s => s.ReportsToId)))
                    throw new InvalidOperationException("That would put someone above themselves in the reporting line.");
                managerCode = manager.StaffId;
            }

            var previousCode = staff.ReportsToId is { } previous
                ? await db.Staff.Where(s => s.Id == previous).Select(s => s.StaffId).FirstOrDefaultAsync(c)
                : null;
            staff.SetReportsTo(req.ManagerId);
            await db.SaveChangesAsync(c);
            await audit.LogAsync("Staff", staff.Id.ToString(), "ReportsToChanged", req.By, req.ByName,
                $"{staff.StaffId}: {previousCode ?? "none"} -> {managerCode ?? "none"}", c);
            return 0;
        }, ct);
}

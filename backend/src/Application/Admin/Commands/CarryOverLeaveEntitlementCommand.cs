using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Admin.Commands;

/// <summary>
/// Carries over unused annual leave hours from one year to the next for all active staff.
/// The carry-over amount per person is capped at <see cref="MaxCarryOver"/> hours.
/// Carry-over hours are ADDED to the destination year's annual entitlement — they do not
/// replace it. Returns the number of employees who had carry-over hours added.
/// </summary>
public record CarryOverLeaveEntitlementCommand(
    int     FromYear,
    int     ToYear,
    decimal MaxCarryOver = 14m) : IRequest<int>;

public class CarryOverLeaveEntitlementCommandHandler(IAppDbContext db)
    : IRequestHandler<CarryOverLeaveEntitlementCommand, int>
{
    public async Task<int> Handle(CarryOverLeaveEntitlementCommand req, CancellationToken ct)
    {
        if (req.FromYear >= req.ToYear)
            throw new ArgumentException("FromYear must be before ToYear.");
        if (req.MaxCarryOver < 0)
            throw new ArgumentException("MaxCarryOver cannot be negative.");
        if (req.MaxCarryOver > 720)
            throw new ArgumentException("MaxCarryOver cannot exceed 720 hours.");

        var activeStaffIds = await db.Staff
            .Where(s => s.IsActive)
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (activeStaffIds.Count == 0) return 0;

        // Wrap all 3 reads + save in a REPEATABLE READ transaction so that a concurrent
        // leave approval between our reads and our save can't produce a stale carry-over.
        // REPEATABLE READ prevents non-repeatable reads: re-reading the same rows within
        // the transaction will always return the same data we saw at first read.
        await using var tx = await db.BeginRepeatableReadAsync(ct);

        // From-year entitlements
        var fromEntitlements = await db.LeaveEntitlements
            .Where(e => activeStaffIds.Contains(e.EmployeeId) && e.Year == req.FromYear)
            .ToListAsync(ct);
        var fromMap = fromEntitlements.ToDictionary(e => e.EmployeeId);

        // Used annual + emergency hours in fromYear (both draw from the annual pool)
        var fromYearStart = new DateTime(req.FromYear,     1, 1, 0, 0, 0, DateTimeKind.Utc);
        var fromYearEnd   = new DateTime(req.FromYear + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1);

        var usedByEmployee = await db.LeaveRequests
            .Where(l => activeStaffIds.Contains(l.EmployeeId)
                     && (l.LeaveType == "annual" || l.LeaveType == "emergency")
                     && l.StartDate >= fromYearStart
                     && l.StartDate <= fromYearEnd
                     && l.Status != "rejected"
                     && l.Status != "cancelled")
            .GroupBy(l => l.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Used = g.Sum(l => l.Hours ?? 0m) })
            .ToListAsync(ct);

        var usedMap = usedByEmployee.ToDictionary(u => u.EmployeeId, u => u.Used);

        // To-year entitlements (to add carry-over on top of existing)
        var toEntitlements = await db.LeaveEntitlements
            .Where(e => activeStaffIds.Contains(e.EmployeeId) && e.Year == req.ToYear)
            .ToListAsync(ct);
        var toMap = toEntitlements.ToDictionary(e => e.EmployeeId);

        int count = 0;
        foreach (var empId in activeStaffIds)
        {
            var annualEnt = fromMap.TryGetValue(empId, out var fe) ? fe.AnnualHours : 0m;
            var used      = usedMap.TryGetValue(empId, out var u) ? u : 0m;
            var remaining = Math.Max(0m, annualEnt - used);
            var carryOver = Math.Min(remaining, req.MaxCarryOver);

            if (carryOver <= 0m) continue;

            if (toMap.TryGetValue(empId, out var te))
                te.AddCarryOver(carryOver);
            else
            {
                var newEnt = LeaveEntitlement.Create(empId, req.ToYear, 0m, 0m);
                newEnt.AddCarryOver(carryOver);
                db.LeaveEntitlements.Add(newEnt);
            }

            count++;
        }

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return count;
    }
}

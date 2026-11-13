using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

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

        return await db.ExecuteInStrategyAsync(async ct =>
        {
            await using var tx = await db.BeginRepeatableReadAsync(ct);

            var fromEntitlements = await db.LeaveEntitlements
                .Where(e => activeStaffIds.Contains(e.EmployeeId) && e.Year == req.FromYear)
                .ToListAsync(ct);
            var fromMap = fromEntitlements.ToDictionary(e => e.EmployeeId);

            var fromYearStart = new DateTime(req.FromYear, 1, 1,   0, 0, 0, DateTimeKind.Utc);
            var fromYearEnd   = new DateTime(req.FromYear, 12, 31, 0, 0, 0, DateTimeKind.Utc);

            var fromYearLeaves = await db.LeaveRequests
                .Where(l => activeStaffIds.Contains(l.EmployeeId)
                         && (l.LeaveType == "annual" || l.LeaveType == "emergency")
                         && l.StartDate <= fromYearEnd
                         && l.EndDate   >= fromYearStart
                         && l.Status != "rejected"
                         && l.Status != "cancelled")
                .Select(l => new { l.EmployeeId, l.StartDate, l.EndDate, l.Hours })
                .ToListAsync(ct);
            // Only the working days that fall in the closing year count as used in it.
            var usedByEmployee = fromYearLeaves
                .GroupBy(l => l.EmployeeId)
                .Select(g => new { EmployeeId = g.Key,
                    Used = g.Sum(l => LeaveHoursCalculator.HoursInYear(l.StartDate, l.EndDate, l.Hours, req.FromYear)) })
                .ToList();

            var usedMap = usedByEmployee.ToDictionary(u => u.EmployeeId, u => u.Used);

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
                {
                    // Idempotency: CarryOverHours is only ever set by a carry-over. If it's
                    // already > 0, this employee was carried over into ToYear before — skip so
                    // a re-run (double-click, retry) can't inflate the balance with `+=`.
                    if (te.CarryOverHours > 0m) continue;
                    te.AddCarryOver(carryOver);
                }
                else
                {
                    var newEnt = LeaveEntitlement.Create(empId, req.ToYear, 0m, 0m);
                    newEnt.AddCarryOver(carryOver);
                    db.LeaveEntitlements.Add(newEnt);
                }

                count++;
            }

            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                throw new InvalidOperationException(
                    "Leave entitlements were modified concurrently. Please try the carry-over again.");
            }
            if (tx is not null) await tx.CommitAsync(ct);
            return count;
        }, ct);
    }
}

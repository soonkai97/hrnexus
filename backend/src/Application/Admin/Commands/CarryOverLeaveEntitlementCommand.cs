using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave;
using HRNexus.Application.Policy;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Admin.Commands;

/// <summary>
/// Carries over unused annual leave hours from one year to the next for all active staff.
/// The rule comes from the company policy: none (unused leave lapses), capped (up to the policy cap) or
/// unlimited (cumulative: everything left carries forward).
/// What is left of the closing year's whole annual pool (base plus its own carry-over) is carried.
/// Carry-over hours are stored in CarryOverHours and add to the destination year's usable pool;
/// the base entitlement is never changed. Returns the number of employees who had carry-over added.
/// </summary>
public record CarryOverLeaveEntitlementCommand(int FromYear, int ToYear) : IRequest<int>;

public class CarryOverLeaveEntitlementCommandHandler(IAppDbContext db)
    : IRequestHandler<CarryOverLeaveEntitlementCommand, int>
{
    public async Task<int> Handle(CarryOverLeaveEntitlementCommand req, CancellationToken ct)
    {
        if (req.FromYear >= req.ToYear)
            throw new ArgumentException("FromYear must be before ToYear.");

        var policy = await CompanyPolicyReader.GetAsync(db, ct);
        if (policy.CarryOverMode == AppConstants.Policy.CarryOverNone) return 0;   // unused leave lapses
        var cap = policy.CarryOverMode == AppConstants.Policy.CarryOverUnlimited ? decimal.MaxValue : policy.CarryOverMaxHours;

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
                    Used = g.Sum(l => LeaveHoursCalculator.HoursInYear(l.StartDate, l.EndDate, l.Hours, req.FromYear, policy.Schedule)) })
                .ToList();

            var usedMap = usedByEmployee.ToDictionary(u => u.EmployeeId, u => u.Used);

            var toEntitlements = await db.LeaveEntitlements
                .Where(e => activeStaffIds.Contains(e.EmployeeId) && e.Year == req.ToYear)
                .ToListAsync(ct);
            var toMap = toEntitlements.ToDictionary(e => e.EmployeeId);

            int count = 0;
            foreach (var empId in activeStaffIds)
            {
                var annualEnt = fromMap.TryGetValue(empId, out var fe) ? fe.TotalAnnualHours : 0m;
                var used      = usedMap.TryGetValue(empId, out var u) ? u : 0m;
                var remaining = Math.Max(0m, annualEnt - used);
                var carryOver = Math.Min(remaining, cap);

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

using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Admin.Commands;

public record DistributeTipsCommand(
    Guid     LocationId,
    string   LocationName,
    DateTime ShiftDate,
    decimal  TotalAmount,
    string   SplitMethod,
    Guid     AdminId,
    string   AdminName
) : IRequest<TipsDistributionResult>;

public record TipsDistributionResult(
    int                    StaffCount,
    List<TipsAllocation>   Allocations,
    List<TipsExcludedStaff> ExcludedStaff);

public record TipsAllocation(Guid EmployeeId, string FullName, decimal Amount, decimal HoursWorked);

/// <summary>Staff who worked this shift but were already clocked out when distribution ran.</summary>
public record TipsExcludedStaff(Guid EmployeeId, string FullName, string Reason);

public class DistributeTipsHandler(IAppDbContext db, IDateTimeProvider dateTime, IAuditService audit)
    : IRequestHandler<DistributeTipsCommand, TipsDistributionResult>
{
    public async Task<TipsDistributionResult> Handle(DistributeTipsCommand req, CancellationToken ct)
    {
        var workDate    = DateOnly.FromDateTime(req.ShiftDate.Date);
        var shiftDateUtc = DateTime.SpecifyKind(req.ShiftDate.Date, DateTimeKind.Utc);

        // ── 0. Input sanity checks ────────────────────────────────────────────
        if (req.TotalAmount <= 0)
            throw new ArgumentException("Total tips amount must be greater than zero.");
        if (req.TotalAmount > AppConstants.Limits.TipsMaxAmount)
            throw new ArgumentException($"Total tips amount cannot exceed {AppConstants.Limits.TipsMaxAmount:N0}. Please verify the amount before distributing.");

        // ── 1. Duplicate prevention ───────────────────────────────────────────
        var alreadyDistributed = await db.TipsPools
            .AnyAsync(p => p.LocationId == req.LocationId
                        && p.ShiftDate  == shiftDateUtc, ct);
        if (alreadyDistributed)
            throw new InvalidOperationException(
                "Tips have already been distributed for this location and date. " +
                "Check the existing pool entry before distributing again.");

        // ── 2. Find active (clocked-in) staff ─────────────────────────────────
        var activeWorkLogs = await db.WorkLogs
            .Include(w => w.Employee)
            .Where(w => w.LocationId == req.LocationId.ToString()
                     && w.WorkDate   == workDate
                     && w.EndTime    == null)
            .ToListAsync(ct);

        if (activeWorkLogs.Count == 0)
            throw new InvalidOperationException(
                "No active (clocked-in) staff found for the given location and date.");

        // ── 3. Find already-clocked-out staff (excluded — for admin warning) ──
        var clockedOutLogs = await db.WorkLogs
            .Include(w => w.Employee)
            .Where(w => w.LocationId == req.LocationId.ToString()
                     && w.WorkDate   == workDate
                     && w.EndTime    != null)
            .ToListAsync(ct);

        var excludedStaff = clockedOutLogs
            .Where(w => w.Employee != null)
            .Select(w => new TipsExcludedStaff(
                w.EmployeeId,
                w.Employee!.FullName,
                $"Clocked out at {w.EndTime} — not included in distribution."))
            .ToList();

        // ── 4. Calculate allocations ──────────────────────────────────────────
        var allocations = new List<TipsAllocation>();

        if (req.SplitMethod.Equals(AppConstants.TipsSplitMethod.Equal, StringComparison.OrdinalIgnoreCase))
        {
            var perPerson    = Math.Round(req.TotalAmount / activeWorkLogs.Count, 2);
            decimal distributed = 0m;

            for (int i = 0; i < activeWorkLogs.Count; i++)
            {
                var wl  = activeWorkLogs[i];
                decimal amt = (i == activeWorkLogs.Count - 1)
                    ? req.TotalAmount - distributed
                    : perPerson;
                distributed += amt;
                allocations.Add(new TipsAllocation(wl.EmployeeId, wl.Employee!.FullName, amt, 0m));
            }
        }
        else // by_hours — split proportional to elapsed time since StartTime
        {
            // Use LocalNow so both sides of the subtraction are in the same wall-clock frame
            // (WorkDate.ToDateTime produces Kind=Unspecified; mixing with UtcNow causes an 8-hour error)
            var now = dateTime.LocalNow;

            var hoursMap = activeWorkLogs.Select(wl =>
            {
                var start   = TimeSpan.Parse(wl.StartTime);
                var startDt = wl.WorkDate.ToDateTime(TimeOnly.FromTimeSpan(start));
                var elapsed = (decimal)(now - startDt).TotalHours;
                if (elapsed < 0) elapsed = 0m;
                return (wl, elapsed);
            }).ToList();

            var totalHours = hoursMap.Sum(x => x.elapsed);
            if (totalHours == 0)
                throw new InvalidOperationException(
                    "Cannot split by hours: no elapsed time found for active shifts.");

            decimal distributed = 0m;
            for (int i = 0; i < hoursMap.Count; i++)
            {
                var (wl, hours) = hoursMap[i];
                decimal amt = (i == hoursMap.Count - 1)
                    ? req.TotalAmount - distributed
                    : Math.Round(req.TotalAmount * hours / totalHours, 2);
                distributed += amt;
                allocations.Add(new TipsAllocation(
                    wl.EmployeeId, wl.Employee!.FullName, amt, Math.Round(hours, 2)));
            }
        }

        // ── 5. Persist pool + individual allocation records ───────────────────
        var pool = TipsPool.Create(
            req.LocationId, req.LocationName, shiftDateUtc, req.TotalAmount, req.SplitMethod);
        pool.MarkDistributed(activeWorkLogs.Count);
        db.TipsPools.Add(pool);
        try
        {
            await db.SaveChangesAsync(ct); // flush pool first so we have pool.Id
        }
        catch (DbUpdateException)
        {
            // Unique index on (LocationId, ShiftDate) fires when a concurrent request
            // slipped past the AnyAsync check above. Surface a clean error.
            throw new InvalidOperationException(
                "Tips have already been distributed for this location and date. " +
                "Check the existing pool entry before distributing again.");
        }

        foreach (var alloc in allocations)
        {
            db.TipsAllocations.Add(TipsAllocationRecord.Create(
                pool.Id, alloc.EmployeeId, alloc.FullName, alloc.Amount, alloc.HoursWorked));
        }
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(
            "TipsPool", pool.Id.ToString(), "Distributed",
            req.AdminId, req.AdminName,
            $"Location={req.LocationName}, Date={workDate}, Amount={req.TotalAmount}, Method={req.SplitMethod}, Staff={activeWorkLogs.Count}",
            ct);

        return new TipsDistributionResult(activeWorkLogs.Count, allocations, excludedStaff);
    }
}

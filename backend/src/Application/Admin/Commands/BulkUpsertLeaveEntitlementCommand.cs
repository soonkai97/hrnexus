using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Admin.Commands;

/// <summary>
/// Sets the same annual and sick leave entitlement for all currently-active staff for a given year.
/// Creates a new entitlement record where none exists; updates existing ones.
/// Returns the number of employee records affected.
/// </summary>
public record BulkUpsertLeaveEntitlementCommand(
    int     Year,
    decimal AnnualHours,
    decimal SickHours) : IRequest<int>;

public class BulkUpsertLeaveEntitlementCommandHandler(IAppDbContext db)
    : IRequestHandler<BulkUpsertLeaveEntitlementCommand, int>
{
    public async Task<int> Handle(BulkUpsertLeaveEntitlementCommand req, CancellationToken ct)
    {
        if (req.AnnualHours < 0 || req.SickHours < 0)
            throw new ArgumentException("Leave entitlement hours cannot be negative.");
        if (req.AnnualHours > 8760 || req.SickHours > 8760)
            throw new ArgumentException("Leave entitlement hours cannot exceed 8760 (one year).");
        if (req.Year < 2000 || req.Year > DateTime.UtcNow.Year + 2)
            throw new ArgumentException("Leave entitlement year is out of valid range.");

        var activeStaffIds = await db.Staff
            .Where(s => s.IsActive)
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (activeStaffIds.Count == 0) return 0;

        var existing = await db.LeaveEntitlements
            .Where(e => activeStaffIds.Contains(e.EmployeeId) && e.Year == req.Year)
            .ToListAsync(ct);

        var existingMap = existing.ToDictionary(e => e.EmployeeId);

        foreach (var empId in activeStaffIds)
        {
            if (existingMap.TryGetValue(empId, out var ent))
                ent.Update(req.AnnualHours, req.SickHours);
            else
                db.LeaveEntitlements.Add(
                    LeaveEntitlement.Create(empId, req.Year, req.AnnualHours, req.SickHours));
        }

        await db.SaveChangesAsync(ct);
        return activeStaffIds.Count;
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Admin.Commands;

public record UpsertLeaveEntitlementCommand(
    Guid    EmployeeId,
    int     Year,
    decimal AnnualHours,
    decimal SickHours) : IRequest;

public class UpsertLeaveEntitlementCommandHandler(IAppDbContext db)
    : IRequestHandler<UpsertLeaveEntitlementCommand>
{
    public async Task Handle(UpsertLeaveEntitlementCommand req, CancellationToken ct)
    {
        if (req.AnnualHours < 0 || req.SickHours < 0)
            throw new ArgumentException("Leave entitlement hours cannot be negative.");
        if (req.AnnualHours > 8760 || req.SickHours > 8760)
            throw new ArgumentException("Leave entitlement hours cannot exceed 8760 (one year).");
        if (req.Year < 2000 || req.Year > DateTime.UtcNow.Year + 2)
            throw new ArgumentException("Leave entitlement year is out of valid range.");

        var ent = await db.LeaveEntitlements
            .FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId && e.Year == req.Year, ct);

        bool wasInsert = ent is null;
        if (wasInsert)
        {
            ent = LeaveEntitlement.Create(req.EmployeeId, req.Year, req.AnnualHours, req.SickHours);
            db.LeaveEntitlements.Add(ent);
        }
        else
        {
            ent!.Update(req.AnnualHours, req.SickHours);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (wasInsert)
        {
            // Two concurrent inserts for the same (EmployeeId, Year): the unique constraint fired.
            // Use ExecuteUpdateAsync to bypass the tracker — the row now exists, just update it.
            await db.LeaveEntitlements
                .Where(e => e.EmployeeId == req.EmployeeId && e.Year == req.Year)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.AnnualHours, req.AnnualHours)
                    .SetProperty(e => e.SickHours, req.SickHours), ct);
        }
    }
}

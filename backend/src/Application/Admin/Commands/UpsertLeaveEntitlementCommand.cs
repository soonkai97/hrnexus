using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Application.Admin.Commands;

public record UpsertLeaveEntitlementCommand(
    Guid EmployeeId,
    int  Year,
    int  AnnualDays,
    int  SickDays) : IRequest;

public class UpsertLeaveEntitlementCommandHandler(IAppDbContext db)
    : IRequestHandler<UpsertLeaveEntitlementCommand>
{
    public async Task Handle(UpsertLeaveEntitlementCommand req, CancellationToken ct)
    {
        if (req.AnnualDays < 0 || req.SickDays < 0)
            throw new ArgumentException("Leave entitlement days cannot be negative.");
        if (req.AnnualDays > 365 || req.SickDays > 365)
            throw new ArgumentException("Leave entitlement days cannot exceed 365.");
        if (req.Year < 2000 || req.Year > DateTime.UtcNow.Year + 2)
            throw new ArgumentException("Leave entitlement year is out of valid range.");

        var ent = await db.LeaveEntitlements
            .FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId && e.Year == req.Year, ct);

        if (ent is null)
        {
            ent = LeaveEntitlement.Create(req.EmployeeId, req.Year, req.AnnualDays, req.SickDays);
            db.LeaveEntitlements.Add(ent);
        }
        else
        {
            ent.Update(req.AnnualDays, req.SickDays);
        }

        await db.SaveChangesAsync(ct);
    }
}

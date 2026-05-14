using MediatR;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Staff.Commands;

public record ActivateStaffCommand(Guid Id) : IRequest;
public record DeactivateStaffCommand(Guid Id) : IRequest;

public class ActivateStaffCommandHandler(IAppDbContext db) : IRequestHandler<ActivateStaffCommand>
{
    public async Task Handle(ActivateStaffCommand req, CancellationToken ct)
    {
        var emp = await db.Staff.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException($"Employee {req.Id} not found.");
        emp.Activate();
        await db.SaveChangesAsync(ct);
    }
}

public class DeactivateStaffCommandHandler(IAppDbContext db) : IRequestHandler<DeactivateStaffCommand>
{
    public async Task Handle(DeactivateStaffCommand req, CancellationToken ct)
    {
        var emp = await db.Staff.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException($"Employee {req.Id} not found.");
        emp.Deactivate();
        await db.SaveChangesAsync(ct);
    }
}

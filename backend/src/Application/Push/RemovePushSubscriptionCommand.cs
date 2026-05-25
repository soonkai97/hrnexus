using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Push;

public record RemovePushSubscriptionCommand(
    Guid   EmployeeId,
    string Endpoint) : IRequest;

public class RemovePushSubscriptionCommandHandler(IAppDbContext db)
    : IRequestHandler<RemovePushSubscriptionCommand>
{
    public async Task Handle(RemovePushSubscriptionCommand req, CancellationToken ct)
    {
        var sub = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.EmployeeId == req.EmployeeId && s.Endpoint == req.Endpoint, ct);

        if (sub is null) return;

        db.PushSubscriptions.Remove(sub);
        await db.SaveChangesAsync(ct);
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Application.Push;

public record SavePushSubscriptionCommand(
    Guid   EmployeeId,
    string Endpoint,
    string P256dh,
    string Auth) : IRequest;

public class SavePushSubscriptionCommandHandler(IAppDbContext db)
    : IRequestHandler<SavePushSubscriptionCommand>
{
    public async Task Handle(SavePushSubscriptionCommand req, CancellationToken ct)
    {
        // Upsert: if endpoint already exists for this employee, skip; otherwise add
        var exists = await db.PushSubscriptions
            .AnyAsync(s => s.EmployeeId == req.EmployeeId && s.Endpoint == req.Endpoint, ct);

        if (exists) return;

        var sub = PushSubscription.Create(req.EmployeeId, req.Endpoint, req.P256dh, req.Auth);
        db.PushSubscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
    }
}

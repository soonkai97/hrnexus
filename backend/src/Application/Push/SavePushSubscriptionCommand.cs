using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Push;

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

        // Cap subscriptions per employee (one per browser/device)
        var count = await db.PushSubscriptions
            .CountAsync(s => s.EmployeeId == req.EmployeeId, ct);
        if (count >= AppConstants.Limits.PushSubscriptionsCap) return;

        var sub = PushSubscription.Create(req.EmployeeId, req.Endpoint, req.P256dh, req.Auth);
        db.PushSubscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Notifications.Commands;

/// <summary>Mark one or all notifications as read for an employee.</summary>
public record MarkNotificationsReadCommand(
    Guid   EmployeeId,
    Guid?  NotificationId  // null = mark ALL as read
) : IRequest;

public class MarkNotificationsReadCommandHandler(IAppDbContext db)
    : IRequestHandler<MarkNotificationsReadCommand>
{
    public async Task Handle(MarkNotificationsReadCommand req, CancellationToken ct)
    {
        if (req.NotificationId.HasValue)
        {
            var n = await db.Notifications.FindAsync([req.NotificationId.Value], ct);
            if (n is not null && n.EmployeeId == req.EmployeeId)
                n.MarkRead();
        }
        else
        {
            var unread = await db.Notifications
                .Where(n => n.EmployeeId == req.EmployeeId && !n.IsRead)
                .ToListAsync(ct);
            foreach (var n in unread) n.MarkRead();
        }

        await db.SaveChangesAsync(ct);
    }
}

using HRNexus.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Notifications.Queries;

public record GetNotificationsQuery(Guid EmployeeId, bool UnreadOnly = false)
    : IRequest<List<NotificationDto>>;

public record NotificationDto(
    string  Id,
    string  Title,
    string  Message,
    string  Type,
    bool    IsRead,
    string? RelatedId,
    string  CreatedAt);

public class GetNotificationsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetNotificationsQuery, List<NotificationDto>>
{
    public async Task<List<NotificationDto>> Handle(GetNotificationsQuery req, CancellationToken ct)
    {
        var query = db.Notifications
            .Where(n => n.EmployeeId == req.EmployeeId);

        if (req.UnreadOnly)
            query = query.Where(n => !n.IsRead);

        var list = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return list.Select(n => new NotificationDto(
            n.Id.ToString(), n.Title, n.Message, n.Type, n.IsRead,
            n.RelatedId, n.CreatedAt.ToString("yyyy-MM-dd HH:mm")))
            .ToList();
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Messaging.Queries;

public record MessageDto(Guid Id, Guid SenderId, string SenderName, bool SenderIsAdmin, Guid? RecipientId, string? RecipientName, string Content, DateTime SentAt, DateTime? ReadAt);

public record GetInboxQuery(Guid UserId, bool IsAdmin) : IRequest<List<MessageDto>>;
public class GetInboxHandler(IAppDbContext db) : IRequestHandler<GetInboxQuery, List<MessageDto>>
{
    public async Task<List<MessageDto>> Handle(GetInboxQuery req, CancellationToken ct)
    {
        IQueryable<HRNexus.Domain.Entities.StaffMessage> q;
        if (req.IsAdmin)
            // Admins see: messages sent TO them (recipientId = userId) OR messages from staff with no specific recipient (recipientId = null, senderIsAdmin = false)
            q = db.StaffMessages.Where(m => m.RecipientId == req.UserId || (m.RecipientId == null && !m.SenderIsAdmin));
        else
            // Staff see: messages sent TO them by admin
            q = db.StaffMessages.Where(m => m.RecipientId == req.UserId && m.SenderIsAdmin);

        return await q.OrderByDescending(m => m.SentAt)
            .Select(m => new MessageDto(m.Id, m.SenderId, m.SenderName, m.SenderIsAdmin, m.RecipientId, m.RecipientName, m.Content, m.SentAt, m.ReadAt))
            .ToListAsync(ct);
    }
}

public record GetSentQuery(Guid UserId) : IRequest<List<MessageDto>>;
public class GetSentHandler(IAppDbContext db) : IRequestHandler<GetSentQuery, List<MessageDto>>
{
    public async Task<List<MessageDto>> Handle(GetSentQuery req, CancellationToken ct) =>
        await db.StaffMessages.Where(m => m.SenderId == req.UserId)
            .OrderByDescending(m => m.SentAt)
            .Select(m => new MessageDto(m.Id, m.SenderId, m.SenderName, m.SenderIsAdmin, m.RecipientId, m.RecipientName, m.Content, m.SentAt, m.ReadAt))
            .ToListAsync(ct);
}

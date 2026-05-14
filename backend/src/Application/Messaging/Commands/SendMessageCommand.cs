using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Application.Messaging.Commands;

public record SendMessageCommand(Guid SenderId, string SenderName, bool SenderIsAdmin, Guid? RecipientId, string? RecipientName, string Content) : IRequest<Guid>;
public class SendMessageHandler(IAppDbContext db, INotificationHub notifHub) : IRequestHandler<SendMessageCommand, Guid>
{
    public async Task<Guid> Handle(SendMessageCommand req, CancellationToken ct)
    {
        // Content validation
        if (string.IsNullOrWhiteSpace(req.Content))
            throw new ArgumentException("Message cannot be empty.");
        if (req.Content.Length > 2000)
            throw new ArgumentException("Message cannot exceed 2000 characters.");

        // Prevent self-messaging
        if (req.RecipientId.HasValue && req.RecipientId.Value == req.SenderId)
            throw new ArgumentException("You cannot send a message to yourself.");

        // Verify recipient exists and is active (skip for broadcast messages where RecipientId is null)
        if (req.RecipientId.HasValue)
        {
            var recipientExists = await db.Staff
                .AnyAsync(s => s.Id == req.RecipientId.Value && s.IsActive, ct);
            if (!recipientExists)
                throw new KeyNotFoundException("Recipient not found or is no longer active.");
        }

        var msg = StaffMessage.Create(req.SenderId, req.SenderName, req.SenderIsAdmin, req.RecipientId, req.RecipientName, req.Content);
        db.StaffMessages.Add(msg);
        await db.SaveChangesAsync(ct);

        // Push real-time notification to recipient
        if (req.RecipientId.HasValue)
        {
            var notif = Notification.Create(req.RecipientId.Value,
                $"New message from {req.SenderName}",
                req.Content.Length > 80 ? req.Content[..80] + "…" : req.Content,
                "message", msg.Id.ToString());
            db.Notifications.Add(notif);
            await db.SaveChangesAsync(ct);
            await notifHub.SendToUserAsync(req.RecipientId.Value,
                new NotificationPayload(notif.Id.ToString(), notif.Title, notif.Message, notif.Type, notif.RelatedId, notif.CreatedAt.ToString("yyyy-MM-dd HH:mm")));
        }

        return msg.Id;
    }
}

public record MarkMessageReadCommand(Guid MessageId, Guid UserId) : IRequest;
public class MarkMessageReadHandler(IAppDbContext db) : IRequestHandler<MarkMessageReadCommand>
{
    public async Task Handle(MarkMessageReadCommand req, CancellationToken ct)
    {
        var msg = await db.StaffMessages.FindAsync(new object[] { req.MessageId }, ct);
        if (msg is null || msg.RecipientId != req.UserId) return;
        msg.MarkRead();
        await db.SaveChangesAsync(ct);
    }
}

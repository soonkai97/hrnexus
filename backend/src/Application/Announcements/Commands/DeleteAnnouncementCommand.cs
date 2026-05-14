using MediatR;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Announcements.Commands;

/// <summary>Permanently delete an announcement and its read receipts.</summary>
public record DeleteAnnouncementCommand(Guid Id) : IRequest;

public class DeleteAnnouncementCommandHandler(IAppDbContext db)
    : IRequestHandler<DeleteAnnouncementCommand>
{
    public async Task Handle(DeleteAnnouncementCommand req, CancellationToken ct)
    {
        var announcement = await db.Announcements.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException($"Announcement {req.Id} not found.");

        db.Announcements.Remove(announcement);
        await db.SaveChangesAsync(ct);
    }
}

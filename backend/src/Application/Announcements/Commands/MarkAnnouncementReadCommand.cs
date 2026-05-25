using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Announcements.Commands;

/// <summary>Mark an announcement as read for an employee. Idempotent — safe to call multiple times.</summary>
public record MarkAnnouncementReadCommand(Guid AnnouncementId, Guid EmployeeId) : IRequest;

public class MarkAnnouncementReadCommandHandler(IAppDbContext db)
    : IRequestHandler<MarkAnnouncementReadCommand>
{
    public async Task Handle(MarkAnnouncementReadCommand req, CancellationToken ct)
    {
        var alreadyRead = await db.AnnouncementReads
            .AnyAsync(r => r.AnnouncementId == req.AnnouncementId && r.EmployeeId == req.EmployeeId, ct);

        if (alreadyRead) return;

        var read = AnnouncementRead.Create(req.AnnouncementId, req.EmployeeId);
        db.AnnouncementReads.Add(read);
        await db.SaveChangesAsync(ct);
    }
}

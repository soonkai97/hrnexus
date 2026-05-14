using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Announcements.Queries;

/// <summary>Admin query — returns all announcements with read-receipt counts.</summary>
public record GetAllAnnouncementsQuery : IRequest<List<AdminAnnouncementDto>>;

public record AdminAnnouncementDto(
    Guid      Id,
    string    Title,
    string    Body,
    string    TargetRoles,
    bool      IsActive,
    string    CreatedByName,
    DateTime  CreatedAt,
    DateTime? ExpiresAt,
    int       ReadCount);

public class GetAllAnnouncementsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetAllAnnouncementsQuery, List<AdminAnnouncementDto>>
{
    public async Task<List<AdminAnnouncementDto>> Handle(GetAllAnnouncementsQuery req, CancellationToken ct)
    {
        var announcements = await db.Announcements
            .Include(a => a.Reads)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        return announcements.Select(a => new AdminAnnouncementDto(
            a.Id,
            a.Title,
            a.Body,
            a.TargetRoles,
            a.IsActive,
            a.CreatedByName,
            a.CreatedAt,
            a.ExpiresAt,
            a.Reads.Count))
            .ToList();
    }
}

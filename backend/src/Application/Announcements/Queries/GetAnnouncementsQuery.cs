using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Announcements.Queries;

/// <summary>Staff query — returns active announcements visible to the caller's roles, with IsRead flag.</summary>
public record GetAnnouncementsQuery(Guid EmployeeId, List<string> Roles) : IRequest<List<AnnouncementDto>>;

public record AnnouncementDto(
    Guid      Id,
    string    Title,
    string    Body,
    string    CreatedByName,
    DateTime  CreatedAt,
    DateTime? ExpiresAt,
    bool      IsRead);

public class GetAnnouncementsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetAnnouncementsQuery, List<AnnouncementDto>>
{
    public async Task<List<AnnouncementDto>> Handle(GetAnnouncementsQuery req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var normalised = req.Roles.Select(r => r.ToLowerInvariant()).ToList();

        var announcements = await db.Announcements
            .Include(a => a.Reads)
            .Where(a => a.IsActive && (a.ExpiresAt == null || a.ExpiresAt > now))
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        // Filter by target roles in memory (TargetRoles is a comma-separated string)
        return announcements
            .Where(a =>
            {
                if (a.TargetRoles == "*") return true;
                var targets = a.TargetRoles.Split(',', StringSplitOptions.RemoveEmptyEntries)
                               .Select(r => r.Trim().ToLowerInvariant());
                return targets.Any(t => normalised.Contains(t));
            })
            .Select(a => new AnnouncementDto(
                a.Id,
                a.Title,
                a.Body,
                a.CreatedByName,
                a.CreatedAt,
                a.ExpiresAt,
                a.Reads.Any(r => r.EmployeeId == req.EmployeeId)))
            .ToList();
    }
}

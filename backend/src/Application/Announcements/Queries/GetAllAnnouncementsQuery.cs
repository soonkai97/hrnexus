using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Announcements.Queries;

/// <summary>Admin query — returns a paged list of announcements with read-receipt counts.</summary>
public record GetAllAnnouncementsQuery(
    int Page     = 1,
    int PageSize = 10) : IRequest<PagedResult<AdminAnnouncementDto>>;

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
    : IRequestHandler<GetAllAnnouncementsQuery, PagedResult<AdminAnnouncementDto>>
{
    public async Task<PagedResult<AdminAnnouncementDto>> Handle(GetAllAnnouncementsQuery req, CancellationToken ct)
    {
        var query = db.Announcements.AsQueryable();

        var total = await query.CountAsync(ct);

        var page     = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AdminAnnouncementDto(
                a.Id,
                a.Title,
                a.Body,
                a.TargetRoles,
                a.IsActive,
                a.CreatedByName,
                a.CreatedAt,
                a.ExpiresAt,
                a.Reads.Count))
            .ToListAsync(ct);

        return new PagedResult<AdminAnnouncementDto>(items, total);
    }
}

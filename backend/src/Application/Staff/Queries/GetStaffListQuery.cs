using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;

namespace HRNexus.Application.Staff.Queries;

public record GetStaffListQuery(
    string? Search       = null,
    bool    ExcludeAdmins = false,
    int     Page         = 1,
    int     PageSize     = 10) : IRequest<PagedResult<StaffDto>>;

public record StaffDto(Guid Id, string StaffId, string FirstName, string LastName, string FullName, string? Email, List<string> Roles,
    bool IsActive, string? PhoneNumber,
    string? IcNo, string? PassportNo, string? DateOfBirth, int? Age);

public class GetStaffListQueryHandler(IAppDbContext db)
    : IRequestHandler<GetStaffListQuery, PagedResult<StaffDto>>
{
    public async Task<PagedResult<StaffDto>> Handle(GetStaffListQuery req, CancellationToken ct)
    {
        var query = db.Staff.AsQueryable();

        if (req.ExcludeAdmins)
            query = query.Where(e => !e.RoleEntries.Any(r => r.Role == AppConstants.Roles.Admin));

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var s = req.Search.ToLower();
            // FullName is an ignored computed property — query on stored columns instead.
            query = query.Where(e => (e.FirstName + " " + e.LastName).ToLower().Contains(s)
                                  || (e.Email != null && e.Email.Contains(s))
                                  || e.StaffId.ToLower().Contains(s)
                                  || e.RoleEntries.Any(r => r.Role.ToLower().Contains(s)));
        }

        var total = await query.CountAsync(ct);

        var page     = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 100);

        var employees = await query
            .OrderBy(e => e.StaffId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = employees.Select(e => new StaffDto(
            e.Id, e.StaffId, e.FirstName, e.LastName, e.FullName, e.Email,
            e.Roles.ToList(),
            e.IsActive, e.PhoneNumber,
            e.IcNo, e.PassportNo,
            e.DateOfBirth?.ToString("yyyy-MM-dd"), e.Age)).ToList();

        return new PagedResult<StaffDto>(items, total);
    }
}

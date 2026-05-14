using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Staff.Queries;

public record GetStaffListQuery(string? Search = null, bool ExcludeAdmins = false) : IRequest<List<StaffDto>>;

public record StaffDto(Guid Id, string StaffId, string FirstName, string LastName, string FullName, string Email, List<string> Roles,
    bool IsActive, string? PhoneNumber,
    string? IcNo, string? PassportNo, string? DateOfBirth, int? Age);

public class GetStaffListQueryHandler(IAppDbContext db)
    : IRequestHandler<GetStaffListQuery, List<StaffDto>>
{
    public async Task<List<StaffDto>> Handle(GetStaffListQuery req, CancellationToken ct)
    {
        var query = db.Staff.AsQueryable();

        if (req.ExcludeAdmins)
            query = query.Where(e => !e.RoleEntries.Any(r => r.Role == "admin"));

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var s = req.Search.ToLower();
            query = query.Where(e => e.FullName.ToLower().Contains(s)
                                  || e.Email.Contains(s)
                                  || e.StaffId.ToLower().Contains(s)
                                  || e.RoleEntries.Any(r => r.Role.Contains(s)));
        }

        var employees = await query
            .OrderBy(e => e.StaffId)
            .ToListAsync(ct);

        var result = employees.Select(e => new StaffDto(
            e.Id, e.StaffId, e.FirstName, e.LastName, e.FullName, e.Email,
            e.Roles.Select(r => r.ToLower()).ToList(),
            e.IsActive, e.PhoneNumber,
            e.IcNo, e.PassportNo,
            e.DateOfBirth?.ToString("yyyy-MM-dd"), e.Age)).ToList();

        return result;
    }
}

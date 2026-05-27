using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Staff.Queries;

public record GetStaffByIdQuery(Guid Id) : IRequest<StaffDetailDto>;

public record StaffDetailDto(
    Guid         Id,
    string       StaffId,
    string       FirstName,
    string       LastName,
    string       FullName,
    string?      Email,
    List<string> Roles,
    bool         IsActive,
    string?      PhoneNumber,
    string?      IcNo,
    string?      PassportNo,
    string?      DateOfBirth,
    int?         Age,
    DateTime     CreatedAt);

public class GetStaffByIdQueryHandler(IAppDbContext db)
    : IRequestHandler<GetStaffByIdQuery, StaffDetailDto>
{
    public async Task<StaffDetailDto> Handle(GetStaffByIdQuery req, CancellationToken ct)
    {
        var emp = await db.Staff
            .Where(e => e.Id == req.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Employee {req.Id} not found.");

        return new StaffDetailDto(
            emp.Id, emp.StaffId, emp.FirstName, emp.LastName, emp.FullName, emp.Email,
            emp.Roles.ToList(),
            emp.IsActive, emp.PhoneNumber,
            emp.IcNo, emp.PassportNo,
            emp.DateOfBirth?.ToString("yyyy-MM-dd"), emp.Age,
            emp.CreatedAt);
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Permissions.Queries;

public record StaffOverrideDto(string PermissionKey, bool Granted);

/// <summary>Per-user permission overrides (grant/revoke on top of role defaults) for a staff member.</summary>
public record GetStaffOverridesQuery(Guid EmployeeId) : IRequest<List<StaffOverrideDto>>;

public class GetStaffOverridesHandler(IAppDbContext db)
    : IRequestHandler<GetStaffOverridesQuery, List<StaffOverrideDto>>
{
    public async Task<List<StaffOverrideDto>> Handle(GetStaffOverridesQuery req, CancellationToken ct)
    {
        if (!await db.Staff.AnyAsync(s => s.Id == req.EmployeeId, ct))
            throw new KeyNotFoundException("Staff member not found.");

        return await db.StaffPermissionOverrides
            .Where(o => o.EmployeeId == req.EmployeeId)
            .Select(o => new StaffOverrideDto(o.PermissionKey, o.Granted))
            .ToListAsync(ct);
    }
}

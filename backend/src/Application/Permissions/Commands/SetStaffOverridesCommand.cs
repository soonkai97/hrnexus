using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions.Queries;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Permissions.Commands;

/// <summary>Replace a user's per-user permission overrides (replace-all semantics).</summary>
public record SetStaffOverridesCommand(Guid EmployeeId, List<StaffOverrideDto> Overrides) : IRequest;

public class SetStaffOverridesHandler(IAppDbContext db, IPermissionResolver resolver)
    : IRequestHandler<SetStaffOverridesCommand>
{
    public async Task Handle(SetStaffOverridesCommand req, CancellationToken ct)
    {
        if (!await db.Staff.AnyAsync(s => s.Id == req.EmployeeId, ct))
            throw new KeyNotFoundException("Staff member not found.");

        var overrides = req.Overrides
            .GroupBy(o => o.PermissionKey)
            .Select(g => g.Last())   // last write wins if a key is sent twice
            .ToList();

        var unknown = overrides.Where(o => !AppConstants.Permissions.All.Contains(o.PermissionKey))
            .Select(o => o.PermissionKey).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown permission(s): {string.Join(", ", unknown)}");

        var existing = await db.StaffPermissionOverrides
            .Where(o => o.EmployeeId == req.EmployeeId).ToListAsync(ct);
        db.StaffPermissionOverrides.RemoveRange(existing);
        foreach (var o in overrides)
            db.StaffPermissionOverrides.Add(
                StaffPermissionOverride.Create(req.EmployeeId, o.PermissionKey, o.Granted));

        await db.SaveChangesAsync(ct);
        resolver.Invalidate(req.EmployeeId);
    }
}

using HRNexus.Application.Common.Interfaces;
using MediatR;

namespace HRNexus.Application.Staff.Commands;

/// <summary>
/// Show or hide a staff account in the admin roster. Presentational only — it does not deactivate the
/// account or change permissions, so the seeded HRADMIN / HRMANAGER accounts can be tucked away without
/// losing the ability to sign in or manage the venue.
/// </summary>
public record SetStaffHiddenCommand(Guid Id, bool Hidden) : IRequest;

public class SetStaffHiddenCommandHandler(IAppDbContext db) : IRequestHandler<SetStaffHiddenCommand>
{
    public async Task Handle(SetStaffHiddenCommand req, CancellationToken ct)
    {
        var staff = await db.Staff.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException($"Employee {req.Id} not found.");
        staff.SetHidden(req.Hidden);
        await db.SaveChangesAsync(ct);
    }
}

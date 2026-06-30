using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.WorkLog.Commands;

/// <summary>Staff-initiated "Update location" — records a fresh GPS fix on the caller's active shift
/// so the admin dashboard shows where they currently are (not just where they clocked in).</summary>
public record UpdateMyShiftLocationCommand(Guid EmployeeId, double Latitude, double Longitude) : IRequest;

public class UpdateMyShiftLocationCommandHandler(IAppDbContext db, IDateTimeProvider dt)
    : IRequestHandler<UpdateMyShiftLocationCommand>
{
    public async Task Handle(UpdateMyShiftLocationCommand req, CancellationToken ct)
    {
        var log = await db.WorkLogs.FirstOrDefaultAsync(
            w => w.EmployeeId == req.EmployeeId && w.EndTime == null, ct)
            ?? throw new InvalidOperationException("You don't have an active shift to update.");

        // Must be UTC-kind — LastLocationAt is a timestamptz column and Npgsql rejects Local/Unspecified.
        log.UpdateLocation(req.Latitude, req.Longitude, dt.UtcNow);
        await db.SaveChangesAsync(ct);
    }
}

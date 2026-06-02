using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.WorkLog.Queries;

namespace HRNexus.Application.WorkLog.Commands;

public record EndWorkLogCommand(Guid Id, Guid EmployeeId, string EndTime) : IRequest;

public class EndWorkLogCommandHandler(IAppDbContext db, ISender sender, INotificationHub hub)
    : IRequestHandler<EndWorkLogCommand>
{
    public async Task Handle(EndWorkLogCommand req, CancellationToken ct)
    {
        if (!TimeOnly.TryParseExact(req.EndTime, "HH:mm", null, System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException("EndTime must be in HH:mm format.");

        var log = await db.WorkLogs.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException("Work log not found.");
        if (log.EmployeeId != req.EmployeeId) throw new UnauthorizedAccessException("Not your shift.");
        if (log.EndTime != null) throw new InvalidOperationException("Shift already ended.");

        // Validate end time produces a non-zero duration (guard against identical start/end)
        var hours = HRNexus.Application.Common.ShiftHours.CalcHours(log.StartTime, req.EndTime);
        if (hours <= 0)
            throw new ArgumentException("End time must be different from the shift start time.");

        // A staff clock-out is "now", so the end date is the same day or the next (overnight) — derive
        // it the historical way; multi-day spans only ever come from an admin correction.
        var endDate = HRNexus.Application.Common.ShiftHours.DeriveEndDate(log.WorkDate, log.StartTime, req.EndTime);
        log.End(endDate, req.EndTime);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("This shift was just changed by someone else. Please refresh and try again.");
        }

        // Broadcast updated live stats to all connected dashboard clients
        var stats = await sender.Send(new GetLiveStatsQuery(), ct);
        await hub.BroadcastLiveStatsAsync(stats, ct);
    }
}

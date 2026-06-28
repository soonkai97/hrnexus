using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.WorkLog.Queries;

namespace HRNexus.Application.WorkLog.Commands;

public record EndWorkLogCommand(Guid Id, Guid EmployeeId, string EndTime) : IRequest;

public class EndWorkLogCommandHandler(IAppDbContext db, ISender sender, INotificationHub hub, IDateTimeProvider dateTime)
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

        // A self clock-out derives a same-day/overnight (≤24h) end. If the shift has actually been
        // open longer than that, the staff plainly forgot — recording it as a short same-day shift
        // would be wrong, so route them to a manager (who can close it with a real multi-day end).
        var startLocal = log.WorkDate.ToDateTime(TimeOnly.Parse(log.StartTime));
        if (dateTime.LocalNow - startLocal > TimeSpan.FromHours(24))
            throw new InvalidOperationException("This shift has been open for more than 24 hours. Please ask a manager to close it.");

        // Validate end time produces a non-zero duration (guard against identical start/end)
        var hours = HRNexus.Application.Common.ShiftHours.CalcHours(log.StartTime, req.EndTime);
        if (hours <= 0)
            throw new ArgumentException("End time must be different from the shift start time.");

        // A staff clock-out is "now", so the end date is the same day or the next (overnight) — derive
        // it the historical way; multi-day spans only ever come from an admin correction.
        var endDate = ShiftHours.DeriveEndDate(log.WorkDate, log.StartTime, req.EndTime);

        // Reject if the resolved interval overlaps another completed shift for this staff member.
        // Clock-in only guards the START instant, so a shift that starts at a free time but runs into
        // a later completed shift must still be caught here (the staff can pick an earlier end time).
        var newStartAbs = ShiftHours.AbsMinutes(log.WorkDate, log.StartTime);
        var newEndAbs   = ShiftHours.AbsMinutes(endDate, req.EndTime);
        var others = await db.WorkLogs
            .Where(w => w.Id != log.Id && w.EmployeeId == log.EmployeeId && w.EndTime != null
                     && w.EndDate >= log.WorkDate && w.WorkDate <= endDate)
            .Select(w => new { w.WorkDate, w.StartTime, w.EndDate, w.EndTime })
            .ToListAsync(ct);
        foreach (var s in others)
        {
            var sEndDate = s.EndDate ?? ShiftHours.DeriveEndDate(s.WorkDate, s.StartTime, s.EndTime!);
            var sStart = ShiftHours.AbsMinutes(s.WorkDate, s.StartTime);
            var sEnd   = ShiftHours.AbsMinutes(sEndDate, s.EndTime!);
            if (newStartAbs < sEnd && sStart < newEndAbs)
                throw new InvalidOperationException("This shift overlaps with an existing shift.");
        }

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

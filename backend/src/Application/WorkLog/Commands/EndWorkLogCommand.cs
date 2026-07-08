using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.WorkLog.Queries;
using HRNexus.Domain.Constants;

namespace HRNexus.Application.WorkLog.Commands;

public record EndWorkLogCommand(Guid Id, Guid EmployeeId, string EndTime, DateOnly? EndDate = null) : IRequest;

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

        // The clock-out records an explicit end date (the client defaults it to the clock-out day), so a
        // shift may run for several days; fall back to the same-day/overnight inference if none was sent.
        var endDate = req.EndDate ?? ShiftHours.DeriveEndDate(log.WorkDate, log.StartTime, req.EndTime);

        // Duration must be positive and within the sanity cap — multi-day is allowed, an absurd span
        // (likely a mis-picked date) is rejected so one shift can't be stretched to months.
        var hours = ShiftHours.CalcHours(log.WorkDate, log.StartTime, endDate, req.EndTime);
        if (hours <= 0)
            throw new ArgumentException("End time must be after the shift start.");
        if (hours > AppConstants.WorkLog.MaxShiftDays * 24)
            throw new ArgumentException($"A shift cannot exceed {AppConstants.WorkLog.MaxShiftDays} days — please check the dates.");

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

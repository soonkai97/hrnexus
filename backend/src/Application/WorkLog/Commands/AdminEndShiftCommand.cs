using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.WorkLog.Queries;
using HRNexus.Domain.Entities;
using System.Globalization;

namespace HRNexus.Application.WorkLog.Commands;

/// <summary>
/// A manager/admin closes an OPEN shift (a staff member who forgot to clock out) by setting the real
/// end time. This fills a gap between the other two commands:
///   • <see cref="EndWorkLogCommand"/> is owner-only (a staff member ending their own shift), and
///   • <see cref="UpdateWorkLogCommand"/> deliberately refuses active shifts ("End it first").
/// Respects payroll-period locks, writes an audit row, and re-broadcasts live dashboard stats.
/// </summary>
public record AdminEndShiftCommand(Guid Id, Guid CallerId, string EndTime) : IRequest;

public class AdminEndShiftCommandHandler(IAppDbContext db, ISender sender, INotificationHub hub)
    : IRequestHandler<AdminEndShiftCommand>
{
    public async Task Handle(AdminEndShiftCommand req, CancellationToken ct)
    {
        if (!TimeOnly.TryParseExact(req.EndTime, "HH:mm", null, DateTimeStyles.None, out _))
            throw new ArgumentException("EndTime must be in HH:mm format.");

        var log = await db.WorkLogs.FindAsync([req.Id], ct)
            ?? throw new KeyNotFoundException("Work log not found.");

        if (log.EndTime != null)
            throw new InvalidOperationException("Shift already ended.");

        // A forgotten clock-out could sit in a month that has since been locked.
        await PayrollLockGuard.EnsureDateOpenAsync(db, log.WorkDate, ct);

        var hours = ShiftHours.CalcHours(log.StartTime, req.EndTime);
        if (hours <= 0)
            throw new ArgumentException("End time must be different from the shift start time.");

        var before = $"Date={log.WorkDate},Start={log.StartTime},End=(open)";
        log.End(req.EndTime);

        var actor = await db.Staff
            .Where(s => s.Id == req.CallerId)
            .Select(s => new { s.FullName })
            .FirstOrDefaultAsync(ct);

        db.AuditLogs.Add(AuditLog.Create(
            entityType:      "WorkLog",
            entityId:        log.Id.ToString(),
            action:          "AdminEnded",
            performedById:   req.CallerId,
            performedByName: actor?.FullName ?? "Unknown",
            changes:         $"Before: {before} | After: End={req.EndTime} (admin closed a forgotten clock-out)"));

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("This shift was just changed by someone else. Please refresh and try again.");
        }

        // Keep every open dashboard in sync (the on-shift count / cost just changed).
        var stats = await sender.Send(new GetLiveStatsQuery(), ct);
        await hub.BroadcastLiveStatsAsync(stats, ct);
    }
}

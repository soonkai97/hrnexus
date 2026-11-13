using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Leave;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRNexus.Application.Admin.Commands;

public record UpsertLeaveCommand(
    Guid?    Id,
    Guid     EmployeeId,
    string   LeaveType,
    DateTime StartDate,
    DateTime EndDate,
    string?  StartTime,
    string?  EndTime,
    decimal? Hours,
    string?  Notes,
    string   Status,
    string?  RejectionReason = null,
    bool     SkipAdvanceNotice = false,
    Guid?    ActingUserId = null) : IRequest<Guid>;

public class UpsertLeaveCommandHandler(IAppDbContext db, INotificationHub notifHub,
    IPushNotificationService pushSvc, IAuditService audit, IDateTimeProvider dt)
    : IRequestHandler<UpsertLeaveCommand, Guid>
{
    private const int MaxLeaveDays = 30;

    public async Task<Guid> Handle(UpsertLeaveCommand req, CancellationToken ct)
    {
        ValidateRequest(req);

        // Stable Id for the create path so a retried attempt can recognise its own committed row.
        // EnableRetryOnFailure re-runs the whole delegate on a transient; without this, an attempt
        // that already COMMITTED could be retried, the retry would re-read the now-full balance and
        // wrongly throw "insufficient" — leaving a committed row while the caller sees an error.
        var newLeaveId = Guid.NewGuid();

        // Validation runs outside the strategy; transactional DB work runs inside.
        return await db.ExecuteInStrategyAsync(async ct =>
        {
            // Start each attempt from a clean tracker so a retry can't collide with entities the
            // previous (rolled-back) attempt left tracked — especially the create entity, which now
            // carries a stable Id across attempts.
            db.ClearChangeTracker();

            var isRejectedOrCancelled = req.Status.Equals(AppConstants.Leave.Rejected,  StringComparison.OrdinalIgnoreCase)
                                     || req.Status.Equals(AppConstants.Leave.Cancelled, StringComparison.OrdinalIgnoreCase);
            var isEmergency         = req.LeaveType.Equals(AppConstants.Leave.Emergency, StringComparison.OrdinalIgnoreCase);
            var isAnnualOrEmergency = req.LeaveType.Equals(AppConstants.Leave.Annual,    StringComparison.OrdinalIgnoreCase) || isEmergency;
            var isSick              = req.LeaveType.Equals(AppConstants.Leave.Sick,      StringComparison.OrdinalIgnoreCase);

            // Serializable (not RepeatableRead): two concurrent approvals insert DIFFERENT leave
            // rows, which snapshot isolation can't serialize — both could pass the balance check
            // and overdraw. Serializable SSI detects the read/write conflict and aborts one; the
            // execution strategy (EnableRetryOnFailure) retries it, and the retry sees the now-
            // committed leave and correctly rejects if over the limit.
            IDbContextTransaction? tx = await db.BeginSerializableAsync(ct);
            try
            {
                // Idempotency guard: if a prior (committed) attempt of this create already inserted
                // the row, the execution strategy is retrying us — return that row rather than
                // re-running the balance check (which would now see a full balance and falsely fail).
                if (!req.Id.HasValue && await db.LeaveRequests.AnyAsync(l => l.Id == newLeaveId, ct))
                {
                    if (tx != null) { await tx.CommitAsync(ct); tx.Dispose(); tx = null; }
                    return newLeaveId;
                }

                // An update is checked against the person who OWNS the leave: a body naming another
                // employee would otherwise run the balance and overlap checks on the wrong person.
                LeaveRequest? existing = null;
                if (req.Id.HasValue)
                {
                    existing = await db.LeaveRequests.FindAsync([req.Id.Value], ct)
                        ?? throw new KeyNotFoundException($"Leave request {req.Id} not found.");
                    if (existing.EmployeeId != req.EmployeeId)
                        throw new ArgumentException("This leave request belongs to a different employee.");
                    EnsureNotOwnDecision(req.ActingUserId, existing.EmployeeId, req.Status);
                    EnsureEditableAfterTheFact(existing, req);
                }

                // The server owns the hours: working days (Mon-Fri, 8h), never the client's number. A
                // status-only edit keeps the stored hours, so a row recorded before this rule (for
                // example a weekend) can still be approved, rejected or marked no-show.
                var datesOrTimesChanged = existing is null
                    || existing.StartDate.Date != req.StartDate.Date || existing.EndDate.Date != req.EndDate.Date
                    || existing.StartTime != req.StartTime || existing.EndTime != req.EndTime;
                var hours = datesOrTimesChanged || existing?.Hours is null
                    ? LeaveHoursCalculator.Calculate(req.StartDate, req.EndDate, req.StartTime, req.EndTime)
                    : existing.Hours.Value;

                if (!isRejectedOrCancelled && (isAnnualOrEmergency || isSick))
                    await EnsureSufficientBalanceAsync(req, hours, isAnnualOrEmergency, ct);

                await EnsureNoOverlapAsync(req, ct);

                if (existing is not null)
                {
                    var prevStatus = existing.Status;
                    existing.Update(req.LeaveType, req.StartDate, req.EndDate,
                        req.StartTime, req.EndTime, hours,
                        req.Notes, req.Status, req.RejectionReason);
                    await db.SaveChangesAsync(ct);

                    if (tx != null) { await tx.CommitAsync(ct); tx.Dispose(); tx = null; }

                    if (!prevStatus.Equals(req.Status, StringComparison.OrdinalIgnoreCase)
                        && (req.Status.Equals(AppConstants.Leave.Approved, StringComparison.OrdinalIgnoreCase)
                            || req.Status.Equals(AppConstants.Leave.Rejected, StringComparison.OrdinalIgnoreCase)))
                    {
                        await CreateAndSendNotification(existing.EmployeeId, existing.Id,
                            req.Status.ToLower(), req.LeaveType, req.StartDate, req.EndDate, ct);
                    }

                    await audit.LogAsync("LeaveRequest", existing.Id.ToString(), "Updated",
                        null, "Admin", $"Status → {req.Status}", ct);

                    return existing.Id;
                }
                else
                {
                    var wasPendingAutoApproved = (isSick || isEmergency)
                        && req.Status.Equals(AppConstants.Leave.Pending, StringComparison.OrdinalIgnoreCase);
                    var resolvedStatus = wasPendingAutoApproved
                        ? AppConstants.Leave.Approved
                        : req.Status;

                    var leave = LeaveRequest.Create(req.EmployeeId, req.LeaveType,
                        req.StartDate, req.EndDate,
                        req.StartTime, req.EndTime, hours,
                        req.Notes, id: newLeaveId);
                    if (!resolvedStatus.Equals(AppConstants.Leave.Pending, StringComparison.OrdinalIgnoreCase))
                        leave.Update(req.LeaveType, req.StartDate, req.EndDate,
                            req.StartTime, req.EndTime, hours,
                            req.Notes, resolvedStatus);
                    db.LeaveRequests.Add(leave);
                    await db.SaveChangesAsync(ct);

                    if (tx != null) { await tx.CommitAsync(ct); tx.Dispose(); tx = null; }

                    // Pass null performer so AuditService attributes the entry to the authenticated
                    // caller (the admin who recorded it, or the staff member for self-service) rather
                    // than always crediting the leave's subject employee.
                    await audit.LogAsync("LeaveRequest", leave.Id.ToString(), "Created",
                        null, "Staff", $"{req.LeaveType} {req.StartDate:yyyy-MM-dd}–{req.EndDate:yyyy-MM-dd} ({hours:0.##}h)", ct);

                    if (wasPendingAutoApproved)
                    {
                        await CreateAndSendNotification(req.EmployeeId, leave.Id,
                            AppConstants.Leave.Approved, req.LeaveType, req.StartDate, req.EndDate, ct);

                        var emp = await db.Staff.FindAsync([req.EmployeeId], ct);
                        var who = emp?.FullName ?? "An employee";
                        var (title, message) = isSick
                            ? ("Sick Leave Reported",   $"{who} has reported sick leave ({req.StartDate:dd/MM} – {req.EndDate:dd/MM}, {hours:0.##}h).")
                            : ("Emergency Leave Taken", $"{who} has taken emergency leave ({req.StartDate:dd/MM} – {req.EndDate:dd/MM}, {hours:0.##}h).");
                        await NotifyManagersAsync(leave.Id.ToString(), title, message, ct);
                    }
                    else if (resolvedStatus.Equals(AppConstants.Leave.Pending, StringComparison.OrdinalIgnoreCase))
                    {
                        var emp = await db.Staff.FindAsync([req.EmployeeId], ct);
                        var who = emp?.FullName ?? "An employee";
                        var message = $"{who} applied for {req.LeaveType} leave " +
                                      $"({req.StartDate:dd/MM} – {req.EndDate:dd/MM}, {hours:0.##}h).";
                        await NotifyManagersAsync(leave.Id.ToString(), "New Leave Request", message, ct);
                    }

                    return leave.Id;
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                if (tx != null)
                    try { tx.Rollback(); } catch { /* ignore rollback errors */ }
                throw new InvalidOperationException(
                    "This leave request was modified concurrently. Please refresh and try again.");
            }
            catch
            {
                if (tx != null)
                    try { tx.Rollback(); } catch { /* ignore rollback errors */ }
                throw;
            }
            finally
            {
                tx?.Dispose();
            }
        }, ct);
    }

    /// <summary>Stateless input validation — runs once, outside the retryable transaction scope.</summary>
    private void ValidateRequest(UpsertLeaveCommand req)
    {
        if (!req.Id.HasValue)
            EnsureNotOwnDecision(req.ActingUserId, req.EmployeeId, req.Status);

        string[] validTypes =
        [
            AppConstants.Leave.Sick, AppConstants.Leave.Annual,
            AppConstants.Leave.Unpaid, AppConstants.Leave.NoShow,
            AppConstants.Leave.Emergency,
        ];
        if (!validTypes.Contains(req.LeaveType, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid leave type '{req.LeaveType}'.");

        if (req.StartDate > req.EndDate)
            throw new ArgumentException("StartDate must be on or before EndDate.");

        if (!req.SkipAdvanceNotice
            && !req.Id.HasValue
            && req.LeaveType.Equals(AppConstants.Leave.Annual, StringComparison.OrdinalIgnoreCase)
            && req.StartDate.Date < dt.LocalNow.Date.AddDays(AppConstants.Leave.AnnualLeaveAdvanceDays))
        {
            throw new ArgumentException(
                $"Annual leave must be applied at least {AppConstants.Leave.AnnualLeaveAdvanceDays} days in advance. " +
                "For urgent or same-day requests, please select Emergency Leave instead.");
        }

        if ((req.EndDate.Date - req.StartDate.Date).Days + 1 > MaxLeaveDays)
            throw new ArgumentException($"Leave duration cannot exceed {MaxLeaveDays} days in a single request.");

        if (req.StartDate.Date > dt.LocalNow.Date.AddYears(1))
            throw new ArgumentException("Leave cannot be requested more than 1 year in advance.");
    }

    /// <summary>
    /// Leave that has already ended keeps its dates, times and type: changing them after the fact would
    /// refund a balance or hide an absence. Its status can still change, and an overdue request can
    /// still become a no-show.
    /// </summary>
    private void EnsureEditableAfterTheFact(LeaveRequest existing, UpsertLeaveCommand req)
    {
        if (existing.EndDate.Date >= dt.LocalNow.Date) return;

        var datesOrTimesChanged = existing.StartDate.Date != req.StartDate.Date || existing.EndDate.Date != req.EndDate.Date
                               || existing.StartTime != req.StartTime || existing.EndTime != req.EndTime;
        var retyped = !existing.LeaveType.Equals(req.LeaveType, StringComparison.OrdinalIgnoreCase);
        var becomesNoShow = req.LeaveType.Equals(AppConstants.Leave.NoShow, StringComparison.OrdinalIgnoreCase);
        if (datesOrTimesChanged || (retyped && !becomesNoShow))
            throw new InvalidOperationException(
                "Leave that has already ended can only have its status changed or be marked as a no-show.");
    }

    /// <summary>
    /// Nobody may approve or reject their own leave. <paramref name="actingUserId"/> is the signed-in
    /// approver (null for self-service and background jobs). A person with leave.approve can still
    /// record their own leave as pending; another approver then decides it.
    /// </summary>
    internal static void EnsureNotOwnDecision(Guid? actingUserId, Guid leaveOwnerId, string status)
    {
        var isDecision = status.Equals(AppConstants.Leave.Approved, StringComparison.OrdinalIgnoreCase)
                      || status.Equals(AppConstants.Leave.Rejected, StringComparison.OrdinalIgnoreCase);
        if (isDecision && actingUserId == leaveOwnerId)
            throw new InvalidOperationException(
                "You cannot approve or reject your own leave request. Ask another approver.");
    }

    /// <summary>Throws if the request would exceed the employee's annual (incl. emergency) or sick
    /// balance. A request that crosses 31 Dec / 1 Jan is checked per year, by the working days that
    /// fall in each year. Runs inside the Serializable transaction so the read can't race a
    /// concurrent insert.</summary>
    private async Task EnsureSufficientBalanceAsync(UpsertLeaveCommand req, decimal hours, bool isAnnualOrEmergency, CancellationToken ct)
    {
        var poolName = isAnnualOrEmergency ? "annual" : "sick";

        for (var year = req.StartDate.Year; year <= req.EndDate.Year; year++)
        {
            var requested = LeaveHoursCalculator.HoursInYear(req.StartDate, req.EndDate, hours, year);
            if (requested <= 0) continue;

            var entitlement = await db.LeaveEntitlements
                .FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId && e.Year == year, ct);

            decimal entitlementHours = isAnnualOrEmergency
                ? (entitlement?.TotalAnnualHours ?? 0m)
                : (entitlement?.SickHours   ?? 0m);

            if (entitlementHours <= 0)
                throw new InvalidOperationException(
                    $"No {poolName} leave entitlement has been set for {year}. Please contact your manager.");

            var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var yearEnd   = new DateTime(year, 12, 31, 0, 0, 0, DateTimeKind.Utc);
            var others = await db.LeaveRequests
                .Where(l => l.EmployeeId == req.EmployeeId
                         && (isAnnualOrEmergency
                             ? (l.LeaveType == AppConstants.Leave.Annual || l.LeaveType == AppConstants.Leave.Emergency)
                             : l.LeaveType == AppConstants.Leave.Sick)
                         && l.StartDate <= yearEnd && l.EndDate >= yearStart
                         && l.Status != AppConstants.Leave.Rejected
                         && l.Status != AppConstants.Leave.Cancelled
                         && l.Id != (req.Id ?? Guid.Empty))
                .Select(l => new { l.StartDate, l.EndDate, l.Hours })
                .ToListAsync(ct);
            var usedHours = others.Sum(l => LeaveHoursCalculator.HoursInYear(l.StartDate, l.EndDate, l.Hours, year));

            if (usedHours + requested > entitlementHours)
            {
                var remaining = Math.Max(0, entitlementHours - usedHours);
                throw new InvalidOperationException(
                    $"Insufficient {poolName} leave balance for {year}. {remaining:0.##}h remaining.");
            }
        }
    }

    /// <summary>Throws if another pending or approved leave for this employee overlaps the date range
    /// (rejected and cancelled leave never blocks a new request).</summary>
    private async Task EnsureNoOverlapAsync(UpsertLeaveCommand req, CancellationToken ct)
    {
        var start = DateTime.SpecifyKind(req.StartDate.Date, DateTimeKind.Utc);
        var end   = DateTime.SpecifyKind(req.EndDate.Date,   DateTimeKind.Utc);

        var hasOverlap = await db.LeaveRequests
            .AnyAsync(l => l.EmployeeId == req.EmployeeId
                        && l.Id != (req.Id ?? Guid.Empty)
                        && l.Status != AppConstants.Leave.Rejected
                        && l.Status != AppConstants.Leave.Cancelled
                        && l.StartDate <= end
                        && l.EndDate   >= start, ct);

        if (hasOverlap)
            throw new InvalidOperationException(
                "A leave request already exists overlapping the selected date range.");
    }

    /// <summary>Persists and pushes a "pending"-type notification to every active manager/admin.</summary>
    private async Task NotifyManagersAsync(string leaveId, string title, string message, CancellationToken ct)
    {
        var managers = await db.Staff
            .Where(e => e.IsActive &&
                (e.RoleEntries.Any(r => r.Role.Name == AppConstants.Roles.Manager) ||
                 e.RoleEntries.Any(r => r.Role.Name == AppConstants.Roles.Admin)))
            .ToListAsync(ct);

        foreach (var mgr in managers)
        {
            var notif = Notification.Create(mgr.Id, title, message,
                AppConstants.NotificationType.LeavePending, leaveId);
            db.Notifications.Add(notif);
            await notifHub.SendToUserAsync(mgr.Id, ToPayload(notif), ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task CreateAndSendNotification(Guid employeeId, Guid leaveId,
        string status, string leaveType, DateTime start, DateTime end, CancellationToken ct)
    {
        var approved = status == "approved";
        var notif = Notification.Create(
            employeeId,
            approved ? "Leave Approved ✓" : "Leave Rejected",
            $"Your {leaveType} leave ({start:MMM d} – {end:MMM d}) has been {status}.",
            approved ? AppConstants.NotificationType.LeaveApproved : AppConstants.NotificationType.LeaveRejected,
            leaveId.ToString());

        db.Notifications.Add(notif);
        await db.SaveChangesAsync(ct);
        await notifHub.SendToUserAsync(employeeId, ToPayload(notif), ct);

        // Also send a browser push notification
        await pushSvc.SendAsync(
            employeeId,
            approved ? "Leave Approved ✓" : "Leave Rejected",
            $"Your {leaveType} leave ({start:MMM d} – {end:MMM d}) has been {status}.",
            "/my-leave", ct);
    }

    private static NotificationPayload ToPayload(Notification n) =>
        new(n.Id.ToString(), n.Title, n.Message, n.Type,
            n.RelatedId, n.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
}

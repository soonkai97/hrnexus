using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Auth.Commands;

public record ForgotPasswordCommand(string StaffId) : IRequest<ForgotPasswordResult>;

/// <param name="Found">Whether a matching active employee was found.</param>
public record ForgotPasswordResult(bool Found);

public class ForgotPasswordCommandHandler(
    IAppDbContext db,
    INotificationHub notificationHub)
    : IRequestHandler<ForgotPasswordCommand, ForgotPasswordResult>
{
    public async Task<ForgotPasswordResult> Handle(ForgotPasswordCommand req, CancellationToken ct)
    {
        var staffId = req.StaffId.Trim();

        // Find the employee by Staff ID (case-insensitive)
        var emp = await db.Staff
            .FirstOrDefaultAsync(e => e.IsActive &&
                e.StaffId.ToLower() == staffId.ToLower(), ct);

        if (emp is null) return new ForgotPasswordResult(false);

        // Rate limiting: reject if a reset was already requested for this employee in the last 5 minutes
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var recentRequest = await db.Notifications
            .AnyAsync(n => n.Type == "password_reset" && n.RelatedId == emp.Id.ToString() && n.CreatedAt >= cutoff, ct);
        if (recentRequest)
            throw new InvalidOperationException("A password reset was already requested recently. Please wait a few minutes before trying again.");

        // Filter managers/admins directly in SQL via the normalised StaffRoles table.
        var adminsAndManagers = await db.Staff
            .Where(e => e.IsActive &&
                (e.RoleEntries.Any(r => r.Role.Name == AppConstants.Roles.Admin) ||
                 e.RoleEntries.Any(r => r.Role.Name == AppConstants.Roles.Manager)))
            .ToListAsync(ct);

        var title   = "🔐 Password Reset Request";
        var message = $"{emp.FullName} ({emp.StaffId}) has requested a password reset. " +
                      $"Please go to Staff → Reset Password to set a new password, then inform them verbally.";

        foreach (var admin in adminsAndManagers)
        {
            var notification = Notification.Create(
                admin.Id, title, message, "password_reset", emp.Id.ToString());
            db.Notifications.Add(notification);

            // Push real-time notification via SignalR
            await notificationHub.SendToUserAsync(admin.Id,
                new NotificationPayload(
                    notification.Id.ToString(),
                    title,
                    message,
                    "password_reset",
                    emp.Id.ToString(),
                    notification.CreatedAt.ToString("o")),
                ct);
        }

        await db.SaveChangesAsync(ct);

        return new ForgotPasswordResult(true);
    }
}

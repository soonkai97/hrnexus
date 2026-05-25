using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Announcements.Commands;

/// <summary>Create or update an announcement. Pass Id=null to create.</summary>
public record UpsertAnnouncementCommand(
    Guid?     Id,
    string    Title,
    string    Body,
    string    TargetRoles,
    DateTime? ExpiresAt,
    Guid      CreatedById,
    string    CreatedByName) : IRequest<Guid>;

public class UpsertAnnouncementCommandHandler(IAppDbContext db,
    INotificationHub hub, IPushNotificationService pushSvc, IAuditService audit)
    : IRequestHandler<UpsertAnnouncementCommand, Guid>
{
    public async Task<Guid> Handle(UpsertAnnouncementCommand req, CancellationToken ct)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Body))
            throw new ArgumentException("Title and body are required.");
        if (req.Title.Length > 200)
            throw new ArgumentException("Title cannot exceed 200 characters.");
        if (req.Body.Length > 5000)
            throw new ArgumentException("Body cannot exceed 5000 characters.");
        if (req.ExpiresAt.HasValue && req.ExpiresAt.Value <= DateTime.UtcNow)
            throw new ArgumentException("Expiry date must be in the future.");

        // Validate that each submitted role name actually exists in the system
        if (!req.TargetRoles.Equals("*", StringComparison.OrdinalIgnoreCase))
        {
            var roles = req.TargetRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var roleName in roles)
            {
                var exists = await db.Roles.FindAsync([roleName.ToUpperInvariant()], ct);
                if (exists is null)
                    throw new ArgumentException($"Role '{roleName}' does not exist in the system.");
            }
        }

        if (req.Id.HasValue)
        {
            var existing = await db.Announcements.FindAsync([req.Id.Value], ct)
                ?? throw new KeyNotFoundException($"Announcement {req.Id} not found.");
            existing.Update(req.Title, req.Body, req.TargetRoles, req.ExpiresAt);
            await db.SaveChangesAsync(ct);
            await audit.LogAsync("Announcement", existing.Id.ToString(), "Updated",
                req.CreatedById, req.CreatedByName, null, ct);
            return existing.Id;
        }
        else
        {
            var announcement = Announcement.Create(
                req.Title, req.Body, req.TargetRoles,
                req.CreatedById, req.CreatedByName, req.ExpiresAt);
            db.Announcements.Add(announcement);
            await db.SaveChangesAsync(ct);

            // Notify all active staff matching the target roles
            var targetRoles = req.TargetRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var allRoles    = targetRoles.Contains("*", StringComparer.OrdinalIgnoreCase);

            var staffQuery = db.Staff.Where(s => s.IsActive);
            var staff = allRoles
                ? await staffQuery.ToListAsync(ct)
                : await staffQuery.Where(s => s.RoleEntries.Any(r => targetRoles.Contains(r.Role))).ToListAsync(ct);

            var staffIds = staff.Select(s => s.Id).ToList();

            // In-app notifications
            foreach (var s in staff)
            {
                var notif = Notification.Create(s.Id, $"📢 {req.Title}", req.Body, "announcement", announcement.Id.ToString());
                db.Notifications.Add(notif);
                try { await hub.SendToUserAsync(s.Id, new NotificationPayload(
                    notif.Id.ToString(), $"📢 {req.Title}", req.Body, "announcement",
                    announcement.Id.ToString(), notif.CreatedAt.ToString("o")), ct); }
                catch { }
            }
            await db.SaveChangesAsync(ct);

            // Browser push (batched)
            try { await pushSvc.SendToManyAsync(staffIds, $"📢 {req.Title}", req.Body, "/clock-in", ct); }
            catch { }

            await audit.LogAsync("Announcement", announcement.Id.ToString(), "Created",
                req.CreatedById, req.CreatedByName, $"TargetRoles={req.TargetRoles}", ct);

            return announcement.Id;
        }
    }
}

using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;
using HRNexus.Domain.Leave;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRNexus.Application.Leave.Commands;

/// <summary>
/// Replaces the company's whole leave-type catalogue in one save (add, edit, deactivate and drop are all one
/// request), like the company policy: a save made from a stale copy is refused.
/// </summary>
/// <param name="ExpectedVersion">The catalogue version the editor loaded (0 while nothing is saved).</param>
/// <param name="Types">The complete catalogue as it should be after the save.</param>
public record SaveLeaveTypesCommand(int ExpectedVersion, IReadOnlyList<LeaveTypeSpec> Types) : IRequest<int>;

public class SaveLeaveTypesCommandHandler(IAppDbContext db, IAuditService audit, ICurrentUserService currentUser)
    : IRequestHandler<SaveLeaveTypesCommand, int>
{
    private const string StaleMessage = "The leave types were changed by someone else. Reload them and try again.";

    /// <returns>The new catalogue version.</returns>
    public async Task<int> Handle(SaveLeaveTypesCommand req, CancellationToken ct)
    {
        // Cheap checks first, outside the transaction.
        var keys = req.Types.Select(t => t.Key?.Trim() ?? "").ToList();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
            throw new ArgumentException("Each leave type needs its own key.");

        return await db.ExecuteInStrategyAsync(async ct =>
        {
            db.ClearChangeTracker();
            // Serializable: two administrators saving at once must not both pass the version check.
            IDbContextTransaction? tx = await db.BeginSerializableAsync(ct);
            try
            {
                var rows = await db.LeaveTypes.ToListAsync(ct);
                var saved = rows.Count > 0;
                var current = saved ? rows.Max(r => r.CatalogVersion) : 0;
                if (req.ExpectedVersion != current) throw new InvalidOperationException(StaleMessage);

                // Until a catalogue is saved the code defaults are in force, so the first save edits THEM.
                var baseline = saved ? rows : [.. LeaveTypeDefaults.All()];
                var byKey = baseline.ToDictionary(r => r.Key, StringComparer.Ordinal);
                var hasLeave = (await db.LeaveRequests.Select(l => l.LeaveType).Distinct().ToListAsync(ct))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var user = currentUser.UserName;

                var final = new List<LeaveTypeDefinition>();
                foreach (var spec in req.Types)
                {
                    var key = spec.Key?.Trim() ?? "";
                    if (byKey.TryGetValue(key, out var row))
                    {
                        row.Update(spec, hasLeave.Contains(key), user);
                        final.Add(row);
                    }
                    else
                        final.Add(LeaveTypeDefinition.Create(spec, isSystem: false, createdBy: user));
                }

                var dropped = baseline.Where(r => !final.Contains(r)).ToList();
                foreach (var r in dropped.Where(r => r.IsSystem || hasLeave.Contains(r.Key)))
                    throw new ArgumentException(r.IsSystem
                        ? $"'{r.Label}' is a system type and must stay in the catalogue."
                        : $"'{r.Label}' already has leave, so it cannot be removed. Deactivate it instead.");

                LeaveTypeDefinition.EnsureValidCatalogue(final);

                var version = current + 1;
                foreach (var t in final) t.SetCatalogVersion(version);

                db.LeaveTypes.AddRange(final.Where(t => !rows.Contains(t)));
                db.LeaveTypes.RemoveRange(dropped);
                await db.SaveChangesAsync(ct);

                // The audit row says THAT the catalogue changed and which types it holds, never their limits.
                // It is written in the same transaction as the change, so neither can exist without the other.
                await audit.LogAsync("LeaveTypes", "catalogue", "Updated", null, "Admin",
                    $"Version {version}; types: {string.Join(", ", final.Select(t => t.Key))}", ct);

                if (tx != null) { await tx.CommitAsync(ct); tx.Dispose(); tx = null; }
                return version;
            }
            catch (DbUpdateException)
            {
                // Two first saves raced and the second hit the key, or a version check passed on stale data.
                if (tx != null) try { tx.Rollback(); } catch { /* ignore rollback errors */ }
                throw new InvalidOperationException(StaleMessage);
            }
            catch
            {
                if (tx != null) try { tx.Rollback(); } catch { /* ignore rollback errors */ }
                throw;
            }
            finally
            {
                tx?.Dispose();
            }
        }, ct);
    }
}

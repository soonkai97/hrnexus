using HRNexus.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRNexus.Application.Common;

/// <summary>
/// One Serializable transaction inside the retry strategy, started from a clean change tracker. Used where a check
/// and the write that depends on it must not interleave with another writer (a stale write surfaces as a clear
/// conflict, and a serialization failure is retried by the strategy with the other writer's result in view).
/// </summary>
public static class SerializableWork
{
    public static Task<T> RunAsync<T>(IAppDbContext db, Func<CancellationToken, Task<T>> work, CancellationToken ct) =>
        db.ExecuteInStrategyAsync(async c =>
        {
            db.ClearChangeTracker();
            IDbContextTransaction? tx = await db.BeginSerializableAsync(c);
            try
            {
                var result = await work(c);
                if (tx != null) await tx.CommitAsync(c);
                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException("This record was changed by someone else. Refresh and try again.");
            }
            finally
            {
                tx?.Dispose();   // an uncommitted transaction rolls back here
            }
        }, ct);
}

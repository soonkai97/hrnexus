using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Payroll;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Payroll.Commands;

/// <summary>The accountant's sign-off that a rate row matches the official table. It unlocks payroll approval.</summary>
public record VerifyStatutoryRateCommand(Guid Id, string Note) : IRequest;

public class VerifyStatutoryRateCommandHandler(
    IAppDbContext db, IAuditService audit, ICurrentUserService currentUser, IDateTimeProvider clock)
    : IRequestHandler<VerifyStatutoryRateCommand>
{
    public async Task Handle(VerifyStatutoryRateCommand req, CancellationToken ct)
    {
        var row = await db.StatutoryRateTables.FirstOrDefaultAsync(t => t.Id == req.Id, ct)
            ?? throw new KeyNotFoundException($"Rate row {req.Id} not found.");

        // Who verified comes from the signed-in user, never from the request.
        var by = currentUser.UserName;
        if (string.IsNullOrWhiteSpace(by))
            throw new UnauthorizedAccessException("You must be signed in to verify a rate.");

        row.Verify(by, clock.UtcNow, req.Note);

        // The audit service saves by itself, so this single call commits the verification and its audit entry
        // together. The entry names the row and carries no rate figures.
        await audit.LogAsync("StatutoryRate", row.Id.ToString(), "Verified", null, "Admin",
            $"{row.Scheme} {row.CitizenshipClass} from {row.EffectiveFrom:yyyy-MM-dd}", ct);
    }
}

/// <summary>Switches an optional HRD Corp levy row on or off. Nothing else about a row can be changed.</summary>
public record SetStatutoryRateActiveCommand(Guid Id, bool Active) : IRequest;

public class SetStatutoryRateActiveCommandHandler(IAppDbContext db, IAuditService audit)
    : IRequestHandler<SetStatutoryRateActiveCommand>
{
    public async Task Handle(SetStatutoryRateActiveCommand req, CancellationToken ct)
    {
        var row = await db.StatutoryRateTables.FirstOrDefaultAsync(t => t.Id == req.Id, ct)
            ?? throw new KeyNotFoundException($"Rate row {req.Id} not found.");

        if (row.Scheme is not (StatutoryScheme.HrdCompulsory or StatutoryScheme.HrdOptional))
            throw new ArgumentException("Only the HRD Corp levy rows can be switched on or off.");

        if (req.Active && !row.Active
            && await db.StatutoryRateTables.AnyAsync(o => o.Id != row.Id && o.Active && o.CitizenshipClass == row.CitizenshipClass
                && (o.Scheme == StatutoryScheme.HrdCompulsory || o.Scheme == StatutoryScheme.HrdOptional), ct))
            throw new InvalidOperationException("Another HRD Corp levy row is already switched on: switch it off first, or the levy would be charged twice.");

        row.SetActive(req.Active);
        await audit.LogAsync("StatutoryRate", row.Id.ToString(), req.Active ? "Activated" : "Deactivated", null, "Admin",
            $"{row.Scheme} {row.CitizenshipClass} from {row.EffectiveFrom:yyyy-MM-dd}", ct);
    }
}

using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Payroll.Commands.Run;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Payroll.Commands;

/// <summary>Where a pay item has been used by a payroll run that is past draft (its figures are frozen or being reviewed).</summary>
internal static class PayItemUse
{
    /// <summary>The ids, out of <paramref name="itemIds"/>, that a submitted, approved or locked run names on a person who is in the run.</summary>
    public static async Task<HashSet<Guid>> UsedByFrozenRunsAsync(IAppDbContext db, IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0) return [];
        var used = await db.PayrollRuns.Where(r => r.State != PayrollRunState.Draft)
            .SelectMany(r => r.Lines).Where(l => !l.Excluded).SelectMany(l => l.Items)
            .Where(i => i.SourceId != null && itemIds.Contains(i.SourceId.Value))
            .Select(i => i.SourceId!.Value).Distinct().ToListAsync(ct);
        return used.ToHashSet();
    }

    /// <summary>The months (year x 12 + month) in which a submitted, approved or locked run has this person in it.</summary>
    public static async Task<HashSet<int>> FrozenMonthsAsync(IAppDbContext db, Guid employeeId, CancellationToken ct) =>
        (await db.PayrollRuns.Where(r => r.State != PayrollRunState.Draft && r.Lines.Any(l => l.EmployeeId == employeeId && !l.Excluded))
            .Select(r => r.Year * 12 + r.Month).ToListAsync(ct)).ToHashSet();

    /// <summary>The latest month (year x 12 + month) a frozen run used the item in, or 0 when none did.</summary>
    public static async Task<int> LatestFrozenMonthAsync(IAppDbContext db, Guid itemId, CancellationToken ct)
    {
        var months = await db.PayrollRuns
            .Where(r => r.State != PayrollRunState.Draft && r.Lines.Any(l => !l.Excluded && l.Items.Any(i => i.SourceId == itemId)))
            .Select(r => r.Year * 12 + r.Month).ToListAsync(ct);
        return months.Count == 0 ? 0 : months.Max();
    }
}

/// <summary>Adds a recurring allowance or deduction to a person's pay. The amount is encrypted at rest; the audit row names the kind and the start date, never the amount or the reason.</summary>
public record AddPayItemCommand(Guid EmployeeId, string Kind, string Reason, decimal Amount, bool CountsForEpf, bool CountsForSocsoEis,
    int Priority, DateOnly From, DateOnly? To, PayrollActor By) : IRequest<Guid>;

public class AddPayItemCommandHandler(IAppDbContext db, IAuditService audit) : IRequestHandler<AddPayItemCommand, Guid>
{
    public async Task<Guid> Handle(AddPayItemCommand req, CancellationToken ct)
    {
        if (decimal.Round(req.Amount, 2) != req.Amount)
            throw new ArgumentException("The amount cannot have more than two decimal places.");
        var item = PayItem.Create(req.EmployeeId, req.Kind, req.Reason, req.Amount, req.CountsForEpf, req.CountsForSocsoEis,
            req.Priority, req.From, req.To, req.By.Name);

        // One Serializable transaction with the audit row, and with the check against frozen months: a run submitted while the
        // item is being added would otherwise freeze without it, and the item would claim to apply to a month it never paid.
        return await SerializableWork.RunAsync(db, async c =>
        {
            await PayrollRunWork.RequirePreparerAsync(db, req.By, c);
            if (!await db.Staff.AnyAsync(s => s.Id == req.EmployeeId, c))
                throw new KeyNotFoundException($"Staff {req.EmployeeId} not found.");
            var frozen = await PayItemUse.FrozenMonthsAsync(db, req.EmployeeId, c);
            if (frozen.Count > 0 && req.From.Year * 12 + req.From.Month <= frozen.Max())
                throw new InvalidOperationException("A payroll run that is already submitted or locked covers that month for this person, so the item cannot start then. Start it from the next month, or pay that month as a one-off adjustment.");

            db.PayItems.Add(item);
            await db.SaveChangesAsync(c);
            await audit.LogAsync("PayItem", item.Id.ToString(), "Created", req.By.Id, req.By.Name, $"{item.Kind} from {item.From:yyyy-MM-dd}", c);
            return item.Id;
        }, ct);
    }
}

/// <summary>
/// Sets the last day a pay item applies. Once a submitted or locked run has used it the item cannot be deleted, so this is how
/// it stops; the end date cannot fall before the last month a frozen run paid it in (that month's payslips stay as they were).
/// </summary>
public record EndPayItemCommand(Guid EmployeeId, Guid ItemId, DateOnly To, PayrollActor By) : IRequest;

public class EndPayItemCommandHandler(IAppDbContext db, IAuditService audit) : IRequestHandler<EndPayItemCommand>
{
    public async Task Handle(EndPayItemCommand req, CancellationToken ct) =>
        await SerializableWork.RunAsync(db, async c =>
        {
            await PayrollRunWork.RequirePreparerAsync(db, req.By, c);
            var item = await db.PayItems.FirstOrDefaultAsync(i => i.Id == req.ItemId && i.EmployeeId == req.EmployeeId, c)
                ?? throw new KeyNotFoundException("Pay item not found.");
            var lastPaid = await PayItemUse.LatestFrozenMonthAsync(db, item.Id, c);
            if (lastPaid > 0 && req.To.Year * 12 + req.To.Month < lastPaid)
                throw new InvalidOperationException("A payroll run that is already submitted or locked paid this item in a later month, so it cannot end before then.");

            // Pushing the end date later must not claim months that were already paid without it.
            if (item.To is { } oldEnd && req.To > oldEnd)
            {
                var frozen = await PayItemUse.FrozenMonthsAsync(db, item.EmployeeId, c);
                var from = oldEnd.Year * 12 + oldEnd.Month;
                var until = req.To.Year * 12 + req.To.Month;
                if (frozen.Any(m => m > from && m <= until))
                    throw new InvalidOperationException("A payroll run that is already submitted or locked covers a month in that stretch for this person, so the item cannot be extended over it.");
            }

            item.End(req.To);
            await db.SaveChangesAsync(c);
            await audit.LogAsync("PayItem", item.Id.ToString(), "Ended", req.By.Id, req.By.Name, $"{item.Kind} ends {req.To:yyyy-MM-dd}", c);
            return 0;
        }, ct);
}

/// <summary>Deletes a pay item entered by mistake. Refused once a submitted, approved or locked run has used it (end it instead).</summary>
public record DeletePayItemCommand(Guid EmployeeId, Guid ItemId, PayrollActor By) : IRequest;

public class DeletePayItemCommandHandler(IAppDbContext db, IAuditService audit) : IRequestHandler<DeletePayItemCommand>
{
    public async Task Handle(DeletePayItemCommand req, CancellationToken ct) =>
        // The check and the delete are one Serializable transaction: a run submitted between them would otherwise freeze a
        // payslip line that names an item that is then gone.
        await SerializableWork.RunAsync(db, async c =>
        {
            await PayrollRunWork.RequirePreparerAsync(db, req.By, c);
            var item = await db.PayItems.FirstOrDefaultAsync(i => i.Id == req.ItemId && i.EmployeeId == req.EmployeeId, c)
                ?? throw new KeyNotFoundException("Pay item not found.");
            if ((await PayItemUse.UsedByFrozenRunsAsync(db, [item.Id], c)).Contains(item.Id))
                throw new InvalidOperationException("A payroll run that is already submitted or locked used this item, so it cannot be deleted. End it instead.");

            db.PayItems.Remove(item);
            await db.SaveChangesAsync(c);
            await audit.LogAsync("PayItem", item.Id.ToString(), "Deleted", req.By.Id, req.By.Name, item.Kind, c);
            return 0;
        }, ct);
}

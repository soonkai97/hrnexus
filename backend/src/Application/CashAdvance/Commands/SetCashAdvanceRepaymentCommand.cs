using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Payroll;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.CashAdvance.Commands;

/// <summary>
/// Sets how much of an advance payroll takes back each month (null = everything still owed, in the next run). It is the
/// amount scheduled, not what is taken: the run takes the lesser of this, what is still owed and what the deduction cap
/// allows. Refused for a settled advance, and while a submitted or approved run has the advance in its figures.
/// </summary>
public record SetCashAdvanceRepaymentCommand(Guid Id, decimal? PerMonth, Guid By, string ByName) : IRequest;

public class SetCashAdvanceRepaymentCommandHandler(IAppDbContext db, IAuditService audit) : IRequestHandler<SetCashAdvanceRepaymentCommand>
{
    public async Task Handle(SetCashAdvanceRepaymentCommand req, CancellationToken ct) =>
        await SerializableWork.RunAsync(db, async c =>
        {
            await PayrollAccess.RequireAsync(db, AppConstants.Permissions.CashAdvanceManage, req.By, c, "You are not allowed to manage cash advances.");
            if (req.PerMonth is { } p && decimal.Round(p, 2) != p)
                throw new ArgumentException("The monthly repayment cannot have more than two decimal places.");
            var advance = await db.CashAdvances.FirstOrDefaultAsync(a => a.Id == req.Id, c)
                ?? throw new KeyNotFoundException("Cash advance not found.");
            if (advance.Status == Domain.Entities.CashAdvance.Settled)
                throw new InvalidOperationException("This cash advance is settled, so there is nothing left to schedule.");

            var inReview = await db.PayrollRuns.Where(r => r.State == PayrollRunState.Submitted || r.State == PayrollRunState.Approved)
                .SelectMany(r => r.Lines).Where(l => !l.Excluded).SelectMany(l => l.Items)
                .AnyAsync(i => i.SourceId == req.Id, c);
            if (inReview)
                throw new InvalidOperationException("A payroll run that is being reviewed already has this cash advance in its figures. Return the run first.");

            advance.SetRepaymentPerMonth(req.PerMonth);
            await db.SaveChangesAsync(c);
            await audit.LogAsync("CashAdvance", advance.Id.ToString(), "ScheduleChanged", req.By, req.ByName, null, c);   // never the amount
            return 0;
        }, ct);
}

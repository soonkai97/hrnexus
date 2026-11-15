using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Payroll.Queries;

public record PayrollWarningDto(string Code, string Message, bool Blocking);
public record PayrollItemDto(string Type, string Section, string Description, decimal Amount);
public record PayrollUnpaidDayDto(DateOnly Date, decimal Days, decimal Amount);

/// <summary>One person's line of a run. Money appears only here and in the totals, and only to callers the endpoint lets through.</summary>
public record PayrollLineDto(
    Guid EmployeeId, string StaffId, string Name, bool Excluded, string? ExcludedReason, decimal? Pcb, bool Calculated,
    decimal Gross, decimal Net, decimal EmployerCost, decimal UnappliedDeductions,
    IReadOnlyList<PayrollWarningDto> Warnings, IReadOnlyList<PayrollItemDto> Items, IReadOnlyList<PayrollUnpaidDayDto> UnpaidDays,
    Guid? SignedOffBy, string? SignedOffByName, DateTime? SignedOffAt);

/// <summary>One step in a run's history: who did what and when. The reason is only ever set on a return.</summary>
public record PayrollRunStepDto(string Action, Guid StaffId, string Name, DateTime At, string? Reason);

public record PayrollRunSummaryDto(
    Guid Id, int Year, int Month, string State, long Version, int People, int LeftOut, int SignedOff,
    decimal TotalGross, decimal TotalNet, decimal TotalEmployerCost, DateTime CreatedAt, DateTime? ExportedAt);

public record NotInRunDto(Guid EmployeeId, string StaffId, string Name, string Reason);

/// <summary>
/// One run in full. UnverifiedRates lists the rate rows the figures rest on that the accountant has not verified (approval is
/// refused while any exist); NotInRun lists people who work this month but cannot be in the run yet, and why (only worked out for a draft).
/// </summary>
public record PayrollRunDetailDto(
    PayrollRunSummaryDto Summary, string CalculatorVersion, decimal Divisor, decimal HoursPerWorkingDay, decimal DeductionCapRate,
    IReadOnlyList<PayrollLineDto> Lines, IReadOnlyList<PayrollRunStepDto> Steps,
    IReadOnlyList<string> UnverifiedRates, IReadOnlyList<NotInRunDto> NotInRun);

/// <summary>A manager's view of one run: only the lines of the people who report to them.</summary>
public record PayrollTeamDto(Guid RunId, int Year, int Month, string State, long Version, IReadOnlyList<PayrollLineDto> Lines);

internal static class PayrollRunMapping
{
    public static PayrollRunSummaryDto Summary(PayrollRun r) => new(
        r.Id, r.Year, r.Month, r.State, r.Version, r.IncludedCount, r.Lines.Count(l => l.Excluded),
        r.Lines.Count(l => !l.Excluded && l.IsSignedOff), r.TotalGross, r.TotalNet,
        r.Lines.Where(l => !l.Excluded).Sum(l => l.EmployerCost), r.CreatedAt, r.ExportedAt);

    public static PayrollLineDto Line(PayrollRunLine l, IReadOnlyDictionary<Guid, string> names) => new(
        l.EmployeeId, l.StaffId, l.Name, l.Excluded, l.ExcludedReason, l.PcbAmount, l.Calculated,
        l.Gross, l.Net, l.EmployerCost, l.UnappliedDeductions,
        l.Warnings.Select(w => new PayrollWarningDto(w.Code, w.Message, w.Blocking)).ToList(),
        l.Items.OrderBy(i => i.Order).Select(i => new PayrollItemDto(i.Type, i.Section.ToString(), i.Description, i.Amount)).ToList(),
        l.UnpaidDays.OrderBy(d => d.Date).Select(d => new PayrollUnpaidDayDto(d.Date, d.Days, d.DeductionAmount)).ToList(),
        l.SignedOffBy, l.SignedOffBy is { } by ? names.GetValueOrDefault(by) : null, l.SignedOffAt);

    /// <summary>Display names for people who acted on a run, including people who have since been deleted.</summary>
    public static async Task<Dictionary<Guid, string>> NamesAsync(IAppDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        var rows = await db.Staff.IgnoreQueryFilters().AsNoTracking().IgnoreAutoIncludes().Where(s => wanted.Contains(s.Id))
            .Select(s => new { s.Id, s.FirstName, s.LastName }).ToListAsync(ct);
        return rows.ToDictionary(s => s.Id, s => $"{s.FirstName} {s.LastName}".Trim());
    }
}

/// <summary>Every run, newest month first, with its totals. Callers must hold payroll.view (enforced at the endpoint).</summary>
public record GetPayrollRunsQuery : IRequest<List<PayrollRunSummaryDto>>;

public class GetPayrollRunsQueryHandler(IAppDbContext db) : IRequestHandler<GetPayrollRunsQuery, List<PayrollRunSummaryDto>>
{
    public async Task<List<PayrollRunSummaryDto>> Handle(GetPayrollRunsQuery req, CancellationToken ct)
    {
        var runs = await db.PayrollRuns.AsNoTracking().Include(r => r.Lines).ToListAsync(ct);   // amounts are encrypted: totals are summed here
        return runs.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).Select(PayrollRunMapping.Summary).ToList();
    }
}

/// <summary>One run in full: every line, its history, the rates it rests on and (for a draft) who cannot be in it yet. Callers must hold payroll.view.</summary>
public record GetPayrollRunQuery(Guid RunId) : IRequest<PayrollRunDetailDto>;

public class GetPayrollRunQueryHandler(IAppDbContext db) : IRequestHandler<GetPayrollRunQuery, PayrollRunDetailDto>
{
    public async Task<PayrollRunDetailDto> Handle(GetPayrollRunQuery req, CancellationToken ct)
    {
        var run = await db.PayrollRuns.Include(r => r.Actors).Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == req.RunId, ct)
            ?? throw new KeyNotFoundException("Payroll run not found.");
        var lines = db.PayrollRuns.Where(r => r.Id == req.RunId).SelectMany(r => r.Lines);
        await lines.Include(l => l.Items).LoadAsync(ct);
        await lines.Include(l => l.UnpaidDays).LoadAsync(ct);
        await lines.Include(l => l.Warnings).LoadAsync(ct);

        var names = await PayrollRunMapping.NamesAsync(db, run.Actors.Select(a => a.StaffId).Concat(run.Lines.Where(l => l.SignedOffBy != null).Select(l => l.SignedOffBy!.Value)), ct);
        var notInRun = new List<NotInRunDto>();
        if (run.State == PayrollRunState.Draft)
        {
            var loaded = await PayrollRunInputs.LoadAsync(db, run.Year, run.Month, ct);
            var present = run.Lines.Select(l => l.EmployeeId).ToHashSet();
            notInRun = loaded.NotInRun.Select(n => new NotInRunDto(n.EmployeeId, n.StaffId, n.Name, n.Reason)).ToList();
            // Someone who has become eligible since the last calculation is not "not in the run": they are waiting for a recalculation.
            notInRun.AddRange(loaded.People.Where(p => !present.Contains(p.EmployeeId))
                .Select(p => new NotInRunDto(p.EmployeeId, p.StaffId, p.Name, "Became eligible after the last calculation; recalculate the run to add them.")));
        }

        return new PayrollRunDetailDto(
            PayrollRunMapping.Summary(run), run.CalculatorVersion, run.Divisor, run.HoursPerWorkingDay, run.DeductionCapRate,
            run.Lines.OrderBy(l => l.StaffId).Select(l => PayrollRunMapping.Line(l, names)).ToList(),
            run.Actors.OrderBy(a => a.At).Select(a => new PayrollRunStepDto(a.Action, a.StaffId, names.GetValueOrDefault(a.StaffId) ?? "", a.At, a.Reason)).ToList(),
            await PayrollRunRates.UnverifiedUsedAsync(db, run, ct), notInRun);
    }
}

/// <summary>
/// The lines of the people who report to the signed-in user, for a run that has been submitted. Needs only payroll.signoff
/// (a manager does not see anyone else's pay, and sees nothing of a draft). Never returns a line of someone who does not report to the caller.
/// </summary>
public record GetPayrollTeamQuery(Guid RunId, Guid ManagerId) : IRequest<PayrollTeamDto>;

public class GetPayrollTeamQueryHandler(IAppDbContext db) : IRequestHandler<GetPayrollTeamQuery, PayrollTeamDto>
{
    public async Task<PayrollTeamDto> Handle(GetPayrollTeamQuery req, CancellationToken ct)
    {
        var run = await db.PayrollRuns.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == req.RunId, ct)
            ?? throw new KeyNotFoundException("Payroll run not found.");
        if (run.State == PayrollRunState.Draft)
            return new PayrollTeamDto(run.Id, run.Year, run.Month, run.State, run.Version, []);   // nothing to sign off yet, and nothing to show

        var reports = await db.Staff.AsNoTracking().IgnoreAutoIncludes().Where(s => s.ReportsToId == req.ManagerId).Select(s => s.Id).ToListAsync(ct);
        var mine = run.Lines.Where(l => !l.Excluded && reports.Contains(l.EmployeeId)).Select(l => l.Id).ToList();
        var lines = db.PayrollRuns.Where(r => r.Id == req.RunId).SelectMany(r => r.Lines).Where(l => mine.Contains(l.Id));
        await lines.Include(l => l.Items).LoadAsync(ct);
        await lines.Include(l => l.UnpaidDays).LoadAsync(ct);
        await lines.Include(l => l.Warnings).LoadAsync(ct);

        var names = await PayrollRunMapping.NamesAsync(db, run.Lines.Where(l => l.SignedOffBy != null).Select(l => l.SignedOffBy!.Value), ct);
        return new PayrollTeamDto(run.Id, run.Year, run.Month, run.State, run.Version,
            run.Lines.Where(l => mine.Contains(l.Id)).OrderBy(l => l.StaffId).Select(l => PayrollRunMapping.Line(l, names)).ToList());
    }
}

using HRNexus.Application.Common.Interfaces;
using HRNexus.Application.Permissions;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;

namespace HRNexus.Application.Payroll;

/// <summary>Which payroll permissions the caller holds right now (read from the database, never from a cached token).</summary>
public sealed record PayrollCaller(Guid Id, bool Manage, bool Approve, bool Signoff, bool Lock, bool Reopen)
{
    public static async Task<PayrollCaller> LoadAsync(IAppDbContext db, Guid id, CancellationToken ct)
    {
        async Task<bool> Holds(string key) => (await PermissionHolders.GetIdsAsync(db, key, ct)).Contains(id);
        return new PayrollCaller(id, await Holds(AppConstants.Permissions.PayrollManage), await Holds(AppConstants.Permissions.PayrollApprove),
            await Holds(AppConstants.Permissions.PayrollSignoff), await Holds(AppConstants.Permissions.PayrollLock),
            await Holds(AppConstants.Permissions.PayrollReopen));
    }
}

/// <summary>
/// What the signed-in person may do with a run right now. The server decides and sends it (<c>allowedActions</c>, and
/// <c>canSignOff</c> on each line); the page only draws the buttons it is told about and never works out who may do what.
/// Every rule here is the one the matching command enforces, so a button that is offered is one the command accepts.
/// </summary>
public static class PayrollRunActions
{
    public const string EditLines = "edit-lines";
    public const string Recalculate = "recalculate";
    public const string Submit = "submit";
    public const string Delete = "delete";
    public const string SignOffAll = "signoff-all";
    public const string Approve = "approve";
    public const string Return = "return";
    public const string Lock = "lock";
    public const string Reopen = "reopen";

    public static IReadOnlyList<string> For(PayrollRun run, PayrollCaller who)
    {
        var actions = new List<string>();
        switch (run.State)
        {
            case PayrollRunState.Draft when who.Manage:
                actions.Add(EditLines);
                actions.Add(Recalculate);
                actions.Add(Submit);
                if (run.CanDelete) actions.Add(Delete);
                break;
            case PayrollRunState.Submitted when who.Approve:
                if (!run.HasPrepared(who.Id))
                {
                    actions.Add(SignOffAll);
                    if (!run.ContainsPayOf(who.Id)) actions.Add(Approve);
                }
                actions.Add(Return);
                break;
            case PayrollRunState.Approved:
                if (who.Lock) actions.Add(Lock);
                if (who.Approve) actions.Add(Return);
                break;
            case PayrollRunState.Locked when who.Reopen && run.ExportedAt is null:
                actions.Add(Reopen);
                break;
        }
        return actions;
    }

    /// <summary>
    /// Whether the caller may sign this line: the head of HR any line, a reporting manager only a direct report; never one's own
    /// line, never when the caller prepared the run, never a line that is left out or already signed.
    /// </summary>
    public static bool CanSignOff(PayrollRun run, PayrollRunLine line, PayrollCaller who, IReadOnlySet<Guid> directReports) =>
        run.State == PayrollRunState.Submitted && !line.Excluded && !line.IsSignedOff
        && line.EmployeeId != who.Id && !run.HasPrepared(who.Id)
        && (who.Approve || (who.Signoff && directReports.Contains(line.EmployeeId)));
}

using HRNexus.Domain.Payroll;

namespace HRNexus.Domain.Entities;

public static class PayrollRunState
{
    public const string Draft     = "draft";
    public const string Submitted = "submitted";
    public const string Approved  = "approved";
    public const string Locked    = "locked";
}

/// <summary>The steps recorded in a run's history (who did what, by staff id).</summary>
public static class PayrollRunAction
{
    public const string Started   = "started";
    public const string Edited    = "edited";
    public const string Submitted = "submitted";
    public const string Returned  = "returned";
    public const string Approved  = "approved";
    public const string Locked    = "locked";
    public const string Reopened  = "reopened";
}

/// <summary>What is needed to reproduce a run's figures months later (copied from the calculator and the company policy when the run starts).</summary>
public sealed record PayrollRunBasis(string CalculatorVersion, decimal Divisor, decimal HoursPerWorkingDay, decimal DeductionCapRate, int PolicyVersion);

/// <summary>One unpaid-leave day a line used and what was deducted for it (so a later correction can be compared with what was really paid).</summary>
public sealed record PayrollRunUnpaidDayInput(DateOnly Date, decimal Days, Guid? LeaveId, decimal DeductionAmount);

/// <summary>The outcome of calculating one person, in the plain form the run stores.</summary>
public sealed record PayrollLineResult(
    decimal Gross,
    decimal Net,
    decimal EmployerCost,
    decimal UnappliedDeductions,
    IReadOnlyList<PayLine> Lines,
    IReadOnlyList<PayrollWarning> Warnings,
    IReadOnlyList<PayrollRunUnpaidDayInput> UnpaidDays);

/// <summary>
/// One month's payroll: the state machine (draft, submitted, approved, locked) and the people in it.
///
/// An aggregate root. It decides which step may follow which and who may take it, so that no caller can
/// approve a run out of order, approve figures they prepared, sign off their own pay, or change a run after
/// the approver has seen it. What it cannot know (who is whose reporting manager, whether a rate row is
/// verified, whether an earlier month is locked) the lifecycle commands check before they call it.
/// </summary>
public class PayrollRun
{
    private readonly List<PayrollRunLine>  _lines  = [];
    private readonly List<PayrollRunActor> _actors = [];

    public Guid     Id                 { get; private set; }
    public int      Year               { get; private set; }
    public int      Month              { get; private set; }
    public string   State              { get; private set; } = default!;
    /// <summary>Counts every change to the run or its lines; an EF concurrency token, so a stale save is refused.</summary>
    public long     Version            { get; private set; }
    /// <summary>Counts Return and Reopen. History rows carry the cycle they happened in.</summary>
    public int      Cycle              { get; private set; }
    public string   CalculatorVersion  { get; private set; } = default!;
    public decimal  Divisor            { get; private set; }
    public decimal  HoursPerWorkingDay { get; private set; }
    public decimal  DeductionCapRate   { get; private set; }
    public int      PolicyVersion      { get; private set; }
    /// <summary>Set when payslips or a bank file were produced (Phase 8e); a run that was exported cannot be reopened.</summary>
    public DateTime? ExportedAt        { get; private set; }
    public DateTime CreatedAt          { get; private set; }

    public IReadOnlyList<PayrollRunLine>  Lines  => _lines;
    public IReadOnlyList<PayrollRunActor> Actors => _actors;

    private PayrollRun() { }

    public static PayrollRun Start(int year, int month, Guid startedBy, PayrollRunBasis basis, DateTime now)
    {
        if (month is < 1 or > 12) throw new ArgumentException("Month must be between 1 and 12.");
        if (year is < 2000 or > 2200) throw new ArgumentException("Year is out of the supported range.");
        var run = new PayrollRun
        {
            Id                 = Guid.NewGuid(),
            Year               = year,
            Month              = month,
            State              = PayrollRunState.Draft,
            CalculatorVersion  = basis.CalculatorVersion,
            Divisor            = basis.Divisor,
            HoursPerWorkingDay = basis.HoursPerWorkingDay,
            DeductionCapRate   = basis.DeductionCapRate,
            PolicyVersion      = basis.PolicyVersion,
            CreatedAt          = now,
        };
        run.Record(PayrollRunAction.Started, startedBy, now);
        return run;
    }

    // ── the people ───────────────────────────────────────────────────────────────

    public PayrollRunLine LineFor(Guid employeeId) =>
        _lines.FirstOrDefault(l => l.EmployeeId == employeeId)
        ?? throw new InvalidOperationException("That person is not in this payroll run.");

    /// <summary>People who are in the run (not left out).</summary>
    public int     IncludedCount => _lines.Count(l => !l.Excluded);
    public decimal TotalGross    => _lines.Where(l => !l.Excluded).Sum(l => l.Gross);
    public decimal TotalNet      => _lines.Where(l => !l.Excluded).Sum(l => l.Net);

    // ── editing a draft ──────────────────────────────────────────────────────────

    public void AddLine(PayrollRunLine line)
    {
        EnsureState(PayrollRunState.Draft, "Only a draft run can be changed.");
        if (_lines.Any(l => l.EmployeeId == line.EmployeeId))
            throw new InvalidOperationException("That person is already in this payroll run.");
        line.AttachTo(Id);
        _lines.Add(line);
        Version++;
    }

    /// <summary>Sets the hand-entered inputs of one person: the PCB and whether they are left out (with a reason).</summary>
    public void SetLineInputs(Guid employeeId, decimal? pcb, bool excluded, string? excludedReason, Guid editedBy, DateTime now)
    {
        EnsureState(PayrollRunState.Draft, "Only a draft run can be changed.");
        var line = LineFor(employeeId);
        line.SetInputs(pcb, excluded, excludedReason);
        RecordEdit(editedBy, now);
        Version++;
    }

    /// <summary>Stores a calculation for one person, replacing the earlier one. The person's inputs are kept.</summary>
    public void ApplyResult(Guid employeeId, PayrollLineResult result, Guid editedBy, DateTime now)
    {
        EnsureState(PayrollRunState.Draft, "Only a draft run can be changed.");
        LineFor(employeeId).ApplyResult(result);
        RecordEdit(editedBy, now);
        Version++;
    }

    public void EnsureCanDelete()
    {
        if (State != PayrollRunState.Draft)
            throw new InvalidOperationException("Only a draft payroll run can be deleted.");
    }

    // ── the lifecycle ────────────────────────────────────────────────────────────

    public void Submit(Guid by, DateTime now)
    {
        EnsureState(PayrollRunState.Draft, "Only a draft run can be submitted.");
        var included = _lines.Where(l => !l.Excluded).ToList();
        if (included.Count == 0)
            throw new InvalidOperationException("The run has nobody in it to pay.");
        if (included.Any(l => !l.Calculated))
            throw new InvalidOperationException("Every person in the run must be calculated before it is submitted.");
        // An unverified rate row is the accountant's gate at Approve; every other blocking warning stops here.
        if (included.Any(l => l.Warnings.Any(w => w.Blocking && w.Code != "unverified-rate")))
            throw new InvalidOperationException("A person in the run has a blocking warning; fix it before submitting.");
        if (included.Any(l => l.Net < 0m))
            throw new InvalidOperationException("A person in the run has negative net pay; fix it before submitting.");
        State = PayrollRunState.Submitted;
        Record(PayrollRunAction.Submitted, by, now);
        Version++;
    }

    /// <summary>
    /// Confirms one person's line. WHO may do it (that person's reporting manager or a head of HR) is checked by
    /// the command; the run refuses the rules it can see itself: not your own line, not by someone who prepared
    /// the run, not twice, and not for someone who is left out.
    /// </summary>
    public void SignOff(Guid employeeId, Guid signerId, DateTime now)
    {
        EnsureState(PayrollRunState.Submitted, "Only a submitted run can be signed off.");
        var line = LineFor(employeeId);
        if (line.Excluded) throw new InvalidOperationException("That person is left out of the run, so there is nothing to sign.");
        if (signerId == employeeId) throw new InvalidOperationException("Nobody can sign off their own pay.");
        EnsureNotPreparer(signerId, "someone who prepared a run cannot sign it off");
        if (line.IsSignedOff) throw new InvalidOperationException("That line is already signed off.");
        line.SignOff(signerId, now);
        Version++;
    }

    public void Approve(Guid by, DateTime now)
    {
        EnsureState(PayrollRunState.Submitted, "Only a submitted run can be approved.");
        if (_lines.Any(l => !l.Excluded && !l.IsSignedOff))
            throw new InvalidOperationException("Every person's line must be signed off before the run is approved.");
        EnsureNotPreparer(by, "whoever prepared a run cannot approve it");
        State = PayrollRunState.Approved;
        Record(PayrollRunAction.Approved, by, now);
        Version++;
    }

    /// <summary>Sends a submitted or approved run back to draft with a reason. Every sign-off is cleared.</summary>
    public void Return(Guid by, string reason, DateTime now)
    {
        if (State is not (PayrollRunState.Submitted or PayrollRunState.Approved))
            throw new InvalidOperationException("Only a submitted or approved run can be returned.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Returning a run needs a reason.");
        BackToDraft(PayrollRunAction.Returned, by, now, reason.Trim());
    }

    public void Lock(Guid by, DateTime now)
    {
        EnsureState(PayrollRunState.Approved, "Only an approved run can be locked.");
        State = PayrollRunState.Locked;
        Record(PayrollRunAction.Locked, by, now);
        Version++;
    }

    public void Reopen(Guid by, DateTime now)
    {
        EnsureState(PayrollRunState.Locked, "Only a locked run can be reopened.");
        if (ExportedAt is not null)
            throw new InvalidOperationException("Payslips or a bank file were already produced for this run, so it cannot be reopened.");
        BackToDraft(PayrollRunAction.Reopened, by, now, null);
    }

    /// <summary>Phase 8e calls this when payslips or a bank file leave the system.</summary>
    public void MarkExported(DateTime now)
    {
        EnsureState(PayrollRunState.Locked, "Only a locked run can be exported.");
        ExportedAt ??= now;
        Version++;
    }

    // ── internals ────────────────────────────────────────────────────────────────

    private void BackToDraft(string action, Guid by, DateTime now, string? reason)
    {
        foreach (var l in _lines) l.ClearSignOff();
        State = PayrollRunState.Draft;
        Cycle++;
        Record(action, by, now, reason);
        Version++;
    }

    private void EnsureState(string expected, string message)
    {
        if (State != expected) throw new InvalidOperationException(message);
    }

    /// <summary>
    /// A "preparer" started the run, or edited or submitted it in the current round. Starting counts in every
    /// round (it is preparing too); an edit before the last Return or Reopen does not, because the figures it
    /// touched were reviewed and sent back.
    /// </summary>
    private void EnsureNotPreparer(Guid staffId, string message)
    {
        var prepared = _actors.Any(a => a.StaffId == staffId &&
            (a.Action == PayrollRunAction.Started ||
             (a.Cycle == Cycle && a.Action is PayrollRunAction.Edited or PayrollRunAction.Submitted)));
        if (prepared) throw new InvalidOperationException($"Not allowed: {message}.");
    }

    private void Record(string action, Guid staffId, DateTime now, string? reason = null) =>
        _actors.Add(PayrollRunActor.Create(Id, staffId, action, Cycle, now, reason));

    /// <summary>One "edited" row per person per round is enough to know they edited.</summary>
    private void RecordEdit(Guid staffId, DateTime now)
    {
        if (!_actors.Any(a => a.StaffId == staffId && a.Action == PayrollRunAction.Edited && a.Cycle == Cycle))
            Record(PayrollRunAction.Edited, staffId, now);
    }
}

/// <summary>A row of a run's history: which step, by whom (staff id, never a display name), when and in which round.</summary>
public class PayrollRunActor
{
    public Guid     Id     { get; private set; }
    public Guid     RunId  { get; private set; }
    public Guid     StaffId { get; private set; }
    public string   Action { get; private set; } = default!;
    public int      Cycle  { get; private set; }
    public DateTime At     { get; private set; }
    public string?  Reason { get; private set; }

    private PayrollRunActor() { }

    internal static PayrollRunActor Create(Guid runId, Guid staffId, string action, int cycle, DateTime at, string? reason) =>
        new() { Id = Guid.NewGuid(), RunId = runId, StaffId = staffId, Action = action, Cycle = cycle, At = at, Reason = reason };
}

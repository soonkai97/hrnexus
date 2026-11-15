using HRNexus.Domain.Payroll;

namespace HRNexus.Domain.Entities;

/// <summary>
/// One person in a payroll run: the snapshot of who they were, the hand-entered inputs, and the figures the
/// calculator produced. Owned by <see cref="PayrollRun"/>, which is the only thing that changes it.
/// Money is encrypted at rest (see PayrollRunLineConfiguration), so nothing here is summed or sorted in SQL.
/// </summary>
public class PayrollRunLine
{
    private readonly List<PayrollRunLineItem>   _items = [];
    private readonly List<PayrollRunUnpaidDay>  _unpaidDays = [];
    private readonly List<PayrollRunWarning>    _warnings = [];

    public Guid     Id                  { get; private set; }
    public Guid     RunId               { get; private set; }
    public Guid     EmployeeId          { get; private set; }
    /// <summary>The person's Staff ID and name as they were when the run was prepared; a locked run never changes when they are renamed.</summary>
    public string   StaffId             { get; private set; } = default!;
    public string   Name                { get; private set; } = default!;
    public bool     Excluded            { get; private set; }
    public string?  ExcludedReason      { get; private set; }
    /// <summary>The monthly tax deduction, typed in by hand. Null means none was entered.</summary>
    public decimal? PcbAmount           { get; private set; }
    public bool     Calculated          { get; private set; }
    public decimal  Gross               { get; private set; }
    public decimal  Net                 { get; private set; }
    public decimal  EmployerCost        { get; private set; }
    public decimal  UnappliedDeductions { get; private set; }
    public Guid?    SignedOffBy         { get; private set; }
    public DateTime? SignedOffAt        { get; private set; }

    public bool IsSignedOff => SignedOffBy is not null;

    public IReadOnlyList<PayrollRunLineItem>  Items     => _items;
    public IReadOnlyList<PayrollRunUnpaidDay> UnpaidDays => _unpaidDays;
    public IReadOnlyList<PayrollRunWarning>   Warnings  => _warnings;

    private PayrollRunLine() { }

    public static PayrollRunLine Create(Guid employeeId, string staffId, string name)
    {
        if (string.IsNullOrWhiteSpace(staffId)) throw new ArgumentException("A line needs the person's Staff ID.");
        return new PayrollRunLine { Id = Guid.NewGuid(), EmployeeId = employeeId, StaffId = staffId, Name = name };
    }

    internal void AttachTo(Guid runId) => RunId = runId;

    internal void SetInputs(decimal? pcb, bool excluded, string? excludedReason)
    {
        if (pcb is < 0m) throw new ArgumentException("The PCB cannot be negative.");
        if (excluded && string.IsNullOrWhiteSpace(excludedReason))
            throw new ArgumentException("Leaving a person out of the run needs a reason.");
        if (excluded && excludedReason!.Trim().Length > 500) throw new ArgumentException("The reason can be at most 500 characters.");
        var reason = excluded ? excludedReason!.Trim() : null;
        // The figures were worked out from the old inputs: a change means they must be calculated again before the run can be submitted.
        if (pcb != PcbAmount || excluded != Excluded) Calculated = false;
        PcbAmount = pcb;
        Excluded = excluded;
        ExcludedReason = reason;
    }

    internal void ApplyResult(PayrollLineResult r)
    {
        _items.Clear();
        _unpaidDays.Clear();
        _warnings.Clear();
        var order = 0;
        foreach (var l in r.Lines) _items.Add(PayrollRunLineItem.Create(Id, order++, l));
        foreach (var d in r.UnpaidDays) _unpaidDays.Add(PayrollRunUnpaidDay.Create(Id, d));
        foreach (var w in r.Warnings) _warnings.Add(PayrollRunWarning.Create(Id, w));
        Gross = r.Gross;
        Net = r.Net;
        EmployerCost = r.EmployerCost;
        UnappliedDeductions = r.UnappliedDeductions;
        Calculated = true;
    }

    internal void SignOff(Guid by, DateTime at)
    {
        SignedOffBy = by;
        SignedOffAt = at;
    }

    internal void ClearSignOff()
    {
        SignedOffBy = null;
        SignedOffAt = null;
    }
}

/// <summary>One line of a payslip, as stored (the calculator's <see cref="PayLine"/>). Amount is encrypted at rest.</summary>
public class PayrollRunLineItem
{
    public Guid        Id         { get; private set; }
    public Guid        LineId     { get; private set; }
    public int         Order      { get; private set; }
    public string      Type       { get; private set; } = default!;
    public LineSection Section    { get; private set; }
    public string      Description { get; private set; } = default!;
    public decimal     Amount     { get; private set; }
    public Guid?       RateRuleId { get; private set; }
    /// <summary>The pay item or cash advance this line came from (null for lines the system computed).</summary>
    public Guid?       SourceId   { get; private set; }

    private PayrollRunLineItem() { }

    internal static PayrollRunLineItem Create(Guid lineId, int order, PayLine l) => new()
    {
        Id = Guid.NewGuid(), LineId = lineId, Order = order, Type = l.Type, Section = l.Section,
        Description = l.Description, Amount = l.Amount, RateRuleId = l.RateRuleId, SourceId = l.SourceId,
    };
}

/// <summary>An unpaid-leave day a line used and what was deducted for it. Amount is encrypted at rest.</summary>
public class PayrollRunUnpaidDay
{
    public Guid     Id              { get; private set; }
    public Guid     LineId          { get; private set; }
    public DateOnly Date            { get; private set; }
    public decimal  Days            { get; private set; }
    public Guid?    LeaveId         { get; private set; }
    public decimal  DeductionAmount { get; private set; }

    private PayrollRunUnpaidDay() { }

    internal static PayrollRunUnpaidDay Create(Guid lineId, PayrollRunUnpaidDayInput d) => new()
    {
        Id = Guid.NewGuid(), LineId = lineId, Date = d.Date, Days = d.Days, LeaveId = d.LeaveId, DeductionAmount = d.DeductionAmount,
    };
}

/// <summary>A calculator warning on a line. The message can contain figures, so it is encrypted at rest; the code is not.</summary>
public class PayrollRunWarning
{
    public Guid   Id       { get; private set; }
    public Guid   LineId   { get; private set; }
    public string Code     { get; private set; } = default!;
    public string Message  { get; private set; } = default!;
    public bool   Blocking { get; private set; }

    private PayrollRunWarning() { }

    internal static PayrollRunWarning Create(Guid lineId, PayrollWarning w) => new()
    {
        Id = Guid.NewGuid(), LineId = lineId, Code = w.Code, Message = w.Message, Blocking = w.Blocking,
    };
}

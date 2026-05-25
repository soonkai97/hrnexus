using HRNexus.Domain.Common;

namespace HRNexus.Domain.Entities;

/// <summary>
/// Records a single night's work for a staff member — clock hours, table runs, overnight booking, etc.
/// VenueEarnings is what the house tracks; PrivateGross/PrivateNet is what the staff personally keeps.
/// </summary>
public sealed class StaffEntry : IHasAudit, ISoftDelete
{
    public Guid     Id            { get; private set; }
    public Guid     StaffId       { get; private set; }
    public DateOnly EntryDate     { get; private set; }
    /// <summary>VENUECLOCK | TABLERUN | OVERNIGHT | PRIVATEHOURS | SELFDEDUCT | REST | SLOWNIGHT</summary>
    public string   WorkType      { get; private set; } = string.Empty;
    /// <summary>Hours (clock/private) or table count (table run). May be 0 for fixed-price work types.</summary>
    public decimal  VenueUnits    { get; private set; }
    /// <summary>Rate per unit (RM/hr or RM/table).</summary>
    public decimal  VenueRate     { get; private set; }
    /// <summary>Amount the venue records for this staff member.</summary>
    public decimal  VenueEarnings { get; private set; }
    /// <summary>Gross private earnings kept by the staff member (e.g. 私总).</summary>
    public decimal  PrivateGross  { get; private set; }
    /// <summary>Net private earnings after deductions (e.g. 私净).</summary>
    public decimal  PrivateNet    { get; private set; }
    /// <summary>SETTLED | PENDING | NOTCOLLECTED | COMP | BANKIN</summary>
    public string   Settlement    { get; private set; } = string.Empty;
    public string?  Notes         { get; private set; }
    public bool      IsDeleted   { get; private set; }
    public DateTime  CreatedAt   { get; private set; }
    public string    CreatedBy   { get; private set; } = string.Empty;
    public DateTime? ModifiedAt  { get; private set; }
    public string?   ModifiedBy  { get; private set; }
    public DateTime? DeletedAt   { get; private set; }
    public string?   DeletedBy   { get; private set; }

    public Staff? Staff { get; private set; }

    private StaffEntry() { }

    public static StaffEntry Create(
        Guid staffId,
        DateOnly entryDate,
        string workType,
        decimal venueUnits,
        decimal venueRate,
        decimal venueEarnings,
        decimal privateGross,
        decimal privateNet,
        string settlement,
        string? notes = null)
    {
        return new StaffEntry
        {
            Id            = Guid.NewGuid(),
            StaffId       = staffId,
            EntryDate     = entryDate,
            WorkType      = workType.Trim().ToUpperInvariant(),
            VenueUnits    = venueUnits,
            VenueRate     = venueRate,
            VenueEarnings = venueEarnings,
            PrivateGross  = privateGross,
            PrivateNet    = privateNet,
            Settlement    = settlement.Trim().ToUpperInvariant(),
            Notes         = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IsDeleted     = false,
            CreatedAt     = DateTime.UtcNow,
            ModifiedAt     = DateTime.UtcNow,
        };
    }

    public void Update(
        DateOnly entryDate,
        string workType,
        decimal venueUnits,
        decimal venueRate,
        decimal venueEarnings,
        decimal privateGross,
        decimal privateNet,
        string settlement,
        string? notes)
    {
        EntryDate     = entryDate;
        WorkType      = workType.Trim().ToUpperInvariant();
        VenueUnits    = venueUnits;
        VenueRate     = venueRate;
        VenueEarnings = venueEarnings;
        PrivateGross  = privateGross;
        PrivateNet    = privateNet;
        Settlement    = settlement.Trim().ToUpperInvariant();
        Notes         = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ModifiedAt     = DateTime.UtcNow;
    }

    /// <summary>Updates only the settlement status — used by bulk-settle operations.</summary>
    public void Settle(string settlement)
    {
        Settlement = settlement.Trim().ToUpperInvariant();
        ModifiedAt  = DateTime.UtcNow;
    }

    public void Delete()
    {
        IsDeleted = true;
        ModifiedAt = DateTime.UtcNow;
    }
}

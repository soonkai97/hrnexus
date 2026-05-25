namespace HRNexus.Domain.Entities;

public class TipsPool
{
    public Guid     Id            { get; private set; }
    public Guid     LocationId    { get; private set; }
    public string   LocationName  { get; private set; } = string.Empty;
    public DateTime ShiftDate     { get; private set; }
    public decimal  TotalAmount   { get; private set; }
    public string   SplitMethod   { get; private set; } = "equal";  // "equal" or "by_hours"
    public bool     IsDistributed { get; private set; }
    public int      StaffCount    { get; private set; }
    public DateTime CreatedAt     { get; private set; }

    private TipsPool() { }

    public static TipsPool Create(Guid locationId, string locationName, DateTime shiftDate, decimal total, string splitMethod) =>
        new() { Id = Guid.NewGuid(), LocationId = locationId, LocationName = locationName,
                ShiftDate = shiftDate, TotalAmount = total, SplitMethod = splitMethod,
                CreatedAt = DateTime.UtcNow };

    public void MarkDistributed(int staffCount)
    {
        IsDistributed = true;
        StaffCount = staffCount;
    }
}

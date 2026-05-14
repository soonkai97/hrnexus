namespace NightclubWorkforce.Domain.Entities;

public class AnnouncementRead
{
    public Guid AnnouncementId { get; private set; }
    public Guid EmployeeId     { get; private set; }
    public DateTime ReadAt     { get; private set; }

    private AnnouncementRead() { }

    public static AnnouncementRead Create(Guid announcementId, Guid employeeId) =>
        new() { AnnouncementId = announcementId, EmployeeId = employeeId, ReadAt = DateTime.UtcNow };
}

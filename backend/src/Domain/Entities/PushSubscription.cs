namespace HRNexus.Domain.Entities;

public class PushSubscription
{
    public Guid     Id         { get; private set; }
    public Guid     EmployeeId { get; private set; }
    public string   Endpoint   { get; private set; } = string.Empty;
    public string   P256dh     { get; private set; } = string.Empty;  // public key
    public string   Auth       { get; private set; } = string.Empty;  // auth secret
    public DateTime CreatedAt  { get; private set; }

    private PushSubscription() { }

    public static PushSubscription Create(Guid employeeId, string endpoint, string p256dh, string auth) =>
        new()
        {
            Id         = Guid.NewGuid(),
            EmployeeId = employeeId,
            Endpoint   = endpoint,
            P256dh     = p256dh,
            Auth       = auth,
            CreatedAt  = DateTime.UtcNow,
        };
}

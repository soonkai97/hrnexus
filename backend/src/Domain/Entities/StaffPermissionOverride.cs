namespace HRNexus.Domain.Entities;

/// <summary>
/// A per-user override on top of role-default permissions. Composite PK (EmployeeId, PermissionKey).
/// <c>Granted = true</c> adds a permission the user's roles don't grant; <c>Granted = false</c>
/// revokes one they would otherwise inherit. Revoke wins over grant wins over role default.
/// </summary>
public class StaffPermissionOverride
{
    public Guid   EmployeeId    { get; private set; }
    public string PermissionKey { get; private set; } = default!;
    public bool   Granted       { get; private set; }

    private StaffPermissionOverride() { }

    public static StaffPermissionOverride Create(Guid employeeId, string permissionKey, bool granted) => new()
    {
        EmployeeId    = employeeId,
        PermissionKey = permissionKey.Trim(),
        Granted       = granted,
    };
}

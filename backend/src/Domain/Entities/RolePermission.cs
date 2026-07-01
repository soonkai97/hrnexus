namespace HRNexus.Domain.Entities;

/// <summary>
/// A permission granted by default to a role. Composite PK (RoleId, PermissionKey).
/// A user's role-default permissions are the union of these rows across all roles they hold;
/// per-user <see cref="StaffPermissionOverride"/> rows then add or remove keys on top.
/// </summary>
public class RolePermission
{
    public Guid   RoleId        { get; private set; }
    public string PermissionKey { get; private set; } = default!;

    private RolePermission() { }

    public static RolePermission Create(Guid roleId, string permissionKey) => new()
    {
        RoleId        = roleId,
        PermissionKey = permissionKey.Trim(),
    };
}

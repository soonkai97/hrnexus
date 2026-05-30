namespace HRNexus.Domain.Entities;

/// <summary>
/// A permission granted by default to a role. Composite PK (RoleName, PermissionKey).
/// A user's role-default permissions are the union of these rows across all roles they hold;
/// per-user <see cref="StaffPermissionOverride"/> rows then add or remove keys on top.
/// </summary>
public class RolePermission
{
    public string RoleName      { get; private set; } = default!;
    public string PermissionKey { get; private set; } = default!;

    private RolePermission() { }

    public static RolePermission Create(string roleName, string permissionKey) => new()
    {
        RoleName      = roleName.Trim().ToUpperInvariant(),
        PermissionKey = permissionKey.Trim(),
    };
}

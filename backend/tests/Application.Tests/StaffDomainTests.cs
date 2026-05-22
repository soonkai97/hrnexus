using E = NightclubWorkforce.Domain.Entities;
using Xunit;

namespace NightclubWorkforce.Application.Tests;

/// <summary>
/// Domain entity tests for the Staff aggregate — creation, role/permission logic,
/// activation, and profile update operations.
/// </summary>
public class StaffDomainTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static E.Staff MakeStaff(List<string>? roles = null) =>
        E.Staff.Create("NC001", "Alice", "Tan", "alice@test.com", "hash",
            roles ?? new List<string> { "BARTENDER" });

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsAllFields()
    {
        var staff = E.Staff.Create("NC001", "Alice", "Tan", "alice@test.com", "hash",
            new List<string> { "BARTENDER" });

        Assert.Equal("NC001",         staff.StaffId);
        Assert.Equal("Alice",         staff.FirstName);
        Assert.Equal("Tan",           staff.LastName);
        Assert.Equal("alice@test.com", staff.Email);
    }

    [Fact]
    public void Create_StaffId_IsUppercased()
    {
        // Staff IDs are normalised to uppercase so lookups are case-insensitive at rest.
        var staff = E.Staff.Create("nc001", "Alice", "Tan", "alice@test.com", "hash",
            new List<string> { "BARTENDER" });

        Assert.Equal("NC001", staff.StaffId);
    }

    [Fact]
    public void Create_Email_IsLowercased()
    {
        // Emails are stored in lowercase to avoid duplicate-account issues from mixed-case input.
        var staff = E.Staff.Create("NC001", "Alice", "Tan", "USER@TEST.COM", "hash",
            new List<string> { "BARTENDER" });

        Assert.Equal("user@test.com", staff.Email);
    }

    [Fact]
    public void Create_IsActive_ByDefault()
    {
        // Newly created staff should be active so they can log in immediately.
        var staff = MakeStaff();

        Assert.True(staff.IsActive);
    }

    [Fact]
    public void Create_AssignsNewId()
    {
        // Each staff record must have a unique non-empty identifier.
        var a = MakeStaff();
        var b = MakeStaff();

        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Create_IsDeleted_False()
    {
        // Staff should not start in a soft-deleted state.
        var staff = MakeStaff();

        Assert.False(staff.IsDeleted);
    }

    [Fact]
    public void Create_Roles_StoredUppercase()
    {
        // Roles are uppercase-normalised so permission checks are always case-insensitive.
        var staff = E.Staff.Create("NC001", "Alice", "Tan", "alice@test.com", "hash",
            new List<string> { "admin", "bartender" });

        Assert.All(staff.RoleEntries, re => Assert.Equal(re.Role, re.Role.ToUpperInvariant()));
    }

    // ── PermissionLevel ───────────────────────────────────────────────────────

    [Fact]
    public void PermissionLevel_AdminRole_IsNotEmployee()
    {
        // Admin staff must receive elevated access — never the plain "Employee" tier.
        var staff = MakeStaff(new List<string> { "admin" });

        Assert.NotEqual("Employee", staff.PermissionLevel);
    }

    [Fact]
    public void PermissionLevel_ManagerRole_IsNotEmployee()
    {
        // Manager staff must receive elevated access — never the plain "Employee" tier.
        var staff = MakeStaff(new List<string> { "manager" });

        Assert.NotEqual("Employee", staff.PermissionLevel);
    }

    [Fact]
    public void PermissionLevel_NoAdminOrManager_IsEmployee()
    {
        // Staff with no privileged roles must land on the default "Employee" tier.
        var staff = MakeStaff(new List<string> { "bartender" });

        Assert.Equal("Employee", staff.PermissionLevel);
    }

    [Fact]
    public void PermissionLevel_AdminTakesPrecedenceOverManager()
    {
        // When a staff member holds both roles, admin-level access must win.
        // The admin claim and manager claim are different strings — they must not be equal.
        var admin   = MakeStaff(new List<string> { "admin" });
        var manager = MakeStaff(new List<string> { "manager" });
        var both    = MakeStaff(new List<string> { "admin", "manager" });

        Assert.Equal(admin.PermissionLevel, both.PermissionLevel);
        Assert.NotEqual(manager.PermissionLevel, both.PermissionLevel);
    }

    // ── Activate / Deactivate ─────────────────────────────────────────────────

    [Fact]
    public void Deactivate_SetsIsActive_False()
    {
        // Deactivated staff must not be able to authenticate.
        var staff = MakeStaff();
        staff.Deactivate();

        Assert.False(staff.IsActive);
    }

    [Fact]
    public void Activate_SetsIsActive_True()
    {
        // Re-activating staff must restore their ability to log in.
        var staff = MakeStaff();
        staff.Deactivate();
        staff.Activate();

        Assert.True(staff.IsActive);
    }

    // ── MustChangePassword ────────────────────────────────────────────────────

    [Fact]
    public void SetMustChangePassword_SetsFlag()
    {
        // After setting the flag the staff member must be forced to change their password on next login.
        var staff = MakeStaff();
        staff.SetMustChangePassword();

        Assert.True(staff.MustChangePassword);
    }

    [Fact]
    public void ClearMustChangePassword_ClearsFlag()
    {
        // Once the password is changed the flag must be cleared so the redirect loop stops.
        var staff = MakeStaff();
        staff.SetMustChangePassword();
        staff.ClearMustChangePassword();

        Assert.False(staff.MustChangePassword);
    }

    // ── UpdateDetails ─────────────────────────────────────────────────────────

    [Fact]
    public void UpdateDetails_ChangesStaffId_Uppercased()
    {
        // UpdateDetails must apply the same uppercase normalisation as Create.
        var staff = MakeStaff();
        staff.UpdateDetails("nc999", "Alice", "Tan", "alice@test.com",
            new List<string> { "BARTENDER" }, null);

        Assert.Equal("NC999", staff.StaffId);
    }

    [Fact]
    public void UpdateDetails_ChangesEmail_Lowercased()
    {
        // UpdateDetails must also lowercase the email to keep the uniqueness invariant.
        var staff = MakeStaff();
        staff.UpdateDetails("NC001", "Alice", "Tan", "NEWEMAIL@TEST.COM",
            new List<string> { "BARTENDER" }, null);

        Assert.Equal("newemail@test.com", staff.Email);
    }
}

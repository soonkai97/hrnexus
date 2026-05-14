namespace NightclubWorkforce.Domain.Entities;

public class Staff
{
    public Guid   Id               { get; private set; }
    public string StaffId          { get; private set; } = default!;
    public string FirstName        { get; private set; } = default!;
    public string LastName         { get; private set; } = default!;
    /// <summary>Computed display name.</summary>
    public string FullName         => $"{FirstName} {LastName}".Trim();
    public string Email            { get; private set; } = default!;
    public string PasswordHash     { get; private set; } = default!;
    /// <summary>Normalized role rows — loaded automatically via AutoInclude.</summary>
    public List<StaffRole> RoleEntries { get; private set; } = new();

    /// <summary>Computed role names derived from RoleEntries. Not stored directly.</summary>
    public List<string> Roles => RoleEntries.Select(r => r.Role).ToList();
    public bool   IsActive         { get; private set; }
    public DateTime CreatedAt      { get; private set; }
    public string? PhoneNumber       { get; private set; }
    public string? IcNo               { get; private set; }   // Malaysian IC number
    public string? PassportNo         { get; private set; }   // Overseas passport number
    public DateTime? DateOfBirth      { get; private set; }
    /// <summary>Computed from DateOfBirth. Null when DOB is not set.</summary>
    public int? Age => DateOfBirth.HasValue
        ? (int)((DateTime.UtcNow - DateOfBirth.Value).TotalDays / 365.25)
        : null;

    /// <summary>UI language preference — ISO 639-1 code, e.g. "en" or "zh".</summary>
    public string PreferredLanguage { get; private set; } = "en";

    public string? RefreshToken    { get; private set; }
    public DateTime? RefreshTokenExpiry { get; private set; }
    public bool MustChangePassword { get; private set; }

    private Staff() { }

    public static Staff Create(string staffId, string firstName, string lastName, string email, string passwordHash,
        List<string> roles, string? phoneNumber = null,
        string? icNo = null, string? passportNo = null, DateTime? dateOfBirth = null)
    {
        var emp = new Staff
        {
            Id           = Guid.NewGuid(),
            StaffId      = staffId.Trim().ToUpperInvariant(),
            FirstName    = firstName.Trim(),
            LastName     = lastName.Trim(),
            Email        = email.ToLowerInvariant(),
            PasswordHash = passwordHash,
            PhoneNumber  = phoneNumber,
            IcNo         = icNo,
            PassportNo   = passportNo,
            DateOfBirth  = dateOfBirth.HasValue
                ? DateTime.SpecifyKind(dateOfBirth.Value.Date, DateTimeKind.Utc)
                : null,
            IsActive  = true,
            CreatedAt = DateTime.UtcNow,
        };
        emp.SetRoles(roles);
        return emp;
    }

    public void UpdateStaffId(string staffId) => StaffId = staffId.Trim().ToUpperInvariant();

    // Maps roles to JWT permission level — highest privilege wins
    public string PermissionLevel
    {
        get
        {
            if (Roles.Any(r => r.Equals("admin",   StringComparison.OrdinalIgnoreCase))) return "Admin";
            if (Roles.Any(r => r.Equals("manager", StringComparison.OrdinalIgnoreCase))) return "Manager";
            return "Employee";
        }
    }

    // Comma-separated roles for display / logging
    public string RolesDisplay => string.Join(", ", Roles);

    public void SetRefreshToken(string token, DateTime expiry)
    {
        RefreshToken       = token;
        RefreshTokenExpiry = expiry;
    }

    public void ClearRefreshToken()
    {
        RefreshToken       = null;
        RefreshTokenExpiry = null;
    }

    public void ChangePassword(string newHash) => PasswordHash = newHash;
    public void SetMustChangePassword()     => MustChangePassword = true;
    public void ClearMustChangePassword()   => MustChangePassword = false;

    public void Deactivate() => IsActive = false;
    public void Activate()   => IsActive = true;

    public void UpdateProfile(string? phoneNumber) => PhoneNumber = phoneNumber;
    public void SetLanguage(string lang) => PreferredLanguage = lang;
    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public void UpdateDetails(string staffId, string firstName, string lastName, string email, List<string> roles,
        string? phoneNumber, string? icNo = null, string? passportNo = null,
        DateTime? dateOfBirth = null)
    {
        StaffId     = staffId.Trim().ToUpperInvariant();
        FirstName   = firstName.Trim();
        LastName    = lastName.Trim();
        Email       = email.ToLowerInvariant();
        PhoneNumber = phoneNumber;
        IcNo        = icNo;
        PassportNo  = passportNo;
        DateOfBirth = dateOfBirth.HasValue
            ? DateTime.SpecifyKind(dateOfBirth.Value.Date, DateTimeKind.Utc)
            : null;
        SetRoles(roles);
    }

    /// <summary>Replaces all role assignments with the supplied list (lowercase-normalised).</summary>
    private void SetRoles(IEnumerable<string> roles)
    {
        RoleEntries.Clear();
        foreach (var r in roles)
            RoleEntries.Add(new StaffRole { EmployeeId = Id, Role = r.ToLowerInvariant() });
    }

}

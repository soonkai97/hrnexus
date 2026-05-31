using HRNexus.Domain.Common;
using HRNexus.Domain.Constants;

namespace HRNexus.Domain.Entities;

public class Staff : IHasAudit, ISoftDelete
{
    public Guid   Id               { get; private set; }
    public string StaffId          { get; private set; } = default!;
    public string FirstName        { get; private set; } = default!;
    public string LastName         { get; private set; } = default!;
    /// <summary>Computed display name.</summary>
    public string FullName         => $"{FirstName} {LastName}".Trim();
    public string? Email           { get; private set; }
    public string PasswordHash     { get; private set; } = default!;
    /// <summary>Normalized role rows — loaded automatically via AutoInclude.</summary>
    public List<StaffRole> RoleEntries { get; private set; } = new();

    /// <summary>Computed role names derived from RoleEntries. Not stored directly.</summary>
    public List<string> Roles => RoleEntries.Select(r => r.Role).ToList();
    public bool   IsActive         { get; private set; }
    public DateTime  CreatedAt   { get; private set; }
    public string    CreatedBy   { get; private set; } = string.Empty;
    public DateTime? ModifiedAt  { get; private set; }
    public string?   ModifiedBy  { get; private set; }
    public bool      IsDeleted   { get; private set; }
    public DateTime? DeletedAt   { get; private set; }
    public string?   DeletedBy   { get; private set; }

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

    // ── Brute-force lockout ──────────────────────────────────────────────────
    /// <summary>Consecutive failed login attempts since the last success. Reset to 0 on success.</summary>
    public int AccessFailedCount { get; private set; }
    /// <summary>When set and in the future, the account is locked and logins must be refused.</summary>
    public DateTime? LockoutEnd { get; private set; }

    private Staff() { }

    public static Staff Create(string staffId, string firstName, string? lastName, string? email, string passwordHash,
        List<string> roles, string? phoneNumber = null,
        string? icNo = null, string? passportNo = null, DateTime? dateOfBirth = null)
    {
        var emp = new Staff
        {
            Id           = Guid.NewGuid(),
            StaffId      = staffId.Trim().ToUpperInvariant(),
            FirstName    = firstName.Trim(),
            LastName     = lastName?.Trim() ?? "",
            Email        = email?.ToLowerInvariant(),
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

    // Maps roles to JWT permission level — highest privilege wins
    public string PermissionLevel
    {
        get
        {
            if (Roles.Any(r => r.Equals(AppConstants.Roles.Admin,   StringComparison.OrdinalIgnoreCase))) return AppConstants.Roles.AdminClaim;
            if (Roles.Any(r => r.Equals(AppConstants.Roles.Manager, StringComparison.OrdinalIgnoreCase))) return AppConstants.Roles.ManagerClaim;
            return "Employee";
        }
    }

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

    // ── Brute-force lockout behaviour ────────────────────────────────────────

    /// <summary>True when the account is currently locked (LockoutEnd is set and still in the future).</summary>
    public bool IsLockedOut(DateTime nowUtc) => LockoutEnd.HasValue && LockoutEnd.Value > nowUtc;

    /// <summary>
    /// Records a failed login. Once <paramref name="maxAttempts"/> consecutive failures accumulate,
    /// the account is locked for <paramref name="lockoutDuration"/> and the counter resets so the
    /// next failure after the lock expires starts a fresh window.
    /// Returns true if THIS failure triggered the lock.
    /// </summary>
    public bool RegisterFailedLogin(DateTime nowUtc, int maxAttempts, TimeSpan lockoutDuration)
    {
        AccessFailedCount++;
        if (AccessFailedCount >= maxAttempts)
        {
            LockoutEnd        = nowUtc.Add(lockoutDuration);
            AccessFailedCount = 0;
            return true;
        }
        return false;
    }

    /// <summary>Clears the failed-attempt counter and any lockout after a successful authentication.</summary>
    public void RegisterSuccessfulLogin()
    {
        AccessFailedCount = 0;
        LockoutEnd        = null;
    }

    public void Deactivate() => IsActive = false;
    public void Activate()   => IsActive = true;

    public void UpdateProfile(string? phoneNumber) => PhoneNumber = phoneNumber;
    public void SetLanguage(string lang) => PreferredLanguage = lang;
    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public void UpdateDetails(string staffId, string firstName, string? lastName, string? email, List<string> roles,
        string? phoneNumber, string? icNo = null, string? passportNo = null,
        DateTime? dateOfBirth = null)
    {
        StaffId     = staffId.Trim().ToUpperInvariant();
        FirstName   = firstName.Trim();
        LastName    = lastName?.Trim() ?? LastName;   // keep existing if null
        Email       = email?.ToLowerInvariant() ?? Email;  // keep existing if null
        PhoneNumber = phoneNumber;
        IcNo        = icNo;
        PassportNo  = passportNo;
        DateOfBirth = dateOfBirth.HasValue
            ? DateTime.SpecifyKind(dateOfBirth.Value.Date, DateTimeKind.Utc)
            : null;
        SetRoles(roles);
    }

    /// <summary>Replaces all role assignments with the supplied list (uppercase-normalised).
    /// Mutates the collection in place — removes only roles being dropped and adds only new roles —
    /// so the EF Core change tracker never holds two instances with the same composite key.
    /// </summary>
    private void SetRoles(IEnumerable<string> roles)
    {
        var incoming = roles.Select(r => r.Trim().ToUpperInvariant()).ToHashSet();
        RoleEntries.RemoveAll(re => !incoming.Contains(re.Role));
        var existing = RoleEntries.Select(re => re.Role).ToHashSet();
        foreach (var role in incoming.Where(r => !existing.Contains(r)))
            RoleEntries.Add(new StaffRole { EmployeeId = Id, Role = role });
    }

}

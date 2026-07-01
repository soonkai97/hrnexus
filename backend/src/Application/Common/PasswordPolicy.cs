namespace HRNexus.Application.Common;

/// <summary>
/// Single source of truth for password rules, shared by every flow that sets a password —
/// self-service change, admin register, admin reset. Kept in sync with the frontend requirements
/// checklist. Deliberately minimal (length floor only, no character-class rules) per the venue's
/// preference for low friction; brute-force is guarded separately by account lockout + IP rate
/// limiting, not by complexity.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 6;

    public static bool IsValid(string? password) =>
        !string.IsNullOrWhiteSpace(password)
        && password!.Length >= MinLength;

    /// <summary>Throws <see cref="ArgumentException"/> with a user-facing message when too short.</summary>
    public static void Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.");
        if (password!.Length < MinLength)
            throw new ArgumentException($"Password must be at least {MinLength} characters.");
    }
}

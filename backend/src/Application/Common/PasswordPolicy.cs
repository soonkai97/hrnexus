namespace HRNexus.Application.Common;

/// <summary>
/// Single source of truth for password strength rules, shared by every flow that sets a
/// password — self-service change, admin register, admin reset. Kept in sync with the
/// frontend requirements checklist: at least 8 characters, one uppercase letter, one number.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    public static bool IsValid(string? password) =>
        !string.IsNullOrWhiteSpace(password)
        && password!.Length >= MinLength
        && password.Any(char.IsUpper)
        && password.Any(char.IsDigit);

    /// <summary>Throws <see cref="ArgumentException"/> with a user-facing message when too weak.</summary>
    public static void Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.");
        if (password!.Length < MinLength)
            throw new ArgumentException($"Password must be at least {MinLength} characters.");
        if (!password.Any(char.IsUpper) || !password.Any(char.IsDigit))
            throw new ArgumentException("Password must include at least one uppercase letter and one number.");
    }
}

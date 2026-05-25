namespace HRNexus.Application.Common;

/// <summary>
/// Converts Malaysian phone numbers to E.164 format (+601X...) so they are
/// stored consistently and match SMS delivery requirements.
/// </summary>
public static class PhoneNormalizer
{
    /// <summary>
    /// Normalises a phone number to E.164.
    /// Examples:
    ///   0123456789   → +60123456789
    ///   60123456789  → +60123456789
    ///   +60123456789 → +60123456789 (unchanged)
    ///   0112345678   → +60112345678
    /// Returns null/empty input unchanged.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        // Strip whitespace, dashes, parentheses — keep digits and leading +
        var clean = new string(raw.Where(c => char.IsDigit(c) || c == '+').ToArray()).Trim();
        if (clean.Length == 0) return raw;

        // Validate: digits only (excluding the leading '+') must be at least 7 characters
        var digits = new string(clean.Where(char.IsDigit).ToArray());
        if (digits.Length < 7)
            return null; // too short to be a real number — treat phone as absent

        if (clean.StartsWith('+'))
            return clean; // already E.164

        if (clean.StartsWith("60") && clean.Length >= 11)
            return "+" + clean;  // 60123456789 → +60123456789

        if (clean.StartsWith("0") && clean.Length >= 10)
            return "+6" + clean; // 0123456789  → +60123456789

        // Unrecognised format — prepend + as best effort
        return "+" + clean;
    }
}

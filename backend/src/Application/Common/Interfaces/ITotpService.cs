namespace HRNexus.Application.Common.Interfaces;

/// <summary>
/// Time-based one-time-password (RFC 6238) helper for two-factor authentication, plus single-use
/// recovery codes. Implemented in Infrastructure so the crypto/QR libraries stay out of the
/// domain/application layers.
/// </summary>
public interface ITotpService
{
    /// <summary>Generates a fresh base32-encoded TOTP shared secret.</summary>
    string GenerateSecret();

    /// <summary>Builds the otpauth:// provisioning URI an authenticator app reads from a QR code.</summary>
    string BuildOtpAuthUri(string secret, string accountName, string issuer);

    /// <summary>Renders the otpauth URI to a PNG QR as a "data:image/png;base64,…" data URI.</summary>
    string BuildQrPngDataUri(string otpAuthUri);

    /// <summary>Verifies a 6-digit code against the secret, tolerating ±1 time step for clock drift.</summary>
    bool VerifyCode(string secret, string code);

    /// <summary>Generates <paramref name="count"/> human-readable single-use recovery codes (plaintext).</summary>
    IReadOnlyList<string> GenerateRecoveryCodes(int count = 10);

    /// <summary>Deterministic hash of a recovery code for at-rest storage and comparison.
    /// Recovery codes are high-entropy, so a fast hash (not a slow password hash) is appropriate.</summary>
    string HashRecoveryCode(string code);
}

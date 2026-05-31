namespace HRNexus.Application.Common.Interfaces;

/// <summary>
/// Encrypts/decrypts sensitive field values at rest (application-level encryption applied via EF Core
/// value converters). Implementations are randomised (AES-GCM) so the same plaintext yields different
/// ciphertext each time — DO NOT use on columns that need equality lookups or unique indexes.
/// </summary>
public interface IFieldEncryptor
{
    /// <summary>Encrypts a value. Null/empty passes through unchanged.</summary>
    string? Encrypt(string? plaintext);

    /// <summary>Decrypts a value. Values without the ciphertext marker are returned as-is, so existing
    /// plaintext rows remain readable until they're re-saved (lazy/idempotent migration).</summary>
    string? Decrypt(string? stored);
}

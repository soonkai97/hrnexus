using System.Security.Cryptography;
using System.Text;
using HRNexus.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HRNexus.Infrastructure.Auth;

/// <summary>
/// AES-256-GCM field encryptor. Stored form is "enc1:" + base64(nonce(12) || tag(16) || ciphertext).
/// The 256-bit key is derived (SHA-256) from the configured passphrase (Pii:Key / PII_ENCRYPTION_KEY),
/// so any sufficiently-long secret works. The key is read lazily so design-time tooling (migrations)
/// — which never encrypts — doesn't require it; a startup guard validates it for the running app.
/// </summary>
public class FieldEncryptor(IConfiguration config) : IFieldEncryptor
{
    private const string Marker = "enc1:";
    private const int NonceSize = 12;   // AES-GCM standard nonce
    private const int TagSize   = 16;   // AES-GCM auth tag

    private byte[]? _key;
    private byte[] Key => _key ??= DeriveKey(config["Pii:Key"]);

    private static byte[] DeriveKey(string? passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new InvalidOperationException("Pii:Key is not configured — set PII_ENCRYPTION_KEY in the environment.");
        return SHA256.HashData(Encoding.UTF8.GetBytes(passphrase));   // 32 bytes
    }

    public string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var plainBytes  = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag         = new byte[TagSize];

        using var aes = new AesGcm(Key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var combined = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, combined, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, combined, NonceSize + TagSize, cipherBytes.Length);
        return Marker + Convert.ToBase64String(combined);
    }

    public string? Decrypt(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        // Legacy / not-yet-encrypted value — return untouched so old rows stay readable.
        if (!stored.StartsWith(Marker, StringComparison.Ordinal)) return stored;

        var combined = Convert.FromBase64String(stored[Marker.Length..]);
        var nonce       = combined.AsSpan(0, NonceSize);
        var tag         = combined.AsSpan(NonceSize, TagSize);
        var cipherBytes = combined.AsSpan(NonceSize + TagSize);
        var plainBytes  = new byte[cipherBytes.Length];

        using var aes = new AesGcm(Key, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }
}

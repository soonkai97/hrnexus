using System.Security.Cryptography;
using System.Text;
using HRNexus.Application.Common.Interfaces;
using OtpNet;
using QRCoder;

namespace HRNexus.Infrastructure.Auth;

public class TotpService : ITotpService
{
    // Unambiguous alphabet for recovery codes (no 0/O/1/I/L) — easy to read and type.
    private const string RecoveryAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public string GenerateSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public string BuildOtpAuthUri(string secret, string accountName, string issuer)
    {
        var label  = Uri.EscapeDataString($"{issuer}:{accountName}");
        var issuerQ = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={secret}&issuer={issuerQ}&algorithm=SHA1&digits=6&period=30";
    }

    public string BuildQrPngDataUri(string otpAuthUri)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(otpAuthUri, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(10);
        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }

    public bool VerifyCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;
        try
        {
            var totp = new Totp(Base32Encoding.ToBytes(secret));
            return totp.VerifyTotp(code.Trim(), out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<string> GenerateRecoveryCodes(int count = 10)
    {
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var chars = new char[10];
            for (var j = 0; j < chars.Length; j++)
                chars[j] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
            // Format XXXXX-XXXXX for readability.
            codes.Add($"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}");
        }
        return codes;
    }

    public string HashRecoveryCode(string code)
    {
        // Normalise (strip dashes/spaces, uppercase) so input matches the stored form regardless of
        // how the user types it, then SHA-256. High entropy → a fast hash is sufficient here.
        var normalized = new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }
}

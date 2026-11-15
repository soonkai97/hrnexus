using System.Globalization;
using HRNexus.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HRNexus.Infrastructure.Persistence.Configurations;

/// <summary>
/// The value converters payroll columns share: money and free text stored as AES-GCM ciphertext (the same field
/// encryptor as the Staff PII and the salary profile). Randomised encryption, so none of these columns can be
/// filtered, sorted or summed in SQL; the rows are found by their plain id and date columns.
/// </summary>
public sealed class EncryptedColumns(IFieldEncryptor encryptor)
{
    public ValueConverter<decimal, string> Money { get; } = new(
        v => encryptor.Encrypt(v.ToString(CultureInfo.InvariantCulture))!,
        v => decimal.Parse(encryptor.Decrypt(v)!, CultureInfo.InvariantCulture));

    public ValueConverter<decimal?, string?> NullableMoney { get; } = new(
        v => v == null ? null : encryptor.Encrypt(v.Value.ToString(CultureInfo.InvariantCulture)),
        v => v == null ? null : decimal.Parse(encryptor.Decrypt(v)!, CultureInfo.InvariantCulture));

    public ValueConverter<string, string> Text { get; } = new(
        v => encryptor.Encrypt(v)!,
        v => encryptor.Decrypt(v)!);
}

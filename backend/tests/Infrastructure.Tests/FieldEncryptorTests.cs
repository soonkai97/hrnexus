using HRNexus.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HRNexus.Infrastructure.Tests;

/// <summary>
/// Guards the AES-GCM field encryptor used to protect PII at rest. If round-trip ever broke, every
/// staff IC/passport/phone/DOB would become unreadable; if it stopped being randomised, equal values
/// would leak via identical ciphertext. The legacy-passthrough is what keeps pre-encryption rows
/// readable until they're backfilled.
/// </summary>
public class FieldEncryptorTests
{
    private static FieldEncryptor Make() => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Pii:Key"] = "Unit_Test_PII_Key_0123456789_abcdefghijk" })
            .Build());

    [Fact]
    public void Encrypt_then_Decrypt_roundtrips()
    {
        var enc = Make();
        var cipher = enc.Encrypt("123456-78-9012");
        Assert.NotNull(cipher);
        Assert.StartsWith("enc1:", cipher);
        Assert.Equal("123456-78-9012", enc.Decrypt(cipher));
    }

    [Fact]
    public void Encrypt_is_randomised_same_plaintext_differs_but_both_decrypt()
    {
        var enc = Make();
        var a = enc.Encrypt("A1234567");
        var b = enc.Encrypt("A1234567");
        Assert.NotEqual(a, b);                       // random nonce → different ciphertext
        Assert.Equal("A1234567", enc.Decrypt(a));
        Assert.Equal("A1234567", enc.Decrypt(b));
    }

    [Fact]
    public void Decrypt_passes_through_legacy_plaintext_without_marker()
    {
        var enc = Make();
        Assert.Equal("0123456789", enc.Decrypt("0123456789"));   // pre-encryption value stays readable
    }

    [Fact]
    public void Null_and_empty_pass_through_both_ways()
    {
        var enc = Make();
        Assert.Null(enc.Encrypt(null));
        Assert.Equal("", enc.Encrypt(""));
        Assert.Null(enc.Decrypt(null));
    }

    [Fact]
    public void A_different_key_cannot_decrypt()
    {
        var cipher = Make().Encrypt("secret-value")!;
        var otherKey = new FieldEncryptor(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Pii:Key"] = "A_Totally_Different_Key_9876543210_zyxwvut" })
            .Build());
        Assert.ThrowsAny<System.Exception>(() => otherKey.Decrypt(cipher));   // GCM auth tag fails
    }

    [Fact]
    public void Missing_key_throws_on_use()
    {
        var enc = new FieldEncryptor(new ConfigurationBuilder().Build());
        Assert.Throws<System.InvalidOperationException>(() => enc.Encrypt("x"));
    }
}

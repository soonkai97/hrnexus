using HRNexus.Infrastructure.Auth;
using Xunit;

namespace HRNexus.Infrastructure.Tests;

/// <summary>
/// Guards the BCrypt password hasher that underpins authentication. If Hash/Verify ever stopped
/// agreeing, every login would silently break — so these assert the round-trip and that wrong
/// passwords are rejected, including the per-hash salting BCrypt relies on.
/// </summary>
public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Verify_ReturnsTrue_ForTheOriginalPassword()
    {
        var hash = _hasher.Hash("Passw0rd!");
        Assert.True(_hasher.Verify("Passw0rd!", hash));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForAWrongPassword()
    {
        var hash = _hasher.Hash("Passw0rd!");
        Assert.False(_hasher.Verify("not-the-password", hash));
    }

    [Fact]
    public void Hash_IsSalted_SoTwoHashesDiffer_ButBothVerify()
    {
        var a = _hasher.Hash("SameInput1");
        var b = _hasher.Hash("SameInput1");

        Assert.NotEqual(a, b);                       // distinct salts → distinct hashes
        Assert.True(_hasher.Verify("SameInput1", a));
        Assert.True(_hasher.Verify("SameInput1", b));
    }

    [Fact]
    public void Verify_IsCaseSensitive()
    {
        var hash = _hasher.Hash("CaseMatters1");
        Assert.False(_hasher.Verify("casematters1", hash));
    }
}

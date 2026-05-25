namespace HRNexus.Application.Common.Interfaces;

public enum UnlockStatus { Success, InvalidPin, LockedOut }

public record UnlockResult(
    UnlockStatus Status,
    string?      Token      = null,
    DateTime?    ExpiresAt  = null,
    DateTime?    RetryAfter = null);

/// <summary>PIN-based access control for the feature management page.</summary>
public interface IFeatureMgmtService
{
    bool         IsPinConfigured { get; }
    string       HashPin(string pin);
    UnlockResult TryUnlock(string pin, string ip);
    bool         ValidateToken(string? token);
}

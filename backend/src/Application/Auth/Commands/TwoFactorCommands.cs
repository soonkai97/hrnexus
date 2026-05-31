using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Auth.Commands;

// ── DTOs ─────────────────────────────────────────────────────────────────────
public record TwoFactorEnrollDto(string Secret, string OtpAuthUri, string QrPngDataUri);
public record TwoFactorRecoveryDto(IReadOnlyList<string> RecoveryCodes);

// ── Begin enrollment: generate a secret + QR (not yet enabled) ───────────────
public record BeginTwoFactorEnrollCommand(Guid StaffId) : IRequest<TwoFactorEnrollDto>;

public class BeginTwoFactorEnrollHandler(IAppDbContext db, ITotpService totp)
    : IRequestHandler<BeginTwoFactorEnrollCommand, TwoFactorEnrollDto>
{
    private const string Issuer = "HRNexus";

    public async Task<TwoFactorEnrollDto> Handle(BeginTwoFactorEnrollCommand req, CancellationToken ct)
    {
        var staff = await db.Staff.FirstOrDefaultAsync(s => s.Id == req.StaffId, ct)
            ?? throw new KeyNotFoundException("Staff not found.");
        if (staff.TwoFactorEnabled)
            throw new InvalidOperationException("Two-factor authentication is already enabled.");

        var secret = totp.GenerateSecret();
        staff.BeginTwoFactorEnrollment(secret);
        await db.SaveChangesAsync(ct);

        var uri = totp.BuildOtpAuthUri(secret, staff.StaffId, Issuer);
        return new TwoFactorEnrollDto(secret, uri, totp.BuildQrPngDataUri(uri));
    }
}

// ── Confirm enrollment: verify the first code, enable, return recovery codes ─
public record ConfirmTwoFactorEnrollCommand(Guid StaffId, string Code) : IRequest<TwoFactorRecoveryDto>;

public class ConfirmTwoFactorEnrollHandler(IAppDbContext db, ITotpService totp)
    : IRequestHandler<ConfirmTwoFactorEnrollCommand, TwoFactorRecoveryDto>
{
    public async Task<TwoFactorRecoveryDto> Handle(ConfirmTwoFactorEnrollCommand req, CancellationToken ct)
    {
        var staff = await db.Staff.FirstOrDefaultAsync(s => s.Id == req.StaffId, ct)
            ?? throw new KeyNotFoundException("Staff not found.");
        if (staff.TwoFactorEnabled)
            throw new InvalidOperationException("Two-factor authentication is already enabled.");
        if (string.IsNullOrEmpty(staff.TwoFactorSecret))
            throw new InvalidOperationException("Start two-factor setup first.");
        if (!totp.VerifyCode(staff.TwoFactorSecret, req.Code))
            throw new ArgumentException("That code is incorrect. Check your authenticator app and try again.");

        var codes  = totp.GenerateRecoveryCodes();
        staff.EnableTwoFactor(codes.Select(totp.HashRecoveryCode));
        await db.SaveChangesAsync(ct);
        return new TwoFactorRecoveryDto(codes);
    }
}

// ── Disable: requires a current code (TOTP or recovery) ──────────────────────
public record DisableTwoFactorCommand(Guid StaffId, string Code) : IRequest;

public class DisableTwoFactorHandler(IAppDbContext db, ITotpService totp)
    : IRequestHandler<DisableTwoFactorCommand>
{
    public async Task Handle(DisableTwoFactorCommand req, CancellationToken ct)
    {
        var staff = await db.Staff.FirstOrDefaultAsync(s => s.Id == req.StaffId, ct)
            ?? throw new KeyNotFoundException("Staff not found.");
        if (!staff.TwoFactorEnabled) return;   // already off — nothing to do

        var ok = totp.VerifyCode(staff.TwoFactorSecret!, req.Code)
              || staff.ConsumeRecoveryCode(totp.HashRecoveryCode(req.Code));
        if (!ok) throw new UnauthorizedAccessException("That code is incorrect.");

        staff.DisableTwoFactor();
        await db.SaveChangesAsync(ct);
    }
}

// ── Verify at login (step 2): exchange a valid code for real session tokens ──
public record VerifyTwoFactorCommand(Guid StaffId, string Code) : IRequest<LoginResult>;

public class VerifyTwoFactorHandler(IAppDbContext db, ITotpService totp, ITokenService tokens, IDateTimeProvider clock)
    : IRequestHandler<VerifyTwoFactorCommand, LoginResult>
{
    public async Task<LoginResult> Handle(VerifyTwoFactorCommand req, CancellationToken ct)
    {
        var staff = await db.Staff.FirstOrDefaultAsync(s => s.Id == req.StaffId && s.IsActive, ct)
            ?? throw new UnauthorizedAccessException("Invalid credentials.");
        if (!staff.TwoFactorEnabled || string.IsNullOrEmpty(staff.TwoFactorSecret))
            throw new UnauthorizedAccessException("Two-factor authentication is not enabled.");

        var ok = totp.VerifyCode(staff.TwoFactorSecret, req.Code)
              || staff.ConsumeRecoveryCode(totp.HashRecoveryCode(req.Code));
        if (!ok) throw new UnauthorizedAccessException("That code is incorrect.");

        var access  = tokens.GenerateAccessToken(staff);
        var refresh = tokens.GenerateRefreshToken();
        staff.SetRefreshToken(refresh, clock.UtcNow.AddDays(30));
        await db.SaveChangesAsync(ct);

        return new LoginResult(access, refresh, 3600,
            new UserDto(staff.Id, staff.StaffId, staff.FullName, staff.Email, staff.Roles.ToList()));
    }
}

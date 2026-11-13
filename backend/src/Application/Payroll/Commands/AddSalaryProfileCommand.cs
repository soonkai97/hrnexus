using HRNexus.Application.Common.Interfaces;
using HRNexus.Domain.Constants;
using HRNexus.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Payroll.Commands;

/// <summary>Adds a row to a person's salary history (a raise, a correction, the first salary). Never edits a row.</summary>
public record AddSalaryProfileCommand(
    Guid     EmployeeId,
    decimal  BasicSalary,
    string   PayFrequency,
    DateOnly EffectiveFrom,
    string?  BankName,
    string?  BankAccount,
    string?  BankHolder) : IRequest<Guid>;

public class AddSalaryProfileCommandHandler(IAppDbContext db, IAuditService audit)
    : IRequestHandler<AddSalaryProfileCommand, Guid>
{
    public async Task<Guid> Handle(AddSalaryProfileCommand req, CancellationToken ct)
    {
        if (req.BasicSalary <= 0 || req.BasicSalary > AppConstants.Payroll.MaxMonthlySalary)
            throw new ArgumentException($"Basic salary must be more than 0 and at most RM {AppConstants.Payroll.MaxMonthlySalary:N0}.");
        if (decimal.Round(req.BasicSalary, 2) != req.BasicSalary)
            throw new ArgumentException("Basic salary cannot have more than two decimal places.");
        if (!string.Equals(req.PayFrequency, AppConstants.Payroll.Monthly, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only monthly pay is supported.");

        var bankName    = Clean(req.BankName, 100, "Bank name");
        var bankAccount = Clean(req.BankAccount, 34, "Bank account");
        var bankHolder  = Clean(req.BankHolder, 100, "Account holder");

        if (!await db.Staff.AnyAsync(s => s.Id == req.EmployeeId, ct))
            throw new KeyNotFoundException($"Staff {req.EmployeeId} not found.");
        if (await db.SalaryProfiles.AnyAsync(p => p.EmployeeId == req.EmployeeId && p.EffectiveFrom == req.EffectiveFrom, ct))
            throw new InvalidOperationException(
                $"A salary effective {req.EffectiveFrom:yyyy-MM-dd} already exists. History is never rewritten: add a later effective date instead.");

        var profile = SalaryProfile.Create(req.EmployeeId, req.EffectiveFrom, req.BasicSalary,
            AppConstants.Payroll.Monthly, bankName, bankAccount, bankHolder);
        db.SalaryProfiles.Add(profile);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // The unique (employee, effective date) index fired: a concurrent request added the same date.
            throw new InvalidOperationException(
                $"A salary effective {req.EffectiveFrom:yyyy-MM-dd} already exists. Please refresh and try again.");
        }

        // The audit row records THAT a salary changed and from when, never the amounts or the account.
        await audit.LogAsync("SalaryProfile", profile.Id.ToString(), "Created", null, "Admin",
            $"Effective {req.EffectiveFrom:yyyy-MM-dd}", ct);
        return profile.Id;
    }

    private static string? Clean(string? value, int max, string label)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw new ArgumentException($"{label} cannot exceed {max} characters.");
        return v;
    }
}

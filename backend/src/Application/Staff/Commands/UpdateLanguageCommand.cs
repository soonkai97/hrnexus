using MediatR;
using Microsoft.EntityFrameworkCore;
using HRNexus.Application.Common.Interfaces;

namespace HRNexus.Application.Staff.Commands;

public record UpdateLanguageCommand(Guid EmployeeId, string Language) : IRequest;

public class UpdateLanguageCommandHandler(IAppDbContext db) : IRequestHandler<UpdateLanguageCommand>
{
    private static readonly HashSet<string> Supported = ["en", "zh"];

    public async Task Handle(UpdateLanguageCommand req, CancellationToken ct)
    {
        if (!Supported.Contains(req.Language))
            throw new ArgumentException($"Unsupported language '{req.Language}'.");

        var emp = await db.Staff.FirstOrDefaultAsync(e => e.Id == req.EmployeeId, ct)
            ?? throw new KeyNotFoundException("Employee not found.");

        emp.SetLanguage(req.Language);
        await db.SaveChangesAsync(ct);
    }
}

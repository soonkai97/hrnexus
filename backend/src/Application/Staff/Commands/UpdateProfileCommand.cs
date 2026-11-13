using HRNexus.Application.Common;
using HRNexus.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Staff.Commands;

public record UpdateProfileCommand(Guid EmployeeId, string? PhoneNumber) : IRequest;

public class UpdateProfileCommandHandler(IAppDbContext db) : IRequestHandler<UpdateProfileCommand>
{
    public async Task Handle(UpdateProfileCommand req, CancellationToken ct)
    {
        var emp = await db.Staff.FirstOrDefaultAsync(e => e.Id == req.EmployeeId, ct)
            ?? throw new KeyNotFoundException("Employee not found.");
        emp.UpdateProfile(PhoneNormalizer.Normalize(req.PhoneNumber));
        await db.SaveChangesAsync(ct);
    }
}

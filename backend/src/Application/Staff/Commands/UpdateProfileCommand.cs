using MediatR;
using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common;
using NightclubWorkforce.Application.Common.Interfaces;

namespace NightclubWorkforce.Application.Staff.Commands;

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

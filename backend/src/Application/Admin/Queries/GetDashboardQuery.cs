using HRNexus.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HRNexus.Application.Admin.Queries;

public record GetDashboardQuery : IRequest<DashboardResponse>;

public record DashboardResponse(DashboardStats Stats);

public record DashboardStats(int TotalStaff);

public class GetDashboardQueryHandler(IAppDbContext db)
    : IRequestHandler<GetDashboardQuery, DashboardResponse>
{
    public async Task<DashboardResponse> Handle(GetDashboardQuery req, CancellationToken ct)
    {
        var totalStaff = await db.Staff.CountAsync(e => e.IsActive, ct);
        return new DashboardResponse(new DashboardStats(totalStaff));
    }
}

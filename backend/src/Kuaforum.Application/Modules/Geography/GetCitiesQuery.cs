using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Geography;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Geography;

public sealed record CityDto(Guid Id, int PlateCode, string Name, string Slug, int ActiveShopCount);
public sealed record DistrictDto(Guid Id, Guid CityId, string Name, string Slug);
public sealed record NeighborhoodDto(Guid Id, Guid DistrictId, string Name, string Slug, string? PostalCode);

public sealed record GetCitiesQuery() : IQuery<IReadOnlyList<CityDto>>;

public sealed class GetCitiesQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetCitiesQuery, Result<IReadOnlyList<CityDto>>>
{
    public async Task<Result<IReadOnlyList<CityDto>>> Handle(GetCitiesQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;

        // Single round-trip: per-city active shop count via correlated subquery.
        // Status=Active filter mirrors SearchBarbersQuery so the count shown on
        // the keşfet screen matches what the search list returns when tapped.
        var list = await ctx.Set<City>().AsNoTracking()
            .OrderBy(c => c.PlateCode)
            .Select(c => new CityDto(
                c.Id,
                c.PlateCode,
                c.Name,
                c.Slug,
                ctx.Set<BarberShop>().Count(s => s.AddressCityId == c.Id && s.Status == BarberShopStatus.Active)))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<CityDto>>(list);
    }
}

public sealed record GetDistrictsByCityQuery(Guid CityId) : IQuery<IReadOnlyList<DistrictDto>>;

public sealed class GetDistrictsByCityQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetDistrictsByCityQuery, Result<IReadOnlyList<DistrictDto>>>
{
    public async Task<Result<IReadOnlyList<DistrictDto>>> Handle(GetDistrictsByCityQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var list = await ctx.Set<District>().AsNoTracking()
            .Where(d => d.CityId == request.CityId)
            .OrderBy(d => d.Name)
            .Select(d => new DistrictDto(d.Id, d.CityId, d.Name, d.Slug))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<DistrictDto>>(list);
    }
}

public sealed record GetNeighborhoodsByDistrictQuery(Guid DistrictId) : IQuery<IReadOnlyList<NeighborhoodDto>>;

public sealed class GetNeighborhoodsByDistrictQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetNeighborhoodsByDistrictQuery, Result<IReadOnlyList<NeighborhoodDto>>>
{
    public async Task<Result<IReadOnlyList<NeighborhoodDto>>> Handle(GetNeighborhoodsByDistrictQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var list = await ctx.Set<Neighborhood>().AsNoTracking()
            .Where(n => n.DistrictId == request.DistrictId)
            .OrderBy(n => n.Name)
            .Select(n => new NeighborhoodDto(n.Id, n.DistrictId, n.Name, n.Slug, n.PostalCode))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<NeighborhoodDto>>(list);
    }
}

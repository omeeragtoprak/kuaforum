using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Barber;

public sealed record BarberShopDto(
    Guid Id,
    Guid OwnerId,
    string Name,
    string Slug,
    string? Description,
    string? Phone,
    SalonGenderType GenderType,
    BarberShopStatus Status,
    Guid? CityId,
    Guid? DistrictId,
    Guid? NeighborhoodId,
    string? AddressLine1,
    double? Latitude,
    double? Longitude,
    string? CommissionTier);

public sealed record StylistDto(
    Guid Id,
    Guid BarberShopId,
    string DisplayName,
    string? PhotoUrl,
    string? Bio,
    bool IsActive,
    bool WalkInAvailable);

public sealed record ServiceDto(
    Guid Id,
    string Name,
    ServiceGender Gender,
    int DurationMinutes,
    int BufferMinutes,
    long PriceMinor,
    string Currency,
    bool IsActive);

public sealed record GetMyBarberShopQuery(Guid OwnerId) : IQuery<BarberShopDto?>;

public sealed class GetMyBarberShopQueryHandler(IApplicationDbContext db) : IRequestHandler<GetMyBarberShopQuery, Result<BarberShopDto?>>
{
    public async Task<Result<BarberShopDto?>> Handle(GetMyBarberShopQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => s.OwnerId == request.OwnerId)
            .Select(s => new BarberShopDto(
                s.Id, s.OwnerId, s.Name, s.Slug, s.Description, s.PhoneE164,
                s.GenderType, s.Status,
                s.AddressCityId, s.AddressDistrictId, s.AddressNeighborhoodId, s.AddressLine1,
                s.Latitude, s.Longitude, s.CommissionTier))
            .FirstOrDefaultAsync(ct);
        return Result.Success(shop);
    }
}

public sealed record GetBarberShopByIdQuery(Guid BarberShopId) : IQuery<BarberShopDto>;

public sealed class GetBarberShopByIdQueryHandler(IApplicationDbContext db) : IRequestHandler<GetBarberShopByIdQuery, Result<BarberShopDto>>
{
    public async Task<Result<BarberShopDto>> Handle(GetBarberShopByIdQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => s.Id == request.BarberShopId && s.Status == BarberShopStatus.Active)
            .Select(s => new BarberShopDto(
                s.Id, s.OwnerId, s.Name, s.Slug, s.Description, s.PhoneE164,
                s.GenderType, s.Status,
                s.AddressCityId, s.AddressDistrictId, s.AddressNeighborhoodId, s.AddressLine1,
                s.Latitude, s.Longitude, s.CommissionTier))
            .FirstOrDefaultAsync(ct);
        if (shop is null) return Result.Failure<BarberShopDto>(Error.NotFound("BarberShop"));
        return Result.Success(shop);
    }
}

// RequesterUserId: bu GET hem anonim/public (müşteri app, web landing) hem de owner'ın
// KENDİ panelinden (mobile (barber)/stylists.tsx, services.tsx) çağrılıyor — ayrı bir
// owner-only liste endpoint'i yok. Draft/PendingKyc/Suspended bir shop'un listesini
// yalnızca sahibi görebilmeli; başka herkese (anonim dahil, GAP fix) 404 — bilinmeyen
// shop ile aynı muamele, hangi shop'un hangi statüde olduğunu sızdırmıyoruz.
public sealed record GetStylistsByShopQuery(Guid BarberShopId, Guid? RequesterUserId = null) : IQuery<IReadOnlyList<StylistDto>>;

public sealed class GetStylistsByShopQueryHandler(IApplicationDbContext db) : IRequestHandler<GetStylistsByShopQuery, Result<IReadOnlyList<StylistDto>>>
{
    public async Task<Result<IReadOnlyList<StylistDto>>> Handle(GetStylistsByShopQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => s.Id == request.BarberShopId)
            .Select(s => new { s.Status, s.OwnerId })
            .FirstOrDefaultAsync(ct);
        if (shop is null || (shop.Status != BarberShopStatus.Active && shop.OwnerId != request.RequesterUserId))
            return Result.Failure<IReadOnlyList<StylistDto>>(Error.NotFound("BarberShop"));

        var list = await ctx.Set<Stylist>().AsNoTracking()
            .Where(s => s.BarberShopId == request.BarberShopId && s.IsActive)
            .Select(s => new StylistDto(s.Id, s.BarberShopId, s.DisplayName, s.PhotoUrl, s.Bio, s.IsActive, s.WalkInAvailable))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<StylistDto>>(list);
    }
}

public sealed record GetServicesByShopQuery(Guid BarberShopId, Guid? RequesterUserId = null) : IQuery<IReadOnlyList<ServiceDto>>;

public sealed class GetServicesByShopQueryHandler(IApplicationDbContext db) : IRequestHandler<GetServicesByShopQuery, Result<IReadOnlyList<ServiceDto>>>
{
    public async Task<Result<IReadOnlyList<ServiceDto>>> Handle(GetServicesByShopQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => s.Id == request.BarberShopId)
            .Select(s => new { s.Status, s.OwnerId })
            .FirstOrDefaultAsync(ct);
        if (shop is null || (shop.Status != BarberShopStatus.Active && shop.OwnerId != request.RequesterUserId))
            return Result.Failure<IReadOnlyList<ServiceDto>>(Error.NotFound("BarberShop"));

        var list = await ctx.Set<ServiceCatalog>().AsNoTracking()
            .Where(s => s.BarberShopId == request.BarberShopId && s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new ServiceDto(s.Id, s.Name, s.Gender, s.DurationMinutes, s.BufferMinutes, s.PriceMinor, s.Currency, s.IsActive))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<ServiceDto>>(list);
    }
}

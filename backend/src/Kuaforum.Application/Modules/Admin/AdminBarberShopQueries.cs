using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Admin;

public sealed record AdminBarberShopDto(
    Guid Id,
    string Name,
    string Slug,
    string? OwnerDisplayName,
    string? OwnerPhone,
    BarberShopStatus Status,
    SalonGenderType GenderType,
    int StylistCount,
    int ServiceCount,
    int CompletedBookingsCount,
    DateTimeOffset CreatedAt,
    // Follow-up (review): admin oran formu bu alan olmadan "kör" dolduruluyordu — mevcut
    // override'ı görmeden PUT /commission-rate çağıran admin yanlışlıkla sıfırlıyordu.
    // Null = global PlatformSettings.CommissionRateGlobal miras alınıyor (bkz. BarberShop.CommissionRateOverride).
    decimal? CommissionRateOverride);

public sealed record AdminBarberShopsPage(
    IReadOnlyList<AdminBarberShopDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetAdminBarberShopsQuery(
    string? Search,
    BarberShopStatus? Status,
    int Page,
    int PageSize) : IQuery<AdminBarberShopsPage>;

public sealed class GetAdminBarberShopsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminBarberShopsQuery, Result<AdminBarberShopsPage>>
{
    public async Task<Result<AdminBarberShopsPage>> Handle(GetAdminBarberShopsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var page = request.Page <= 0 ? 1 : request.Page;
        var size = request.PageSize is <= 0 or > 100 ? 25 : request.PageSize;

        var q = ctx.Set<BarberShop>().AsNoTracking().AsQueryable();
        if (request.Status is not null) q = q.Where(s => s.Status == request.Status.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim().ToLower();
            q = q.Where(x => x.Name.ToLower().Contains(s) || x.Slug.ToLower().Contains(s));
        }

        var total = await q.CountAsync(ct);

        var rows = await (
            from shop in q
            join owner in ctx.Set<Domain.Modules.Identity.User>().AsNoTracking() on shop.OwnerId equals owner.Id into oj
            from owner in oj.DefaultIfEmpty()
            orderby shop.CreatedAt descending
            select new
            {
                shop,
                OwnerName = owner != null ? owner.DisplayName : null,
                OwnerPhone = owner != null ? owner.PhoneE164 : null,
                StylistCount = ctx.Set<Stylist>().Count(st => st.BarberShopId == shop.Id),
                ServiceCount = ctx.Set<ServiceCatalog>().Count(sc => sc.BarberShopId == shop.Id),
                CompletedCount = ctx.Set<Domain.Modules.Booking.Booking>()
                    .Count(b => b.BarberShopId == shop.Id && b.Status == Domain.Modules.Booking.BookingStatus.Completed),
            })
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);

        var dtos = rows.Select(r => new AdminBarberShopDto(
            r.shop.Id, r.shop.Name, r.shop.Slug,
            r.OwnerName, r.OwnerPhone,
            r.shop.Status, r.shop.GenderType,
            r.StylistCount, r.ServiceCount, r.CompletedCount,
            r.shop.CreatedAt, r.shop.CommissionRateOverride)).ToList();

        return Result.Success(new AdminBarberShopsPage(dtos, page, size, total));
    }
}

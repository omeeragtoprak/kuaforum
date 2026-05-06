using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Review;
using ReviewAggregate = Kuaforum.Domain.Modules.Review.Review;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Barber;

public sealed record SearchBarbersQuery(
    Guid? CityId,
    Guid? DistrictId,
    SalonGenderType? GenderType,
    bool? OpenWalkInOnly,
    int Page,
    int PageSize) : IQuery<SearchBarbersResponse>;

public sealed record BarberSearchResultDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    SalonGenderType GenderType,
    string? AddressLine1,
    double? Latitude,
    double? Longitude,
    int StylistCount,
    int ServiceCount,
    long? MinPriceMinor,
    bool HasWalkInAvailable,
    double? RatingAverage,
    int RatingCount);

public sealed record SearchBarbersResponse(
    IReadOnlyList<BarberSearchResultDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed class SearchBarbersQueryValidator : AbstractValidator<SearchBarbersQuery>
{
    public SearchBarbersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

public sealed class SearchBarbersQueryHandler(IApplicationDbContext db)
    : IRequestHandler<SearchBarbersQuery, Result<SearchBarbersResponse>>
{
    public async Task<Result<SearchBarbersResponse>> Handle(SearchBarbersQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var query = ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => s.Status == BarberShopStatus.Active);

        if (request.CityId.HasValue)
            query = query.Where(s => s.AddressCityId == request.CityId);
        if (request.DistrictId.HasValue)
            query = query.Where(s => s.AddressDistrictId == request.DistrictId);
        if (request.GenderType.HasValue)
            query = query.Where(s => s.GenderType == request.GenderType.Value
                                    || s.GenderType == SalonGenderType.Unisex);

        var total = await query.CountAsync(ct);

        var page = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new
            {
                Shop = s,
                StylistCount = ctx.Set<Stylist>().Count(st => st.BarberShopId == s.Id && st.IsActive),
                ServiceCount = ctx.Set<ServiceCatalog>().Count(sv => sv.BarberShopId == s.Id && sv.IsActive),
                MinPrice = ctx.Set<ServiceCatalog>()
                    .Where(sv => sv.BarberShopId == s.Id && sv.IsActive)
                    .OrderBy(sv => sv.PriceMinor)
                    .Select(sv => (long?)sv.PriceMinor)
                    .FirstOrDefault(),
                HasWalkIn = ctx.Set<Stylist>()
                    .Any(st => st.BarberShopId == s.Id && st.IsActive && st.WalkInAvailable),
                RatingAverage = ctx.Set<ReviewAggregate>()
                    .Where(r => r.BarberShopId == s.Id && r.ModerationStatus == ReviewModerationStatus.Approved)
                    .Average(r => (double?)r.Rating),
                RatingCount = ctx.Set<ReviewAggregate>()
                    .Count(r => r.BarberShopId == s.Id && r.ModerationStatus == ReviewModerationStatus.Approved),
            })
            .ToListAsync(ct);

        var items = page.Select(p => new BarberSearchResultDto(
            p.Shop.Id, p.Shop.Name, p.Shop.Slug, p.Shop.Description,
            p.Shop.GenderType, p.Shop.AddressLine1,
            p.Shop.Latitude, p.Shop.Longitude,
            p.StylistCount, p.ServiceCount, p.MinPrice, p.HasWalkIn,
            p.RatingAverage, p.RatingCount))
            .ToList();

        // Optional walk-in filter (post-query for simplicity)
        if (request.OpenWalkInOnly == true)
            items = items.Where(i => i.HasWalkInAvailable).ToList();

        return Result.Success(new SearchBarbersResponse(items, request.Page, request.PageSize, total));
    }
}

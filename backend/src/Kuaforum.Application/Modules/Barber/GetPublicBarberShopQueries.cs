using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Geography;
using Kuaforum.Domain.Modules.Review;
using ReviewAggregate = Kuaforum.Domain.Modules.Review.Review;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Barber;

// RFC-011 — web landing + kuaför public profil sayfaları. Bu dosyadaki DTO'lar
// kasıtlı olarak GetBarberShopQueries.cs'teki BarberShopDto'dan AYRI: BarberShopDto
// OwnerId + CommissionTier taşıyor (owner panel / internal kullanım içindir).
// Public/anonim yüzeye (web landing, kuaforum.com/salon/{slug}) dönecek her alan
// burada, tek dosyada, tek gözden geçirmede denetlenebilir olsun diye izole edildi.

public sealed record PublicServiceSummaryDto(
    string Name,
    int DurationMinutes,
    long PriceMinor,
    string Currency);

public sealed record PublicWorkingHoursSummaryDto(
    DayOfWeek DayOfWeek,
    TimeOnly? OpenTime,
    TimeOnly? CloseTime,
    bool IsClosed);

public sealed record PublicBarberShopDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? Phone,
    SalonGenderType GenderType,
    string? CityName,
    string? DistrictName,
    string? NeighborhoodName,
    string? AddressLine1,
    double? Latitude,
    double? Longitude,
    double? RatingAverage,
    int RatingCount,
    IReadOnlyList<PublicServiceSummaryDto> Services,
    IReadOnlyList<PublicWorkingHoursSummaryDto> WorkingHours,
    IReadOnlyList<string> StylistNames);

public sealed record PublicBarberShopListItemDto(
    Guid Id,
    string Name,
    string Slug,
    string? CityName,
    string? DistrictName,
    double? RatingAverage,
    int RatingCount,
    int ServiceCount);

public sealed record PublicBarberShopListResponse(
    IReadOnlyList<PublicBarberShopListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetPublicBarberShopBySlugQuery(string Slug) : IQuery<PublicBarberShopDto>;

public sealed class GetPublicBarberShopBySlugQueryValidator : AbstractValidator<GetPublicBarberShopBySlugQuery>
{
    public GetPublicBarberShopBySlugQueryValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(200);
    }
}

public sealed class GetPublicBarberShopBySlugQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetPublicBarberShopBySlugQuery, Result<PublicBarberShopDto>>
{
    private static readonly DayOfWeek[] OrderedDays =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    public async Task<Result<PublicBarberShopDto>> Handle(GetPublicBarberShopBySlugQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        // Slug her zaman SlugHelper.Make ile küçük harf/ascii üretilir (bkz.
        // Kuaforum.Domain.Common.SlugHelper); gelen route param'ı savunmacı şekilde
        // normalize ediyoruz ki "Kadikoy-Modern" gibi elle yazılmış URL de eşleşsin.
        var normalizedSlug = request.Slug.Trim().ToLowerInvariant();

        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => s.Slug == normalizedSlug && s.Status == BarberShopStatus.Active)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Slug,
                s.Description,
                s.PhoneE164,
                s.GenderType,
                s.AddressLine1,
                s.Latitude,
                s.Longitude,
                CityName = ctx.Set<City>().Where(c => c.Id == s.AddressCityId).Select(c => c.Name).FirstOrDefault(),
                DistrictName = ctx.Set<District>().Where(d => d.Id == s.AddressDistrictId).Select(d => d.Name).FirstOrDefault(),
                NeighborhoodName = ctx.Set<Neighborhood>().Where(n => n.Id == s.AddressNeighborhoodId).Select(n => n.Name).FirstOrDefault(),
                RatingAverage = ctx.Set<ReviewAggregate>()
                    .Where(r => r.BarberShopId == s.Id && r.ModerationStatus == ReviewModerationStatus.Approved)
                    .Average(r => (double?)r.Rating),
                RatingCount = ctx.Set<ReviewAggregate>()
                    .Count(r => r.BarberShopId == s.Id && r.ModerationStatus == ReviewModerationStatus.Approved),
            })
            .FirstOrDefaultAsync(ct);

        // Draft/PendingKyc/Suspended/Closed shop veya bilinmeyen slug — hepsi aynı 404.
        // Status ayrımını dışarı sızdırmıyoruz (bir salonun "askıya alındığını" anonim
        // ziyaretçiye ifşa etmek gereksiz bilgi sızıntısı).
        if (shop is null) return Result.Failure<PublicBarberShopDto>(Error.NotFound("BarberShop"));

        var services = await ctx.Set<ServiceCatalog>().AsNoTracking()
            .Where(sv => sv.BarberShopId == shop.Id && sv.IsActive)
            .OrderBy(sv => sv.Name)
            .Select(sv => new PublicServiceSummaryDto(sv.Name, sv.DurationMinutes, sv.PriceMinor, sv.Currency))
            .ToListAsync(ct);

        var activeStylists = await ctx.Set<Stylist>().AsNoTracking()
            .Where(st => st.BarberShopId == shop.Id && st.IsActive)
            .OrderBy(st => st.DisplayName)
            .Select(st => new { st.Id, st.DisplayName })
            .ToListAsync(ct);
        var stylistNames = activeStylists.Select(s => s.DisplayName).ToList();

        // WorkingHours stylist-bazlı (shop-bazlı değil — bkz. WorkingHours.cs). Public
        // profil için günlük özet: o gün en az bir aktif stilist açıksa açık say,
        // açılış = en erken, kapanış = en geç. Round-trip sayısı (shop + services +
        // stylists + hours = 4) bilinçli — bu bir ISR/CDN'de cache'lenecek profil
        // sayfası, hot path değil; tek sorguya sıkıştırmak bu aşamada gereksiz karmaşıklık.
        // stylistIds boşsa Contains(w.StylistId) SQL'de boş IN/ANY'e çevrilir (0 satır) —
        // ayrı bir "boşsa sorgulama" dalına gerek yok.
        var stylistIds = activeStylists.Select(s => s.Id).ToList();
        var hoursRows = await ctx.Set<WorkingHours>().AsNoTracking()
            .Where(w => stylistIds.Contains(w.StylistId))
            .Select(w => new { w.DayOfWeek, w.OpenTime, w.CloseTime, w.IsClosed })
            .ToListAsync(ct);

        var workingHours = OrderedDays.Select(day =>
        {
            var openRows = hoursRows.Where(w => w.DayOfWeek == day && !w.IsClosed).ToList();
            return openRows.Count == 0
                ? new PublicWorkingHoursSummaryDto(day, null, null, true)
                : new PublicWorkingHoursSummaryDto(day, openRows.Min(w => w.OpenTime), openRows.Max(w => w.CloseTime), false);
        }).ToList();

        var dto = new PublicBarberShopDto(
            shop.Id, shop.Name, shop.Slug, shop.Description, shop.PhoneE164, shop.GenderType,
            shop.CityName, shop.DistrictName, shop.NeighborhoodName, shop.AddressLine1,
            shop.Latitude, shop.Longitude,
            shop.RatingAverage, shop.RatingCount,
            services, workingHours, stylistNames);

        return Result.Success(dto);
    }
}

public sealed record GetPublicBarberShopsQuery(
    Guid? CityId,
    int Page,
    int PageSize) : IQuery<PublicBarberShopListResponse>;

public sealed class GetPublicBarberShopsQueryValidator : AbstractValidator<GetPublicBarberShopsQuery>
{
    public GetPublicBarberShopsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

public sealed class GetPublicBarberShopsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetPublicBarberShopsQuery, Result<PublicBarberShopListResponse>>
{
    public async Task<Result<PublicBarberShopListResponse>> Handle(GetPublicBarberShopsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var query = ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => s.Status == BarberShopStatus.Active);

        if (request.CityId.HasValue)
            query = query.Where(s => s.AddressCityId == request.CityId);

        var total = await query.CountAsync(ct);

        // SearchBarbersQuery (müşteri app içi keşif) CreatedAt desc sıralar (yeni işletme
        // öne çıksın); bu bir dizin/katalog sayfası (SEO landing), alfabetik daha
        // öngörülebilir/taranabilir — bilinçli farklı sıralama.
        var items = await query
            .OrderBy(s => s.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new PublicBarberShopListItemDto(
                s.Id,
                s.Name,
                s.Slug,
                ctx.Set<City>().Where(c => c.Id == s.AddressCityId).Select(c => c.Name).FirstOrDefault(),
                ctx.Set<District>().Where(d => d.Id == s.AddressDistrictId).Select(d => d.Name).FirstOrDefault(),
                ctx.Set<ReviewAggregate>()
                    .Where(r => r.BarberShopId == s.Id && r.ModerationStatus == ReviewModerationStatus.Approved)
                    .Average(r => (double?)r.Rating),
                ctx.Set<ReviewAggregate>()
                    .Count(r => r.BarberShopId == s.Id && r.ModerationStatus == ReviewModerationStatus.Approved),
                ctx.Set<ServiceCatalog>().Count(sv => sv.BarberShopId == s.Id && sv.IsActive)))
            .ToListAsync(ct);

        return Result.Success(new PublicBarberShopListResponse(items, request.Page, request.PageSize, total));
    }
}

using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Identity;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using ReviewAggregate = Kuaforum.Domain.Modules.Review.Review;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Booking;

public sealed record BookingItemDto(Guid Id, Guid ServiceCatalogId, string ServiceName, int DurationMinutes, long PriceMinor);

public sealed record BookingDto(
    Guid Id,
    Guid CustomerId,
    Guid BarberShopId,
    Guid StylistId,
    DateTimeOffset ScheduledAt,
    DateTimeOffset ScheduledEndsAt,
    BookingStatus Status,
    long TotalAmountMinor,
    string Currency,
    int? RefundPercentage,
    IReadOnlyList<BookingItemDto> Items,
    string? CustomerName = null,
    string? CustomerPhone = null,
    string? CustomerNote = null,
    string? ShopName = null,
    string? StylistName = null,
    bool HasReview = false);

public sealed record GetMyBookingsQuery(Guid CustomerId, BookingStatus? StatusFilter) : IQuery<IReadOnlyList<BookingDto>>;

public sealed class GetMyBookingsQueryHandler(IApplicationDbContext db) : IRequestHandler<GetMyBookingsQuery, Result<IReadOnlyList<BookingDto>>>
{
    public async Task<Result<IReadOnlyList<BookingDto>>> Handle(GetMyBookingsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var query = ctx.Set<BookingAggregate>().AsNoTracking()
            .Include(b => b.Items)
            .Where(b => b.CustomerId == request.CustomerId);

        if (request.StatusFilter.HasValue)
            query = query.Where(b => b.Status == request.StatusFilter.Value);

        var list = await query
            .OrderByDescending(b => b.ScheduledAt)
            .ToListAsync(ct);

        var customerIds = list.Select(b => b.CustomerId).Distinct().ToList();
        var customers = await ctx.Set<User>().AsNoTracking()
            .Where(u => customerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.PhoneE164 })
            .ToDictionaryAsync(u => u.Id, ct);

        var shopIds = list.Select(b => b.BarberShopId).Distinct().ToList();
        var shops = await ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => shopIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, ct);

        var stylistIds = list.Select(b => b.StylistId).Distinct().ToList();
        var stylists = await ctx.Set<Stylist>().AsNoTracking()
            .Where(s => stylistIds.Contains(s.Id))
            .Select(s => new { s.Id, s.DisplayName })
            .ToDictionaryAsync(s => s.Id, ct);

        var bookingIds = list.Select(b => b.Id).ToList();
        var reviewedBookingIds = await ctx.Set<ReviewAggregate>().AsNoTracking()
            .Where(r => bookingIds.Contains(r.BookingId))
            .Select(r => r.BookingId)
            .ToListAsync(ct);
        var reviewedSet = reviewedBookingIds.ToHashSet();

        var result = list.Select(b =>
        {
            customers.TryGetValue(b.CustomerId, out var c);
            shops.TryGetValue(b.BarberShopId, out var sh);
            stylists.TryGetValue(b.StylistId, out var st);
            return new BookingDto(
                b.Id, b.CustomerId, b.BarberShopId, b.StylistId,
                b.ScheduledAt, b.ScheduledEndsAt, b.Status,
                b.TotalAmountMinor, b.Currency, b.RefundPercentage,
                b.Items.OrderBy(i => i.OrderIndex)
                    .Select(i => new BookingItemDto(i.Id, i.ServiceCatalogId, i.ServiceName, i.DurationMinutes, i.PriceMinor))
                    .ToList(),
                CustomerName: c?.DisplayName,
                CustomerPhone: c?.PhoneE164,
                CustomerNote: b.CustomerNote,
                ShopName: sh?.Name,
                StylistName: st?.DisplayName,
                HasReview: reviewedSet.Contains(b.Id));
        }).ToList();

        return Result.Success<IReadOnlyList<BookingDto>>(result);
    }
}

public sealed record GetBookingByIdQuery(Guid UserId, Guid BookingId) : IQuery<BookingDto>;

public sealed class GetBookingByIdQueryHandler(IApplicationDbContext db) : IRequestHandler<GetBookingByIdQuery, Result<BookingDto>>
{
    public async Task<Result<BookingDto>> Handle(GetBookingByIdQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var booking = await ctx.Set<BookingAggregate>().AsNoTracking()
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == request.BookingId, ct);

        if (booking is null) return Result.Failure<BookingDto>(Error.NotFound("Booking"));

        var isCustomer = booking.CustomerId == request.UserId;
        var isOwner = false;
        if (!isCustomer)
        {
            var shop = await ctx.Set<Kuaforum.Domain.Modules.Barber.BarberShop>().AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == booking.BarberShopId, ct);
            isOwner = shop is not null && shop.OwnerId == request.UserId;
        }
        if (!isCustomer && !isOwner)
            return Result.Failure<BookingDto>(Error.Forbidden("Bu randevu size ait değil."));

        var customer = await ctx.Set<User>().AsNoTracking()
            .Where(u => u.Id == booking.CustomerId)
            .Select(u => new { u.DisplayName, u.PhoneE164 })
            .FirstOrDefaultAsync(ct);
        var shopMeta = await ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => s.Id == booking.BarberShopId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct);
        var stylistMeta = await ctx.Set<Stylist>().AsNoTracking()
            .Where(s => s.Id == booking.StylistId)
            .Select(s => s.DisplayName)
            .FirstOrDefaultAsync(ct);

        var hasReview = await ctx.Set<ReviewAggregate>().AsNoTracking()
            .AnyAsync(r => r.BookingId == booking.Id, ct);

        return Result.Success(new BookingDto(
            booking.Id, booking.CustomerId, booking.BarberShopId, booking.StylistId,
            booking.ScheduledAt, booking.ScheduledEndsAt, booking.Status,
            booking.TotalAmountMinor, booking.Currency, booking.RefundPercentage,
            booking.Items.OrderBy(i => i.OrderIndex)
                .Select(i => new BookingItemDto(i.Id, i.ServiceCatalogId, i.ServiceName, i.DurationMinutes, i.PriceMinor))
                .ToList(),
            CustomerName: customer?.DisplayName,
            CustomerPhone: customer?.PhoneE164,
            CustomerNote: booking.CustomerNote,
            ShopName: shopMeta,
            StylistName: stylistMeta,
            HasReview: hasReview));
    }
}

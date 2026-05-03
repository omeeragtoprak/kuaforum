using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
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
    IReadOnlyList<BookingItemDto> Items);

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

        var result = list.Select(b => new BookingDto(
            b.Id, b.CustomerId, b.BarberShopId, b.StylistId,
            b.ScheduledAt, b.ScheduledEndsAt, b.Status,
            b.TotalAmountMinor, b.Currency, b.RefundPercentage,
            b.Items.OrderBy(i => i.OrderIndex)
                .Select(i => new BookingItemDto(i.Id, i.ServiceCatalogId, i.ServiceName, i.DurationMinutes, i.PriceMinor))
                .ToList()))
            .ToList();

        return Result.Success<IReadOnlyList<BookingDto>>(result);
    }
}

public sealed record GetBookingByIdQuery(Guid CustomerId, Guid BookingId) : IQuery<BookingDto>;

public sealed class GetBookingByIdQueryHandler(IApplicationDbContext db) : IRequestHandler<GetBookingByIdQuery, Result<BookingDto>>
{
    public async Task<Result<BookingDto>> Handle(GetBookingByIdQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var booking = await ctx.Set<BookingAggregate>().AsNoTracking()
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == request.BookingId, ct);

        if (booking is null) return Result.Failure<BookingDto>(Error.NotFound("Booking"));
        if (booking.CustomerId != request.CustomerId)
            return Result.Failure<BookingDto>(Error.Forbidden("Not your booking."));

        return Result.Success(new BookingDto(
            booking.Id, booking.CustomerId, booking.BarberShopId, booking.StylistId,
            booking.ScheduledAt, booking.ScheduledEndsAt, booking.Status,
            booking.TotalAmountMinor, booking.Currency, booking.RefundPercentage,
            booking.Items.OrderBy(i => i.OrderIndex)
                .Select(i => new BookingItemDto(i.Id, i.ServiceCatalogId, i.ServiceName, i.DurationMinutes, i.PriceMinor))
                .ToList()));
    }
}

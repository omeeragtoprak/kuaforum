using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Identity;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Booking;

public sealed record GetOwnerBookingsQuery(
    Guid OwnerUserId,
    DateTimeOffset From,
    DateTimeOffset To,
    BookingStatus? StatusFilter) : IQuery<IReadOnlyList<BookingDto>>;

public sealed class GetOwnerBookingsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetOwnerBookingsQuery, Result<IReadOnlyList<BookingDto>>>
{
    public async Task<Result<IReadOnlyList<BookingDto>>> Handle(GetOwnerBookingsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerId == request.OwnerUserId, ct);
        if (shop is null) return Result.Failure<IReadOnlyList<BookingDto>>(Error.NotFound("BarberShop"));

        var query = ctx.Set<BookingAggregate>().AsNoTracking()
            .Include(b => b.Items)
            .Where(b => b.BarberShopId == shop.Id
                        && b.ScheduledAt >= request.From
                        && b.ScheduledAt < request.To);

        if (request.StatusFilter.HasValue)
            query = query.Where(b => b.Status == request.StatusFilter.Value);

        var list = await query
            .OrderBy(b => b.ScheduledAt)
            .ToListAsync(ct);

        var customerIds = list.Select(b => b.CustomerId).Distinct().ToList();
        var customers = await ctx.Set<User>().AsNoTracking()
            .Where(u => customerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.PhoneE164 })
            .ToDictionaryAsync(u => u.Id, ct);

        var stylistIds = list.Select(b => b.StylistId).Distinct().ToList();
        var stylists = await ctx.Set<Stylist>().AsNoTracking()
            .Where(s => stylistIds.Contains(s.Id))
            .Select(s => new { s.Id, s.DisplayName })
            .ToDictionaryAsync(s => s.Id, ct);

        var dtos = list.Select(b =>
        {
            customers.TryGetValue(b.CustomerId, out var c);
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
                ShopName: shop.Name,
                StylistName: st?.DisplayName);
        }).ToList();

        return Result.Success<IReadOnlyList<BookingDto>>(dtos);
    }
}

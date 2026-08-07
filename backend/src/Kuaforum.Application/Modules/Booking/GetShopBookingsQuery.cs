using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Identity;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using PaymentAggregate = Kuaforum.Domain.Modules.Payment.Payment;
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

        // RFC-006 Task 5: tek toplu WHERE BookingId IN (...) sorgusu — liste tarafında N+1 yok.
        var bookingIds = list.Select(b => b.Id).ToList();
        var payments = await ctx.Set<PaymentAggregate>().AsNoTracking()
            .Where(p => bookingIds.Contains(p.BookingId) && BookingPaymentProjection.PaidStatuses.Contains(p.Status))
            .Select(p => new { p.BookingId, p.Kind, p.GrossAmountMinor })
            .ToDictionaryAsync(p => p.BookingId, ct);

        var dtos = list.Select(b =>
        {
            customers.TryGetValue(b.CustomerId, out var c);
            stylists.TryGetValue(b.StylistId, out var st);
            payments.TryGetValue(b.Id, out var pay);
            var (paymentKind, paidOnlineMinor, remainingAtShopMinor) =
                BookingPaymentProjection.Resolve(b.TotalAmountMinor, pay?.Kind, pay?.GrossAmountMinor);
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
                StylistName: st?.DisplayName,
                PaymentKind: paymentKind,
                PaidOnlineMinor: paidOnlineMinor,
                RemainingAtShopMinor: remainingAtShopMinor,
                AutoConfirmEnabled: b.AutoConfirmSnapshot);
        }).ToList();

        return Result.Success<IReadOnlyList<BookingDto>>(dtos);
    }
}

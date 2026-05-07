using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Booking;

public sealed record OwnerDashboardStatsDto(
    int ThisWeekCompletedCount,
    long ThisWeekRevenueMinor,
    int LastWeekCompletedCount,
    long LastWeekRevenueMinor,
    int RevenueDeltaPercent,
    string? TopServiceName,
    int TopServiceBookingCount);

public sealed record GetOwnerDashboardStatsQuery(Guid OwnerUserId) : IQuery<OwnerDashboardStatsDto>;

/// Owner Today screen üstündeki kompakt trend widget'ı. Tek round-trip:
/// bu hafta (Pzt 00:00 → şimdi) ve geçen hafta (önceki Pzt → o hafta sonu)
/// için Completed/NoShow dahil ciro ve sayı, en popüler hizmet (booking item
/// count) son 30 gün içinde. % delta UI'da renklendirilir.
public sealed class GetOwnerDashboardStatsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetOwnerDashboardStatsQuery, Result<OwnerDashboardStatsDto>>
{
    public async Task<Result<OwnerDashboardStatsDto>> Handle(GetOwnerDashboardStatsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerId == request.OwnerUserId, ct);
        if (shop is null) return Result.Failure<OwnerDashboardStatsDto>(Error.NotFound("BarberShop"));

        var now = DateTimeOffset.UtcNow;
        var thisWeekStart = MondayStartUtc(now);
        var lastWeekStart = thisWeekStart.AddDays(-7);
        var lastWeekEnd = thisWeekStart;

        // Revenue counted only for Completed bookings (consistent with isBookingRevenueCounted).
        var thisWeek = await ctx.Set<BookingAggregate>().AsNoTracking()
            .Where(b => b.BarberShopId == shop.Id
                        && b.Status == BookingStatus.Completed
                        && b.ScheduledAt >= thisWeekStart && b.ScheduledAt < now)
            .GroupBy(b => 1)
            .Select(g => new { Count = g.Count(), Revenue = g.Sum(b => b.TotalAmountMinor) })
            .FirstOrDefaultAsync(ct);

        var lastWeek = await ctx.Set<BookingAggregate>().AsNoTracking()
            .Where(b => b.BarberShopId == shop.Id
                        && b.Status == BookingStatus.Completed
                        && b.ScheduledAt >= lastWeekStart && b.ScheduledAt < lastWeekEnd)
            .GroupBy(b => 1)
            .Select(g => new { Count = g.Count(), Revenue = g.Sum(b => b.TotalAmountMinor) })
            .FirstOrDefaultAsync(ct);

        var thisWeekCount = thisWeek?.Count ?? 0;
        var thisWeekRevenue = thisWeek?.Revenue ?? 0L;
        var lastWeekCount = lastWeek?.Count ?? 0;
        var lastWeekRevenue = lastWeek?.Revenue ?? 0L;

        // % delta — only when last week is non-zero (else "Yeni" rather than divide-by-zero).
        var deltaPercent = lastWeekRevenue == 0
            ? (thisWeekRevenue > 0 ? 100 : 0)
            : (int)Math.Round(((double)thisWeekRevenue - lastWeekRevenue) / lastWeekRevenue * 100);

        // Top service over last 30 days by booking-item count for this shop.
        var since30 = now.AddDays(-30);
        var topService = await ctx.Set<BookingItem>().AsNoTracking()
            .Where(i => ctx.Set<BookingAggregate>()
                .Any(b => b.Id == i.BookingId
                       && b.BarberShopId == shop.Id
                       && b.Status == BookingStatus.Completed
                       && b.ScheduledAt >= since30))
            .GroupBy(i => i.ServiceName)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefaultAsync(ct);

        return Result.Success(new OwnerDashboardStatsDto(
            ThisWeekCompletedCount: thisWeekCount,
            ThisWeekRevenueMinor: thisWeekRevenue,
            LastWeekCompletedCount: lastWeekCount,
            LastWeekRevenueMinor: lastWeekRevenue,
            RevenueDeltaPercent: deltaPercent,
            TopServiceName: topService?.Name,
            TopServiceBookingCount: topService?.Count ?? 0));
    }

    private static DateTimeOffset MondayStartUtc(DateTimeOffset now)
    {
        var date = now.UtcDateTime.Date;
        var diff = ((int)date.DayOfWeek + 6) % 7; // Sunday=0 → 6 days back; Monday=1 → 0
        var monday = date.AddDays(-diff);
        return new DateTimeOffset(monday, TimeSpan.Zero);
    }
}

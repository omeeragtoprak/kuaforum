using Kuaforum.Domain.Modules.Barber;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Booking;

/// <summary>
/// RFC-014 §2 D4 / docs/GAPS.md G1: paylaşılan cross-shop çakışma sorgusu — müşterinin verilen
/// zaman aralığıyla kesişen, BAŞKA bir salondaki aktif (Pending/Confirmed/Started) randevusu
/// var mı. Salon içi çakışma bunun kapsamı dışında — aynı stylist için zaten DB'deki EXCLUDE
/// constraint (M15) engelliyor; aynı salonda farklı stylist senaryosu bilinçli olarak
/// dokunulmadı (görev kapsamı "cross-shop").
///
/// İKİ çağıran var, ikisi de AYNI mantığı kullanmalı (coordinator review, 2026-08-07 —
/// başlangıçta yalnız InitiateBookingCommandHandler'da private metottu, buraya taşındı):
/// <list type="bullet">
/// <item><see cref="InitiateBookingCommandHandler"/> — randevu oluşturulduktan SONRA, "son söz"
/// (ön-kontrol ile oluşturma arasında araya giren bir randevu varsa bunu yakalayan tek yer).</item>
/// <item><see cref="CheckBookingConflictQueryHandler"/> — randevu oluşturulmadan ÖNCE, özet
/// ekranının ön-kontrolü (kapora akışında gereksiz tahsilat+iade sürtünmesini önlemek için).</item>
/// </list>
/// </summary>
public static class CrossShopConflictFinder
{
    private static readonly BookingStatus[] ConflictStatuses =
    [
        BookingStatus.Pending,
        BookingStatus.Confirmed,
        BookingStatus.Started,
    ];

    /// <summary>
    /// Sorgu <c>ix_bookings_customer_scheduled</c> (CustomerId, ScheduledAt) ile taranır — TÜM
    /// randevu geçmişini çekmek yerine yalnız durum + BarberShopId filtresiyle daraltılmış tek
    /// satır (<c>FirstOrDefaultAsync</c>) döner, N+1 yok (en fazla 2 sorgu: bu + eşleşirse salon
    /// adı için 1). <paramref name="customerId"/> HER ZAMAN authenticated caller'ın kendi id'si
    /// olmalı — çağıranlar bunu request body'den DEĞİL, ctx.UserId'den almalı (başka müşterinin
    /// randevu bilgisini sızdırmamak için).
    /// </summary>
    public static async Task<ConflictingBookingDto?> FindAsync(
        DbContext ctx, Guid customerId, Guid excludeShopId, DateTimeOffset scheduledAt, DateTimeOffset endsAt,
        CancellationToken ct)
    {
        var conflict = await ctx.Set<BookingAggregate>().AsNoTracking()
            .Where(b => b.CustomerId == customerId
                        && b.BarberShopId != excludeShopId
                        && ConflictStatuses.Contains(b.Status)
                        // Sınır durumu: bitiş anı == başlangıç anı çakışma SAYILMAZ — kesin
                        // eşitsizlikler (BookingBusyRangeQuery'deki aynı desen).
                        && b.ScheduledAt < endsAt
                        && b.ScheduledEndsAt > scheduledAt)
            .OrderBy(b => b.ScheduledAt)
            .Select(b => new { b.Id, b.BarberShopId, b.ScheduledAt, b.ScheduledEndsAt })
            .FirstOrDefaultAsync(ct);

        if (conflict is null) return null;

        var shopName = await ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => s.Id == conflict.BarberShopId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? "";

        return new ConflictingBookingDto(conflict.Id, shopName, conflict.ScheduledAt, conflict.ScheduledEndsAt);
    }
}

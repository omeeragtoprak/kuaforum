using Kuaforum.Application.Modules.Booking;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Barber;

/// <summary>
/// RFC-015 §2 D1 / §4 madde 1: bir WorkingHours değişikliği (belirli bir gün için yeni
/// open/close/isClosed) hangi gelecekteki Pending/Confirmed randevuları "dışarıda" bırakıyor.
/// Temel liste <see cref="StylistFutureBookingsFinder"/>'dan gelir (D2 ile paylaşılan tek
/// kaynak) — bu sınıf yalnız o listeye day-of-week + saat-aralığı FİLTRESİNİ ekler.
///
/// TZ semantiği <see cref="Kuaforum.Application.Modules.Availability.StylistScheduleChecker"/>
/// ile BİREBİR aynı (Europe/Istanbul local wall-time karşılaştırması, sınırda eşitlik "içeride"
/// sayılır — <c>start &lt; open || end &gt; close</c> ancak o zaman "dışarıda") — booking oluşturma
/// yolunda "bookable" kabul edilen bir slot, WorkingHours değişmediği sürece burada da "etkilenmez"
/// çıkmalı; aksi halde iki yer farklı sınır kuralına göre farklı cevap verirdi.
/// </summary>
public static class WorkingHoursImpactFinder
{
    private static readonly TimeZoneInfo IstanbulTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public static async Task<List<AffectedBookingDto>> FindOutsideNewRangeAsync(
        DbContext ctx,
        Guid stylistId,
        DayOfWeek dayOfWeek,
        TimeOnly openTime,
        TimeOnly closeTime,
        bool isClosed,
        CancellationToken ct)
    {
        var future = await StylistFutureBookingsFinder.GetFutureActiveBookingsAsync(ctx, stylistId, ct);
        if (future.Count == 0) return future;

        var result = new List<AffectedBookingDto>();
        foreach (var b in future)
        {
            var startLocal = TimeZoneInfo.ConvertTime(b.ScheduledAt, IstanbulTz);
            if (startLocal.DayOfWeek != dayOfWeek) continue;

            if (isClosed)
            {
                result.Add(b);
                continue;
            }

            var date = DateOnly.FromDateTime(startLocal.DateTime);
            var openUtc = ToUtc(date, openTime);
            var closeUtc = ToUtc(date, closeTime);

            if (b.ScheduledAt < openUtc || b.ScheduledEndsAt > closeUtc)
                result.Add(b);
        }

        return result;
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, IstanbulTz.GetUtcOffset(local)).ToUniversalTime();
    }
}

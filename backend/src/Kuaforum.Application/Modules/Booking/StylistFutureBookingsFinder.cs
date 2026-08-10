using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Booking;

/// <summary>
/// RFC-015 §4 madde 1: "belirli bir stylist için gelecekteki aktif randevular" — D1 (WorkingHours
/// değişikliği, <c>SetWorkingHoursCommandHandler</c> / <c>GetWorkingHoursChangeImpactQueryHandler</c>,
/// <c>Kuaforum.Application.Modules.Barber.WorkingHoursImpactFinder</c> üzerinden) ve D2 (Stylist
/// pasifleştirme, <c>UpdateStylistCommandHandler</c>) İKİSİ DE bu sorguyu kullanır — kopyalanmaz
/// (<see cref="CrossShopConflictFinder"/> presedenti: ikizlenmiş kurallar bir kez ayrışmıştı).
///
/// D2 bu listeyi OLDUĞU GİBİ kullanır — pasifleştirme saat aralığından bağımsız, o stylist'in HER
/// gelecekteki aktif randevusu etkilenir. D1 bu temel listeden kendi day-of-week + saat-aralığı
/// filtresini UYGULAR (bkz. <c>WorkingHoursImpactFinder.FindOutsideNewRangeAsync</c>) — aynı
/// temel EF sorgusu (join + projeksiyon) iki yerde yeniden yazılmaz.
/// </summary>
public static class StylistFutureBookingsFinder
{
    private static readonly BookingStatus[] ActiveStatuses = [BookingStatus.Pending, BookingStatus.Confirmed];

    /// <summary>
    /// Sınır durumu: <c>ScheduledAt &gt; now</c> (kesin eşitsizlik) — "şu an başlayan" bir randevu
    /// "gelecekteki" sayılmaz, zaten başlamış kabul edilir (aynı ilke <see cref="CrossShopConflictFinder"/>
    /// ve <c>BookingBusyRangeQuery</c>'deki sınır karşılaştırmalarıyla tutarlı).
    /// </summary>
    public static async Task<List<AffectedBookingDto>> GetFutureActiveBookingsAsync(
        DbContext ctx, Guid stylistId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        return await (
            from b in ctx.Set<BookingAggregate>().AsNoTracking()
            where b.StylistId == stylistId
                  && ActiveStatuses.Contains(b.Status)
                  && b.ScheduledAt > now
            join u in ctx.Set<User>().AsNoTracking() on b.CustomerId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            orderby b.ScheduledAt
            select new AffectedBookingDto(
                b.Id, b.CustomerId, u != null ? u.DisplayName : null, b.ScheduledAt, b.ScheduledEndsAt))
            .ToListAsync(ct);
    }
}

/// <summary>Etkilenen randevu satırı — kuaförün önizleme/uyarı listesinde göstermesi için yeterli alan seti.</summary>
public sealed record AffectedBookingDto(
    Guid BookingId,
    Guid CustomerId,
    string? CustomerName,
    DateTimeOffset ScheduledAt,
    DateTimeOffset ScheduledEndsAt);

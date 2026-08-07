using FluentValidation;
using Kuaforum.Application.Common;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kuaforum.Application.Modules.Booking;

public sealed record InitiateBookingCommand(
    Guid CustomerId,
    Guid StylistId,
    DateTimeOffset ScheduledAt,
    IReadOnlyList<Guid> ServiceCatalogIds,
    bool IsWalkIn,
    string? CustomerNote) : ICommand<InitiateBookingResponse>;

public sealed record InitiateBookingResponse(
    Guid BookingId,
    DateTimeOffset ScheduledAt,
    DateTimeOffset ScheduledEndsAt,
    long TotalAmountMinor,
    string Currency,
    // RFC-014 §2 D2 madde 6: müşteri özet ekranının doğru vaadi göstermesi için. Status
    // Confirmed ise randevu anında kesinleşti. Status Pending iken AutoConfirmEnabled true
    // olması TEK bir şeyi ifade edebilir (bkz. Booking.Initiate): bu salon kapora/tam ödeme
    // istiyor VE ödeme tamamlanınca kuaför onayı beklemeden Confirmed olacak — auto-confirm
    // açıkken online ödeme GEREKMEYEN randevular zaten doğrudan Confirmed döner, hiç Pending
    // görünmezler. AutoConfirmEnabled false + Status Pending ise klasik "kuaför onaylayacak" akışı.
    BookingStatus Status,
    bool AutoConfirmEnabled,
    // RFC-014 §2 D4 / docs/GAPS.md G1: müşterinin AYNI zaman aralığına BAŞKA bir salondan
    // çakışan (Pending/Confirmed/Started) randevusu varsa doldurulur, yoksa null. Founder
    // kararı: backend bunu yalnız BİLDİRİR, randevuyu REDDETMEZ (meşru senaryo — eş/çocuk
    // adına randevu, art arda iki farklı hizmet). Mobile bu alanı "X Kuaför'de de randevun
    // var, yine de devam?" onay diyaloğu için kullanır — randevu bu yanıt dönene kadar zaten
    // OLUŞTURULMUŞ olur, diyalog geri almak için değil bilgilendirmek içindir.
    ConflictingBookingDto? ConflictingBooking = null);

/// <summary>
/// RFC-014 §2 D4: müşterinin cross-shop çakışan randevusu hakkında minimum bilgi — mobile'ın
/// uyarı metnini kurması için salon adı + zaman + booking id yeterli, tüm BookingDto'yu taşımaya
/// gerek yok.
/// </summary>
public sealed record ConflictingBookingDto(
    Guid BookingId,
    string BarberShopName,
    DateTimeOffset ScheduledAt,
    DateTimeOffset ScheduledEndsAt);

public sealed class InitiateBookingCommandValidator : AbstractValidator<InitiateBookingCommand>
{
    public InitiateBookingCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.StylistId).NotEmpty();
        RuleFor(x => x.ServiceCatalogIds).NotEmpty();
    }
}

public sealed class InitiateBookingCommandHandler(
    IApplicationDbContext db,
    IDistributedLock distributedLock,
    IBusyRangeQuery busyRangeQuery,
    IStylistScheduleChecker scheduleChecker,
    IAuditLogger audit,
    IOptions<PaymentOptions> paymentOptions) : IRequestHandler<InitiateBookingCommand, Result<InitiateBookingResponse>>
{
    private static readonly TimeSpan SlotLockDuration = TimeSpan.FromMinutes(2);

    /// <summary>
    /// RFC-014 §2 D4: cross-shop çakışma kontrolünün "aktif" saydığı durumlar — <see
    /// cref="Infrastructure.BackgroundJobs.BookingBusyRangeQuery.BlockingStatuses"/> ile aynı
    /// küme (Cancelled/Completed/NoShow bir sonraki randevuyu etkilemiyor).
    /// </summary>
    private static readonly BookingStatus[] CrossShopConflictStatuses =
    [
        BookingStatus.Pending,
        BookingStatus.Confirmed,
        BookingStatus.Started,
    ];

    /// <summary>RFC-013 review I2: MaxBookingsPerMonth ay sınırı client'ın gönderdiği DateTimeOffset.Offset'e göre DEĞİL, sabit Europe/Istanbul takvimine göre hesaplanır (bkz. StylistScheduleChecker'daki aynı desen — Türkiye 2016'dan beri DST uygulamıyor, ama doğru API kullanımı önemli).</summary>
    private static readonly TimeZoneInfo IstanbulTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    /// <summary>
    /// Caps concurrent unpaid Pending bookings per customer. Without this, a single logged-in
    /// customer can open unlimited Pending bookings — each blocks a stylist's calendar via
    /// BookingBusyRangeQuery — without ever paying. ExpirePendingBookingsJob eventually frees
    /// stale ones (30 min), but this bounds the damage in the meantime instead of relying only
    /// on the sweep. `public` (was `internal`) — RFC-013 review M4: pinned against
    /// <see cref="Kuaforum.Domain.Modules.Barber.BookingPolicy.MaxPendingPerCustomerCeiling"/>
    /// by PolicyConstantsPinTests so the two can't silently drift apart.
    /// </summary>
    public const int MaxActivePendingBookingsPerCustomer = 3;

    public async Task<Result<InitiateBookingResponse>> Handle(InitiateBookingCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;

        var activePendingCount = await ctx.Set<BookingAggregate>().AsNoTracking()
            .CountAsync(b => b.CustomerId == request.CustomerId && b.Status == BookingStatus.Pending, cancellationToken);
        if (activePendingCount >= MaxActivePendingBookingsPerCustomer)
            return Result.Failure<InitiateBookingResponse>(Error.Validation(
                $"En fazla {MaxActivePendingBookingsPerCustomer} bekleyen (henüz onaylanmamış) randevunuz olabilir. " +
                "Devam etmek için bunlardan birini tamamlayın veya iptal edin."));

        var stylist = await ctx.Set<Stylist>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.StylistId, cancellationToken);
        if (stylist is null) return Result.Failure<InitiateBookingResponse>(Error.NotFound("Stylist"));
        if (!stylist.IsActive) return Result.Failure<InitiateBookingResponse>(Error.Validation("Stilist aktif değil."));

        var shop = await ctx.Set<BarberShop>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, cancellationToken);
        if (shop is null || shop.Status != BarberShopStatus.Active)
            return Result.Failure<InitiateBookingResponse>(Error.Validation("İşletme aktif değil."));

        var policyCheck = await EnsureBookingPolicyAsync(ctx, shop, request.CustomerId, request.ScheduledAt, cancellationToken);
        if (policyCheck.IsFailure) return Result.Failure<InitiateBookingResponse>(policyCheck.Error);

        var services = await ctx.Set<ServiceCatalog>().AsNoTracking()
            .Where(s => s.BarberShopId == stylist.BarberShopId
                        && request.ServiceCatalogIds.Contains(s.Id)
                        && s.IsActive)
            .ToListAsync(cancellationToken);

        if (services.Count != request.ServiceCatalogIds.Count)
            return Result.Failure<InitiateBookingResponse>(Error.Validation("Seçilen hizmetlerin bazıları bulunamadı veya aktif değil."));

        var totalDuration = services.Sum(s => s.DurationMinutes + s.BufferMinutes);
        var endsAt = request.ScheduledAt.AddMinutes(totalDuration);

        var scheduleResult = await scheduleChecker.EnsureSlotIsBookableAsync(
            request.StylistId, request.ScheduledAt, endsAt, cancellationToken);
        if (scheduleResult.IsFailure) return Result.Failure<InitiateBookingResponse>(scheduleResult.Error);

        // Locked per-stylist, NOT per-slot-minute: two overlapping-but-not-identical requests
        // (e.g. 10:00-11:00 and 10:30-11:30) would previously take different lock keys and both
        // pass the busy-range check concurrently. Coarser lock, but Initiate is infrequent and
        // short-lived per stylist, so contention is acceptable. The EXCLUDE constraint on
        // bookings (see M15_BookingSlotExclusion) is the actual guarantee — this lock is just
        // the fast, cheap path that avoids hitting it under normal load.
        var lockKey = $"slot-lock:{request.StylistId}";
        await using var lockHandle = await distributedLock.TryAcquireAsync(lockKey, SlotLockDuration, cancellationToken);
        if (lockHandle is null)
            return Result.Failure<InitiateBookingResponse>(Error.Conflict("Bu saat şu anda başka bir müşteri tarafından rezerve ediliyor. Lütfen tekrar deneyin."));

        var busyRanges = await busyRangeQuery.GetBusyRangesAsync(request.StylistId, request.ScheduledAt, endsAt, cancellationToken);
        if (busyRanges.Any(b => request.ScheduledAt < b.EndsAt && b.StartsAt < endsAt))
            return Result.Failure<InitiateBookingResponse>(Error.Conflict("Bu saat dolu, başka bir saat seçin."));

        var orderedServices = request.ServiceCatalogIds
            .Select(id => services.First(s => s.Id == id))
            .Select(s => (s.Id, s.Name, s.DurationMinutes, s.BufferMinutes, s.PriceMinor))
            .ToList();

        // RFC-013 C4: initiate anında SNAPSHOT'lanır — global master-switch AND shop'un o anki
        // kapora ayarı. Sonradan owner kaporayı kapatsa/açsa veya admin flag'i değiştirse bile
        // bu booking'in davranışı değişmez (ExpirePendingBookingsJob bu alana göre dallanır).
        var requiresOnlinePayment = paymentOptions.Value.OnlinePaymentsEnabled && shop.BookingPolicyDepositEnabled;

        var bookingResult = BookingAggregate.Initiate(
            request.CustomerId,
            stylist.BarberShopId,
            request.StylistId,
            request.ScheduledAt,
            orderedServices,
            request.IsWalkIn ? BookingType.WalkIn : BookingType.Scheduled,
            // Review fix (2026-08-07): Payment:CommissionRate config'i KALDIRILDI (bkz.
            // Infrastructure.DependencyInjection.AddInfrastructure yorumu) — komisyon RFC-012'den
            // beri ödeme anında hiç kesilmiyor ve RFC-014 ile hiçbir faturayı beslemiyor. Booking.
            // PlatformCommissionMinor vestigial alanı (Booking aggregate'i bu paketin kapsamı
            // dışında olduğu için silinmedi) artık sabit 0 yazılır — "platform geliri" gibi
            // görünen ama para yoluna hiç girmeyen bir kolon üretmemek için.
            commissionRate: 0m,
            requiresOnlinePayment,
            request.CustomerNote,
            // RFC-013 M1: shop'un O ANKİ kapora oranı da booking'e snapshot'lanır — yalnız
            // InitiatePaymentCommand'ın son-çare fallback'i için (bkz. Booking.DepositPercentSnapshot
            // XML doc). Normal yolda hâlâ canlı shop değeri kullanılır.
            shop.BookingPolicyDepositPercent,
            // RFC-014 §2 D2: shop'un O ANKİ auto-confirm ayarı da snapshot'lanır (Booking.
            // AutoConfirmSnapshot XML doc) — RequiresOnlinePayment/DepositPercentSnapshot'la
            // aynı ilke, sonradan owner ayarı değiştirse bile bu booking'in davranışı sabit kalır.
            shop.BookingPolicyAutoConfirmEnabled);

        if (bookingResult.IsFailure) return Result.Failure<InitiateBookingResponse>(bookingResult.Error);
        var booking = bookingResult.Value;

        ctx.Set<BookingAggregate>().Add(booking);

        await audit.RecordAsync(
            "booking.initiated",
            nameof(BookingAggregate),
            booking.Id.ToString(),
            actorUserId: request.CustomerId,
            payload: new { booking.StylistId, booking.ScheduledAt, booking.TotalAmountMinor, services = request.ServiceCatalogIds },
            ct: cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PostgresErrorClassifier.IsExclusionViolation(ex, out _))
        {
            // Last-line-of-defense: the Redis lock + busy-range check above raced past each
            // other (lock held per-stylist but not perfectly serialized against a concurrent
            // reschedule, or the busy-range read happened just before another transaction
            // committed). The DB-level EXCLUDE constraint is what actually guarantees no two
            // active bookings overlap for the same stylist.
            return Result.Failure<InitiateBookingResponse>(Error.Conflict("Bu saat az önce doldu, başka bir saat seçin."));
        }

        var conflictingBooking = await FindCrossShopConflictAsync(
            ctx, request.CustomerId, stylist.BarberShopId, request.ScheduledAt, endsAt, cancellationToken);

        return Result.Success(new InitiateBookingResponse(
            booking.Id, booking.ScheduledAt, booking.ScheduledEndsAt, booking.TotalAmountMinor, booking.Currency,
            booking.Status, booking.AutoConfirmSnapshot, conflictingBooking));
    }

    /// <summary>
    /// RFC-014 §2 D4 / docs/GAPS.md G1: müşterinin YENİ oluşturulan randevuyla zaman aralığı
    /// kesişen, BAŞKA bir salondaki aktif randevusu var mı. Salon içi çakışma bunun kapsamı
    /// dışında — aynı stylist için zaten DB'deki EXCLUDE constraint (M15) engelliyor; aynı
    /// salonda farklı stylist senaryosu bilinçli olarak dokunulmadı (görev kapsamı "cross-shop").
    /// Sorgu <c>ix_bookings_customer_scheduled</c> (CustomerId, ScheduledAt) ile taranır — TÜM
    /// randevu geçmişini çekmek yerine yalnız durum + BarberShopId filtresiyle daraltılmış tek
    /// satır (<c>FirstOrDefaultAsync</c>) döner, N+1 yok (booking başına en fazla 2 sorgu: bu +
    /// eşleşirse salon adı için 1).
    /// </summary>
    private static async Task<ConflictingBookingDto?> FindCrossShopConflictAsync(
        DbContext ctx, Guid customerId, Guid newBookingShopId, DateTimeOffset scheduledAt, DateTimeOffset endsAt,
        CancellationToken ct)
    {
        var conflict = await ctx.Set<BookingAggregate>().AsNoTracking()
            .Where(b => b.CustomerId == customerId
                        && b.BarberShopId != newBookingShopId
                        && CrossShopConflictStatuses.Contains(b.Status)
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

    /// <summary>
    /// RFC-013 C3: per-shop anti-spam kuralları — MEVCUT global 3-Pending tabanının
    /// (yukarıdaki <see cref="MaxActivePendingBookingsPerCustomer"/> kontrolü, shop'tan
    /// bağımsız) YANINA eklenir, onun yerine geçmez. Sayım semantiği (RFC-013 ara karar,
    /// koordinatörce onaylandı): NoShow dahil, Cancelled hariç — bir müşterinin gelmediği
    /// randevu yine "o ayki/o aralıktaki hakkını" kullanmış sayılır, aksi halde no-show
    /// yapıp limitten muaf kalma kaçağı doğar.
    /// </summary>
    private static async Task<Result> EnsureBookingPolicyAsync(
        DbContext ctx, BarberShop shop, Guid customerId, DateTimeOffset scheduledAt, CancellationToken ct)
    {
        // (a) per-shop MaxPendingPerCustomer — global tavan zaten üstte kontrol edildi,
        // shop kendi limitini onun altına çekebilir (varsayılan 2 < platform tavanı 3).
        var shopPendingCount = await ctx.Set<BookingAggregate>().AsNoTracking()
            .CountAsync(b => b.CustomerId == customerId && b.BarberShopId == shop.Id && b.Status == BookingStatus.Pending, ct);
        if (shopPendingCount >= shop.BookingPolicyMaxPendingPerCustomer)
        {
            return Result.Failure(Error.Validation(
                $"Bu işletmede en fazla {shop.BookingPolicyMaxPendingPerCustomer} bekleyen (henüz onaylanmamış) randevunuz olabilir. " +
                "Devam etmek için bunlardan birini tamamlayın veya iptal edin."));
        }

        // (b) MinDaysBetweenBookings — PENCERE sorgusu, "en son booking" DEĞİL. İlk
        // implementasyon yalnız ScheduledAt'i EN BÜYÜK olan randevuya bakıyordu; bu, bir
        // müşterinin ÖNCE uzak bir tarihe (örn. +30 gün) randevu alıp sonra aradaki HER
        // günü doldurabilmesi anlamına geliyordu (o "en son" hep uzaktaki randevu olarak
        // kalıyor, aradaki günlerin hiçbiri onun penceresine girmiyordu — kural fiilen
        // atlanabiliyordu). Doğru kural: yeni talebin ±minDays penceresinde iptal edilmemiş
        // HERHANGİ bir randevu var mı (RFC-013 review, C1). ix_bookings_customer_scheduled
        // (CustomerId, ScheduledAt) bu sorguyu tek indexle karşılıyor.
        if (shop.BookingPolicyMinDaysBetweenBookings is int minDays)
        {
            var windowStart = scheduledAt.AddDays(-minDays);
            var windowEnd = scheduledAt.AddDays(minDays);
            var hasNearbyBooking = await ctx.Set<BookingAggregate>().AsNoTracking()
                .AnyAsync(b => b.CustomerId == customerId && b.BarberShopId == shop.Id && b.Status != BookingStatus.Cancelled
                    && b.ScheduledAt > windowStart && b.ScheduledAt < windowEnd, ct);
            if (hasNearbyBooking)
            {
                return Result.Failure(Error.Validation(
                    $"Bu işletmede randevularınız arasında en az {minDays} gün olmalı."));
            }
        }

        // (c) MaxBookingsPerMonth — yeni talebin takvim ayı içinde, aynı shop'ta, iptal
        // edilmemiş (NoShow dahil) kaç randevusu var. Ay sınırı Europe/Istanbul takvimine
        // göre sabitlenir (RFC-013 review I2) — önceki `scheduledAt.Offset` kullanımı
        // client'ın gönderdiği offset'e göre keyfi ay sınırları üretiyordu (aynı anı farklı
        // offset'le gönderen iki client farklı "ay" görebilirdi).
        if (shop.BookingPolicyMaxBookingsPerMonth is int maxPerMonth)
        {
            var scheduledLocal = TimeZoneInfo.ConvertTime(scheduledAt, IstanbulTz);
            var monthStartLocal = new DateTime(scheduledLocal.Year, scheduledLocal.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var monthEndLocal = monthStartLocal.AddMonths(1);
            // Npgsql yalnız Offset=0 (UTC) DateTimeOffset'i "timestamp with time zone" query
            // parametresine yazabiliyor ("Cannot write DateTimeOffset with Offset=03:00:00...")
            // — +03:00 offset'li değeri DOĞRUDAN LINQ'e vermek 500'e patlıyor. Istanbul'daki
            // takvim sınırını hesapladıktan SONRA UTC'ye çeviriyoruz; instant değişmiyor.
            var monthStart = new DateTimeOffset(monthStartLocal, IstanbulTz.GetUtcOffset(monthStartLocal)).ToUniversalTime();
            var monthEnd = new DateTimeOffset(monthEndLocal, IstanbulTz.GetUtcOffset(monthEndLocal)).ToUniversalTime();
            var countThisMonth = await ctx.Set<BookingAggregate>().AsNoTracking()
                .CountAsync(b => b.CustomerId == customerId && b.BarberShopId == shop.Id
                    && b.Status != BookingStatus.Cancelled
                    && b.ScheduledAt >= monthStart && b.ScheduledAt < monthEnd, ct);
            if (countThisMonth >= maxPerMonth)
            {
                return Result.Failure(Error.Validation(
                    $"Bu işletmede aylık en fazla {maxPerMonth} randevu alabilirsiniz."));
            }
        }

        return Result.Success();
    }
}

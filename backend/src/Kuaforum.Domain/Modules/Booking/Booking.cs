using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Booking.Events;

namespace Kuaforum.Domain.Modules.Booking;

public sealed class Booking : AggregateRoot
{
    private readonly List<BookingItem> _items = new();

    private Booking() { }

    public Guid CustomerId { get; private set; }
    public Guid BarberShopId { get; private set; }
    public Guid StylistId { get; private set; }
    public DateTimeOffset ScheduledAt { get; private set; }
    public DateTimeOffset ScheduledEndsAt { get; private set; }
    public BookingStatus Status { get; private set; }
    public BookingType BookingType { get; private set; }
    public long TotalAmountMinor { get; private set; }
    public long PlatformCommissionMinor { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public string? CustomerNote { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public BookingCancellationReason? CancellationReason { get; private set; }
    public int? RefundPercentage { get; private set; }
    public Guid? PaymentId { get; private set; }

    /// <summary>
    /// RFC-013 C4: initiate anında hesaplanıp SNAPSHOT'lanır — <c>global
    /// Payment:OnlinePaymentsEnabled &amp;&amp; shop.BookingPolicy.DepositEnabled</c>. Sonradan
    /// flag/policy değişse bile bu booking'in davranışı değişmez. <see
    /// cref="Infrastructure.BackgroundJobs.ExpirePendingBookingsJob"/> bu alana göre dallanır —
    /// job'un her tick'te shop policy'sini ayrıca sorgulamasını (N+1) engeller.
    /// </summary>
    /// <remarks>
    /// RFC-013 review M2: bu XML doc önceden "RFC-006'nın kapora-oranı snapshot presedentiyle
    /// aynı ilke" diyordu — YANLIŞTI, o presedent yoktu; <see cref="DepositPercentSnapshot"/>
    /// eklenene kadar oran hiç snapshot'lanmıyordu, her <c>InitiatePaymentCommand</c> çağrısında
    /// canlı <c>shop.BookingPolicyDepositPercent</c>'ten okunuyordu (M1 review'ının bulduğu
    /// "sessiz ceza" da tam bu yüzdendi). Şimdi ikisi ayrı ayrı gerçek: bu alan (bool) her zaman
    /// snapshot'lıydı; <see cref="DepositPercentSnapshot"/> (oran) RFC-013 M1 fix'iyle eklendi —
    /// ama yalnız FALLBACK olarak kullanılır (bkz. o alanın XML doc'u), normal yolda oran hâlâ
    /// canlı okunur.
    /// </remarks>
    public bool RequiresOnlinePayment { get; private set; }

    /// <summary>
    /// RFC-013 M1 (adversarial review): <c>InitiateBookingCommandHandler</c>
    /// anında <c>shop.BookingPolicyDepositPercent</c>'in o anki değeri buraya da yazılır
    /// (RequiresOnlinePayment true iken; aksi halde null). Normal yolda
    /// <c>InitiatePaymentCommand</c> kapora tutarını hâlâ CANLI shop değerinden hesaplar — bu
    /// alan yalnız şu son-çare senaryosunda devreye girer: owner booking oluşturulduktan SONRA
    /// kaporayı kapatırsa (shop.BookingPolicyDepositEnabled=false → BookingPolicy.Create
    /// DepositPercent'i null'a normalize eder), müşteri kaporayı ödeyemeden 409 alır ve
    /// ExpirePendingBookingsJob 30dk sonra randevuyu sessizce iptal eder. Bu snapshot o "sessiz
    /// ceza"yı önler: canlı değer yoksa (booking.RequiresOnlinePayment true olduğu sürece) bu
    /// alan devreye girer — "booking'in kendi anındaki taahhüdü kazanır".
    /// </summary>
    public decimal? DepositPercentSnapshot { get; private set; }

    /// <summary>
    /// RFC-013 C4: kapora/tam-ödeme capture edildiğinde <see cref="MarkPaymentReceived"/> ile
    /// set edilir. Booking Status'ü Pending KALIR — ödeme artık onaylamıyor, onay her zaman
    /// ayrı bir <see cref="OwnerConfirm"/> adımı. Null iken "ödeme yok/henüz alınmadı".
    /// </summary>
    public DateTimeOffset? DepositPaidAt { get; private set; }

    public IReadOnlyCollection<BookingItem> Items => _items.AsReadOnly();

    public static Result<Booking> Initiate(
        Guid customerId,
        Guid barberShopId,
        Guid stylistId,
        DateTimeOffset scheduledAt,
        IReadOnlyList<(Guid ServiceId, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor)> services,
        BookingType bookingType,
        decimal commissionRate,
        bool requiresOnlinePayment = false,
        string? customerNote = null,
        decimal? depositPercentSnapshot = null)
    {
        if (services.Count == 0)
            return Result.Failure<Booking>(Error.Validation("En az bir hizmet seçilmeli."));
        if (scheduledAt < DateTimeOffset.UtcNow.AddMinutes(-1))
            return Result.Failure<Booking>(Error.Validation("Geçmiş tarih için randevu oluşturulamaz."));
        if (commissionRate is < 0 or > 0.5m)
            return Result.Failure<Booking>(Error.Validation("Komisyon oranı %0 ile %50 arasında olmalı."));

        var totalDuration = services.Sum(s => s.DurationMinutes + s.BufferMinutes);
        var totalAmount = services.Sum(s => s.PriceMinor);
        var commission = PaymentAmounts.CalculateCommissionMinor(totalAmount, commissionRate);

        var booking = new Booking
        {
            CustomerId = customerId,
            BarberShopId = barberShopId,
            StylistId = stylistId,
            ScheduledAt = scheduledAt,
            ScheduledEndsAt = scheduledAt.AddMinutes(totalDuration),
            Status = BookingStatus.Pending,
            BookingType = bookingType,
            TotalAmountMinor = totalAmount,
            PlatformCommissionMinor = commission,
            RequiresOnlinePayment = requiresOnlinePayment,
            // RFC-013 M1: yalnız requiresOnlinePayment true iken anlamlı — false iken caller ne
            // gönderirse göndersin null'a normalize edilir (BookingPolicy.Create'in DepositEnabled
            // false iken DepositPercent'i null'a normalize etmesiyle aynı ilke).
            DepositPercentSnapshot = requiresOnlinePayment ? depositPercentSnapshot : null,
            CustomerNote = customerNote?.Length > 500 ? customerNote[..500] : customerNote,
        };

        var orderIndex = 0;
        foreach (var s in services)
        {
            booking._items.Add(BookingItem.Create(booking.Id, s.ServiceId, s.Name, s.DurationMinutes, s.BufferMinutes, s.PriceMinor, orderIndex++));
        }

        booking.RaiseDomainEvent(new BookingInitiatedEvent(
            booking.Id, customerId, barberShopId, stylistId, scheduledAt, totalAmount));

        return booking;
    }

    /// <summary>
    /// RFC-013 C4: kapora/tam-ödeme capture edildiğinde (mock-complete + PayTR webhook ortak
    /// yolu, <see cref="Kuaforum.Application.Modules.Payment.PaymentCompletionService"/>)
    /// çağrılır. Booking'i Confirm ETMEZ (Status Pending kalır) — yalnızca "ödeme alındı, onay
    /// bekleniyor" işaretler: <see cref="PaymentId"/> + <see cref="DepositPaidAt"/>. Onay her
    /// zaman ayrı, açık bir adım: <see cref="OwnerConfirm"/> (madde 2/3 — "önce kapora, sonra
    /// onay", <c>PaymentKind.FullOnline</c> dahil — RFC-006'nın eski "tam ödeme = anında onay"
    /// varsayımı bu RFC ile bilinçli olarak terk edildi).
    /// İdempotent: aynı paymentId ile ikinci çağrı (webhook retry) no-op döner — booking bu
    /// arada owner tarafından zaten Confirmed'a taşınmış olsa BİLE (guard Status'e değil
    /// PaymentId eşitliğine bakar, aksi halde meşru bir retry haksız yere Failure alırdı).
    /// </summary>
    public Result MarkPaymentReceived(Guid paymentId)
    {
        if (PaymentId == paymentId)
            return Result.Success(); // idempotent no-op — webhook retry güvenliği

        if (Status != BookingStatus.Pending)
            return Result.Failure(Error.Validation($"{Status} durumundaki randevu için ödeme işaretlenemez."));

        PaymentId = paymentId;
        DepositPaidAt = DateTimeOffset.UtcNow;
        Touch();

        RaiseDomainEvent(new BookingPaymentReceivedEvent(Id, CustomerId, StylistId, ScheduledAt));
        return Result.Success();
    }

    /// <summary>
    /// RFC-013 C1 (adversarial review — "para sızıntısı"): PSP capture, <see
    /// cref="MarkPaymentReceived"/>'ın Pending-guard'ından SONRA gerçekleşebilir — owner müşteri
    /// PayTR ekranındayken reddetmiş (<see cref="Cancel"/> BarberRequested) veya
    /// <c>ExpirePendingBookingsJob</c> 30dk'da zaten iptal etmiş (PendingTimeout) olabilir; PSP
    /// webhook'u/mock-complete daha SONRA gelir. Eski davranış: bu durumda capture booking'e HİÇ
    /// bağlanmıyordu (webhook yolu) veya komut tamamen düşüyordu (mock yolu) — ikisinde de
    /// <see cref="PaymentId"/> null kalıyor, para PSP'de tahsil edilmiş ama sistemde hiçbir yere
    /// bağlı olmadığı için <c>BookingRefundService</c> onu asla göremiyordu (yetim para, sessiz
    /// kayıp, alarm yok).
    ///
    /// Bu metot capture'ı Status'ten BAĞIMSIZ olarak booking'e bağlar. Idempotent: PaymentId
    /// zaten aynı değere eşitse no-op. <see cref="DepositPaidAt"/>'e KASITLI dokunmaz — o alan
    /// yalnız normal/Pending yolunda ("ödeme alındı, onay bekleniyor" bildirimi) anlamlı; burada
    /// booking zaten terminal/onaylı bir durumda, farklı bir bildirim/anlam gerektirmez.
    ///
    /// Çağıran taraf (<c>PaymentCompletionService</c>) her orphan capture'da bir refund job
    /// enqueue eder — booking Cancelled/NoShow değilse veya iade tutarı 0'sa
    /// <c>BookingRefundService</c> zaten kendi no-op'unu üretir (bkz. o sınıfın XML doc'u); bu
    /// yüzden bu metot burada Cancelled/NoShow'a özel bir dallanma YAPMAZ — "her zaman ekle,
    /// iade zincirine karar verdirt" ilkesiyle basit tutulur.
    /// </summary>
    public Result AttachOrphanPayment(Guid paymentId)
    {
        if (PaymentId == paymentId)
            return Result.Success(); // idempotent no-op

        if (Status == BookingStatus.Pending)
            return Result.Failure(Error.Validation(
                "Pending durumundaki randevu için AttachOrphanPayment kullanılmaz — MarkPaymentReceived çağrılmalı."));

        PaymentId = paymentId;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Owner manually confirms a Pending booking. RFC-013 C4: this is now the ONLY path from
    /// Pending to Confirmed — used both for the walk-in/no-online-payment flow (PSP unavailable
    /// or shop's deposit policy off) AND after a deposit/full-online payment has already been
    /// received via <see cref="MarkPaymentReceived"/> (which deliberately leaves Status as
    /// Pending). Doesn't touch PaymentId — if MarkPaymentReceived ran first, the previously-set
    /// PaymentId survives untouched; this is what makes "payment already recorded, confirm is a
    /// separate step" work without this method needing to know whether a payment exists.
    /// </summary>
    public Result OwnerConfirm()
    {
        if (Status != BookingStatus.Pending)
            return Result.Failure(Error.Validation($"{Status} durumundaki randevu onaylanamaz."));

        Status = BookingStatus.Confirmed;
        ConfirmedAt = DateTimeOffset.UtcNow;
        Touch();

        RaiseDomainEvent(new BookingConfirmedEvent(Id, CustomerId, StylistId, ScheduledAt));
        return Result.Success();
    }

    public Result Start()
    {
        if (Status != BookingStatus.Confirmed)
            return Result.Failure(Error.Validation($"{Status} durumundaki randevu başlatılamaz."));

        Status = BookingStatus.Started;
        StartedAt = DateTimeOffset.UtcNow;
        Touch();

        RaiseDomainEvent(new BookingStartedEvent(Id, StartedAt.Value));
        return Result.Success();
    }

    public Result Complete()
    {
        if (Status != BookingStatus.Started)
            return Result.Failure(Error.Validation($"{Status} durumundaki randevu tamamlanamaz."));

        Status = BookingStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        Touch();

        RaiseDomainEvent(new BookingCompletedEvent(Id, CustomerId, StylistId, CompletedAt.Value));
        return Result.Success();
    }

    public Result Cancel(BookingCancellationReason reason)
    {
        if (Status is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.NoShow)
            return Result.Failure(Error.Validation($"{Status} durumundaki randevu iptal edilemez."));

        var refundPct = ComputeRefundPercentage(reason);

        Status = BookingStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        CancellationReason = reason;
        RefundPercentage = refundPct;
        Touch();

        RaiseDomainEvent(new BookingCancelledEvent(Id, reason, refundPct));
        return Result.Success();
    }

    /// <summary>
    /// Move the booking to a new scheduled time. Only Pending or Confirmed bookings may be rescheduled.
    /// Caller MUST verify slot availability for the new time before invoking — domain doesn't know about slots.
    /// </summary>
    public Result Reschedule(DateTimeOffset newScheduledAt)
    {
        if (Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
            return Result.Failure(Error.Validation($"{Status} durumundaki randevu yeniden planlanamaz."));

        if (newScheduledAt <= DateTimeOffset.UtcNow)
            return Result.Failure(Error.Validation("Yeni randevu zamanı gelecekte olmalı."));

        var totalDuration = ScheduledEndsAt - ScheduledAt;
        var oldScheduledAt = ScheduledAt;
        ScheduledAt = newScheduledAt;
        ScheduledEndsAt = newScheduledAt.Add(totalDuration);
        Touch();

        RaiseDomainEvent(new BookingRescheduledEvent(Id, CustomerId, StylistId, oldScheduledAt, newScheduledAt));
        return Result.Success();
    }

    public Result MarkNoShow()
    {
        if (Status != BookingStatus.Confirmed)
            return Result.Failure(Error.Validation($"{Status} durumundaki randevu için gelmedi işareti konulamaz."));
        if (DateTimeOffset.UtcNow < ScheduledEndsAt)
            return Result.Failure(Error.Validation("Randevu bitiş saatinden önce gelmedi işareti konulamaz."));

        Status = BookingStatus.NoShow;
        CancellationReason = BookingCancellationReason.NoShow;
        RefundPercentage = 0;
        Touch();

        RaiseDomainEvent(new BookingNoShowEvent(Id, CustomerId));
        return Result.Success();
    }

    private int ComputeRefundPercentage(BookingCancellationReason reason)
    {
        if (reason == BookingCancellationReason.BarberRequested) return 100;
        if (reason == BookingCancellationReason.PaymentFailed) return 100;
        if (reason == BookingCancellationReason.PendingTimeout) return 100;
        if (reason == BookingCancellationReason.AdminOverride) return 100;
        if (reason == BookingCancellationReason.NoShow) return 0;

        // Kalan tek olasılık: CustomerRequested (bkz. CancelBookingCommand.ResolveReasonAsync —
        // müşteri/owner-tetikli iptalde reason server-side türetilir, müşteri kendisi
        // BarberRequested/AdminOverride gönderemez).
        //
        // RFC-013 C2 (adversarial review): owner HENÜZ ONAYLAMAMIŞKEN (ConfirmedAt null) müşteri
        // iptal ederse iade her zaman %100 — saat-bazlı kademe yalnız OwnerConfirm() ile
        // ONAYLANMIŞ randevularda işler. Eski davranış saat farkına bakıyordu (Status'ten
        // bağımsız): kuaför daha talebi hiç onaylamamışken bile <12h kala iptalde %0 iade
        // üretiyordu — kabul edilmemiş bir talep için para alıkonuyordu, RFC-013'ün "kuaför
        // onaylamadan randevu oluşmaz" ilkesiyle çelişiyordu.
        if (ConfirmedAt is null) return 100;

        var hoursUntil = (ScheduledAt - DateTimeOffset.UtcNow).TotalHours;
        if (hoursUntil >= 24) return 100;
        if (hoursUntil >= 12) return 50;
        return 0;
    }
}

public sealed class BookingItem : Entity
{
    private BookingItem() { }

    public Guid BookingId { get; private set; }
    public Guid ServiceCatalogId { get; private set; }
    public string ServiceName { get; private set; } = null!;
    public int DurationMinutes { get; private set; }
    public int BufferMinutes { get; private set; }
    public long PriceMinor { get; private set; }
    public int OrderIndex { get; private set; }

    public static BookingItem Create(Guid bookingId, Guid serviceCatalogId, string name, int duration, int buffer, long price, int orderIndex)
    {
        return new BookingItem
        {
            BookingId = bookingId,
            ServiceCatalogId = serviceCatalogId,
            ServiceName = name,
            DurationMinutes = duration,
            BufferMinutes = buffer,
            PriceMinor = price,
            OrderIndex = orderIndex,
        };
    }
}

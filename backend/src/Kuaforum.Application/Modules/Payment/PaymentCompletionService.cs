using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using PaymentAggregate = Kuaforum.Domain.Modules.Payment.Payment;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Application.Modules.Payment;

/// <summary>
/// Capture → booking'i "ödeme alındı" işaretle → audit ortak bloğu (RFC-006 T4, RFC-013 C4).
/// İki tüketici: CompletePaymentCommand (dev mock akışı) ve ProcessPaytrWebhookCommand (PayTR
/// bildirimi). SaveChanges ÇAĞIRMAZ — commit kararı handler'ındır.
///
/// RFC-013 C4: capture artık booking'i Confirm ETMİYOR — <see cref="BookingAggregate.MarkPaymentReceived"/>
/// çağrılır (Status Pending kalır, PaymentId + DepositPaidAt set edilir). Onay her zaman ayrı
/// bir adım: owner'ın OwnerConfirm() çağrısı (PaymentKind.FullOnline dahil — "her randevuda
/// onay zorunlu"). Capture PaymentCapturedEvent, MarkPaymentReceived BookingPaymentReceivedEvent
/// yayar; oto-bildirim zinciri event handler'larda çalışır (owner'a "ödenmiş talep onayınızı
/// bekliyor").
///
/// RFC-013 C1 (adversarial review — "para sızıntısı"): capture, booking'in Pending guard'ını
/// kaybettiği bir anda gelebilir — owner müşteri PayTR ekranındayken reddetmiş veya
/// ExpirePendingBookingsJob 30dk'da zaten iptal etmiş olabilir; PSP webhook'u/mock-complete daha
/// SONRA gelir. Eski davranış booking'in TERMİNAL DURUM MATRİSİNE göre iki farklı ama ikisi de
/// yanlış sonuç üretiyordu:
///   - webhook (eski confirmOnlyIfPending=true): capture'ı booking'e hiç bağlamadan sessizce
///     bırakıyordu — payment.Captured, booking.PaymentId NULL, BookingRefundService parayı asla
///     göremiyordu (yetim para, iade yok, alarm yok).
///   - mock-complete (eski confirmOnlyIfPending=false): MarkPaymentReceived'ın Status guard'ı
///     Failure döndürüyordu → tüm komut düşüyordu (SaveChanges hiç çağrılmıyordu) — gerçek bir
///     PSP'de bu, "PSP'de para çekildi ama bizim sistemimiz bunu hiç bilmiyor" demek.
///
/// Yeni davranış confirmOnlyIfPending ayrımını KALDIRIR (o ayrımın kendisi bug'ın kaynağıydı —
/// iki dal da yanlıştı) ve booking'in o anki durumuna göre TEK bir kural uygular (terminal durum
/// matrisi):
///   - Pending  → MarkPaymentReceived (mevcut/normal yol, davranış değişmedi).
///   - PaymentId zaten bu payment'a eşitse → hiçbir şey yapma (idempotent retry — booking meşru
///     şekilde Confirmed/Started'a ilerlemiş olabilir, MarkPaymentReceived zaten bu durumu
///     Pending guard'ından ÖNCE yakalardı; burada da aynı kısa devre).
///   - Diğer TÜM durumlar (Cancelled, NoShow, Confirmed/Started/Completed'da PaymentId farklı/
///     boş) → AttachOrphanPayment (booking Status'ten bağımsız PaymentId'yi bağlar) + refund job
///     enqueue + "payment.orphan_capture" audit + LogError (operasyon HER ZAMAN görsün — capture
///     bu noktada asla olmaması gereken bir olay, iade gerekmese bile araştırılmalı).
///     BookingRefundService zaten idempotent + duruma göre no-op üretebiliyor (Cancelled/NoShow
///     değilse veya iade tutarı 0'sa job'ın kendisi no-op döner) — bu yüzden burada Cancelled'a
///     özel bir dallanma YOK, her orphan capture aynı yolu izler, karar refund zincirine bırakılır.
/// </summary>
public sealed class PaymentCompletionService(
    IApplicationDbContext db,
    IAuditLogger audit,
    IBookingRefundJobEnqueuer refundJobEnqueuer,
    ILogger<PaymentCompletionService> logger)
{
    public async Task<Result> CaptureAndConfirmAsync(
        PaymentAggregate payment,
        string providerTransactionId,
        long pspMdrMinor,
        CancellationToken ct)
    {
        var captureResult = payment.Capture(providerTransactionId, pspMdrMinor);
        if (captureResult.IsFailure) return captureResult;

        var ctx = (DbContext)db;
        var booking = await ctx.Set<BookingAggregate>()
            .FirstOrDefaultAsync(b => b.Id == payment.BookingId, ct);
        if (booking is null) return Result.Failure(Error.NotFound("Booking"));

        if (booking.Status == BookingStatus.Pending)
        {
            var markPaidResult = booking.MarkPaymentReceived(payment.Id);
            if (markPaidResult.IsFailure) return markPaidResult;
        }
        else if (booking.PaymentId != payment.Id)
        {
            var attachResult = booking.AttachOrphanPayment(payment.Id);
            if (attachResult.IsFailure) return attachResult;

            refundJobEnqueuer.EnqueueRefund(booking.Id);

            logger.LogError(
                "PaymentCompletionService: orphan capture — payment {PaymentId} ({GrossAmountMinor} kuruş) " +
                "captured for booking {BookingId} which is already {BookingStatus} (not Pending). " +
                "Refund job enqueued; BookingRefundService will decide whether anything is owed.",
                payment.Id, payment.GrossAmountMinor, booking.Id, booking.Status);

            await audit.RecordAsync("payment.orphan_capture", nameof(PaymentAggregate), payment.Id.ToString(),
                payload: new
                {
                    bookingId = booking.Id,
                    bookingStatus = booking.Status.ToString(),
                    payment.GrossAmountMinor,
                    refundPercentage = booking.RefundPercentage,
                },
                ct: ct);
        }
        // else: booking.PaymentId == payment.Id zaten — booking bu payment'la meşru şekilde
        // Confirmed/Started/Completed'a ilerlemiş idempotent bir retry (webhook tekrarı). Ekstra
        // bir işlem gerekmez, aşağıdaki "payment.captured" audit'i yine de yazılır.

        await audit.RecordAsync("payment.captured", nameof(PaymentAggregate), payment.Id.ToString(),
            payload: new { providerTxId = providerTransactionId, payment.GrossAmountMinor },
            ct: ct);
        return Result.Success();
    }
}

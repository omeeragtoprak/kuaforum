using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using PaymentAggregate = Kuaforum.Domain.Modules.Payment.Payment;
using Microsoft.EntityFrameworkCore;

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
/// </summary>
public sealed class PaymentCompletionService(IApplicationDbContext db, IAuditLogger audit)
{
    /// <param name="confirmOnlyIfPending">
    /// true (webhook): booking Pending değilse MarkPaymentReceived hiç ÇAĞRILMAZ, ama capture
    /// yine kaydedilir (idempotent retry / booking bu arada owner tarafından zaten Confirmed'a
    /// taşınmış — MarkPaymentReceived'ın kendi PaymentId-eşitliği idempotency'si zaten bu
    /// durumu güvenli karşılar, ama burada hiç çağırmamak ekstra bir savunma katmanı). false
    /// (mock complete): her zaman çağrılır; Pending olmayan booking'de MarkPaymentReceived
    /// hatası tüm işlemi düşürür — mevcut davranış korunur.
    /// </param>
    public async Task<Result> CaptureAndConfirmAsync(
        PaymentAggregate payment,
        string providerTransactionId,
        long pspMdrMinor,
        bool confirmOnlyIfPending,
        CancellationToken ct)
    {
        var captureResult = payment.Capture(providerTransactionId, pspMdrMinor);
        if (captureResult.IsFailure) return captureResult;

        var ctx = (DbContext)db;
        var booking = await ctx.Set<BookingAggregate>()
            .FirstOrDefaultAsync(b => b.Id == payment.BookingId, ct);
        if (booking is null) return Result.Failure(Error.NotFound("Booking"));

        if (!confirmOnlyIfPending || booking.Status == BookingStatus.Pending)
        {
            var markPaidResult = booking.MarkPaymentReceived(payment.Id);
            if (markPaidResult.IsFailure) return markPaidResult;
        }

        await audit.RecordAsync("payment.captured", nameof(PaymentAggregate), payment.Id.ToString(),
            payload: new { providerTxId = providerTransactionId, payment.GrossAmountMinor },
            ct: ct);
        return Result.Success();
    }
}

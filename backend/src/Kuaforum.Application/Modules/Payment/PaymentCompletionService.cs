using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using PaymentAggregate = Kuaforum.Domain.Modules.Payment.Payment;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Payment;

/// <summary>
/// Capture → booking Confirm → audit ortak bloğu (RFC-006 T4). İki tüketici:
/// CompletePaymentCommand (dev mock akışı) ve ProcessPaytrWebhookCommand (PayTR bildirimi).
/// SaveChanges ÇAĞIRMAZ — commit kararı handler'ındır. Capture PaymentCapturedEvent,
/// Confirm BookingConfirmedEvent yayar; oto-bildirim zinciri event handler'larda çalışır.
/// </summary>
public sealed class PaymentCompletionService(IApplicationDbContext db, IAuditLogger audit)
{
    /// <param name="confirmOnlyIfPending">
    /// true (webhook): booking Pending değilse Confirm atlanır ama capture yine kaydedilir
    /// (idempotent retry / önceden onaylanmış akış). false (mock complete): Pending olmayan
    /// booking'de Confirm hatası tüm işlemi düşürür — mevcut davranış korunur.
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
            var confirmResult = booking.Confirm(payment.Id);
            if (confirmResult.IsFailure) return confirmResult;
        }

        await audit.RecordAsync("payment.captured", nameof(PaymentAggregate), payment.Id.ToString(),
            payload: new { providerTxId = providerTransactionId, payment.GrossAmountMinor },
            ct: ct);
        return Result.Success();
    }
}

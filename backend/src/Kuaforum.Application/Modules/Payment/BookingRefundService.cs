using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using PaymentAggregate = Kuaforum.Domain.Modules.Payment.Payment;
using Kuaforum.Domain.Modules.Payment;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Payment;

public enum BookingRefundOutcomeKind
{
    /// <summary>A Refund row was created and the PSP call succeeded.</summary>
    Refunded,

    /// <summary>Booking isn't Cancelled/NoShow yet — nothing to refund.</summary>
    BookingNotCancelled,

    /// <summary>RefundPercentage is unset or 0 (e.g. &lt;12h customer cancel) — no refund owed.</summary>
    NoRefundOwed,

    /// <summary>Booking never had a linked payment (owner-confirmed / walk-in flow, no PSP charge to reverse).</summary>
    NoPayment,

    /// <summary>Payment exists but isn't in a refundable state (Initiated/Authorized/Failed/already fully Refunded).</summary>
    PaymentNotRefundable,

    /// <summary>Owed amount is already fully covered by prior successful refunds — idempotent no-op.</summary>
    AlreadyFullyRefunded,
}

public sealed record BookingRefundOutcome(BookingRefundOutcomeKind Kind, Refund? Refund = null);

/// <summary>
/// Single source of truth for "how much do we owe back on this booking, and did we already
/// give it" — used by both the admin manual-refund endpoint (RefundPaymentCommand) and the
/// automatic post-cancellation flow (ProcessBookingRefundJob, triggered by
/// BookingCancelledRefundHandler). Idempotent by construction: every call re-derives the
/// remaining owed amount from Payment.Refunds rather than assuming "first call wins", so
/// Hangfire retries / duplicate enqueues / a manual admin nudge after the auto-refund already
/// ran are all safe no-ops instead of double refunds.
///
/// Does NOT call SaveChangesAsync — callers own the transaction boundary (and, for the admin
/// path, also own translating a non-Refunded outcome into a user-facing error).
/// </summary>
public interface IBookingRefundService
{
    Task<Result<BookingRefundOutcome>> RefundIfEligibleAsync(Guid bookingId, Guid? actorUserId, CancellationToken ct);
}

public sealed class BookingRefundService(
    IApplicationDbContext db,
    IPaymentProviderRegistry providerRegistry,
    IAuditLogger audit) : IBookingRefundService
{
    public async Task<Result<BookingRefundOutcome>> RefundIfEligibleAsync(
        Guid bookingId, Guid? actorUserId, CancellationToken ct)
    {
        var ctx = (DbContext)db;

        var booking = await ctx.Set<BookingAggregate>().AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null) return Result.Failure<BookingRefundOutcome>(Error.NotFound("Booking"));

        if (booking.Status is not (BookingStatus.Cancelled or BookingStatus.NoShow))
            return new BookingRefundOutcome(BookingRefundOutcomeKind.BookingNotCancelled);

        if (booking.RefundPercentage is not { } refundPercentage || refundPercentage <= 0)
            return new BookingRefundOutcome(BookingRefundOutcomeKind.NoRefundOwed);

        if (booking.PaymentId is null)
            return new BookingRefundOutcome(BookingRefundOutcomeKind.NoPayment);

        var payment = await ctx.Set<PaymentAggregate>()
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.Id == booking.PaymentId, ct);
        if (payment is null)
            return new BookingRefundOutcome(BookingRefundOutcomeKind.NoPayment);

        // Domain's Payment.AddRefund allows Captured/Settled/PartiallyRefunded. Checking it
        // here too (before ever calling the PSP) avoids firing a RefundAsync call for a
        // payment that was never captured (Initiated/Authorized) or already failed — the old
        // RefundPaymentCommandHandler called the PSP unconditionally and only found out via a
        // domain-validation failure afterward.
        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.Settled or PaymentStatus.PartiallyRefunded))
            return new BookingRefundOutcome(BookingRefundOutcomeKind.PaymentNotRefundable);

        var alreadyRefunded = payment.Refunds.Where(r => r.Status == RefundStatus.Succeeded).Sum(r => r.AmountMinor);
        var owedTotalMinor = payment.GrossAmountMinor * refundPercentage / 100;
        var amountMinor = owedTotalMinor - alreadyRefunded;
        if (amountMinor <= 0)
            return new BookingRefundOutcome(BookingRefundOutcomeKind.AlreadyFullyRefunded);

        var reason = MapRefundReason(booking.CancellationReason);

        var provider = providerRegistry.GetProvider(payment.Provider);
        var pspResult = await provider.RefundAsync(new RefundRequest(
            ProviderTransactionId: payment.ProviderTransactionId ?? string.Empty,
            AmountMinor: amountMinor,
            Reason: reason), ct);

        if (!pspResult.Succeeded)
            return Result.Failure<BookingRefundOutcome>(
                Error.Validation(pspResult.FailureReason ?? "Ödeme sağlayıcısında iade başarısız oldu."));

        var refundResult = payment.AddRefund(amountMinor, reason, pspResult.ProviderRefundId);
        if (refundResult.IsFailure) return Result.Failure<BookingRefundOutcome>(refundResult.Error);

        // EF Core, Include(p => p.Refunds) ile yüklenmiş koleksiyona domain metodunun eklediği
        // yeni child'ı Modified olarak track ediyor (aynı BarberReply bug'ı — bkz. ReviewActions.
        // ReplyToReviewCommandHandler). Explicit Added olmadan SaveChanges "refunds" tablosunda
        // olmayan bir satırı UPDATE etmeye çalışıp DbUpdateConcurrencyException fırlatır.
        ctx.Set<Refund>().Add(refundResult.Value);

        await audit.RecordAsync("payment.refunded", nameof(PaymentAggregate), payment.Id.ToString(),
            actorUserId: actorUserId,
            payload: new { amountMinor, reason = reason.ToString(), pspResult.ProviderRefundId, bookingId },
            ct: ct);

        return new BookingRefundOutcome(BookingRefundOutcomeKind.Refunded, refundResult.Value);
    }

    private static RefundReason MapRefundReason(BookingCancellationReason? reason) => reason switch
    {
        BookingCancellationReason.CustomerRequested => RefundReason.CustomerRequested,
        BookingCancellationReason.BarberRequested => RefundReason.BarberRequested,
        BookingCancellationReason.PaymentFailed => RefundReason.PaymentFailed,
        BookingCancellationReason.NoShow => RefundReason.NoShowRefund,
        BookingCancellationReason.AdminOverride => RefundReason.AdminOverride,
        BookingCancellationReason.PendingTimeout => RefundReason.AdminOverride,
        _ => RefundReason.AdminOverride,
    };
}

using Hangfire;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Payment;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// <summary>
/// Executes the actual PSP refund call for a just-cancelled booking. Enqueued by
/// BookingCancelledRefundHandler (Application layer) AFTER the cancel command's own
/// SaveChangesAsync has already committed — never called inline from the cancel command
/// itself, so a PSP outage can never fail or roll back the customer's/owner's/admin's
/// cancellation.
///
/// Idempotent: BookingRefundService re-derives the remaining owed amount from
/// Payment.Refunds on every call, so a Hangfire retry or a duplicate enqueue for a booking
/// that's already fully refunded is a safe no-op rather than a double refund.
///
/// Throwing lets Hangfire's [AutomaticRetry] do its job — a thrown exception here means the
/// PSP call (or a downstream domain/db failure) needs another attempt; a normal return means
/// either the refund succeeded or there was legitimately nothing to refund (never captured,
/// 0% owed, already refunded).
/// </summary>
public sealed class ProcessBookingRefundJob(
    IBookingRefundService refundService,
    IApplicationDbContext db,
    ILogger<ProcessBookingRefundJob> logger)
{
    [AutomaticRetry(Attempts = 5)]
    public async Task RunAsync(Guid bookingId, CancellationToken ct = default)
    {
        var result = await refundService.RefundIfEligibleAsync(bookingId, actorUserId: null, ct);
        if (result.IsFailure)
        {
            logger.LogError(
                "ProcessBookingRefundJob: refund attempt failed for booking {BookingId}: {Error}. Hangfire will retry.",
                bookingId, result.Error.Message);
            throw new InvalidOperationException($"Booking {bookingId} refund failed: {result.Error.Message}");
        }

        var outcome = result.Value;
        if (outcome.Kind == BookingRefundOutcomeKind.Refunded)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "ProcessBookingRefundJob: refunded {AmountMinor} kuruş for booking {BookingId} (refund {RefundId}).",
                outcome.Refund!.AmountMinor, bookingId, outcome.Refund.Id);
        }
        else
        {
            logger.LogInformation(
                "ProcessBookingRefundJob: no-op for booking {BookingId} (outcome: {Outcome}).",
                bookingId, outcome.Kind);
        }
    }
}

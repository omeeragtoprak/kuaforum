using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// Hangfire recurring job that auto-cancels Pending bookings that have sat unpaid too long.
/// Without this, a logged-in customer can open unlimited Pending bookings — each one blocks
/// the stylist's calendar via BookingBusyRangeQuery (Pending counts as busy) — and never pay,
/// freezing the stylist's schedule indefinitely. A free DoS vector, not just an edge case.
/// InitiateBookingCommand also caps concurrent Pending bookings per customer
/// (MaxActivePendingBookingsPerCustomer), which bounds one customer's blast radius, but this
/// job is what actually frees the slot back up for everyone else.
/// Cancel(PendingTimeout) computes a 100% refund percentage by convention, but since a Pending
/// booking never had a captured payment, the downstream refund job is a documented no-op — see
/// AutoRefundFlowTests.Cancel_of_never_completed_payment_produces_no_refund_and_no_error.
/// Runs every 15 minutes. Threshold: 30 minutes since CreatedAt (not ScheduledAt — a booking
/// made today for a slot 3 months out is still stale if unpaid for 30 minutes).
public sealed class ExpirePendingBookingsJob(
    IApplicationDbContext db,
    IAuditLogger audit,
    ILogger<ExpirePendingBookingsJob> logger)
{
    public const string JobId = "kuaforum.expire-pending-bookings";
    public const string CronExpression = "*/15 * * * *"; // every 15 min
    public static readonly TimeSpan PendingTimeout = TimeSpan.FromMinutes(30);

    public async Task RunAsync(CancellationToken ct = default)
    {
        var ctx = (DbContext)db;
        var threshold = DateTimeOffset.UtcNow - PendingTimeout;

        var stale = await ctx.Set<Booking>()
            .Where(b => b.Status == BookingStatus.Pending && b.CreatedAt < threshold)
            .ToListAsync(ct);

        if (stale.Count == 0)
        {
            logger.LogDebug("ExpirePendingBookingsJob: no stale Pending bookings.");
            return;
        }

        logger.LogInformation(
            "ExpirePendingBookingsJob: auto-cancelling {Count} Pending bookings older than {Timeout}.",
            stale.Count, PendingTimeout);

        foreach (var booking in stale)
        {
            var result = booking.Cancel(BookingCancellationReason.PendingTimeout);
            if (result.IsFailure)
            {
                logger.LogWarning("ExpirePendingBookingsJob: skip booking {BookingId} — {Error}.", booking.Id, result.Error.Message);
                continue;
            }

            await audit.RecordAsync(
                "booking.cancelled.pending_timeout",
                nameof(Booking),
                booking.Id.ToString(),
                actorUserId: null,
                payload: new { booking.CustomerId, booking.StylistId, booking.ScheduledAt, booking.CreatedAt },
                ct: ct);
        }

        await db.SaveChangesAsync(ct);
    }
}

using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// Hangfire recurring job that auto-cancels Pending bookings that have sat around too long.
/// Without this, a logged-in customer can open unlimited Pending bookings — each one blocks
/// the stylist's calendar via BookingBusyRangeQuery (Pending counts as busy) — and never get
/// resolved, freezing the stylist's schedule indefinitely. A free DoS vector, not just an edge
/// case. InitiateBookingCommand also caps concurrent Pending bookings per customer
/// (MaxActivePendingBookingsPerCustomer), which bounds one customer's blast radius, but this
/// job is what actually frees the slot back up for everyone else.
/// Cancel(PendingTimeout) computes a 100% refund percentage by convention. Whether that actually
/// pays anything back is entirely downstream of whether the booking has a captured payment:
/// BookingRefundService derives eligibility from booking.PaymentId, so a Pending booking whose
/// deposit was never paid produces a documented no-op refund — see
/// AutoRefundFlowTests.Cancel_of_never_completed_payment_produces_no_refund_and_no_error. Runs
/// every 15 minutes.
///
/// RFC-013 C4: per-booking dallanma. RFC-009's job used to branch on the GLOBAL
/// Payment:OnlinePaymentsEnabled flag alone — that stopped being enough once kapora became a
/// per-shop opt-in (RFC-013 C3): the same platform can have deposit-active and deposit-inactive
/// shops running side by side at the same instant, so a single global switch can't describe
/// every booking's threshold. Each booking instead carries its own
/// <see cref="Booking.RequiresOnlinePayment"/> snapshot (global flag AND shop's policy, captured
/// at Initiate time — see that property's XML doc), and the query below is the union of three
/// mutually exclusive cases:
///
///   (a) RequiresOnlinePayment &amp;&amp; DepositPaidAt == null &amp;&amp; CreatedAt is 30+ minutes old
///       — kapora bekleniyordu, hiç ödenmedi (ödeme terk edildi). Cancel(PendingTimeout); no
///       payment ever existed so the refund job is a no-op.
///   (b) DepositPaidAt != null &amp;&amp; ScheduledAt has passed
///       — kapora ÖDENDİ (RFC-013 C4: PaymentCompletionService.MarkPaymentReceived no longer
///       auto-confirms) but the owner never approved it before the appointment time. Cancel
///       (PendingTimeout); booking.PaymentId is set, so BookingCancelledEvent → the existing
///       auto-refund chain (BookingRefundService via ProcessBookingRefundJob) pays back 100%.
///   (c) !RequiresOnlinePayment &amp;&amp; ScheduledAt has passed
///       — no deposit was ever part of this booking's flow (RFC-009's original flag-off branch,
///       now per-booking); owner never confirmed by appointment time. Cancel(PendingTimeout); no
///       payment, no refund.
///
/// (a)/(c) together are exactly RFC-009's original two-branch behavior — a shop with the deposit
/// policy off, or the global flag off, produces bookings where RequiresOnlinePayment is always
/// false, so only (c) can ever fire for them; that's the same "wait until ScheduledAt" behavior
/// as before. (b) is the genuinely new case this RFC introduces.
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
        var now = DateTimeOffset.UtcNow;
        var unpaidStaleThreshold = now - PendingTimeout;

        var stale = await ctx.Set<Booking>().Where(b => b.Status == BookingStatus.Pending && (
                (b.RequiresOnlinePayment && b.DepositPaidAt == null && b.CreatedAt < unpaidStaleThreshold) ||
                (b.DepositPaidAt != null && b.ScheduledAt < now) ||
                (!b.RequiresOnlinePayment && b.ScheduledAt < now)))
            .ToListAsync(ct);

        if (stale.Count == 0)
        {
            logger.LogDebug("ExpirePendingBookingsJob: no stale Pending bookings.");
            return;
        }

        logger.LogInformation(
            "ExpirePendingBookingsJob: auto-cancelling {Count} Pending bookings.", stale.Count);

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
                payload: new
                {
                    booking.CustomerId, booking.StylistId, booking.ScheduledAt, booking.CreatedAt,
                    booking.RequiresOnlinePayment, wasDepositPaid = booking.DepositPaidAt is not null,
                },
                ct: ct);
        }

        await db.SaveChangesAsync(ct);
    }
}

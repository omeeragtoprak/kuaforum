using Kuaforum.Application.Common;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// Hangfire recurring job that auto-cancels Pending bookings that have sat around too long.
/// Without this, a logged-in customer can open unlimited Pending bookings — each one blocks
/// the stylist's calendar via BookingBusyRangeQuery (Pending counts as busy) — and never get
/// resolved, freezing the stylist's schedule indefinitely. A free DoS vector, not just an edge
/// case. InitiateBookingCommand also caps concurrent Pending bookings per customer
/// (MaxActivePendingBookingsPerCustomer), which bounds one customer's blast radius, but this
/// job is what actually frees the slot back up for everyone else.
/// Cancel(PendingTimeout) computes a 100% refund percentage by convention, but since a Pending
/// booking never had a captured payment, the downstream refund job is a documented no-op — see
/// AutoRefundFlowTests.Cancel_of_never_completed_payment_produces_no_refund_and_no_error.
/// Runs every 15 minutes.
///
/// RFC-009 ücretsiz büyüme modu — threshold, <see cref="PaymentOptions.OnlinePaymentsEnabled"/>'e
/// göre dallanır:
///   - Flag AÇIK: Pending = "ödeme bekleniyor". Threshold: 30 dakika since CreatedAt (not
///     ScheduledAt — a booking made today for a slot 3 months out is still stale if unpaid for
///     30 minutes). Mevcut RFC-006 davranışı, değişmedi.
///   - Flag KAPALI: hiç online ödeme başlamıyor (InitiatePaymentCommand 409 döner) — Pending
///     artık "kuaför onayı bekleniyor" anlamına gelir ve bir kuaförün telefonuna bakıp
///     onaylaması dakikalar değil saatler sürebilir. 30 dakikalık eşik burada uygulanırsa flag
///     kapalıyken oluşan HER randevu 30 dk sonra sessizce iptal olur — gerçek bir regresyon.
///     Founder kararı (2026-08-05, RFC-009 "Açık soru → Karar"): ayrı bir
///     `Booking:OwnerConfirmTimeout` config'i EKLENMEDİ (YAGNI — ek config yükü). Onay penceresi
///     doğal deadline'ı kullanır: randevu saati (ScheduledAt) geçmiş hâlâ Pending olan randevular
///     iptal edilir — "kuaför saatine kadar onaylamadı" → no-show riskine düşmeden temizlenir.
public sealed class ExpirePendingBookingsJob(
    IApplicationDbContext db,
    IAuditLogger audit,
    IOptions<PaymentOptions> paymentOptions,
    ILogger<ExpirePendingBookingsJob> logger)
{
    public const string JobId = "kuaforum.expire-pending-bookings";
    public const string CronExpression = "*/15 * * * *"; // every 15 min
    public static readonly TimeSpan PendingTimeout = TimeSpan.FromMinutes(30);

    public async Task RunAsync(CancellationToken ct = default)
    {
        var ctx = (DbContext)db;
        var now = DateTimeOffset.UtcNow;
        var onlinePaymentsEnabled = paymentOptions.Value.OnlinePaymentsEnabled;

        // Flag açık → ödeme-bekleniyor varsayımı: CreatedAt bazlı 30dk eşik.
        // Flag kapalı → onay-bekleniyor varsayımı: randevu saati geçmiş Pending'ler temizlenir.
        var query = onlinePaymentsEnabled
            ? ctx.Set<Booking>().Where(b => b.Status == BookingStatus.Pending && b.CreatedAt < now - PendingTimeout)
            : ctx.Set<Booking>().Where(b => b.Status == BookingStatus.Pending && b.ScheduledAt < now);

        var stale = await query.ToListAsync(ct);

        if (stale.Count == 0)
        {
            logger.LogDebug("ExpirePendingBookingsJob: no stale Pending bookings.");
            return;
        }

        logger.LogInformation(
            "ExpirePendingBookingsJob: auto-cancelling {Count} Pending bookings (OnlinePaymentsEnabled={OnlinePaymentsEnabled}).",
            stale.Count, onlinePaymentsEnabled);

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

namespace Kuaforum.Application.Common.Interfaces;

/// <summary>
/// Post-commit hook that schedules the actual PSP refund for a just-cancelled booking.
/// Deliberately NOT invoked inline inside the cancel command handlers — a PSP outage must
/// never fail or roll back the customer's cancellation itself. See
/// Kuaforum.Application.Modules.Payment.BookingCancelledRefundHandler (the domain-event
/// subscriber that calls this once Booking.Cancel()'s SaveChangesAsync has committed — see
/// ApplicationDbContext.SaveChangesAsync, domain events publish AFTER the base SaveChanges
/// call) and Infrastructure.BackgroundJobs.ProcessBookingRefundJob (the actual worker,
/// Hangfire-retried on failure).
///
/// In dev/test environments where Hangfire is disabled (Hangfire:Enabled=false — see
/// IntegrationTestFactory, which disables it to avoid an eager Postgres connection outside
/// Testcontainers), this resolves to a no-op logging stub so DI doesn't break. Tests that need
/// to exercise the refund flow instantiate ProcessBookingRefundJob directly instead of relying
/// on an actual Hangfire dispatch.
/// </summary>
public interface IBookingRefundJobEnqueuer
{
    void EnqueueRefund(Guid bookingId);
}

using Hangfire;
using Kuaforum.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// <summary>
/// Production/dev path — Hangfire is enabled (services.AddHangfire ran, so IBackgroundJobClient
/// is in the container), so actually schedule ProcessBookingRefundJob.
/// </summary>
public sealed class HangfireBookingRefundJobEnqueuer(IBackgroundJobClient client) : IBookingRefundJobEnqueuer
{
    public void EnqueueRefund(Guid bookingId) =>
        client.Enqueue<ProcessBookingRefundJob>(job => job.RunAsync(bookingId, CancellationToken.None));
}

/// <summary>
/// Registered when Hangfire:Enabled=false (integration tests — see IntegrationTestFactory,
/// which disables Hangfire to avoid UsePostgreSqlStorage's eager connection racing the
/// Testcontainers Postgres instance). Hangfire's IBackgroundJobClient isn't in the container in
/// that mode, so cancel-flow code that depends on IBookingRefundJobEnqueuer would fail DI
/// resolution without a stand-in. This is that stand-in: it logs and does nothing. Tests that
/// need to exercise the actual refund need to instantiate ProcessBookingRefundJob directly and
/// call RunAsync — that's the documented, legitimate way to test this path (see
/// RefundFlowTests/CancellationFlowTests).
/// </summary>
public sealed class NullBookingRefundJobEnqueuer(ILogger<NullBookingRefundJobEnqueuer> logger) : IBookingRefundJobEnqueuer
{
    public void EnqueueRefund(Guid bookingId) =>
        logger.LogWarning(
            "NullBookingRefundJobEnqueuer: Hangfire disabled — refund job for booking {BookingId} was NOT enqueued.",
            bookingId);
}

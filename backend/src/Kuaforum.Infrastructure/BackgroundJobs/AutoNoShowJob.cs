using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// Hangfire recurring job that auto-marks Confirmed bookings as NoShow when
/// their scheduled window plus a 30-min grace has elapsed without an owner
/// action (Start/Complete/Cancel). Started bookings are left alone (kuaför
/// is mid-service). Owner can still manually mark a booking earlier; this is
/// the safety net for forgotten ones. Audit log retained per row.
/// Runs every 30 minutes.
public sealed class AutoNoShowJob(
    IApplicationDbContext db,
    IAuditLogger audit,
    ILogger<AutoNoShowJob> logger)
{
    public const string JobId = "kuaforum.auto-no-show";
    public const string CronExpression = "*/30 * * * *"; // every 30 min
    private static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(30);

    public async Task RunAsync(CancellationToken ct = default)
    {
        var ctx = (DbContext)db;
        var threshold = DateTimeOffset.UtcNow - GracePeriod;

        var stale = await ctx.Set<Booking>()
            .Where(b => b.Status == BookingStatus.Confirmed && b.ScheduledEndsAt < threshold)
            .ToListAsync(ct);

        if (stale.Count == 0)
        {
            logger.LogDebug("AutoNoShowJob: no Confirmed bookings overdue.");
            return;
        }

        logger.LogInformation("AutoNoShowJob: auto-marking {Count} bookings as NoShow.", stale.Count);

        foreach (var booking in stale)
        {
            var result = booking.MarkNoShow();
            if (result.IsFailure)
            {
                logger.LogWarning("AutoNoShowJob: skip booking {BookingId} — {Error}.", booking.Id, result.Error.Message);
                continue;
            }

            await audit.RecordAsync(
                "booking.no_show.auto",
                nameof(Booking),
                booking.Id.ToString(),
                actorUserId: null,
                payload: new { booking.CustomerId, booking.StylistId, booking.ScheduledEndsAt },
                ct: ct);
        }

        await db.SaveChangesAsync(ct);
    }
}

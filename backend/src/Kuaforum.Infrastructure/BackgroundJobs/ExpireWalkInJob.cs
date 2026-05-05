using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Barber;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// Hangfire recurring job that auto-disables stylist "şu an müsait" walk-in
/// flags after MaxWalkInAge (default 2h). Owner toggles walk-in at 14:00 and
/// forgets to toggle off → customers see "şu an müsait" at 22:00 and walk
/// to a closed shop. Without this job, stale walk-in promises break trust.
/// Runs every 30 minutes. Audit log retained.
public sealed class ExpireWalkInJob(
    IApplicationDbContext db,
    IAuditLogger audit,
    ILogger<ExpireWalkInJob> logger)
{
    public const string JobId = "kuaforum.expire-walkin";
    public const string CronExpression = "*/30 * * * *"; // every 30 min
    private static readonly TimeSpan MaxWalkInAge = TimeSpan.FromHours(2);

    public async Task RunAsync(CancellationToken ct = default)
    {
        var ctx = (DbContext)db;
        var threshold = DateTimeOffset.UtcNow - MaxWalkInAge;

        var stale = await ctx.Set<Stylist>()
            .Where(s => s.WalkInAvailable && s.WalkInToggledAt != null && s.WalkInToggledAt < threshold)
            .ToListAsync(ct);

        if (stale.Count == 0)
        {
            logger.LogDebug("ExpireWalkInJob: no stylists due for walk-in expiry.");
            return;
        }

        logger.LogInformation("ExpireWalkInJob: auto-disabling walk-in for {Count} stylists older than {MaxAge}.", stale.Count, MaxWalkInAge);

        foreach (var stylist in stale)
        {
            stylist.ToggleWalkIn(false);
            await audit.RecordAsync(
                "barber.stylist.walkin.auto-expired",
                nameof(Stylist),
                stylist.Id.ToString(),
                actorUserId: null,
                payload: new { stylist.BarberShopId, toggledAt = stylist.WalkInToggledAt },
                ct: ct);
        }

        await db.SaveChangesAsync(ct);
    }
}

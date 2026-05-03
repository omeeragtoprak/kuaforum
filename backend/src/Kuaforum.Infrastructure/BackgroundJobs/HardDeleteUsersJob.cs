using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire recurring job that hard-deletes (anonymizes) users whose soft-delete
/// window (30 days) has elapsed. Audit log retained per KVKK m.7.
/// Runs daily at 03:00 Europe/Istanbul.
/// </summary>
public sealed class HardDeleteUsersJob(
    IApplicationDbContext db,
    IAuditLogger audit,
    ILogger<HardDeleteUsersJob> logger)
{
    public const string JobId = "kuaforum.hard-delete-users";
    public const string CronExpression = "0 3 * * *"; // 03:00 daily

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var ctx = (DbContext)db;
        var threshold = DateTimeOffset.UtcNow.AddDays(-30);

        var dueUsers = await ctx.Set<User>()
            .Where(u => u.DeletedAt != null && u.DeletedAt < threshold)
            .ToListAsync(cancellationToken);

        if (dueUsers.Count == 0)
        {
            logger.LogInformation("HardDeleteUsersJob: no users due for hard delete.");
            return;
        }

        logger.LogInformation("HardDeleteUsersJob: anonymizing {Count} users.", dueUsers.Count);

        foreach (var user in dueUsers)
        {
            user.AnonymizeForHardDelete();
            await audit.RecordAsync(
                "user.delete.hard",
                nameof(User),
                user.Id.ToString(),
                actorUserId: null,
                payload: new { hardDeletedAt = DateTimeOffset.UtcNow },
                ct: cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("HardDeleteUsersJob: completed.");
    }
}

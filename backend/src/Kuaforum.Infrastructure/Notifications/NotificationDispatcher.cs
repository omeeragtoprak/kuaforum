using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Notification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Notifications;

public sealed class NotificationDispatcher(
    IEnumerable<INotificationChannelHandler> handlers,
    IApplicationDbContext db,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    private readonly Dictionary<NotificationChannel, INotificationChannelHandler> _byChannel =
        handlers.ToDictionary(h => h.Channel);

    public async Task<NotificationDeliveryResult> DispatchAsync(NotificationRequest request, CancellationToken ct = default)
    {
        var ctx = (DbContext)db;

        var log = NotificationLog.Create(
            request.UserId, request.Channel, request.TemplateKey,
            request.Recipient, request.Subject, request.Body);
        ctx.Set<NotificationLog>().Add(log);

        if (!_byChannel.TryGetValue(request.Channel, out var handler))
        {
            log.MarkFailed($"No handler for channel {request.Channel}.");
            await db.SaveChangesAsync(ct);
            return new NotificationDeliveryResult(false, null, $"Unsupported channel: {request.Channel}");
        }

        try
        {
            var result = await handler.SendAsync(request, ct);
            if (result.Succeeded)
                log.MarkSent(result.ProviderMessageId);
            else
                log.MarkFailed(result.FailureReason ?? "unknown");

            await db.SaveChangesAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Notification dispatch failed for channel {Channel}.", request.Channel);
            log.MarkFailed(ex.Message);
            await db.SaveChangesAsync(ct);
            return new NotificationDeliveryResult(false, null, ex.Message);
        }
    }
}

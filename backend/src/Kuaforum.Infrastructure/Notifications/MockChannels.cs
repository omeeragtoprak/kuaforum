using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Notification;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Notifications;

/// <summary>
/// Dev-only stubs that log instead of actually sending. Replace with FCM /
/// NetGSM / Postmark adapters in staging/production.
/// </summary>
public sealed class MockPushChannel(ILogger<MockPushChannel> logger) : INotificationChannelHandler
{
    public NotificationChannel Channel => NotificationChannel.Push;

    public Task<NotificationDeliveryResult> SendAsync(NotificationRequest request, CancellationToken ct = default)
    {
        logger.LogInformation("MOCK PUSH → user={UserId} title='{Subject}' body='{Body}' (recipient={Recipient}, template={Tpl})",
            request.UserId, request.Subject, request.Body, request.Recipient, request.TemplateKey);
        return Task.FromResult(new NotificationDeliveryResult(true, $"mock-push-{Guid.NewGuid():N}", null));
    }
}

public sealed class MockSmsChannel(ILogger<MockSmsChannel> logger) : INotificationChannelHandler
{
    public NotificationChannel Channel => NotificationChannel.Sms;

    public Task<NotificationDeliveryResult> SendAsync(NotificationRequest request, CancellationToken ct = default)
    {
        logger.LogInformation("MOCK SMS → {Recipient}: {Body} (template={Tpl})",
            request.Recipient, request.Body, request.TemplateKey);
        return Task.FromResult(new NotificationDeliveryResult(true, $"mock-sms-{Guid.NewGuid():N}", null));
    }
}

public sealed class MockEmailChannel(ILogger<MockEmailChannel> logger) : INotificationChannelHandler
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public Task<NotificationDeliveryResult> SendAsync(NotificationRequest request, CancellationToken ct = default)
    {
        logger.LogInformation("MOCK EMAIL → {Recipient} subject='{Subject}' body='{Body}' (template={Tpl})",
            request.Recipient, request.Subject, request.Body, request.TemplateKey);
        return Task.FromResult(new NotificationDeliveryResult(true, $"mock-email-{Guid.NewGuid():N}", null));
    }
}

public sealed class InAppNotificationChannel(ILogger<InAppNotificationChannel> logger) : INotificationChannelHandler
{
    public NotificationChannel Channel => NotificationChannel.InApp;

    public Task<NotificationDeliveryResult> SendAsync(NotificationRequest request, CancellationToken ct = default)
    {
        // InApp = persisted to NotificationLog only (no external send). Caller queries this list.
        logger.LogDebug("InApp notification recorded for user {UserId}: {Subject}", request.UserId, request.Subject);
        return Task.FromResult(new NotificationDeliveryResult(true, null, null));
    }
}

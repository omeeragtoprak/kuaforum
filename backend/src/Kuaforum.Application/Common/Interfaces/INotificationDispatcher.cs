using Kuaforum.Domain.Modules.Notification;

namespace Kuaforum.Application.Common.Interfaces;

public sealed record NotificationRequest(
    Guid? UserId,
    NotificationChannel Channel,
    string TemplateKey,
    string Recipient,
    string Subject,
    string Body);

public sealed record NotificationDeliveryResult(
    bool Succeeded,
    string? ProviderMessageId,
    string? FailureReason);

/// <summary>
/// Sends a notification through the appropriate channel implementation.
/// </summary>
public interface INotificationDispatcher
{
    Task<NotificationDeliveryResult> DispatchAsync(NotificationRequest request, CancellationToken ct = default);
}

/// <summary>
/// Per-channel transport implementation (FCM, NetGSM, Postmark, in-app).
/// </summary>
public interface INotificationChannelHandler
{
    NotificationChannel Channel { get; }
    Task<NotificationDeliveryResult> SendAsync(NotificationRequest request, CancellationToken ct = default);
}

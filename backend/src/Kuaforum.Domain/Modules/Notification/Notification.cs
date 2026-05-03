using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Notification;

public enum NotificationChannel
{
    Push = 0,
    Sms = 1,
    Email = 2,
    InApp = 3,
}

public enum NotificationStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
}

public sealed class NotificationLog : Entity
{
    private NotificationLog() { }

    public Guid? UserId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public string TemplateKey { get; private set; } = null!;
    public string Recipient { get; private set; } = null!; // phone, email, fcm token
    public string Subject { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public NotificationStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }

    public static NotificationLog Create(
        Guid? userId,
        NotificationChannel channel,
        string templateKey,
        string recipient,
        string subject,
        string body)
    {
        return new NotificationLog
        {
            UserId = userId,
            Channel = channel,
            TemplateKey = templateKey,
            Recipient = recipient,
            Subject = subject,
            Body = body,
            Status = NotificationStatus.Pending,
        };
    }

    public void MarkSent(string? providerMessageId)
    {
        Status = NotificationStatus.Sent;
        SentAt = DateTimeOffset.UtcNow;
        ProviderMessageId = providerMessageId;
        Touch();
    }

    public void MarkFailed(string reason)
    {
        Status = NotificationStatus.Failed;
        FailedAt = DateTimeOffset.UtcNow;
        FailureReason = reason;
        Touch();
    }
}

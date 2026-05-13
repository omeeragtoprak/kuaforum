using Kuaforum.Domain.Modules.Notification;
using Xunit;

namespace Kuaforum.Tests.Domain.Notification;

public class NotificationLogTests
{
    private static NotificationLog Create(NotificationChannel channel = NotificationChannel.InApp) =>
        NotificationLog.Create(
            userId: Guid.NewGuid(),
            channel: channel,
            templateKey: "booking.confirmed.customer",
            recipient: "user@example.com",
            subject: "Randevun onaylandı",
            body: "Randevun onaylandı. Görüşmek üzere.",
            relatedBookingId: Guid.NewGuid());

    [Fact]
    public void Create_HappyPath_DefaultsPendingAndUnread()
    {
        var n = Create();
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.False(n.IsRead);
        Assert.Null(n.ReadAt);
        Assert.Null(n.SentAt);
        Assert.Null(n.FailedAt);
        Assert.NotNull(n.RelatedBookingId);
    }

    [Fact]
    public void MarkRead_FirstCall_StampsReadAt()
    {
        var n = Create();
        n.MarkRead();
        Assert.True(n.IsRead);
        Assert.NotNull(n.ReadAt);
    }

    [Fact]
    public void MarkRead_Twice_IdempotentDoesNotChangeReadAt()
    {
        var n = Create();
        n.MarkRead();
        var firstReadAt = n.ReadAt;
        Thread.Sleep(2);
        n.MarkRead();
        Assert.Equal(firstReadAt, n.ReadAt);
    }

    [Fact]
    public void MarkSent_TransitionsToSentWithProviderId()
    {
        var n = Create();
        n.MarkSent("provider-msg-123");
        Assert.Equal(NotificationStatus.Sent, n.Status);
        Assert.NotNull(n.SentAt);
        Assert.Equal("provider-msg-123", n.ProviderMessageId);
    }

    [Fact]
    public void MarkSent_NullProviderId_AllowedForInApp()
    {
        var n = Create(NotificationChannel.InApp);
        n.MarkSent(null);
        Assert.Equal(NotificationStatus.Sent, n.Status);
        Assert.Null(n.ProviderMessageId);
    }

    [Fact]
    public void MarkFailed_TransitionsToFailedWithReason()
    {
        var n = Create();
        n.MarkFailed("FCM: invalid token");
        Assert.Equal(NotificationStatus.Failed, n.Status);
        Assert.Equal("FCM: invalid token", n.FailureReason);
        Assert.NotNull(n.FailedAt);
    }

    [Fact]
    public void Status_TransitionFromSent_To_FailedIsAllowedForRecording()
    {
        // Domain explicit invariant yok — provider tarafından geç gelen başarısızlık
        // bildirimi MarkFailed çağrısı ile state'i Failed yapabilir.
        var n = Create();
        n.MarkSent("ok");
        n.MarkFailed("Provider returned bounce after delivery");
        Assert.Equal(NotificationStatus.Failed, n.Status);
    }
}

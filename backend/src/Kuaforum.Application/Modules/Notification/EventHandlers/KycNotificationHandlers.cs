using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Barber.Events;
using Kuaforum.Domain.Modules.Notification;
using MediatR;

namespace Kuaforum.Application.Modules.Notification.EventHandlers;

/// <summary>
/// KYC sonucu owner'a bildirilir. KycSubmission zaten KycApprovedEvent/KycRejectedEvent
/// raise ediyordu (bkz. KycSubmission.Approve/Reject) ama hiçbir handler abone
/// değildi — mobile "onaylandığında bildirim gelir" vaat ediyor, bu iki handler
/// olmadan hiç gitmiyordu. BookingNotificationHandlers ile aynı kalıp: NotificationLog
/// (in-app feed) + push. Owner'ın UserId'sine gider (event.OwnerUserId == shop.OwnerId).
/// </summary>
public sealed class KycApprovedNotificationHandler(
    INotificationDispatcher dispatcher) : INotificationHandler<KycApprovedEvent>
{
    public async Task Handle(KycApprovedEvent notification, CancellationToken cancellationToken)
    {
        const string subject = "KYC başvurun onaylandı";
        const string body = "İşletmen artık aktif! Müşteriler seni arayıp randevu alabilir.";

        await NotificationHelpers.DispatchInAppAsync(dispatcher, notification.OwnerUserId,
            "kyc.approved", subject, body, cancellationToken);

        await dispatcher.DispatchAsync(new NotificationRequest(
            UserId: notification.OwnerUserId,
            Channel: NotificationChannel.Push,
            TemplateKey: "kyc.approved",
            Recipient: $"fcm-token-{notification.OwnerUserId:N}",
            Subject: subject,
            Body: body), cancellationToken);
    }
}

public sealed class KycRejectedNotificationHandler(
    INotificationDispatcher dispatcher) : INotificationHandler<KycRejectedEvent>
{
    public async Task Handle(KycRejectedEvent notification, CancellationToken cancellationToken)
    {
        const string subject = "KYC başvurun reddedildi";
        var body = $"Sebep: {notification.Reason}. Belgeleri düzenleyip yeniden başvurabilirsin.";

        await NotificationHelpers.DispatchInAppAsync(dispatcher, notification.OwnerUserId,
            "kyc.rejected", subject, body, cancellationToken);

        await dispatcher.DispatchAsync(new NotificationRequest(
            UserId: notification.OwnerUserId,
            Channel: NotificationChannel.Push,
            TemplateKey: "kyc.rejected",
            Recipient: $"fcm-token-{notification.OwnerUserId:N}",
            Subject: subject,
            Body: body), cancellationToken);
    }
}

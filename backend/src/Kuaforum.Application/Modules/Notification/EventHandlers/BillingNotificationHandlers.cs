using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing.Events;
using Kuaforum.Domain.Modules.Notification;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Notification.EventHandlers;

/// <summary>
/// RFC-012 (RFC-014 ile abonelik faturasına genelleştirildi): fatura bildirimi ve askıya alma
/// bildirimleri. KycNotificationHandlers / BookingNotificationHandlers ile aynı desen —
/// MediatR domain-event handler, in-process, ApplicationDbContext.SaveChangesAsync sonrası
/// publish edilir. Event yalnız BarberShopId taşıdığı için OwnerId burada DB'den çözülür
/// (BookingConfirmedNotificationHandler presedenti).
/// </summary>
public sealed class SubscriptionInvoiceNotifiedNotificationHandler(
    IApplicationDbContext db,
    INotificationDispatcher dispatcher) : INotificationHandler<SubscriptionInvoiceNotifiedEvent>
{
    public async Task Handle(SubscriptionInvoiceNotifiedEvent notification, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.BarberShopId, cancellationToken);
        if (shop is null) return;

        var amountTl = notification.AmountMinor / 100m;
        var subject = "Abonelik faturanız oluştu";
        var body = $"{notification.PeriodStart:MMMM yyyy} dönemi abonelik faturanız: {amountTl:N2} TL. Son ödeme: {notification.DueDate:dd.MM.yyyy}.";

        await NotificationHelpers.DispatchInAppAsync(dispatcher, shop.OwnerId,
            "subscription_invoice.notified", subject, body, cancellationToken);

        await dispatcher.DispatchAsync(new NotificationRequest(
            UserId: shop.OwnerId,
            Channel: NotificationChannel.Push,
            TemplateKey: "subscription_invoice.notified",
            Recipient: $"fcm-token-{shop.OwnerId:N}",
            Subject: subject,
            Body: body), cancellationToken);
    }
}

public sealed class SubscriptionInvoiceSuspendedNotificationHandler(
    IApplicationDbContext db,
    INotificationDispatcher dispatcher) : INotificationHandler<SubscriptionInvoiceSuspendedEvent>
{
    public async Task Handle(SubscriptionInvoiceSuspendedEvent notification, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.BarberShopId, cancellationToken);
        if (shop is null) return;

        const string subject = "Hesabınız askıya alındı";
        const string body = "Hesabınız ödenmemiş abonelik borcu nedeniyle askıya alındı. Borcunuzu öderseniz destek ekibi hesabınızı yeniden aktive edebilir.";

        await NotificationHelpers.DispatchInAppAsync(dispatcher, shop.OwnerId,
            "subscription_invoice.suspended", subject, body, cancellationToken);

        await dispatcher.DispatchAsync(new NotificationRequest(
            UserId: shop.OwnerId,
            Channel: NotificationChannel.Push,
            TemplateKey: "subscription_invoice.suspended",
            Recipient: $"fcm-token-{shop.OwnerId:N}",
            Subject: subject,
            Body: body), cancellationToken);
    }
}

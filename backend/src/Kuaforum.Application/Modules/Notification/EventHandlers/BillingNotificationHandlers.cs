using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing.Events;
using Kuaforum.Domain.Modules.Notification;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Notification.EventHandlers;

/// <summary>
/// RFC-012: statement kapanışı ve askıya alma bildirimleri. KycNotificationHandlers /
/// BookingNotificationHandlers ile aynı desen — MediatR domain-event handler, in-process,
/// ApplicationDbContext.SaveChangesAsync sonrası publish edilir. Event yalnız BarberShopId
/// taşıdığı için OwnerId burada DB'den çözülür (BookingConfirmedNotificationHandler presedenti).
/// </summary>
public sealed class CommissionStatementNotifiedNotificationHandler(
    IApplicationDbContext db,
    INotificationDispatcher dispatcher) : INotificationHandler<CommissionStatementNotifiedEvent>
{
    public async Task Handle(CommissionStatementNotifiedEvent notification, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.BarberShopId, cancellationToken);
        if (shop is null) return;

        var amountTl = notification.AmountDueMinor / 100m;
        var subject = "Komisyon borcunuz oluştu";
        var body = $"{notification.PeriodStart:MMMM yyyy} dönemi komisyon borcunuz: {amountTl:N2} TL. Son ödeme: {notification.DueDate:dd.MM.yyyy}.";

        await NotificationHelpers.DispatchInAppAsync(dispatcher, shop.OwnerId,
            "commission_statement.notified", subject, body, cancellationToken);

        await dispatcher.DispatchAsync(new NotificationRequest(
            UserId: shop.OwnerId,
            Channel: NotificationChannel.Push,
            TemplateKey: "commission_statement.notified",
            Recipient: $"fcm-token-{shop.OwnerId:N}",
            Subject: subject,
            Body: body), cancellationToken);
    }
}

public sealed class CommissionStatementSuspendedNotificationHandler(
    IApplicationDbContext db,
    INotificationDispatcher dispatcher) : INotificationHandler<CommissionStatementSuspendedEvent>
{
    public async Task Handle(CommissionStatementSuspendedEvent notification, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.BarberShopId, cancellationToken);
        if (shop is null) return;

        const string subject = "Hesabınız askıya alındı";
        const string body = "Hesabınız ödenmemiş komisyon borcu nedeniyle askıya alındı. Borcunuzu öderseniz destek ekibi hesabınızı yeniden aktive edebilir.";

        await NotificationHelpers.DispatchInAppAsync(dispatcher, shop.OwnerId,
            "commission_statement.suspended", subject, body, cancellationToken);

        await dispatcher.DispatchAsync(new NotificationRequest(
            UserId: shop.OwnerId,
            Channel: NotificationChannel.Push,
            TemplateKey: "commission_statement.suspended",
            Recipient: $"fcm-token-{shop.OwnerId:N}",
            Subject: subject,
            Body: body), cancellationToken);
    }
}

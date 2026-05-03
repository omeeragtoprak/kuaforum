using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Booking.Events;
using Kuaforum.Domain.Modules.Notification;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Notification.EventHandlers;

/// <summary>
/// Sends customer + barber confirmations when a booking is paid.
/// In-process MediatR notification — runs after Booking.Confirm() SaveChanges.
/// Production-ready version moves these to Outbox for at-least-once delivery.
/// </summary>
public sealed class BookingConfirmedNotificationHandler(
    IApplicationDbContext db,
    INotificationDispatcher dispatcher) : INotificationHandler<BookingConfirmedEvent>
{
    public async Task Handle(BookingConfirmedEvent notification, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var booking = await ctx.Set<Domain.Modules.Booking.Booking>().AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == notification.BookingId, cancellationToken);
        if (booking is null) return;

        var customer = await ctx.Set<Domain.Modules.Identity.User>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == booking.CustomerId, cancellationToken);

        var shop = await ctx.Set<Domain.Modules.Barber.BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == booking.BarberShopId, cancellationToken);

        var stylist = await ctx.Set<Domain.Modules.Barber.Stylist>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == booking.StylistId, cancellationToken);

        if (customer is not null)
        {
            var localTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(booking.ScheduledAt.UtcDateTime, "Europe/Istanbul");
            var subject = "Randevun onaylandı";
            var body = $"{shop?.Name ?? "Kuaför"} — {stylist?.DisplayName ?? "stilist"} ile {localTime:dd MMM HH:mm} randevun hazır.";

            // Push notification (mock log)
            await dispatcher.DispatchAsync(new NotificationRequest(
                UserId: customer.Id,
                Channel: NotificationChannel.Push,
                TemplateKey: "booking.confirmed.customer",
                Recipient: $"fcm-token-{customer.Id:N}", // production'da gerçek FCM token
                Subject: subject,
                Body: body), cancellationToken);

            // SMS fallback (kritik bildirim)
            if (!string.IsNullOrEmpty(customer.PhoneE164))
            {
                await dispatcher.DispatchAsync(new NotificationRequest(
                    UserId: customer.Id,
                    Channel: NotificationChannel.Sms,
                    TemplateKey: "booking.confirmed.customer.sms",
                    Recipient: customer.PhoneE164,
                    Subject: subject,
                    Body: body), cancellationToken);
            }
        }

        // Notify barber owner too
        if (shop is not null)
        {
            var owner = await ctx.Set<Domain.Modules.Identity.User>().AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == shop.OwnerId, cancellationToken);
            if (owner is not null)
            {
                var localTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(booking.ScheduledAt.UtcDateTime, "Europe/Istanbul");
                await dispatcher.DispatchAsync(new NotificationRequest(
                    UserId: owner.Id,
                    Channel: NotificationChannel.Push,
                    TemplateKey: "booking.confirmed.barber",
                    Recipient: $"fcm-token-{owner.Id:N}",
                    Subject: "Yeni randevu",
                    Body: $"{stylist?.DisplayName ?? "Stilist"} için {localTime:dd MMM HH:mm} yeni randevu."),
                    cancellationToken);
            }
        }
    }
}

public sealed class BookingCancelledNotificationHandler(
    IApplicationDbContext db,
    INotificationDispatcher dispatcher) : INotificationHandler<BookingCancelledEvent>
{
    public async Task Handle(BookingCancelledEvent notification, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var booking = await ctx.Set<Domain.Modules.Booking.Booking>().AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == notification.BookingId, cancellationToken);
        if (booking is null) return;

        var customer = await ctx.Set<Domain.Modules.Identity.User>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == booking.CustomerId, cancellationToken);
        if (customer is null) return;

        var refundText = notification.RefundPercentage > 0
            ? $"%{notification.RefundPercentage} iade işleme alındı."
            : "İade hakkı yok (geç iptal).";

        await dispatcher.DispatchAsync(new NotificationRequest(
            UserId: customer.Id,
            Channel: NotificationChannel.Push,
            TemplateKey: "booking.cancelled",
            Recipient: $"fcm-token-{customer.Id:N}",
            Subject: "Randevu iptal edildi",
            Body: refundText), cancellationToken);
    }
}

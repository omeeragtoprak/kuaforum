using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Booking.Events;
using Kuaforum.Domain.Modules.Notification;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Notification.EventHandlers;

internal static class NotificationHelpers
{
    public static string LocalTr(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc.UtcDateTime, "Europe/Istanbul").ToString("dd MMM HH:mm");

    public static async Task DispatchInAppAsync(
        INotificationDispatcher dispatcher,
        Guid userId,
        string templateKey,
        string subject,
        string body,
        CancellationToken ct,
        Guid? relatedBookingId = null)
    {
        await dispatcher.DispatchAsync(new NotificationRequest(
            UserId: userId,
            Channel: NotificationChannel.InApp,
            TemplateKey: templateKey,
            Recipient: $"user:{userId:N}",
            Subject: subject,
            Body: body,
            RelatedBookingId: relatedBookingId), ct);
    }
}

/// <summary>
/// Sends customer + barber confirmations when a booking is paid or owner-confirmed.
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

        var localTime = NotificationHelpers.LocalTr(booking.ScheduledAt);

        if (customer is not null)
        {
            var subject = "Randevun onaylandı";
            var body = $"{shop?.Name ?? "Kuaför"} — {stylist?.DisplayName ?? "stilist"} ile {localTime} randevun hazır.";

            await NotificationHelpers.DispatchInAppAsync(dispatcher, customer.Id,
                "booking.confirmed.customer", subject, body, cancellationToken,
                relatedBookingId: booking.Id);

            await dispatcher.DispatchAsync(new NotificationRequest(
                UserId: customer.Id,
                Channel: NotificationChannel.Push,
                TemplateKey: "booking.confirmed.customer",
                Recipient: $"fcm-token-{customer.Id:N}",
                Subject: subject,
                Body: body,
                RelatedBookingId: booking.Id), cancellationToken);

            if (!string.IsNullOrEmpty(customer.PhoneE164))
            {
                await dispatcher.DispatchAsync(new NotificationRequest(
                    UserId: customer.Id,
                    Channel: NotificationChannel.Sms,
                    TemplateKey: "booking.confirmed.customer.sms",
                    Recipient: customer.PhoneE164,
                    Subject: subject,
                    Body: body,
                    RelatedBookingId: booking.Id), cancellationToken);
            }
        }

        if (shop is not null)
        {
            var owner = await ctx.Set<Domain.Modules.Identity.User>().AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == shop.OwnerId, cancellationToken);
            if (owner is not null)
            {
                var subject = "Yeni randevu";
                var body = $"{stylist?.DisplayName ?? "Stilist"} için {localTime} yeni randevu.";

                await NotificationHelpers.DispatchInAppAsync(dispatcher, owner.Id,
                    "booking.confirmed.barber", subject, body, cancellationToken,
                    relatedBookingId: booking.Id);

                await dispatcher.DispatchAsync(new NotificationRequest(
                    UserId: owner.Id,
                    Channel: NotificationChannel.Push,
                    TemplateKey: "booking.confirmed.barber",
                    Recipient: $"fcm-token-{owner.Id:N}",
                    Subject: subject,
                    Body: body,
                    RelatedBookingId: booking.Id), cancellationToken);
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

        var shop = await ctx.Set<Domain.Modules.Barber.BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == booking.BarberShopId, cancellationToken);

        var refundText = notification.RefundPercentage > 0
            ? $"%{notification.RefundPercentage} iade işleme alındı."
            : "İade hakkı yok (geç iptal).";

        if (customer is not null)
        {
            await NotificationHelpers.DispatchInAppAsync(dispatcher, customer.Id,
                "booking.cancelled", "Randevu iptal edildi", refundText, cancellationToken,
                relatedBookingId: booking.Id);

            await dispatcher.DispatchAsync(new NotificationRequest(
                UserId: customer.Id,
                Channel: NotificationChannel.Push,
                TemplateKey: "booking.cancelled",
                Recipient: $"fcm-token-{customer.Id:N}",
                Subject: "Randevu iptal edildi",
                Body: refundText,
                RelatedBookingId: booking.Id), cancellationToken);
        }

        if (shop is not null)
        {
            var owner = await ctx.Set<Domain.Modules.Identity.User>().AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == shop.OwnerId, cancellationToken);
            if (owner is not null)
            {
                var localTime = NotificationHelpers.LocalTr(booking.ScheduledAt);
                var subject = "Randevu iptali";
                var body = $"{localTime} randevusu iptal edildi.";

                await NotificationHelpers.DispatchInAppAsync(dispatcher, owner.Id,
                    "booking.cancelled.barber", subject, body, cancellationToken,
                    relatedBookingId: booking.Id);
            }
        }
    }
}

public sealed class BookingStartedNotificationHandler(
    IApplicationDbContext db,
    INotificationDispatcher dispatcher) : INotificationHandler<BookingStartedEvent>
{
    public async Task Handle(BookingStartedEvent notification, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var booking = await ctx.Set<Domain.Modules.Booking.Booking>().AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == notification.BookingId, cancellationToken);
        if (booking is null) return;

        await NotificationHelpers.DispatchInAppAsync(dispatcher, booking.CustomerId,
            "booking.started", "Randevun başladı", "Stilistin randevuna başladı. İyi gelmesi dileğiyle!", cancellationToken,
            relatedBookingId: booking.Id);
    }
}

public sealed class BookingCompletedNotificationHandler(
    IApplicationDbContext db,
    INotificationDispatcher dispatcher) : INotificationHandler<BookingCompletedEvent>
{
    public async Task Handle(BookingCompletedEvent notification, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var booking = await ctx.Set<Domain.Modules.Booking.Booking>().AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == notification.BookingId, cancellationToken);
        if (booking is null) return;

        var subject = "Nasıl gitti?";
        var body = "Randevun tamamlandı. Deneyimini puanlayarak diğer müşterilere yardımcı ol.";

        await NotificationHelpers.DispatchInAppAsync(dispatcher, booking.CustomerId,
            "booking.completed", subject, body, cancellationToken,
            relatedBookingId: booking.Id);

        await dispatcher.DispatchAsync(new NotificationRequest(
            UserId: booking.CustomerId,
            Channel: NotificationChannel.Push,
            TemplateKey: "booking.completed",
            Recipient: $"fcm-token-{booking.CustomerId:N}",
            Subject: subject,
            Body: body,
            RelatedBookingId: booking.Id), cancellationToken);
    }
}

public sealed class BookingNoShowNotificationHandler(
    INotificationDispatcher dispatcher) : INotificationHandler<BookingNoShowEvent>
{
    public async Task Handle(BookingNoShowEvent notification, CancellationToken cancellationToken)
    {
        var subject = "Randevu kaçırıldı";
        var body = "Randevuna gelmediğin için iade yapılmadı. Bir sonraki randevunda görüşmek üzere.";

        await NotificationHelpers.DispatchInAppAsync(dispatcher, notification.CustomerId,
            "booking.no_show", subject, body, cancellationToken,
            relatedBookingId: notification.BookingId);
    }
}

public sealed class BookingRescheduledNotificationHandler(
    IApplicationDbContext db,
    INotificationDispatcher dispatcher) : INotificationHandler<BookingRescheduledEvent>
{
    public async Task Handle(BookingRescheduledEvent notification, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var booking = await ctx.Set<Domain.Modules.Booking.Booking>().AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == notification.BookingId, cancellationToken);
        if (booking is null) return;

        var shop = await ctx.Set<Domain.Modules.Barber.BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == booking.BarberShopId, cancellationToken);

        var oldLocal = NotificationHelpers.LocalTr(notification.OldScheduledAt);
        var newLocal = NotificationHelpers.LocalTr(notification.NewScheduledAt);

        await NotificationHelpers.DispatchInAppAsync(dispatcher, notification.CustomerId,
            "booking.rescheduled.customer",
            "Randevu yeniden planlandı",
            $"{shop?.Name ?? "Kuaför"} — Eski: {oldLocal} → Yeni: {newLocal}",
            cancellationToken,
            relatedBookingId: booking.Id);

        if (shop is not null)
        {
            await NotificationHelpers.DispatchInAppAsync(dispatcher, shop.OwnerId,
                "booking.rescheduled.barber",
                "Randevu zamanı değişti",
                $"Bir randevu {oldLocal} → {newLocal} olarak güncellendi.",
                cancellationToken,
                relatedBookingId: booking.Id);
        }
    }
}

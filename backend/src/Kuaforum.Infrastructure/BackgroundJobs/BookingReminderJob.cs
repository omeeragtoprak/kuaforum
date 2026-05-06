using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Notification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// Hangfire recurring job that sends T-24h reminder push + in-app feed entry
/// for Confirmed bookings approaching ~24h from now. Idempotent via
/// NotificationLog row check (TemplateKey + RelatedBookingId). Reduces
/// no-show rate by re-engaging customers a day before the appointment.
/// Runs hourly. Window: ScheduledAt ∈ [now+23h, now+25h] (catches anything
/// missed by an earlier hour and runs ahead by 1h to avoid edge skips).
public sealed class BookingReminderJob(
    IApplicationDbContext db,
    INotificationDispatcher dispatcher,
    ILogger<BookingReminderJob> logger)
{
    public const string JobId = "kuaforum.booking-reminder-t24";
    public const string CronExpression = "0 * * * *"; // hourly on the hour
    private const string TemplateKey = "booking.reminder.t24";
    private static readonly TimeZoneInfo IstanbulTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public async Task RunAsync(CancellationToken ct = default)
    {
        var ctx = (DbContext)db;
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddHours(23);
        var windowEnd = now.AddHours(25);

        var due = await ctx.Set<Booking>().AsNoTracking()
            .Where(b => b.Status == BookingStatus.Confirmed
                        && b.ScheduledAt >= windowStart
                        && b.ScheduledAt < windowEnd)
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            logger.LogDebug("BookingReminderJob: no Confirmed bookings in T-24h window.");
            return;
        }

        // Pre-fetch already-sent reminders for idempotency.
        var bookingIds = due.Select(b => b.Id).ToList();
        var alreadySent = await ctx.Set<NotificationLog>().AsNoTracking()
            .Where(n => n.TemplateKey == TemplateKey
                        && n.RelatedBookingId != null
                        && bookingIds.Contains(n.RelatedBookingId.Value))
            .Select(n => n.RelatedBookingId!.Value)
            .ToListAsync(ct);
        var alreadySentSet = alreadySent.ToHashSet();

        var pendingCount = 0;
        foreach (var booking in due)
        {
            if (alreadySentSet.Contains(booking.Id)) continue;

            var localTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(booking.ScheduledAt.UtcDateTime, IstanbulTz.Id)
                .ToString("dd MMM HH:mm");
            var subject = "Randevu hatırlatması";
            var body = $"Yarın {localTime} kuaför randevun var. Görüşmek üzere!";

            await dispatcher.DispatchAsync(new NotificationRequest(
                UserId: booking.CustomerId,
                Channel: NotificationChannel.InApp,
                TemplateKey: TemplateKey,
                Recipient: $"user:{booking.CustomerId:N}",
                Subject: subject,
                Body: body,
                RelatedBookingId: booking.Id), ct);

            await dispatcher.DispatchAsync(new NotificationRequest(
                UserId: booking.CustomerId,
                Channel: NotificationChannel.Push,
                TemplateKey: TemplateKey,
                Recipient: $"fcm-token-{booking.CustomerId:N}",
                Subject: subject,
                Body: body,
                RelatedBookingId: booking.Id), ct);

            pendingCount++;
        }

        if (pendingCount > 0)
            logger.LogInformation("BookingReminderJob: dispatched {Count} T-24h reminders.", pendingCount);
    }
}

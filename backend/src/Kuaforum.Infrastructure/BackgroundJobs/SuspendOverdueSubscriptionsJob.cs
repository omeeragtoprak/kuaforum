using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Domain.Modules.Notification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// <summary>
/// RFC-012 (RFC-014 ile abonelik faturasına genelleştirildi): Hangfire recurring job, günlük
/// 03:00 UTC (HardDeleteUsersJob presedenti). İki sorumluluk (tek job'ta — ayrı bir üçüncü
/// recurring job açmaktan kaçınmak için, plan'ın "DailyOverdueCheckJob" tek-job tasarımına
/// karşılık gelir):
///
/// 1. Vade zinciri: Notified + DueDate geçmiş → Overdue → SuspensionApplied (aynı tick) +
///    BarberShop.Suspend() (mevcut admin-suspend mekanizması, sistem tetikli — AutoNoShowJob
///    presedenti: actorUserId: null). SubscriptionInvoiceSuspendedEvent → owner bildirimi.
///    Review fix: sorgu yalnız Notified'ı değil, ZATEN Overdue durumundaki fatura'ları da
///    tarar — bir önceki koşumda shop.Suspend() başarısız olduysa (örn. geçici bir hata) o
///    fatura Overdue'da asılı kalırdı, çünkü eski sorgu yalnız Notified'a bakıyordu ve
///    Overdue'ya bir daha asla dönmüyordu. Artık her koşum askıya alınmamış tüm Overdue'ları
///    yeniden dener. Bir fatura'nın DueDate'i admin tarafından geleceğe uzatılırsa
///    (SubscriptionInvoice.ExtendDueDate) Overdue→Notified'a geri alınır — bu retry taraması
///    onu artık askı adayı SANMAZ.
///
/// 2. Vade yaklaşıyor hatırlatması (T-2 gün): Notified + DueDate == bugün+2 → doğrudan
///    dispatch (BookingReminderJob presedenti — state değişikliği yok, domain event yok).
///    Idempotency: DueDate sabit olduğu için bu koşul bir fatura için yalnız bir günde true
///    olur — job günde bir kez çalıştığı sürece doğal olarak tekilleşir (ekstra dedup tablosu
///    gerekmiyor; günde birden çok manuel tetiklemede nadir bir çift-bildirim riski kabul edildi).
///
/// NOT: Bu job <see cref="ShopSubscription"/>.Status'e (Active/PastDue/Cancelled) hiç dokunmaz —
/// yalnız BarberShop.Suspend()/fatura durumu üzerinden çalışır (RFC-012'den değişmedi). Abonelik
/// aggregate'inin kendi PastDue/Reactivate mekanizması şu an hiçbir job/handler tarafından
/// tetiklenmiyor — bu paketin kapsamı dışında bırakılan, önceden var olan bir boşluk.
/// </summary>
public sealed class SuspendOverdueSubscriptionsJob(
    IApplicationDbContext db,
    IAuditLogger audit,
    INotificationDispatcher dispatcher,
    ILogger<SuspendOverdueSubscriptionsJob> logger)
{
    public const string JobId = "kuaforum.suspend-overdue-subscriptions";
    public const string CronExpression = "0 3 * * *"; // 03:00 daily

    public async Task RunAsync(CancellationToken ct = default)
    {
        var ctx = (DbContext)db;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        await ProcessOverdueAsync(ctx, today, ct);
        await SendDueSoonRemindersAsync(ctx, today, ct);
    }

    private async Task ProcessOverdueAsync(DbContext ctx, DateOnly today, CancellationToken ct)
    {
        var newlyOverdue = await ctx.Set<SubscriptionInvoice>()
            .Where(s => s.Status == SubscriptionInvoiceStatus.Notified && s.DueDate < today)
            .ToListAsync(ct);

        // Retry: bir önceki koşumda Overdue'ya geçip suspension'ı başaramamış fatura'lar.
        // ExtendDueDate gelecekteki bir tarihe uzatıldıysa zaten Notified'a döner, bu yüzden
        // burada hâlâ görünenler gerçekten hâlâ askı bekliyor demektir.
        var stuckOverdue = await ctx.Set<SubscriptionInvoice>()
            .Where(s => s.Status == SubscriptionInvoiceStatus.Overdue)
            .ToListAsync(ct);

        if (newlyOverdue.Count == 0 && stuckOverdue.Count == 0)
        {
            logger.LogDebug("SuspendOverdueSubscriptionsJob: vadesi geçmiş veya askı bekleyen fatura yok.");
            return;
        }

        foreach (var invoice in newlyOverdue)
        {
            invoice.MarkOverdue(); // Notified→Overdue guard'ı — bu grupta zaten Notified, pratikte hep başarılı.
        }

        var candidates = newlyOverdue.Concat(stuckOverdue).ToList();
        var shopIds = candidates.Select(s => s.BarberShopId).Distinct().ToList();
        var shops = await ctx.Set<BarberShop>()
            .Where(s => shopIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var suspendedCount = 0;
        foreach (var invoice in candidates)
        {
            if (invoice.Status != SubscriptionInvoiceStatus.Overdue) continue; // MarkOverdue() bu run'da başarısız oldu

            if (!shops.TryGetValue(invoice.BarberShopId, out var shop))
            {
                logger.LogWarning("SuspendOverdueSubscriptionsJob: shop {ShopId} bulunamadı, fatura Overdue'da kalıyor.", invoice.BarberShopId);
                continue;
            }

            var suspendResult = shop.Suspend($"subscription_overdue:{invoice.Id}");
            if (suspendResult.IsFailure)
            {
                // Örn. shop zaten Closed — fatura Overdue'da kalır, bir sonraki koşumda tekrar denenir.
                logger.LogWarning("SuspendOverdueSubscriptionsJob: shop {ShopId} askıya alınamadı — {Error}.", shop.Id, suspendResult.Error.Message);
                continue;
            }

            var applyResult = invoice.ApplySuspension();
            if (applyResult.IsFailure) continue;

            await audit.RecordAsync(
                "system.subscription_invoice.suspended",
                nameof(SubscriptionInvoice),
                invoice.Id.ToString(),
                actorUserId: null,
                payload: new { shopId = shop.Id, invoiceId = invoice.Id, reason = "subscription_overdue", invoice.AmountMinor, invoice.DueDate },
                ct: ct);

            suspendedCount++;
        }

        await db.SaveChangesAsync(ct);
        if (suspendedCount > 0)
            logger.LogInformation("SuspendOverdueSubscriptionsJob: {Count} shop ödenmemiş abonelik borcu nedeniyle askıya alındı.", suspendedCount);
    }

    private async Task SendDueSoonRemindersAsync(DbContext ctx, DateOnly today, CancellationToken ct)
    {
        var dueSoonDate = today.AddDays(2);
        var dueSoon = await ctx.Set<SubscriptionInvoice>().AsNoTracking()
            .Where(s => s.Status == SubscriptionInvoiceStatus.Notified && s.DueDate == dueSoonDate)
            .ToListAsync(ct);

        if (dueSoon.Count == 0) return;

        var shopIds = dueSoon.Select(s => s.BarberShopId).Distinct().ToList();
        var owners = await ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => shopIds.Contains(s.Id))
            .Select(s => new { s.Id, s.OwnerId })
            .ToDictionaryAsync(x => x.Id, x => x.OwnerId, ct);

        foreach (var invoice in dueSoon)
        {
            if (!owners.TryGetValue(invoice.BarberShopId, out var ownerId)) continue;

            var amountTl = invoice.AmountMinor / 100m;
            var subject = "Abonelik faturası vadesi yaklaşıyor";
            var body = $"{amountTl:N2} TL abonelik faturasının son ödeme tarihi {invoice.DueDate:dd.MM.yyyy}.";

            await dispatcher.DispatchAsync(new NotificationRequest(
                UserId: ownerId,
                Channel: NotificationChannel.InApp,
                TemplateKey: "subscription_invoice.due_soon",
                Recipient: $"user:{ownerId:N}",
                Subject: subject,
                Body: body), ct);

            await dispatcher.DispatchAsync(new NotificationRequest(
                UserId: ownerId,
                Channel: NotificationChannel.Push,
                TemplateKey: "subscription_invoice.due_soon",
                Recipient: $"fcm-token-{ownerId:N}",
                Subject: subject,
                Body: body), ct);
        }

        logger.LogInformation("SuspendOverdueSubscriptionsJob: {Count} vade yaklaşıyor hatırlatması gönderildi.", dueSoon.Count);
    }
}

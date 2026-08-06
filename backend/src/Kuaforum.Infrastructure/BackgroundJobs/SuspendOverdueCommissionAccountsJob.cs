using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Domain.Modules.Notification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// <summary>
/// RFC-012: Hangfire recurring job, günlük 03:00 UTC (HardDeleteUsersJob presedenti).
/// İki sorumluluk (tek job'ta — ayrı bir üçüncü recurring job açmaktan kaçınmak için, plan'ın
/// "DailyOverdueCheckJob" tek-job tasarımına karşılık gelir):
///
/// 1. Vade zinciri: Notified + DueDate geçmiş → Overdue → SuspensionApplied (aynı tick) +
///    BarberShop.Suspend() (mevcut admin-suspend mekanizması, sistem tetikli — AutoNoShowJob
///    presedenti: actorUserId: null). CommissionStatementSuspendedEvent → owner bildirimi.
///    Review fix: sorgu yalnız Notified'ı değil, ZATEN Overdue durumundaki statement'ları da
///    tarar — bir önceki koşumda shop.Suspend() başarısız olduysa (örn. geçici bir hata) o
///    statement Overdue'da asılı kalırdı, çünkü eski sorgu yalnız Notified'a bakıyordu ve
///    Overdue'ya bir daha asla dönmüyordu. Artık her koşum askıya alınmamış tüm Overdue'ları
///    yeniden dener. Bir statement'ın DueDate'i admin tarafından geleceğe uzatılırsa
///    (CommissionStatement.ExtendDueDate) Overdue→Notified'a geri alınır — bu retry taraması
///    onu artık askı adayı SANMAZ.
///
/// 2. Vade yaklaşıyor hatırlatması (T-2 gün): Notified + DueDate == bugün+2 → doğrudan
///    dispatch (BookingReminderJob presedenti — state değişikliği yok, domain event yok).
///    Idempotency: DueDate sabit olduğu için bu koşul bir statement için yalnız bir günde true
///    olur — job günde bir kez çalıştığı sürece doğal olarak tekilleşir (ekstra dedup tablosu
///    gerekmiyor; günde birden çok manuel tetiklemede nadir bir çift-bildirim riski kabul edildi).
/// </summary>
public sealed class SuspendOverdueCommissionAccountsJob(
    IApplicationDbContext db,
    IAuditLogger audit,
    INotificationDispatcher dispatcher,
    ILogger<SuspendOverdueCommissionAccountsJob> logger)
{
    public const string JobId = "kuaforum.suspend-overdue-commission-accounts";
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
        var newlyOverdue = await ctx.Set<CommissionStatement>()
            .Where(s => s.Status == CommissionStatementStatus.Notified && s.DueDate < today)
            .ToListAsync(ct);

        // Retry: bir önceki koşumda Overdue'ya geçip suspension'ı başaramamış statement'lar.
        // ExtendDueDate gelecekteki bir tarihe uzatıldıysa zaten Notified'a döner, bu yüzden
        // burada hâlâ görünenler gerçekten hâlâ askı bekliyor demektir.
        var stuckOverdue = await ctx.Set<CommissionStatement>()
            .Where(s => s.Status == CommissionStatementStatus.Overdue)
            .ToListAsync(ct);

        if (newlyOverdue.Count == 0 && stuckOverdue.Count == 0)
        {
            logger.LogDebug("SuspendOverdueCommissionAccountsJob: vadesi geçmiş veya askı bekleyen statement yok.");
            return;
        }

        foreach (var statement in newlyOverdue)
        {
            statement.MarkOverdue(); // Notified→Overdue guard'ı — bu grupta zaten Notified, pratikte hep başarılı.
        }

        var candidates = newlyOverdue.Concat(stuckOverdue).ToList();
        var shopIds = candidates.Select(s => s.BarberShopId).Distinct().ToList();
        var shops = await ctx.Set<BarberShop>()
            .Where(s => shopIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var suspendedCount = 0;
        foreach (var statement in candidates)
        {
            if (statement.Status != CommissionStatementStatus.Overdue) continue; // MarkOverdue() bu run'da başarısız oldu

            if (!shops.TryGetValue(statement.BarberShopId, out var shop))
            {
                logger.LogWarning("SuspendOverdueCommissionAccountsJob: shop {ShopId} bulunamadı, statement Overdue'da kalıyor.", statement.BarberShopId);
                continue;
            }

            var suspendResult = shop.Suspend($"commission_overdue:{statement.Id}");
            if (suspendResult.IsFailure)
            {
                // Örn. shop zaten Closed — statement Overdue'da kalır, bir sonraki koşumda tekrar denenir.
                logger.LogWarning("SuspendOverdueCommissionAccountsJob: shop {ShopId} askıya alınamadı — {Error}.", shop.Id, suspendResult.Error.Message);
                continue;
            }

            var applyResult = statement.ApplySuspension();
            if (applyResult.IsFailure) continue;

            await audit.RecordAsync(
                "system.commission_statement.suspended",
                nameof(CommissionStatement),
                statement.Id.ToString(),
                actorUserId: null,
                payload: new { shopId = shop.Id, statementId = statement.Id, reason = "commission_overdue", statement.AmountDueMinor, statement.DueDate },
                ct: ct);

            suspendedCount++;
        }

        await db.SaveChangesAsync(ct);
        if (suspendedCount > 0)
            logger.LogInformation("SuspendOverdueCommissionAccountsJob: {Count} shop ödenmemiş komisyon borcu nedeniyle askıya alındı.", suspendedCount);
    }

    private async Task SendDueSoonRemindersAsync(DbContext ctx, DateOnly today, CancellationToken ct)
    {
        var dueSoonDate = today.AddDays(2);
        var dueSoon = await ctx.Set<CommissionStatement>().AsNoTracking()
            .Where(s => s.Status == CommissionStatementStatus.Notified && s.DueDate == dueSoonDate)
            .ToListAsync(ct);

        if (dueSoon.Count == 0) return;

        var shopIds = dueSoon.Select(s => s.BarberShopId).Distinct().ToList();
        var owners = await ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => shopIds.Contains(s.Id))
            .Select(s => new { s.Id, s.OwnerId })
            .ToDictionaryAsync(x => x.Id, x => x.OwnerId, ct);

        foreach (var statement in dueSoon)
        {
            if (!owners.TryGetValue(statement.BarberShopId, out var ownerId)) continue;

            var amountTl = statement.AmountDueMinor / 100m;
            var subject = "Komisyon borcu vadesi yaklaşıyor";
            var body = $"{amountTl:N2} TL komisyon borcunun son ödeme tarihi {statement.DueDate:dd.MM.yyyy}.";

            await dispatcher.DispatchAsync(new NotificationRequest(
                UserId: ownerId,
                Channel: NotificationChannel.InApp,
                TemplateKey: "commission_statement.due_soon",
                Recipient: $"user:{ownerId:N}",
                Subject: subject,
                Body: body), ct);

            await dispatcher.DispatchAsync(new NotificationRequest(
                UserId: ownerId,
                Channel: NotificationChannel.Push,
                TemplateKey: "commission_statement.due_soon",
                Recipient: $"fcm-token-{ownerId:N}",
                Subject: subject,
                Body: body), ct);
        }

        logger.LogInformation("SuspendOverdueCommissionAccountsJob: {Count} vade yaklaşıyor hatırlatması gönderildi.", dueSoon.Count);
    }
}

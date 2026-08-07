using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// <summary>
/// RFC-014 §2 D1: Hangfire recurring job — her ayın 1'i 00:15 UTC'de önceki takvim ayını kapatır.
/// RFC-012'nin ciro-komisyonu matrahının (Completed booking toplamı × oran) yerini artık salonun
/// bağlı olduğu <see cref="ShopSubscription"/> → <see cref="SubscriptionPlan"/>.PriceMinor'ı aldı —
/// booking hacminden tamamen BAĞIMSIZ, sabit tutar.
///
/// PriceMinor null veya 0 olan planlar (V1'de LAUNCH_FREE) için hiç fatura ÜRETİLMEZ (RFC-014
/// madde 3: "LAUNCH_FREE fiyatı 0 olduğu için fatura üretilmez"). Cancelled abonelikler de
/// atlanır — iptal edilmiş bir aboneliğe yeni dönem faturası kesilmez.
///
/// Idempotent: aynı dönem+shop için fatura zaten varsa (ux_subscription_invoices_shop_period)
/// atlanır — job iki kez çalışsa da (manuel tetik + zamanlanmış) tek fatura kalır.
/// </summary>
public sealed class CloseMonthlySubscriptionInvoicesJob(
    IApplicationDbContext db,
    IAuditLogger audit,
    ILogger<CloseMonthlySubscriptionInvoicesJob> logger)
{
    public const string JobId = "kuaforum.close-monthly-subscription-invoices";
    public const string CronExpression = "15 0 1 * *"; // ayın 1'i 00:15 UTC

    public async Task RunAsync(CancellationToken ct = default)
    {
        var ctx = (DbContext)db;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var periodStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var alreadyClosedShopIds = await ctx.Set<SubscriptionInvoice>().AsNoTracking()
            .Where(s => s.PeriodStart == periodStart)
            .Select(s => s.BarberShopId)
            .ToListAsync(ct);
        var alreadyClosedSet = alreadyClosedShopIds.ToHashSet();

        // Yalnız Cancelled OLMAYAN abonelikler + fiyatı gerçekten pozitif VE hâlâ AKTİF olan
        // planlar faturalanır. Review fix (2026-08-07): plan.IsActive önceden hiç okunmuyordu —
        // fiyatı olan bir plan admin tarafından pasifleştirilse (satıştan kaldırılsa) bile o
        // plandaki mevcut salonlar sonsuza dek faturalanmaya devam ediyordu. NOT: bu filtre
        // RichDemoSeeder'ın DEMO_PAID_QA'sını (bilerek IsActive=false, bkz. o seeder'ın XML
        // doc'u) SESSİZCE dokunmaz — o seeder'ın 4 demo faturası bu job'dan değil doğrudan
        // seeder'ın kendisinden gelir (sabit geçmiş dönemler için SubscriptionInvoice.Create
        // doğrudan çağrılır), yani owner/admin faturalama ekranları bu değişiklikten
        // ETKİLENMEZ — yalnız job'un GELECEKTE bu plana yeni dönem faturası ÜRETMESİ engellenir
        // (zaten istenen davranış: pasif bir demo/QA planın canlı job tarafından
        // "gerçek" gibi yeniden üretilmemesi).
        var billable = await (
            from sub in ctx.Set<ShopSubscription>().AsNoTracking()
            join plan in ctx.Set<SubscriptionPlan>().AsNoTracking() on sub.PlanCode equals plan.Code
            where sub.Status != ShopSubscriptionStatus.Cancelled
                  && plan.IsActive
                  && plan.PriceMinor != null && plan.PriceMinor > 0
                  && !alreadyClosedSet.Contains(sub.BarberShopId)
            select new { sub.BarberShopId, plan.Code, PriceMinor = plan.PriceMinor!.Value })
            .ToListAsync(ct);

        if (billable.Count == 0)
        {
            logger.LogInformation("CloseMonthlySubscriptionInvoicesJob: {Period} için faturalanacak salon yok (herkes ücretsiz plan/zaten kapatılmış).", periodStart);
            return;
        }

        var created = 0;
        foreach (var entry in billable)
        {
            var invoiceResult = SubscriptionInvoice.Create(entry.BarberShopId, periodStart, periodEnd, entry.Code, entry.PriceMinor);
            if (invoiceResult.IsFailure)
            {
                logger.LogWarning("CloseMonthlySubscriptionInvoicesJob: shop {ShopId} fatura oluşturulamadı — {Error}.", entry.BarberShopId, invoiceResult.Error.Message);
                continue;
            }

            var invoice = invoiceResult.Value;
            var notifyResult = invoice.MarkNotified();
            if (notifyResult.IsFailure) continue;

            ctx.Set<SubscriptionInvoice>().Add(invoice);

            await audit.RecordAsync(
                "system.subscription_invoice.notified",
                nameof(SubscriptionInvoice),
                invoice.Id.ToString(),
                actorUserId: null,
                payload: new
                {
                    shopId = entry.BarberShopId,
                    periodStart,
                    periodEnd,
                    planCode = entry.Code,
                    planPriceMinor = entry.PriceMinor,
                    amountMinor = invoice.AmountMinor,
                    dueDate = invoice.DueDate,
                },
                ct: ct);

            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("CloseMonthlySubscriptionInvoicesJob: {Period} için {Count} fatura oluşturuldu.", periodStart, created);
        }
    }
}

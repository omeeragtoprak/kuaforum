using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.BackgroundJobs;

/// <summary>
/// RFC-012: Hangfire recurring job — her ayın 1'i 00:15 UTC'de önceki takvim ayını kapatır.
/// Matrah = o dönemde Status==Completed olan randevuların TotalAmountMinor toplamı — WalkIn
/// dahil (RFC bilerek: walk-in'i hariç tutmak "walk-in diye işaretleyip komisyondan kaçma"
/// kapısı açardı), Cancelled/NoShow/Pending hariç (Status filtresiyle zaten dışarıda).
///
/// Ay sınırı kararı: dönem UTC takvim ayı sınırlarıyla hesaplanır (CompletedAt >= period start UTC
/// AND < period end+1 UTC), Europe/Istanbul yerel gün sınırı DEĞİL — diğer günlük job'larla
/// (HardDeleteUsersJob, AutoNoShowJob) tutarlı basit bir seçim. Bilinen sınırlama: ayın son
/// gecesi 00:00-03:00 TR saatinde tamamlanan (UTC'de önceki gün) bir randevu bir sonraki UTC
/// ayına sarkabilir — düşük hacimli gece randevuları için kabul edilebilir, launch'ı bloklamıyor.
///
/// Sıfır-matrahlı shop'lar için statement üretilmez (gürültü önleme, RFC-012).
/// Idempotent: aynı dönem+shop için statement zaten varsa (ux_commission_statements_shop_period)
/// atlanır — job iki kez çalışsa da (manuel tetik + zamanlanmış) tek statement kalır.
/// </summary>
public sealed class CloseMonthlyCommissionStatementsJob(
    IApplicationDbContext db,
    IAuditLogger audit,
    ILogger<CloseMonthlyCommissionStatementsJob> logger)
{
    public const string JobId = "kuaforum.close-monthly-commission-statements";
    public const string CronExpression = "15 0 1 * *"; // ayın 1'i 00:15 UTC

    public async Task RunAsync(CancellationToken ct = default)
    {
        var ctx = (DbContext)db;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var periodStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var periodStartUtc = new DateTimeOffset(periodStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var periodEndExclusiveUtc = new DateTimeOffset(periodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var platformSettings = await ctx.Set<PlatformSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        if (platformSettings is null)
        {
            logger.LogError("CloseMonthlyCommissionStatementsJob: PlatformSettings satırı bulunamadı — seeder çalışmamış olabilir. Bu dönem atlanıyor.");
            return;
        }

        var alreadyClosedShopIds = await ctx.Set<CommissionStatement>().AsNoTracking()
            .Where(s => s.PeriodStart == periodStart)
            .Select(s => s.BarberShopId)
            .ToListAsync(ct);
        var alreadyClosedSet = alreadyClosedShopIds.ToHashSet();

        var basisByShop = await ctx.Set<BookingAggregate>().AsNoTracking()
            .Where(b => b.Status == BookingStatus.Completed
                        && b.CompletedAt != null
                        && b.CompletedAt >= periodStartUtc
                        && b.CompletedAt < periodEndExclusiveUtc)
            .GroupBy(b => b.BarberShopId)
            .Select(g => new { BarberShopId = g.Key, BasisAmountMinor = g.Sum(b => b.TotalAmountMinor) })
            .Where(x => x.BasisAmountMinor > 0)
            .ToListAsync(ct);

        var dueShopIds = basisByShop.Select(x => x.BarberShopId)
            .Where(id => !alreadyClosedSet.Contains(id))
            .ToList();

        if (dueShopIds.Count == 0)
        {
            logger.LogInformation("CloseMonthlyCommissionStatementsJob: {Period} için kapanacak shop yok.", periodStart);
            return;
        }

        var shopRates = await ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => dueShopIds.Contains(s.Id))
            .Select(s => new { s.Id, s.CommissionRateOverride })
            .ToDictionaryAsync(x => x.Id, x => x.CommissionRateOverride, ct);

        var created = 0;
        foreach (var entry in basisByShop.Where(x => dueShopIds.Contains(x.BarberShopId)))
        {
            if (!shopRates.TryGetValue(entry.BarberShopId, out var rateOverride))
            {
                logger.LogWarning("CloseMonthlyCommissionStatementsJob: shop {ShopId} bulunamadı, atlanıyor.", entry.BarberShopId);
                continue;
            }

            var rateApplied = rateOverride ?? platformSettings.CommissionRateGlobal;
            var statementResult = CommissionStatement.Create(entry.BarberShopId, periodStart, periodEnd, entry.BasisAmountMinor, rateApplied);
            if (statementResult.IsFailure)
            {
                logger.LogWarning("CloseMonthlyCommissionStatementsJob: shop {ShopId} statement oluşturulamadı — {Error}.", entry.BarberShopId, statementResult.Error.Message);
                continue;
            }

            var statement = statementResult.Value;
            var notifyResult = statement.MarkNotified();
            if (notifyResult.IsFailure) continue;

            ctx.Set<CommissionStatement>().Add(statement);

            await audit.RecordAsync(
                "system.commission_statement.notified",
                nameof(CommissionStatement),
                statement.Id.ToString(),
                actorUserId: null,
                payload: new
                {
                    shopId = entry.BarberShopId,
                    periodStart,
                    periodEnd,
                    basisAmountMinor = entry.BasisAmountMinor,
                    rateApplied,
                    amountDueMinor = statement.AmountDueMinor,
                    dueDate = statement.DueDate,
                },
                ct: ct);

            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("CloseMonthlyCommissionStatementsJob: {Period} için {Count} statement oluşturuldu.", periodStart, created);
        }
    }
}

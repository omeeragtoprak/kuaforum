using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Persistence.Seeding;

/// <summary>
/// RFC-014 §2 D1: abonelik omurgasının seed'i — PlatformSettingsSeeder ile aynı desende
/// (idempotent, her startup'ta SeedReferenceDataAsync içinde çağrılır). İki sorumluluğu var:
/// (1) 3 plan + entitlement satırlarının VAR OLDUĞUNU garantiler, (2) henüz bir aboneliği
/// olmayan tüm salonları LAUNCH_FREE'ye bağlar (backfill). Bu ikinci adım DemoBarberSeeder/
/// ComprehensiveDemoSeeder/RichDemoSeeder'dan SONRA çağrılmalı — onlar BarberShop.Create()'i
/// CreateBarberShopCommandHandler'ı (otomatik abonelik yaratan) ATLAYARAK doğrudan çağırır,
/// yoksa demo salonlar aboneliksiz kalır. Bkz. DependencyInjection.SeedReferenceDataAsync
/// çağrı sırası.
/// </summary>
public sealed class SubscriptionPlanSeeder(ApplicationDbContext db, ILogger<SubscriptionPlanSeeder> logger)
{
    // "-1" = sınırsız sentinel — bkz. EntitlementService.ParseLimit. 0 kullanılmadı çünkü 0,
    // "hiç çalışan/randevu eklenemez" anlamına gelirdi; yanlış okunursa feature'ı tamamen
    // kilitlerdi. Negatif bir sayı hem "gerçek bir limit değil" sinyalini taşır hem de yanlışlıkla
    // geçerli bir sınır olarak yorumlanamaz.
    private const string UnlimitedSentinel = "-1";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // LAUNCH_FREE: V1 launch planı, herkes burada, gerçekten ücretsiz (PriceMinor=0 —
        // "belirsiz" değil, kesin ücretsiz).
        await SeedPlanAsync(
            SubscriptionPlanCodes.LaunchFree, "Launch (Ücretsiz)", priceMinor: 0, isActive: true, displayOrder: 0,
            entitlements:
            [
                (EntitlementKeys.MaxStaff, "20"),
                (EntitlementKeys.MaxMonthlyBookings, UnlimitedSentinel),
                (EntitlementKeys.CanUseSms, "false"),
                (EntitlementKeys.CanUseDeposit, "true"),
            ],
            ct);

        // STARTER/PRO: V2'de fiyat belirlenince IsActive=true'ya çekilecek. PriceMinor=null
        // bilinçli — "fiyat henüz belirlenmedi" demek, 0 = "ücretsiz" ile karışmasın diye.
        // Entitlement değerleri de yalnızca launch sonrası ürün kararına kadar geçerli
        // PLACEHOLDER'lar — burada garanti edilen tek şey satırların VAR OLMASı.
        await SeedPlanAsync(
            SubscriptionPlanCodes.Starter, "Starter", priceMinor: null, isActive: false, displayOrder: 1,
            entitlements:
            [
                (EntitlementKeys.MaxStaff, "5"),
                (EntitlementKeys.MaxMonthlyBookings, UnlimitedSentinel),
                (EntitlementKeys.CanUseSms, "true"),
                (EntitlementKeys.CanUseDeposit, "true"),
            ],
            ct);

        await SeedPlanAsync(
            SubscriptionPlanCodes.Pro, "Pro", priceMinor: null, isActive: false, displayOrder: 2,
            entitlements:
            [
                (EntitlementKeys.MaxStaff, UnlimitedSentinel),
                (EntitlementKeys.MaxMonthlyBookings, UnlimitedSentinel),
                (EntitlementKeys.CanUseSms, "true"),
                (EntitlementKeys.CanUseDeposit, "true"),
            ],
            ct);

        await BackfillShopSubscriptionsAsync(ct);
    }

    private async Task SeedPlanAsync(
        string code, string name, long? priceMinor, bool isActive, int displayOrder,
        (string Key, string Value)[] entitlements, CancellationToken ct)
    {
        var exists = await db.Set<SubscriptionPlan>().AnyAsync(p => p.Code == code, ct);
        if (!exists)
        {
            var plan = SubscriptionPlan.Create(code, name, priceMinor, isActive, displayOrder).Value;
            db.Set<SubscriptionPlan>().Add(plan);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("SubscriptionPlanSeeder: plan {Code} oluşturuldu.", code);
        }

        // Entitlement'lar plan satırından bağımsız idempotent kontrol edilir — böylece
        // ileride bir plana yeni bir entitlement anahtarı eklenirse (plan satırı zaten var
        // olsa bile) eksik olan yalnızca o satır tamamlanır, mevcut satırlar dokunulmadan kalır.
        var existingKeys = await db.Set<PlanEntitlement>()
            .Where(e => e.PlanCode == code)
            .Select(e => e.Key)
            .ToListAsync(ct);

        var missing = entitlements.Where(e => !existingKeys.Contains(e.Key)).ToList();
        if (missing.Count == 0) return;

        foreach (var (key, value) in missing)
            db.Set<PlanEntitlement>().Add(PlanEntitlement.Create(code, key, value).Value);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("SubscriptionPlanSeeder: plan {Code} için {Count} entitlement eklendi.", code, missing.Count);
    }

    /// <summary>Mevcut tüm salonlar (seed sırasında zaten var olanlar dahil) LAUNCH_FREE'ye bağlanır — aboneliksiz salon hiç kalmaz.</summary>
    private async Task BackfillShopSubscriptionsAsync(CancellationToken ct)
    {
        var shopIdsWithoutSubscription = await db.Set<BarberShop>()
            .Where(s => !db.Set<ShopSubscription>().Any(sub => sub.BarberShopId == s.Id))
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (shopIdsWithoutSubscription.Count == 0) return;

        foreach (var shopId in shopIdsWithoutSubscription)
            db.Set<ShopSubscription>().Add(ShopSubscription.CreateLaunchFreeDefault(shopId));

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "SubscriptionPlanSeeder: {Count} salon LAUNCH_FREE aboneliğine bağlandı (backfill).",
            shopIdsWithoutSubscription.Count);
    }
}

using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Billing;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Infrastructure.Modules.Billing;

/// <summary>
/// RFC-014 §2 D1: entitlement DEĞERLERİ DB'den (plan_entitlements) okunur, koda gömülmez —
/// yalnızca anahtar İSİMLERİ (EntitlementKeys) sabit. "-1" tam olarak sınırsız anlamına gelir
/// (SubscriptionPlanSeeder bu sentinel'i yazar); eksik/bozuk bir satır SESSİZCE sınırsız'a
/// düşmez — ParseLimit bkz. aşağıdaki yorum.
///
/// Aboneliksiz salon (ShopSubscription satırı yok) için varsayılan: LAUNCH_FREE'ye düşürülür,
/// exception FIRLATILMAZ. Gerekçe: V1'de her salon CreateBarberShopCommandHandler'da atomik
/// olarak LAUNCH_FREE'ye bağlanıyor VE SubscriptionPlanSeeder tüm eski salonları backfill
/// ediyor — pratikte bu dal hiç tetiklenmemeli. Yine de önleyici bir garanti olarak "aboneliksiz
/// = en kısıtlayıcı ücretsiz plan" seçildi (fail-safe): olası bir veri tutarsızlığında owner'ı
/// sessizce sınırsız yetkiye çıkarmak (fail-open, güvenlik açığı) yerine bilinen en düşük
/// pakete düşürüyoruz.
/// </summary>
public sealed class EntitlementService(IApplicationDbContext db) : IEntitlementService
{
    public async Task<ShopEntitlements> GetAsync(Guid barberShopId, CancellationToken ct = default)
    {
        var ctx = (DbContext)db;

        var planCode = await ctx.Set<ShopSubscription>()
            .AsNoTracking()
            .Where(s => s.BarberShopId == barberShopId)
            .Select(s => s.PlanCode)
            .FirstOrDefaultAsync(ct) ?? SubscriptionPlanCodes.LaunchFree;

        var entitlements = await ctx.Set<PlanEntitlement>()
            .AsNoTracking()
            .Where(e => e.PlanCode == planCode)
            .ToDictionaryAsync(e => e.Key, e => e.Value, ct);

        return new ShopEntitlements(
            MaxStaff: ParseLimit(entitlements.GetValueOrDefault(EntitlementKeys.MaxStaff)),
            MaxMonthlyBookings: ParseLimit(entitlements.GetValueOrDefault(EntitlementKeys.MaxMonthlyBookings)),
            CanUseSms: ParseBool(entitlements.GetValueOrDefault(EntitlementKeys.CanUseSms)),
            CanUseDeposit: ParseBool(entitlements.GetValueOrDefault(EntitlementKeys.CanUseDeposit)));
    }

    /// <summary>
    /// Yalnızca sentinel "-1" (veya herhangi bir negatif sayı) sınırsız (null) demektir.
    /// Satır tamamen EKSİKSE ya da sayı olarak parse edilemiyorsa (veri tutarsızlığı) 0
    /// döneriz — sınırsız DEĞİL, en kısıtlayıcı değer. Bilinçli fail-safe seçim: bozuk bir
    /// entitlement satırı sessizce "sınırsız hak" anlamına gelmemeli, aksi halde bir seed/
    /// migration hatası owner'lara istemeden sınırsız kota açardı. Sıfır sınır owner'ı hemen
    /// engeller ve sorunu görünür kılar (loglanabilir/izlenebilir), sınırsız erişim ise
    /// sessizce maliyet/güvenlik riski biriktirirdi.
    /// </summary>
    private static int? ParseLimit(string? raw)
    {
        if (raw is null) return 0;
        if (!int.TryParse(raw, out var value)) return 0;
        return value < 0 ? null : value;
    }

    private static bool ParseBool(string? raw) => raw is not null && bool.TryParse(raw, out var value) && value;
}

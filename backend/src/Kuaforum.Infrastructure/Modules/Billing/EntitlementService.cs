using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Modules.Billing;

/// <summary>
/// RFC-014 §2 D1: entitlement DEĞERLERİ DB'den (plan_entitlements) okunur, koda gömülmez —
/// yalnızca anahtar İSİMLERİ (EntitlementKeys) sabit. "-1" tam olarak sınırsız anlamına gelir
/// (SubscriptionPlanSeeder bu sentinel'i yazar).
///
/// Eksik/bozuk bir entitlement satırı her zaman bir VERİ TUTARSIZLIĞIDIR (seed/migration hatası)
/// ve her zaman <see cref="LogError"/> ile bize (platforma) görünür kılınır — bu servisin tek
/// alarm sinyali budur. Ancak bu hatanın OWNER'A yansıyan davranışsal fallback'i anahtarın
/// TÜRÜNE göre ikiye ayrılır (bkz. ParseLimit/ParseBool XML doc'ları):
///   - Kota (MaxStaff, MaxMonthlyBookings): fallback = sınırsız (null). Sıfıra düşürmek
///     owner'ın işletmesini (çalışan/randevu ekleyemez hale gelerek) TAMAMEN kilitlerdi; V1'de
///     tüm planlar ücretsiz olduğu için sınırsıza düşmenin gelir kaybı maliyeti yok, buna
///     karşılık owner'ı sessizce kilitlemenin destek-talebi maliyeti var.
///   - Yetenek (CanUseSms, CanUseDeposit): fallback = false (fail-closed) — bunlar gerçek para
///     hareketi tetikler (SMS ücreti, kapora ödeme akışı), bozuk veri yüzünden sessizce açılmamalı.
///
/// Aboneliksiz salon (ShopSubscription satırı yok) için varsayılan: LAUNCH_FREE'ye düşürülür,
/// exception FIRLATILMAZ, ama <see cref="LogWarning"/> ile işaretlenir (beklenmedik durum).
/// Gerekçe: V1'de her salon CreateBarberShopCommandHandler'da atomik olarak LAUNCH_FREE'ye
/// bağlanıyor VE SubscriptionPlanSeeder tüm eski salonları backfill ediyor — pratikte bu dal
/// hiç tetiklenmemeli. Yine de önleyici bir garanti olarak "aboneliksiz = en kısıtlayıcı
/// ücretsiz plan" seçildi (fail-safe): olası bir veri tutarsızlığında owner'ı sessizce
/// sınırsız yetkiye çıkarmak (fail-open, güvenlik açığı) yerine bilinen en düşük pakete
/// düşürüyoruz.
/// </summary>
public sealed class EntitlementService(IApplicationDbContext db, ILogger<EntitlementService> logger) : IEntitlementService
{
    public async Task<ShopEntitlements> GetAsync(Guid barberShopId, CancellationToken ct = default)
    {
        var ctx = (DbContext)db;

        var planCode = await ctx.Set<ShopSubscription>()
            .AsNoTracking()
            .Where(s => s.BarberShopId == barberShopId)
            .Select(s => s.PlanCode)
            .FirstOrDefaultAsync(ct);

        if (planCode is null)
        {
            logger.LogWarning(
                "EntitlementService: BarberShopId {BarberShopId} için ShopSubscription satırı bulunamadı — {FallbackPlan} varsayılana düşürüldü. Beklenmedik durum (SubscriptionPlanSeeder backfill'i ve CreateBarberShopCommandHandler'ın atomik oluşturması bu satırı garanti etmeli).",
                barberShopId, SubscriptionPlanCodes.LaunchFree);
            planCode = SubscriptionPlanCodes.LaunchFree;
        }

        var entitlements = await ctx.Set<PlanEntitlement>()
            .AsNoTracking()
            .Where(e => e.PlanCode == planCode)
            .ToDictionaryAsync(e => e.Key, e => e.Value, ct);

        return new ShopEntitlements(
            MaxStaff: ParseLimit(entitlements.GetValueOrDefault(EntitlementKeys.MaxStaff), EntitlementKeys.MaxStaff, planCode, barberShopId),
            MaxMonthlyBookings: ParseLimit(entitlements.GetValueOrDefault(EntitlementKeys.MaxMonthlyBookings), EntitlementKeys.MaxMonthlyBookings, planCode, barberShopId),
            CanUseSms: ParseBool(entitlements.GetValueOrDefault(EntitlementKeys.CanUseSms), EntitlementKeys.CanUseSms, planCode, barberShopId),
            CanUseDeposit: ParseBool(entitlements.GetValueOrDefault(EntitlementKeys.CanUseDeposit), EntitlementKeys.CanUseDeposit, planCode, barberShopId));
    }

    /// <summary>
    /// Sentinel "-1" (veya herhangi bir negatif sayı) sınırsız (null) demektir. Satır tamamen
    /// EKSİKSE ya da sayı olarak parse edilemiyorsa (veri tutarsızlığı) yine sınırsız (null)
    /// döneriz — <see cref="LogError"/> ile önce bize görünür kılınır. Bilinçli fail-open seçim:
    /// bir kota (max_staff/max_monthly_bookings) satırının eksik/bozuk olması owner'ı sessizce
    /// işletmesini kullanılamaz hale getirerek (çalışan/randevu ekleyemez) kilitlemeyi göze
    /// almamalı — V1'de tüm planlar ücretsiz olduğundan sınırsıza düşmenin gelir kaybı maliyeti
    /// yok, buna karşılık owner'ı sessizce kilitlemenin (biz haberdar olmadan) destek-talebi
    /// maliyeti var. Bu ayrım kota anahtarlarına özgüdür; bkz. <see cref="ParseBool"/> — yetenek
    /// anahtarları (SMS/kapora gerçek para hareketi tetikler) bilerek ters yönde fail-closed'dır.
    /// </summary>
    private int? ParseLimit(string? raw, string key, string planCode, Guid barberShopId)
    {
        if (raw is null)
        {
            LogMissingOrInvalidEntitlement(key, planCode, barberShopId, raw: null);
            return null;
        }

        if (!int.TryParse(raw, out var value))
        {
            LogMissingOrInvalidEntitlement(key, planCode, barberShopId, raw);
            return null;
        }

        return value < 0 ? null : value;
    }

    /// <summary>
    /// Satır eksikse ya da "true"/"false" olarak parse edilemiyorsa false (fail-closed) döneriz —
    /// <see cref="LogError"/> ile önce bize görünür kılınır. can_use_sms/can_use_deposit gerçek
    /// para hareketi tetikler (SMS ücreti, kapora ödeme akışı); bozuk/eksik bir satır yüzünden
    /// bu yetenekler sessizce açılmamalı. Bkz. <see cref="ParseLimit"/> — kota anahtarları
    /// bilerek ters yönde (sınırsız) fail-open'dır, çünkü oradaki risk gelir kaybı değil
    /// owner'ın işletmesinin tamamen kilitlenmesidir.
    /// </summary>
    private bool ParseBool(string? raw, string key, string planCode, Guid barberShopId)
    {
        if (raw is not null && bool.TryParse(raw, out var value)) return value;

        LogMissingOrInvalidEntitlement(key, planCode, barberShopId, raw);
        return false;
    }

    private void LogMissingOrInvalidEntitlement(string key, string planCode, Guid barberShopId, string? raw)
    {
        logger.LogError(
            "EntitlementService: veri tutarsızlığı — PlanCode {PlanCode}, Key {Key} için entitlement satırı {RowState} (BarberShopId {BarberShopId}, ham değer: {RawValue}). Seed/migration eksikliğini kontrol edin.",
            planCode, key, raw is null ? "EKSİK" : "PARSE EDİLEMEDİ", barberShopId, raw ?? "(null)");
    }
}

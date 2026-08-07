using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Persistence.Seeding;

/// <summary>
/// Development-only demo veri tazeleme (2026-08-07 canlı bulgu): RichDemoSeeder tek-seferlik
/// idempotent bir gate kullanır (<c>RichShopOwnerPhones[0]</c> zaten var mı) — bir kez seed
/// edildi mi bir daha ASLA çalışmaz. Randevu tarihleri (ScheduledAt) MUTLAK
/// <see cref="DateTimeOffset"/> olduğu için, seed anından bir gün sonra "bugünün randevusu"
/// artık gerçek bugüne denk gelmez ve owner paneli/faturalama ekranları BOŞ görünür
/// (founder'ın 08-07 sabahı canlı yaşadığı sorun — en erken randevu 08-08 iken sistem
/// tarihi zaten 08-07'ye geçmişti).
///
/// Bu sınıf o bayatlamayı tespit edip (<see cref="IsStaleAsync"/>) coğrafya (cities/districts/
/// neighborhoods) ve plan tanımları (subscription_plans/plan_entitlements) HARİÇ tüm kullanıcı/
/// randevu/ödeme/fatura/yorum/bildirim/KYC verisini TRUNCATE eder (<see cref="ResetAsync"/>) —
/// çağıran taraf (<see cref="DependencyInjection.SeedReferenceDataAsync"/>) ardından normal
/// seeder zincirini (AdminSeeder → PlatformSettingsSeeder → DemoBarberSeeder → ... →
/// RichDemoSeeder → SubscriptionPlanSeeder) sıfırdan, bu kez "şimdi"ye göre yeniden çalıştırır.
///
/// PRODUCTION GARANTİSİ (üç bağımsız katman — herhangi biri tek başına yeterli):
/// 1) <see cref="ResetAsync"/> enjekte edilen <see cref="IHostEnvironment"/>.IsProduction()
///    ise throw eder — bu sınıf HANGİ çağıran taraftan (bug, yanlış wiring, gelecekte eklenen
///    yeni bir çağrı noktası) gelirse gelsin Production'da asla TRUNCATE ÇALIŞTIRMAZ.
/// 2) Tek gerçek çağıran <see cref="DependencyInjection.SeedReferenceDataAsync"/>, kendi
///    <c>isProduction</c> parametresiyle (Program.cs'te <c>app.Environment.IsProduction()</c>)
///    bu sınıfı hiç DEVREYE SOKMAZ.
/// 3) Dev-only endpoint (<c>DevEndpoints.MapDevEndpoints</c>) route'u
///    <c>env.IsDevelopment()</c> değilken hiç map EDİLMEZ (AuthEndpoints "dev-login" ile
///    birebir aynı kalıp) — Production'da bu path fiziksel olarak yok, 404.
/// appsettings.Production.json'da ayrıca Seed:RichDemo/Demo/ComprehensiveDemo hepsi false
/// (bkz. o dosya) — otomatik tazeleme yolu zaten seedRichDemo=false iken hiç tetiklenmez.
/// </summary>
public sealed class DemoDataResetter(ApplicationDbContext db, IHostEnvironment env, ILogger<DemoDataResetter> logger)
{
    // RichDemoSeeder.DevLoginOwnerPhone / DevLoginCommand.BarberOwnerDemoPhone ile birebir aynı.
    private const string DevLoginOwnerPhone = "+905001000001";

    // RichDemoSeeder.RichShopOwnerPhones[0] ile birebir aynı — RichDemoSeeder'ın kendi idempotency gate'i.
    private const string RichGatePhone = "+905002000001";

    private static readonly TimeZoneInfo IstanbulTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    /// <summary>
    /// Gate telefonu (RichGatePhone) hiç yoksa "stale" DEĞİLDİR — henüz hiç seed edilmemiş
    /// demektir, bu normal ilk-çalışma yoludur ve RichDemoSeeder kendi gate'iyle zaten taze
    /// veri üretecektir (reset'e gerek yok, TRUNCATE etmek gereksiz maliyet olurdu).
    /// Gate varsa ama dev-login owner personasının salonunun BUGÜNE (Europe/Istanbul) ait
    /// hiç randevusu yoksa veri bayatlamıştır → true.
    /// </summary>
    public async Task<bool> IsStaleAsync(CancellationToken ct = default)
    {
        var gateExists = await db.Users.AsNoTracking().AnyAsync(u => u.PhoneE164 == RichGatePhone, ct);
        if (!gateExists) return false;

        var devOwnerId = await db.Users.AsNoTracking()
            .Where(u => u.PhoneE164 == DevLoginOwnerPhone)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        if (devOwnerId is null) return true; // gate var ama dev-login şablonu yok — tutarsız, tazele

        var devShopId = await db.BarberShops.AsNoTracking()
            .Where(s => s.OwnerId == devOwnerId.Value)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (devShopId is null) return true;

        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IstanbulTz);
        var todayLocal = DateOnly.FromDateTime(nowLocal.DateTime);
        var startLocal = new DateTime(todayLocal.Year, todayLocal.Month, todayLocal.Day, 0, 0, 0, DateTimeKind.Unspecified);
        // Npgsql "timestamp with time zone" yalnız Offset=0 (UTC) kabul eder (RichDemoSeeder'ın
        // da karşılaştığı aynı gotcha, bkz. o dosyadaki "now" normalizasyon yorumu) — Istanbul
        // ofsetiyle kurup ToUniversalTime() ile açıkça UTC'ye çeviriyoruz.
        var startUtc = new DateTimeOffset(startLocal, IstanbulTz.GetUtcOffset(startLocal)).ToUniversalTime();
        var endUtc = startUtc.AddDays(1);

        var hasTodayBooking = await db.Bookings.AsNoTracking()
            .AnyAsync(b => b.BarberShopId == devShopId.Value && b.ScheduledAt >= startUtc && b.ScheduledAt < endUtc, ct);
        return !hasTodayBooking;
    }

    /// <summary>
    /// Coğrafya (cities/districts/neighborhoods) ve plan tanımları (subscription_plans/
    /// plan_entitlements) HARİÇ tüm kullanıcı/randevu/ödeme/fatura/yorum/bildirim/KYC
    /// verisini TRUNCATE eder. <c>RESTART IDENTITY CASCADE</c> — CASCADE burada AÇIKÇA
    /// listelenmeyen ama bu tablolara FK ile bağlı çocuk tabloları (ör. device_tokens) da
    /// otomatik temizler (Postgres TRUNCATE ... CASCADE semantiği — DELETE CASCADE'den farklı
    /// olarak referans eden tabloları listeye gerek kalmadan dahil eder); FK'sı OLMAYAN
    /// tablolar (cities/districts/neighborhoods/subscription_plans/plan_entitlements/
    /// platform_settings) bu kapsama hiç GİRMEZ — task'ın "coğrafya ve plan tanımlarına
    /// dokunma" kısıtı bu yüzden TRUNCATE listesine değil, FK grafiğine dayanıyor.
    /// </summary>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        if (env.IsProduction())
            throw new InvalidOperationException(
                "DemoDataResetter.ResetAsync Production ortamında asla çalıştırılamaz (bkz. sınıf XML doc'u).");

        logger.LogWarning("DemoDataResetter: demo verisi TRUNCATE ediliyor (Environment={Env}).", env.EnvironmentName);

        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE
              notification_logs,
              audit_logs,
              subscription_invoice_payments, subscription_invoices,
              shop_subscriptions,
              barber_replies, reviews,
              refunds, payments,
              booking_items, bookings,
              stylist_services, time_offs, working_hours, stylists, service_catalogs,
              kyc_documents, kyc_submissions, barber_shops,
              consents, refresh_tokens, otp_codes, admin_credentials, device_tokens, users
            RESTART IDENTITY CASCADE;
            """, ct);

        // Bu DbContext instance'ı (Scoped) TRUNCATE öncesi hiçbir entity track ETMEMİŞ olabilir
        // (SeedReferenceDataAsync akışında ResetAsync migration'dan hemen sonra, herhangi bir
        // seeder çalışmadan ÖNCE çağrılır) — yine de defensive: raw SQL, ChangeTracker'ın
        // haberi olmadan satırları siler, olası stale tracked entity'lerin sonraki
        // SaveChangesAsync'te "zaten silinmiş satırı UPDATE etmeye çalış" hatası vermesini önler.
        db.ChangeTracker.Clear();

        logger.LogInformation("DemoDataResetter: demo verisi temizlendi (coğrafya/plan tanımları korundu).");
    }
}

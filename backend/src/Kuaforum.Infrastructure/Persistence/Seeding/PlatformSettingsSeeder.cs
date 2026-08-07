using Kuaforum.Domain.Modules.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Persistence.Seeding;

/// <summary>
/// RFC-012: PlatformSettings tek satır olarak var olmalı — admin ekranları (kapora sınırları,
/// banka bilgisi) bunu varsayar. Idempotent: satır zaten varsa (banka alanları hariç) dokunmaz.
/// AdminSeeder/GeographySeeder ile aynı desende, her startup'ta SeedReferenceDataAsync içinde çağrılır.
/// </summary>
public sealed class PlatformSettingsSeeder(ApplicationDbContext db, ILogger<PlatformSettingsSeeder> logger)
{
    // 2026-08-07 founder bulgusu: admin/billing/settings ekranı IBAN/banka/hesap sahibi hep
    // null geliyordu (hiçbir seeder UpdateBankingInfo çağırmıyordu) — founder ödeme kanalını
    // hiç GÖREMİYORDU. Bu değerler gerçek değil, yalnız demo/dev ortamda ekranın dolu
    // görünmesi için; gerçek IBAN admin panelinden PUT /v1/admin/billing/settings ile girilir.
    private const string DemoIban = "TR33 0006 1005 1978 6457 8413 26";
    private const string DemoBankName = "Demo Kalkınma Bankası A.Ş.";
    private const string DemoAccountHolderName = "Kuaforum Teknoloji A.Ş.";

    /// <param name="seedDemoBankInfo">
    /// true iken (herhangi bir Seed:Demo/ComprehensiveDemo/RichDemo flag'i açıksa) banka
    /// alanları hâlâ null ise demo/dev placeholder değerlerle doldurulur — admin GERÇEK bir
    /// değer girdiyse (non-null) ASLA üzerine yazılmaz. appsettings.Production.json'da tüm
    /// Seed:* flag'leri false olduğu için Production'da bu parametre hep false gelir.
    /// </param>
    public async Task SeedAsync(bool seedDemoBankInfo = false, CancellationToken ct = default)
    {
        var settings = await db.Set<PlatformSettings>().FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = PlatformSettings.CreateDefault();
            if (seedDemoBankInfo)
                settings.UpdateBankingInfo(DemoIban, DemoBankName, DemoAccountHolderName);
            db.Set<PlatformSettings>().Add(settings);
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "PlatformSettingsSeeder: varsayılan DepositPercentMin={Min}/Max={Max} ile satır oluşturuldu (demo banka bilgisi: {HasBank}).",
                settings.DepositPercentMin, settings.DepositPercentMax, seedDemoBankInfo);
            return;
        }

        if (seedDemoBankInfo && settings.CommissionPaymentIban is null)
        {
            settings.UpdateBankingInfo(DemoIban, DemoBankName, DemoAccountHolderName);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("PlatformSettingsSeeder: mevcut satıra demo banka bilgisi backfill edildi.");
            return;
        }

        logger.LogDebug("PlatformSettingsSeeder: satır zaten var, atlanıyor.");
    }
}

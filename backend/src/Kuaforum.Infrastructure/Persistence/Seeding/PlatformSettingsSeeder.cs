using Kuaforum.Domain.Modules.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Persistence.Seeding;

/// <summary>
/// RFC-012: PlatformSettings tek satır olarak var olmalı — CloseMonthlyCommissionStatementsJob
/// ve admin ekranları bunu varsayar. Idempotent: satır zaten varsa dokunmaz. AdminSeeder/
/// GeographySeeder ile aynı desende, her startup'ta SeedReferenceDataAsync içinde çağrılır.
/// </summary>
public sealed class PlatformSettingsSeeder(ApplicationDbContext db, ILogger<PlatformSettingsSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var exists = await db.Set<PlatformSettings>().AnyAsync(ct);
        if (exists)
        {
            logger.LogDebug("PlatformSettingsSeeder: satır zaten var, atlanıyor.");
            return;
        }

        // Founder'ın kesin ilk oran kararı implementasyon anında netleşmedi (RFC-012 "Founder
        // Aksiyonları") — %20 yalnız RFC'deki örnek. Admin PUT /v1/admin/billing/settings ile
        // launch öncesi kesinleştirmeli; bu seed değeri yalnız satırın var olmasını garantiler.
        var settings = PlatformSettings.CreateDefault();
        db.Set<PlatformSettings>().Add(settings);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("PlatformSettingsSeeder: varsayılan CommissionRateGlobal={Rate} ile satır oluşturuldu.", settings.CommissionRateGlobal);
    }
}

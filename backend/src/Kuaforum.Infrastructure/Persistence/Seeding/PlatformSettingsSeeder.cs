using Kuaforum.Domain.Modules.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Persistence.Seeding;

/// <summary>
/// RFC-012: PlatformSettings tek satır olarak var olmalı — admin ekranları (kapora sınırları,
/// banka bilgisi) bunu varsayar. Idempotent: satır zaten varsa dokunmaz. AdminSeeder/
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

        var settings = PlatformSettings.CreateDefault();
        db.Set<PlatformSettings>().Add(settings);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PlatformSettingsSeeder: varsayılan DepositPercentMin={Min}/Max={Max} ile satır oluşturuldu.",
            settings.DepositPercentMin, settings.DepositPercentMax);
    }
}

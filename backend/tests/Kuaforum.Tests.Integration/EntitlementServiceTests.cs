using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Infrastructure.Modules.Billing;
using Kuaforum.Infrastructure.Persistence;
using Kuaforum.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// EntitlementService review düzeltmesi: eksik/bozuk entitlement satırları artık (1) yapılandırılmış
/// LogError ile bize görünür kılınıyor, (2) fallback davranışı anahtar TÜRÜNE göre ayrılıyor —
/// kota (max_staff/max_monthly_bookings) sınırsıza (fail-open, owner'ı kilitlememek için), yetenek
/// (can_use_sms/can_use_deposit) false'a (fail-closed, gerçek para hareketi tetikledikleri için)
/// düşüyor. Aboneliksiz salon LAUNCH_FREE'ye düşerken LogWarning yazıyor. Bkz. EntitlementService.cs.
///
/// ÖNEMLİ — paylaşılan reference data izolasyonu: plan_entitlements/subscription_plans,
/// IntegrationTestFactory.ResetDatabaseAsync'in TRUNCATE listesinde YOK (bilinçli — çoğu test dosyası
/// SubscriptionPlanSeeder'ı hiç çağırmadan barber-shop API'sine güveniyor, bu yüzden reference data
/// tüm test run'ı boyunca kalıcı olmalı). Bu sınıf entitlement DEĞERLERİNİ kasıtlı olarak bozduğu için
/// her mutasyonu try/finally ile eski haline döndürür — aksi halde başka test dosyalarına (örn.
/// SubscriptionPlanTests) sızıntı olur (bu, ilk yazımda gerçekten yakalandı: bkz. git history).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class EntitlementServiceTests(IntegrationTestFactory factory)
{
    private async Task SeedAsync()
    {
        await factory.ResetDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var demoSeeder = scope.ServiceProvider.GetRequiredService<DemoBarberSeeder>();
        await demoSeeder.SeedAsync(default);

        var planSeeder = scope.ServiceProvider.GetRequiredService<SubscriptionPlanSeeder>();
        await planSeeder.SeedAsync(default);
    }

    private async Task<Guid> GetFirstShopIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shop = await db.Set<BarberShop>().AsNoTracking().FirstAsync();
        return shop.Id;
    }

    private async Task<string?> GetEntitlementRawAsync(string key)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Set<PlanEntitlement>()
            .AsNoTracking()
            .Where(e => e.PlanCode == SubscriptionPlanCodes.LaunchFree && e.Key == key)
            .Select(e => e.Value)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// rawValue null ise satır tamamen SİLİNİR (eksik satır senaryosu); değilse mevcut satır
    /// güncellenir ya da (silinmiş bir satırı geri koymak için) yeniden oluşturulur — restore
    /// yolunda satır zaten silinmiş olabileceğinden EF üzerinden upsert edilir (ham "INSERT" id
    /// kolonunu atlardı; Entity.Id DB-generated değil, app tarafında üretilir).
    /// </summary>
    private async Task SetEntitlementRawAsync(string key, string? rawValue)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (rawValue is null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM plan_entitlements WHERE plan_code = {SubscriptionPlanCodes.LaunchFree} AND key = {key}");
            return;
        }

        var existing = await db.Set<PlanEntitlement>()
            .FirstOrDefaultAsync(e => e.PlanCode == SubscriptionPlanCodes.LaunchFree && e.Key == key);
        if (existing is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE plan_entitlements SET value = {rawValue} WHERE plan_code = {SubscriptionPlanCodes.LaunchFree} AND key = {key}");
        }
        else
        {
            db.Set<PlanEntitlement>().Add(PlanEntitlement.Create(SubscriptionPlanCodes.LaunchFree, key, rawValue).Value);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// LAUNCH_FREE/{key} satırının orijinal değerini alır, testi mutasyonla çalıştırır, ardından
    /// (satır silinmişse de) MUTLAKA eski haline döndürür — paylaşılan reference data'yı diğer test
    /// dosyalarına sızdırmamak için.
    /// </summary>
    private async Task RunIsolatedAsync(string key, string? mutatedRawValue, Func<Guid, Task> assertions)
    {
        await SeedAsync();
        var shopId = await GetFirstShopIdAsync();
        var original = await GetEntitlementRawAsync(key);
        try
        {
            await SetEntitlementRawAsync(key, mutatedRawValue);
            await assertions(shopId);
        }
        finally
        {
            await SetEntitlementRawAsync(key, original);
        }
    }

    private async Task<(ShopEntitlements Result, ILogger<EntitlementService> Logger)> GetWithLoggerAsync(Guid shopId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = Substitute.For<ILogger<EntitlementService>>();
        var service = new EntitlementService(db, logger);
        var result = await service.GetAsync(shopId);
        return (result, logger);
    }

    private static void AssertErrorLogged(ILogger<EntitlementService> logger, string expectedKey) =>
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString()!.Contains(expectedKey)),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

    private static void AssertNoErrorLogged(ILogger<EntitlementService> logger) =>
        logger.DidNotReceive().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

    // ── Kota anahtarları (max_staff, max_monthly_bookings): eksik/bozuk → sınırsız + LogError ──

    [Fact]
    public Task MissingQuotaKey_FallsBackToUnlimited_AndLogsError() =>
        RunIsolatedAsync(EntitlementKeys.MaxStaff, mutatedRawValue: null, async shopId =>
        {
            var (result, logger) = await GetWithLoggerAsync(shopId);

            Assert.Null(result.MaxStaff); // sınırsız — owner kilitlenmiyor
            AssertErrorLogged(logger, EntitlementKeys.MaxStaff);
        });

    [Fact]
    public Task InvalidQuotaValue_FallsBackToUnlimited_AndLogsError() =>
        RunIsolatedAsync(EntitlementKeys.MaxStaff, "abc", async shopId => // parse edilemeyen değer
        {
            var (result, logger) = await GetWithLoggerAsync(shopId);

            Assert.Null(result.MaxStaff);
            AssertErrorLogged(logger, EntitlementKeys.MaxStaff);
        });

    [Fact]
    public Task MissingMaxMonthlyBookingsKey_FallsBackToUnlimited_AndLogsError() =>
        RunIsolatedAsync(EntitlementKeys.MaxMonthlyBookings, mutatedRawValue: null, async shopId =>
        {
            var (result, logger) = await GetWithLoggerAsync(shopId);

            Assert.Null(result.MaxMonthlyBookings);
            AssertErrorLogged(logger, EntitlementKeys.MaxMonthlyBookings);
        });

    // ── Yetenek anahtarları (can_use_sms, can_use_deposit): eksik/bozuk → false + LogError ────

    [Fact]
    public Task MissingCapabilityKey_FallsBackToFalse_AndLogsError() =>
        RunIsolatedAsync(EntitlementKeys.CanUseSms, mutatedRawValue: null, async shopId =>
        {
            var (result, logger) = await GetWithLoggerAsync(shopId);

            Assert.False(result.CanUseSms); // fail-closed — SMS gerçek para harcar
            AssertErrorLogged(logger, EntitlementKeys.CanUseSms);
        });

    [Fact]
    public Task InvalidCapabilityValue_FallsBackToFalse_AndLogsError() =>
        RunIsolatedAsync(EntitlementKeys.CanUseDeposit, "not-a-bool", async shopId =>
        {
            var (result, logger) = await GetWithLoggerAsync(shopId);

            Assert.False(result.CanUseDeposit); // fail-closed — kapora gerçek para akışı
            AssertErrorLogged(logger, EntitlementKeys.CanUseDeposit);
        });

    // ── Sentinel / geçerli değer davranışı bozulmamış ────────────────────────────────────────

    [Fact]
    public Task NegativeSentinel_IsStillUnlimited_NoErrorLogged() =>
        RunIsolatedAsync(EntitlementKeys.MaxStaff, "-1", async shopId =>
        {
            var (result, logger) = await GetWithLoggerAsync(shopId);

            Assert.Null(result.MaxStaff);
            AssertNoErrorLogged(logger); // geçerli sentinel — veri tutarsızlığı DEĞİL
        });

    [Fact]
    public Task PositiveValue_IsStillThatValue_NoErrorLogged() =>
        RunIsolatedAsync(EntitlementKeys.MaxStaff, "7", async shopId =>
        {
            var (result, logger) = await GetWithLoggerAsync(shopId);

            Assert.Equal(7, result.MaxStaff);
            AssertNoErrorLogged(logger);
        });

    // ── Aboneliksiz salon → LAUNCH_FREE + LogWarning ─────────────────────────────────────────
    // (shop_subscriptions ResetDatabaseAsync'in TRUNCATE listesinde OLDUĞU için burada ayrı bir
    // restore gerekmiyor — bir sonraki testin SeedAsync'i zaten temiz başlıyor.)

    [Fact]
    public async Task ShopWithoutSubscriptionRow_FallsBackToLaunchFree_AndLogsWarning()
    {
        await SeedAsync();
        var shopId = await GetFirstShopIdAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM shop_subscriptions WHERE barber_shop_id = {shopId}");
        }

        var (result, logger) = await GetWithLoggerAsync(shopId);

        Assert.Equal(20, result.MaxStaff); // LAUNCH_FREE varsayılanı
        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString()!.Contains(shopId.ToString())),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}

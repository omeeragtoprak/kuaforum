using System.Net;
using System.Net.Http.Json;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Infrastructure.Persistence;
using Kuaforum.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-014 §2 D1 — sabit aylık abonelik omurgası (SubscriptionPlan/PlanEntitlement/
/// ShopSubscription + IEntitlementService). V1'de herkes LAUNCH_FREE'de.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class SubscriptionPlanTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record CreatedIdDto(Guid Id);

    private async Task SeedAsync()
    {
        await factory.ResetDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var demoSeeder = scope.ServiceProvider.GetRequiredService<DemoBarberSeeder>();
        await demoSeeder.SeedAsync(default);

        // Gerçek startup akışında SubscriptionPlanSeeder demo seeder'lardan SONRA çalışır
        // (bkz. DependencyInjection.SeedReferenceDataAsync) — testte de aynı sırayı taklit ediyoruz.
        var planSeeder = scope.ServiceProvider.GetRequiredService<SubscriptionPlanSeeder>();
        await planSeeder.SeedAsync(default);
    }

    private async Task<int> GetActiveStylistCountAsync(Guid shopId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Set<Stylist>().CountAsync(s => s.BarberShopId == shopId && s.IsActive);
    }

    private async Task SetMaxStaffEntitlementAsync(int value)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE plan_entitlements SET value = {value.ToString()} WHERE plan_code = {SubscriptionPlanCodes.LaunchFree} AND key = {EntitlementKeys.MaxStaff}");
    }

    // ── (b) SubscriptionPlanSeeder idempotent ───────────────────────────

    [Fact]
    public async Task SubscriptionPlanSeeder_RunTwice_DoesNotDuplicateRows()
    {
        await SeedAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<SubscriptionPlanSeeder>();
            await seeder.SeedAsync(default); // ikinci koşum
        }

        using var verifyScope = factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(3, await db.Set<SubscriptionPlan>().CountAsync());
        Assert.Equal(12, await db.Set<PlanEntitlement>().CountAsync()); // 3 plan × 4 entitlement anahtarı

        var launchFree = await db.Set<SubscriptionPlan>().AsNoTracking().SingleAsync(p => p.Code == SubscriptionPlanCodes.LaunchFree);
        Assert.True(launchFree.IsActive);
        Assert.Equal(0, launchFree.PriceMinor);

        var starter = await db.Set<SubscriptionPlan>().AsNoTracking().SingleAsync(p => p.Code == SubscriptionPlanCodes.Starter);
        Assert.False(starter.IsActive);
        Assert.Null(starter.PriceMinor); // fiyat belirlenmedi, 0 DEĞİL
    }

    // ── (a) yeni salon → otomatik LAUNCH_FREE aboneliği ─────────────────

    [Fact]
    public async Task CreateBarberShop_AutomaticallyCreatesLaunchFreeSubscription()
    {
        await SeedAsync();

        var customerClient = factory.CreateClient();
        var login = await customerClient.DevLoginAsync("Customer");
        customerClient.SetBearer(login.AccessToken);

        var createResp = await customerClient.PostAsJsonAsync("/v1/owner/barber-shop", new
        {
            name = "Yeni Test Salonu",
            genderType = "Unisex",
            description = (string?)null,
            phone = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = (await createResp.Content.ReadFromJsonAsync<CreatedIdDto>(TestHelpers.Json))!;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subscription = await db.Set<ShopSubscription>().AsNoTracking().SingleAsync(s => s.BarberShopId == created.Id);
        Assert.Equal(SubscriptionPlanCodes.LaunchFree, subscription.PlanCode);
        Assert.Equal(ShopSubscriptionStatus.Active, subscription.Status);
    }

    /// <summary>Backfill'in ayrıca bütün mevcut salonları kapsadığının kanıtı — DemoBarberSeeder'ın ürettiği TÜM salonlar (CreateBarberShopCommandHandler'ı hiç çağırmayan seeder) sayı olarak eşleşmeli.</summary>
    [Fact]
    public async Task AllExistingShops_AreBackfilledWithASubscription()
    {
        await SeedAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shopCount = await db.Set<BarberShop>().CountAsync();
        var subscriptionCount = await db.Set<ShopSubscription>().CountAsync();

        Assert.True(shopCount > 0);
        Assert.Equal(shopCount, subscriptionCount);
    }

    // ── (c) entitlement okuma ────────────────────────────────────────────

    [Fact]
    public async Task EntitlementService_ReturnsLaunchFreeValues_ForShopWithoutOverride()
    {
        await SeedAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shop = await db.Set<BarberShop>().AsNoTracking().FirstAsync();

        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
        var result = await entitlementService.GetAsync(shop.Id);

        Assert.Equal(20, result.MaxStaff);
        Assert.Null(result.MaxMonthlyBookings); // sentinel "-1" → sınırsız
        Assert.False(result.CanUseSms);
        Assert.True(result.CanUseDeposit);
    }

    [Fact]
    public async Task EntitlementService_ShopWithoutAnySubscriptionRow_FallsBackToLaunchFree()
    {
        await SeedAsync();

        // Bilinçli tutarsız durum simülasyonu: bir shop'un abonelik satırını sil.
        Guid shopId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var shop = await db.Set<BarberShop>().AsNoTracking().FirstAsync();
            shopId = shop.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM shop_subscriptions WHERE barber_shop_id = {shopId}");
        }

        using var verifyScope = factory.Services.CreateScope();
        var entitlementService = verifyScope.ServiceProvider.GetRequiredService<IEntitlementService>();
        var result = await entitlementService.GetAsync(shopId);

        Assert.Equal(20, result.MaxStaff); // LAUNCH_FREE varsayılanı — fail-open değil
    }

    // ── (d) max_staff limiti ─────────────────────────────────────────────

    [Fact]
    public async Task AddStylist_AtOrOverMaxStaff_FailsWithValidationError()
    {
        await SeedAsync();

        var ownerClient = factory.CreateClient();
        var login = await ownerClient.DevLoginAsync("BarberOwner");
        ownerClient.SetBearer(login.AccessToken);
        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        var currentActiveCount = await GetActiveStylistCountAsync(shop.Id);
        await SetMaxStaffEntitlementAsync(currentActiveCount); // limit == mevcut aktif sayı

        var resp = await ownerClient.PostAsJsonAsync($"/v1/owner/barber-shop/{shop.Id}/stylists", new
        {
            displayName = "Limit Aşan Çalışan",
            userId = (Guid?)null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var countAfter = await db.Set<Stylist>().CountAsync(s => s.BarberShopId == shop.Id);
        Assert.Equal(currentActiveCount, countAfter); // yan etki yok, eklenmedi
    }

    [Fact]
    public async Task AddStylist_UnderMaxStaff_Succeeds()
    {
        await SeedAsync();

        var ownerClient = factory.CreateClient();
        var login = await ownerClient.DevLoginAsync("BarberOwner");
        ownerClient.SetBearer(login.AccessToken);
        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        var currentActiveCount = await GetActiveStylistCountAsync(shop.Id);
        await SetMaxStaffEntitlementAsync(currentActiveCount + 1); // tam olarak bir kişilik yer var

        var resp = await ownerClient.PostAsJsonAsync($"/v1/owner/barber-shop/{shop.Id}/stylists", new
        {
            displayName = "Sınır Altı Çalışan",
            userId = (Guid?)null,
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }
}

using System.Net.Http.Json;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Infrastructure.Persistence;
using Kuaforum.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// Review fix: max_staff kotası önceden yalnız AddStylistCommandHandler'da uygulanıyordu.
/// UpdateStylistCommandHandler'ın SetActive(true) dalı kontrolsüzdü — owner limitteyken pasif
/// bir stylist'i aktifleştirerek kotayı sessizce aşabiliyordu. Bu artık ortak
/// StylistLimitPolicy.EnforceMaxStaffAsync ile her iki yolda da uygulanıyor (bkz. StylistCommands.cs).
///
/// LAUNCH_FREE max_staff entitlement'ını bu test dosyasına özgü küçük bir değere geçici olarak
/// indirir — EntitlementServiceTests'in aynı reference-data-izolasyon kalıbı (try/finally ile
/// eski değere geri dönülür, aksi halde koleksiyondaki diğer testlere sızar).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class StylistMaxStaffLimitTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record StylistDto(Guid Id, string DisplayName, bool IsActive);

    private async Task<(HttpClient Owner, Guid ShopId, int InitialActiveCount)> SeedAsync()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var demoSeeder = scope.ServiceProvider.GetRequiredService<DemoBarberSeeder>();
            await demoSeeder.SeedAsync(default);
            var planSeeder = scope.ServiceProvider.GetRequiredService<SubscriptionPlanSeeder>();
            await planSeeder.SeedAsync(default);
        }

        var ownerClient = factory.CreateClient();
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);
        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        int activeCount;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            activeCount = await db.Set<Stylist>().AsNoTracking()
                .CountAsync(s => s.BarberShopId == shop.Id && s.IsActive);
        }

        return (ownerClient, shop.Id, activeCount);
    }

    private async Task SetMaxStaffAsync(int value)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE plan_entitlements SET value = {value.ToString()} WHERE plan_code = {SubscriptionPlanCodes.LaunchFree} AND key = {EntitlementKeys.MaxStaff}");
    }

    private async Task<string?> GetMaxStaffRawAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Set<PlanEntitlement>().AsNoTracking()
            .Where(e => e.PlanCode == SubscriptionPlanCodes.LaunchFree && e.Key == EntitlementKeys.MaxStaff)
            .Select(e => e.Value)
            .FirstOrDefaultAsync();
    }

    [Fact]
    public async Task UpdateStylist_ActivatingPassiveStylist_AtLimit_IsRejected()
    {
        var (owner, shopId, activeCount) = await SeedAsync();
        var original = await GetMaxStaffRawAsync();
        try
        {
            // Kota, mevcut aktif sayıya EŞİT — salon tam limitte.
            await SetMaxStaffAsync(activeCount);

            // Pasif bir stylist ekle (doğrudan DB — AddStylist zaten limitte reddederdi).
            Guid passiveStylistId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var stylist = Stylist.Create(shopId, "Yedek Berber").Value;
                stylist.SetActive(false);
                db.Set<Stylist>().Add(stylist);
                await db.SaveChangesAsync();
                passiveStylistId = stylist.Id;
            }

            var resp = await owner.PatchAsJsonAsync($"/v1/stylists/{passiveStylistId}", new { isActive = true }, TestHelpers.Json);
            Assert.False(resp.IsSuccessStatusCode);

            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stillPassive = await verifyDb.Set<Stylist>().AsNoTracking().SingleAsync(s => s.Id == passiveStylistId);
            Assert.False(stillPassive.IsActive); // yan etki yok
        }
        finally
        {
            await SetMaxStaffAsync(int.Parse(original ?? "20"));
        }
    }

    [Fact]
    public async Task UpdateStylist_ActivatingPassiveStylist_UnderLimit_Succeeds()
    {
        var (owner, shopId, activeCount) = await SeedAsync();
        var original = await GetMaxStaffRawAsync();
        try
        {
            // Kota, mevcut aktif sayıdan BİR FAZLA — tam olarak bir aktivasyona yer var.
            await SetMaxStaffAsync(activeCount + 1);

            Guid passiveStylistId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var stylist = Stylist.Create(shopId, "Yedek Berber").Value;
                stylist.SetActive(false);
                db.Set<Stylist>().Add(stylist);
                await db.SaveChangesAsync();
                passiveStylistId = stylist.Id;
            }

            var resp = await owner.PatchAsJsonAsync($"/v1/stylists/{passiveStylistId}", new { isActive = true }, TestHelpers.Json);
            Assert.True(resp.IsSuccessStatusCode);

            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var nowActive = await verifyDb.Set<Stylist>().AsNoTracking().SingleAsync(s => s.Id == passiveStylistId);
            Assert.True(nowActive.IsActive);
        }
        finally
        {
            await SetMaxStaffAsync(int.Parse(original ?? "20"));
        }
    }

    [Fact]
    public async Task UpdateStylist_ReconfirmingAlreadyActiveStylist_AtLimit_IsNotBlocked()
    {
        // Regresyon kilidi: zaten aktif bir stylist'e tekrar isActive=true göndermek
        // (no-op niyetiyle) kotayı DEĞİŞTİRMEZ — kendi kendini sayıp yanlışlıkla reddetmemeli.
        var (owner, shopId, activeCount) = await SeedAsync();
        var original = await GetMaxStaffRawAsync();
        try
        {
            await SetMaxStaffAsync(activeCount); // tam limitte

            Guid activeStylistId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                activeStylistId = await db.Set<Stylist>().AsNoTracking()
                    .Where(s => s.BarberShopId == shopId && s.IsActive)
                    .Select(s => s.Id)
                    .FirstAsync();
            }

            var resp = await owner.PatchAsJsonAsync($"/v1/stylists/{activeStylistId}", new { isActive = true }, TestHelpers.Json);
            Assert.True(resp.IsSuccessStatusCode);
        }
        finally
        {
            await SetMaxStaffAsync(int.Parse(original ?? "20"));
        }
    }

    [Fact]
    public async Task AddStylist_AtLimit_IsRejected_UnderLimit_Succeeds()
    {
        var (owner, shopId, activeCount) = await SeedAsync();
        var original = await GetMaxStaffRawAsync();
        try
        {
            await SetMaxStaffAsync(activeCount);
            var atLimit = await owner.PostJsonAsync($"/v1/owner/barber-shop/{shopId}/stylists", new { displayName = "Yeni Berber" });
            Assert.False(atLimit.IsSuccessStatusCode);

            await SetMaxStaffAsync(activeCount + 1);
            var underLimit = await owner.PostJsonAsync($"/v1/owner/barber-shop/{shopId}/stylists", new { displayName = "Yeni Berber 2" });
            Assert.True(underLimit.IsSuccessStatusCode);
        }
        finally
        {
            await SetMaxStaffAsync(int.Parse(original ?? "20"));
        }
    }
}

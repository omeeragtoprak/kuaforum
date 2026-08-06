using System.Net;
using System.Net.Http.Json;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// Review'lardan biriken küçük backend follow-up'ları (2026-08-06): admin/owner DTO
/// eksikleri + dönem filtresi. Her madde ayrı, odaklı bir test — büyük fixture'lar
/// gerektirmediğinden CommissionBillingTests/BookingPolicyTests'in ağır ShopFixture
/// kalıbını tekrarlamak yerine kendi minimal kurulumunu yapar.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class BackendFollowUpTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record AdminBarberShopDto(
        Guid Id, string Name, string Slug, string? OwnerDisplayName, string? OwnerPhone,
        BarberShopStatus Status, SalonGenderType GenderType, int StylistCount, int ServiceCount,
        int CompletedBookingsCount, DateTimeOffset CreatedAt, decimal? CommissionRateOverride);
    private sealed record AdminBarberShopsPage(IReadOnlyList<AdminBarberShopDto> Items, int Page, int PageSize, int TotalCount);
    private sealed record BookingPolicyDto(
        bool DepositEnabled, decimal? DepositPercent, bool EffectiveDepositActive,
        int MaxPendingPerCustomer, int? MinDaysBetweenBookings, int? MaxBookingsPerMonth,
        decimal DepositPercentMin, decimal DepositPercentMax, bool OnlinePaymentsEnabled);
    private sealed record BarberShopDetailDto(
        Guid Id, Guid OwnerId, string Name, string Slug, string? Description, string? Phone,
        SalonGenderType GenderType, BarberShopStatus Status, Guid? CityId, Guid? DistrictId,
        Guid? NeighborhoodId, string? AddressLine1, double? Latitude, double? Longitude,
        string? CommissionTier, bool DepositEnabled, decimal? DepositPercent);

    private async Task SeedAsync()
    {
        await factory.ResetDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
        await seeder.SeedAsync(default);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailRes = Kuaforum.Domain.Modules.Identity.ValueObjects.EmailAddress.TryCreate("admin-followup@kuaforum.test");
        var adminRes = Kuaforum.Domain.Modules.Identity.User.CreateAdmin(emailRes.Value, "Test Admin");
        db.Users.Add(adminRes.Value);
        await db.SaveChangesAsync();
    }

    // ── B2: AdminBarberShopDto.CommissionRateOverride ─────────────────────

    [Fact]
    public async Task AdminListBarberShops_ReturnsCommissionRateOverride_NullByDefault_SetValueAfterAdminPut()
    {
        await SeedAsync();
        using var ownerClient = factory.CreateClient();
        using var adminClient = factory.CreateClient();
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);
        adminClient.SetBearer((await adminClient.DevLoginAsync("Admin")).AccessToken);
        var ownerShop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        var before = await adminClient.GetJson<AdminBarberShopsPage>("/v1/admin/barber-shops?pageSize=50");
        var beforeItem = before.Items.Single(x => x.Id == ownerShop.Id);
        Assert.Null(beforeItem.CommissionRateOverride);

        var putResp = await adminClient.PutAsJsonAsync($"/v1/admin/billing/barber-shops/{ownerShop.Id}/commission-rate", new { rateOverride = 0.07m });
        Assert.Equal(HttpStatusCode.NoContent, putResp.StatusCode);

        var after = await adminClient.GetJson<AdminBarberShopsPage>("/v1/admin/barber-shops?pageSize=50");
        var afterItem = after.Items.Single(x => x.Id == ownerShop.Id);
        Assert.Equal(0.07m, afterItem.CommissionRateOverride);
    }

    // ── B4: BookingPolicyDto.OnlinePaymentsEnabled (ham global bayrak) ────

    [Fact]
    public async Task GetBookingPolicy_ReturnsRawOnlinePaymentsEnabledFlag_SeparateFromEffectiveDepositActive()
    {
        await SeedAsync();
        using var ownerClient = factory.CreateClient();
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var policy = await ownerClient.GetJson<BookingPolicyDto>("/v1/owner/barber-shop/booking-policy");

        // IntegrationTestFactory Development env'i Payment:OnlinePaymentsEnabled=true'ya sabitliyor.
        Assert.True(policy.OnlinePaymentsEnabled);
        // DepositEnabled henüz false — EffectiveDepositActive de false, ama global bayrak true
        // olarak AYRI görünür bilgi taşımalı (owner "kapora şu an platformda açık, ayarını
        // aç" mesajını KAYDETMEDEN önce görebilsin).
        Assert.False(policy.DepositEnabled);
        Assert.False(policy.EffectiveDepositActive);
    }

    // ── B5: BookingPolicyCommands — PlatformSettings eksikliği ayrı hata kodu/mesaj ───

    [Fact]
    public async Task GetBookingPolicy_WhenPlatformSettingsRowMissing_Returns500WithDistinctErrorCode_NotShopNotFound()
    {
        await SeedAsync();
        using var ownerClient = factory.CreateClient();
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        // platform_settings tekil satırı ResetDatabaseAsync'in TRUNCATE listesinde YOK
        // (bilerek — CloseMonthlyCommissionStatementsJob ve diğer testler onun her zaman var
        // olduğunu varsayar). Bu yüzden burada silip try/finally ile GERİ eklemeden bırakmak
        // aynı koleksiyondaki SONRAKİ testleri (ör. CommissionBillingTests) kalıcı olarak bozar
        // — bu satırı yalnızca BU test içinde, geçici olarak yok ediyoruz.
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.ExecuteSqlRawAsync("DELETE FROM platform_settings");
            }

            var resp = await ownerClient.GetAsync("/v1/owner/barber-shop/booking-policy");
            Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            // Mesaj "Kuaför bulunamadı" (şirket kendi shop'unun eksik olduğu senaryo) DEĞİL —
            // platformun kendi ayar satırının eksik olduğunu söylemeli, aksi halde owner
            // "salonum silindi mi" diye yanlış alarma geçer.
            Assert.DoesNotContain("Kuaför bulunamadı", body);
            Assert.Contains("Platform ayarları", body);
        }
        finally
        {
            using var restoreScope = factory.Services.CreateScope();
            var seeder = restoreScope.ServiceProvider
                .GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.PlatformSettingsSeeder>();
            await seeder.SeedAsync(default);
        }
    }

    // ── B6: BarberShopDto (id-bazlı, mobile) — depositEnabled + depositPercent ───────

    [Fact]
    public async Task GetBarberShopById_ReturnsEffectiveDepositFields_OnlyWhenDepositEnabledAndGlobalFlagOn()
    {
        await SeedAsync();
        using var ownerClient = factory.CreateClient();
        using var customerClient = factory.CreateClient();
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        var ownerShop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        var before = await customerClient.GetJson<BarberShopDetailDto>($"/v1/barber-shops/{ownerShop.Id}");
        Assert.False(before.DepositEnabled);
        Assert.Null(before.DepositPercent);

        var putResp = await ownerClient.PutAsJsonAsync("/v1/owner/barber-shop/booking-policy", new
        {
            depositEnabled = true,
            depositPercent = 0.15m,
            maxPendingPerCustomer = 2,
            minDaysBetweenBookings = (int?)null,
            maxBookingsPerMonth = (int?)null,
        });
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        var after = await customerClient.GetJson<BarberShopDetailDto>($"/v1/barber-shops/{ownerShop.Id}");
        Assert.True(after.DepositEnabled);
        Assert.Equal(0.15m, after.DepositPercent);

        // Public/owner alan sızıntısı kontrolü kapsam dışı — OwnerId/CommissionTier bu DTO'da
        // zaten (önceden) vardı, bu follow-up'ın konusu değil.
        Assert.Equal(ownerShop.OwnerId, after.OwnerId);
    }
}

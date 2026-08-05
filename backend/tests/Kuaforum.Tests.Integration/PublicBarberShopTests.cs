using System.Net;
using System.Net.Http.Json;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using Kuaforum.Infrastructure.Persistence;
using Kuaforum.Infrastructure.Persistence.Seeding;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-011 backend — public kuaför profil + liste endpoint'leri. Odak: dar
/// <c>PublicBarberShopDto</c> gerçekten dar mı (OwnerId/CommissionTier sızmıyor mu),
/// yalnızca Active salonlar mı görünüyor, ve anonim erişim gerçekten anonim mi.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PublicBarberShopTests(IntegrationTestFactory factory)
{
    public sealed record OwnerShopRow(Guid Id, string Slug, string Name);
    public sealed record CityRow(Guid Id, int PlateCode, string Name, string Slug, int ActiveShopCount);

    public sealed record PublicServiceSummaryRow(string Name, int DurationMinutes, long PriceMinor, string Currency);
    public sealed record PublicWorkingHoursRow(DayOfWeek DayOfWeek, TimeOnly? OpenTime, TimeOnly? CloseTime, bool IsClosed);
    public sealed record PublicShopDetailRow(
        Guid Id,
        string Name,
        string Slug,
        string? Description,
        string? Phone,
        SalonGenderType GenderType,
        string? CityName,
        string? DistrictName,
        string? NeighborhoodName,
        string? AddressLine1,
        double? Latitude,
        double? Longitude,
        double? RatingAverage,
        int RatingCount,
        IReadOnlyList<PublicServiceSummaryRow> Services,
        IReadOnlyList<PublicWorkingHoursRow> WorkingHours,
        IReadOnlyList<string> StylistNames);

    public sealed record PublicListItemRow(Guid Id, string Name, string Slug, string? CityName, string? DistrictName, double? RatingAverage, int RatingCount, int ServiceCount);
    public sealed record PublicListResponseRow(IReadOnlyList<PublicListItemRow> Items, int Page, int PageSize, int TotalCount);

    // GetStylistsByShopQuery/GetServicesByShopQuery'nin (owner panel + eski public liste)
    // DTO'larının bir alt kümesi — yalnızca bu testlerde ihtiyaç duyulanlar.
    public sealed record StylistRow(Guid Id, string DisplayName);
    public sealed record ServiceRow(Guid Id, string Name);

    private async Task SeedDemoAsync()
    {
        using var scope = factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DemoBarberSeeder>();
        await seeder.SeedAsync(default);
    }

    private async Task<OwnerShopRow> GetShopViaOwnerLoginAsync(string devLoginRole)
    {
        using var ownerClient = factory.CreateClient();
        var login = await ownerClient.DevLoginAsync(devLoginRole);
        ownerClient.SetBearer(login.AccessToken);
        return await ownerClient.GetJson<OwnerShopRow>("/v1/owner/barber-shop");
    }

    [Fact]
    public async Task Active_shop_slug_returns_public_dto_without_owner_or_commission_fields()
    {
        await factory.ResetDatabaseAsync();
        await SeedDemoAsync();
        var shop = await GetShopViaOwnerLoginAsync("BarberOwner");

        // Bilinçli olarak ANONİM client — hiçbir Authorization header set edilmiyor.
        using var anonClient = factory.CreateClient();
        var resp = await anonClient.GetAsync($"/v1/public/barber-shops/{shop.Slug}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var rawBody = await resp.Content.ReadAsStringAsync();
        // Kontrat testi (RFC-011): iç alanlar (owner kimliği, komisyon oranı, submerchant
        // anahtarı) hiçbir biçimde JSON gövdesinde bulunmamalı — property adı ne olursa
        // olsun (camelCase serialization) ham string üzerinden case-insensitive arıyoruz.
        Assert.DoesNotContain("ownerId", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("commissionTier", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("subMerchantKey", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("status", rawBody, StringComparison.OrdinalIgnoreCase);

        var dto = await resp.Content.ReadFromJsonAsync<PublicShopDetailRow>(TestHelpers.Json);
        Assert.NotNull(dto);
        Assert.Equal(shop.Id, dto!.Id);
        Assert.Equal(shop.Slug, dto.Slug);
        Assert.Equal("Modern Stüdyo Kuaför", dto.Name);
        Assert.Equal("Kadıköy", dto.DistrictName);
        Assert.NotEmpty(dto.Services);
        Assert.NotEmpty(dto.StylistNames);
        Assert.Equal(7, dto.WorkingHours.Count); // haftanın 7 günü, hepsi dolu (Pzt-Cmt açık, Pazar kapalı)
        Assert.Contains(dto.WorkingHours, w => w.DayOfWeek == DayOfWeek.Sunday && w.IsClosed);
        Assert.Contains(dto.WorkingHours, w => w.DayOfWeek == DayOfWeek.Monday && !w.IsClosed && w.OpenTime == new TimeOnly(9, 0));
    }

    [Fact]
    public async Task Draft_shop_slug_returns_404()
    {
        await factory.ResetDatabaseAsync();
        await SeedDemoAsync();
        var draftShop = await GetShopViaOwnerLoginAsync("BarberOwnerDraft");

        using var anonClient = factory.CreateClient();
        var resp = await anonClient.GetAsync($"/v1/public/barber-shops/{draftShop.Slug}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    /// <summary>
    /// GAP fix (kapsam-dışı bulgu, bu iş kapsamına alındı): <c>GET /v1/barber-shops/{id}/stylists</c>
    /// ve <c>/services</c> — mevcut, RFC-011'den ÖNCE de public/AllowAnonymous olan iki
    /// endpoint — shop Status'unu hiç kontrol etmiyordu; Draft/Suspended bir shop'un
    /// GUID'ini bilen herkes listesini görebiliyordu. Aynı zamanda owner'ın KENDİ
    /// panelinin (mobile (barber)/stylists.tsx, services.tsx) AYNI GET'i kullandığı
    /// doğrulandı — ayrı bir owner-only liste endpoint'i yok. Bu yüzden fix requester-aware:
    /// anonim/başkası → 404, shop sahibinin kendisi (Bearer token) → 200.
    /// </summary>
    [Fact]
    public async Task Draft_shop_stylists_and_services_hidden_from_anonymous_but_owner_still_manages()
    {
        await factory.ResetDatabaseAsync();
        await SeedDemoAsync();

        using var draftClient = factory.CreateClient();
        var draftLogin = await draftClient.DevLoginAsync("BarberOwnerDraft");
        draftClient.SetBearer(draftLogin.AccessToken);
        var draftShop = await draftClient.GetJson<OwnerShopRow>("/v1/owner/barber-shop");

        // Owner kendi Draft shop'una stilist + servis ekleyebiliyor (mevcut davranış,
        // bu fix'le kırılmamalı) — AddStylistCommand/CreateServiceCommand shop status
        // kontrolü yapmıyor, onboarding sihirbazı zaten Draft aşamasında bunları kurduruyor.
        var addStylist = await draftClient.PostAsJsonAsync($"/v1/owner/barber-shop/{draftShop.Id}/stylists",
            new { displayName = "Taslak Stilist", userId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Created, addStylist.StatusCode);

        var addService = await draftClient.PostAsJsonAsync($"/v1/owner/barber-shop/{draftShop.Id}/services", new
        {
            name = "Taslak Hizmet",
            gender = "Any",
            durationMinutes = 30,
            priceMinor = 20000L,
            bufferMinutes = 5,
            description = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, addService.StatusCode);

        // Anonim: 404 — Draft shop'un varlığı bile sızdırılmıyor.
        using var anonClient = factory.CreateClient();
        var anonStylists = await anonClient.GetAsync($"/v1/barber-shops/{draftShop.Id}/stylists");
        var anonServices = await anonClient.GetAsync($"/v1/barber-shops/{draftShop.Id}/services");
        Assert.Equal(HttpStatusCode.NotFound, anonStylists.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, anonServices.StatusCode);

        // Owner: kendi Draft shop'unu yönetmeye devam edebiliyor — 200 + az önce eklenenler görünür.
        var ownerStylists = await draftClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{draftShop.Id}/stylists");
        var ownerServices = await draftClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{draftShop.Id}/services");
        Assert.Contains(ownerStylists, s => s.DisplayName == "Taslak Stilist");
        Assert.Contains(ownerServices, s => s.Name == "Taslak Hizmet");

        // Başka bir yetkisiz kullanıcı (Customer token'ı, shop sahibi DEĞİL) da 404 almalı —
        // salt "authenticated" olmak yetmez, tam da O shop'un sahibi olmak gerekiyor.
        using var otherCustomer = factory.CreateClient();
        var customerLogin = await otherCustomer.DevLoginAsync("Customer");
        otherCustomer.SetBearer(customerLogin.AccessToken);
        var otherStylists = await otherCustomer.GetAsync($"/v1/barber-shops/{draftShop.Id}/stylists");
        Assert.Equal(HttpStatusCode.NotFound, otherStylists.StatusCode);
    }

    [Fact]
    public async Task Suspended_shop_stylists_and_services_hidden_from_anonymous_but_owner_still_sees_them()
    {
        await factory.ResetDatabaseAsync();
        await SeedDemoAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var email = EmailAddress.TryCreate("admin-suspend-subresource@kuaforum.test").Value;
            db.Users.Add(User.CreateAdmin(email, "Suspend Subresource Admin").Value);
            await db.SaveChangesAsync(default);
        }

        var ownerShop = await GetShopViaOwnerLoginAsync("BarberOwner"); // Active demo shop, önceden stilist+servis dolu

        using var adminClient = factory.CreateClient();
        adminClient.SetBearer((await adminClient.DevLoginAsync("Admin")).AccessToken);
        var suspend = await adminClient.PostAsJsonAsync($"/v1/admin/barber-shops/{ownerShop.Id}/suspend",
            new { reason = "GAP fix testi — askıdaki shop'un alt-kaynakları anonime kapalı olmalı." });
        Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);

        using var anonClient = factory.CreateClient();
        var anonStylists = await anonClient.GetAsync($"/v1/barber-shops/{ownerShop.Id}/stylists");
        var anonServices = await anonClient.GetAsync($"/v1/barber-shops/{ownerShop.Id}/services");
        Assert.Equal(HttpStatusCode.NotFound, anonStylists.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, anonServices.StatusCode);

        using var ownerClient = factory.CreateClient();
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);
        var ownerStylistsResp = await ownerClient.GetAsync($"/v1/barber-shops/{ownerShop.Id}/stylists");
        var ownerServicesResp = await ownerClient.GetAsync($"/v1/barber-shops/{ownerShop.Id}/services");
        Assert.Equal(HttpStatusCode.OK, ownerStylistsResp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownerServicesResp.StatusCode);
    }

    [Fact]
    public async Task Suspended_shop_slug_returns_404()
    {
        await factory.ResetDatabaseAsync();
        await SeedDemoAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var email = EmailAddress.TryCreate("admin-public-shop@kuaforum.test").Value;
            db.Users.Add(User.CreateAdmin(email, "Public Test Admin").Value);
            await db.SaveChangesAsync(default);
        }

        var shop = await GetShopViaOwnerLoginAsync("BarberOwner");

        using var adminClient = factory.CreateClient();
        var adminLogin = await adminClient.DevLoginAsync("Admin");
        adminClient.SetBearer(adminLogin.AccessToken);
        var suspend = await adminClient.PostAsJsonAsync($"/v1/admin/barber-shops/{shop.Id}/suspend",
            new { reason = "RFC-011 test — public profil askıda salonu göstermemeli." });
        Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);

        using var anonClient = factory.CreateClient();
        var resp = await anonClient.GetAsync($"/v1/public/barber-shops/{shop.Slug}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Unknown_slug_returns_404()
    {
        await factory.ResetDatabaseAsync();

        using var anonClient = factory.CreateClient();
        var resp = await anonClient.GetAsync("/v1/public/barber-shops/bu-slug-hic-var-olmadi-xyz");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task List_paginates_active_shops_only()
    {
        await factory.ResetDatabaseAsync();
        await SeedDemoAsync();

        using var anonClient = factory.CreateClient();
        var page1 = await anonClient.GetJson<PublicListResponseRow>("/v1/public/barber-shops?page=1&pageSize=5");
        Assert.Equal(1, page1.Page);
        Assert.Equal(5, page1.PageSize);
        Assert.Equal(5, page1.Items.Count);
        // DemoBarberSeeder: 13 shop, 1 tanesi Draft (Şişli) → 12 Active bekleniyor.
        Assert.Equal(12, page1.TotalCount);

        var page2 = await anonClient.GetJson<PublicListResponseRow>("/v1/public/barber-shops?page=2&pageSize=5");
        Assert.Equal(5, page2.Items.Count);
        Assert.Empty(page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)));

        Assert.DoesNotContain(page1.Items.Concat(page2.Items), i => i.Name.Contains("Draft"));
    }

    [Fact]
    public async Task List_filters_by_city()
    {
        await factory.ResetDatabaseAsync();
        await SeedDemoAsync();

        using var anonClient = factory.CreateClient();
        var cities = await anonClient.GetJson<IReadOnlyList<CityRow>>("/v1/geography/cities");
        var erzurum = cities.Single(c => c.PlateCode == 25);

        var result = await anonClient.GetJson<PublicListResponseRow>($"/v1/public/barber-shops?cityId={erzurum.Id}&pageSize=20");
        Assert.True(result.TotalCount >= 6, $"Erzurum'da en az 6 demo aktif kuaför bekleniyor, bulunan: {result.TotalCount}");
        Assert.All(result.Items, i => Assert.Equal("Erzurum", i.CityName));
    }

    [Fact]
    public async Task Anonymous_client_without_authorization_header_gets_200_not_401()
    {
        await factory.ResetDatabaseAsync();
        await SeedDemoAsync();
        var shop = await GetShopViaOwnerLoginAsync("BarberOwner");

        using var anonClient = factory.CreateClient();
        Assert.Null(anonClient.DefaultRequestHeaders.Authorization); // hiç token yok

        var detailResp = await anonClient.GetAsync($"/v1/public/barber-shops/{shop.Slug}");
        var listResp = await anonClient.GetAsync("/v1/public/barber-shops");

        Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
    }
}

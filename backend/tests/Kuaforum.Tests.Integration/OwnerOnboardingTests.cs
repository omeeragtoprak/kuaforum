using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-007 Task 1: self-serve owner onboarding — Customer bir işletme oluşturduğunda
/// AYNI transaction içinde BarberOwner'a terfi eder (atomik rol geçişi).
///
/// Temiz-müşteri stratejisi: ResetDatabaseAsync tüm kullanıcıları TRUNCATE eder;
/// dev-login "Customer" kullanıcı yoksa sıfırdan yaratır (+905551234567, shop'suz).
/// Bu yüzden seed'e gerek yok — her test taze, işletmesiz bir Customer ile başlar.
///
/// "Stylist işletme oluşturamaz" davranışı: UserRole.Stylist domain'de hiçbir meşru
/// yolla atanamadığından (bkz. UserTests notu) handler'ın "Customer değil VE
/// BarberOwner değil → Forbidden" dalı Admin rolüyle test ediliyor — aynı dal
/// Stylist'i de kapsar.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class OwnerOnboardingTests(IntegrationTestFactory factory)
{
    public sealed record CreatedResp(Guid Id);

    private static Task<HttpResponseMessage> PostCreateShopAsync(HttpClient client, string name) =>
        client.PostAsJsonAsync("/v1/owner/barber-shop", new
        {
            name,
            genderType = "MaleOnly",
            description = (string?)null,
            phone = "+904420000001",
        }, TestHelpers.Json);

    [Fact]
    public async Task Customer_create_shop_promotes_to_barber_owner_and_creates_draft()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        Assert.Equal("Customer", login.Role); // ön koşul: reset sonrası temiz, işletmesiz müşteri
        client.SetBearer(login.AccessToken);

        var create = await PostCreateShopAsync(client, "Test Salon");
        if (create.StatusCode != HttpStatusCode.Created)
        {
            var body = await create.Content.ReadAsStringAsync();
            Assert.Fail($"Create shop failed: {(int)create.StatusCode} body={body}");
        }
        var shopId = (await create.Content.ReadFromJsonAsync<CreatedResp>(TestHelpers.Json))!.Id;
        Assert.NotEqual(Guid.Empty, shopId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == login.UserId);
        var shop = await db.Set<BarberShop>().AsNoTracking().FirstAsync(s => s.Id == shopId);
        Assert.Equal(UserRole.BarberOwner, user.Role);
        Assert.Equal(BarberShopStatus.Draft, shop.Status);
        Assert.Equal(login.UserId, shop.OwnerId);
    }

    [Fact]
    public async Task Second_shop_returns_409()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        var first = await PostCreateShopAsync(client, "İlk Salon");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await PostCreateShopAsync(client, "İkinci Salon");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Set<BarberShop>().CountAsync(s => s.OwnerId == login.UserId));
    }

    [Fact]
    public async Task Refresh_after_promotion_issues_token_with_new_role()
    {
        await factory.ResetDatabaseAsync();

        const string fingerprint = "onboarding-refresh-fp";
        using var client = factory.CreateClient();

        // Fingerprint'i AÇIKÇA gönder — refresh endpoint'i device-bound eşleşme ister,
        // dev-login default'u connection id içerdiğinden istekler arasında değişir.
        var loginResp = await client.PostAsJsonAsync("/v1/auth/dev-login",
            new { role = "Customer", deviceFingerprint = fingerprint }, TestHelpers.Json);
        loginResp.EnsureSuccessStatusCode();
        var login = (await loginResp.Content.ReadFromJsonAsync<TestHelpers.DevLoginResponse>(TestHelpers.Json))!;
        client.SetBearer(login.AccessToken);
        Assert.Equal("Customer", ReadJwtClaim(login.AccessToken, "role"));

        var create = await PostCreateShopAsync(client, "Refresh Test Salon");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var refreshResp = await client.PostAsJsonAsync("/v1/auth/refresh",
            new { refreshToken = login.RefreshToken, deviceFingerprint = fingerprint }, TestHelpers.Json);
        if (refreshResp.StatusCode != HttpStatusCode.OK)
        {
            var body = await refreshResp.Content.ReadAsStringAsync();
            Assert.Fail($"Refresh failed: {(int)refreshResp.StatusCode} body={body}");
        }
        var refreshed = (await refreshResp.Content.ReadFromJsonAsync<TestHelpers.DevLoginResponse>(TestHelpers.Json))!;

        Assert.Equal("BarberOwner", ReadJwtClaim(refreshed.AccessToken, "role"));
        Assert.Equal("BarberOwner", refreshed.User.Role);
    }

    /// <summary>
    /// Handler'ın "Customer değil VE BarberOwner değil → Forbidden" dalı: Admin bu dala
    /// düşer (Stylist de aynı dala düşer — meşru yolla Stylist user kurulamadığından
    /// dal Admin ile pinleniyor).
    /// </summary>
    [Fact]
    public async Task Admin_create_shop_returns_403_and_creates_nothing()
    {
        await factory.ResetDatabaseAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var email = EmailAddress.TryCreate("admin-onboarding@kuaforum.test").Value;
            db.Users.Add(User.CreateAdmin(email, "Onboarding Admin").Value);
            await db.SaveChangesAsync(default);
        }

        using var adminClient = factory.CreateClient();
        var login = await adminClient.DevLoginAsync("Admin");
        adminClient.SetBearer(login.AccessToken);

        var create = await PostCreateShopAsync(adminClient, "Yasak Salon");
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db2.Set<BarberShop>().CountAsync());
        var admin = await db2.Users.AsNoTracking().FirstAsync(u => u.Id == login.UserId);
        Assert.Equal(UserRole.Admin, admin.Role); // rol dokunulmamış
    }

    /// <summary>
    /// GAP P1-4: owner grubu "BarberOwner" policy'sinde — Customer token'ı 403 alır.
    /// İstisna: create-shop (RFC-007 rol geçiş tetiği) ayrı grupta, Customer çağırabilir.
    /// </summary>
    [Fact]
    public async Task Customer_gets_403_on_owner_endpoints_but_can_create_shop()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        var patch = await client.PatchAsJsonAsync($"/v1/owner/barber-shop/{Guid.NewGuid()}",
            new { name = "Hacklenen Salon" }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);

        var kycStatus = await client.GetAsync("/v1/owner/barber-shop/kyc/status");
        Assert.Equal(HttpStatusCode.Forbidden, kycStatus.StatusCode);

        // İstisna: create-shop Customer'a açık — promote burada tetikleniyor.
        var create = await PostCreateShopAsync(client, "Policy İstisna Salonu");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    }

    [Fact]
    public async Task Customer_gets_403_on_stylist_endpoints()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        var patch = await client.PatchAsJsonAsync($"/v1/stylists/{Guid.NewGuid()}",
            new { displayName = "Sahte Stilist" }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
    }

    /// <summary>
    /// Mobil sihirbazın 2-3. adımlarının kanıtı: create-shop sonrası REFRESH şart —
    /// eski access token'ın role claim'i hâlâ "Customer" olduğundan owner endpoint'leri
    /// 403 döner (stale-claim pin'i); taze token'la 200.
    /// </summary>
    [Fact]
    public async Task Promoted_owner_with_fresh_token_can_call_owner_endpoints()
    {
        await factory.ResetDatabaseAsync();

        const string fingerprint = "onboarding-policy-fp";
        using var client = factory.CreateClient();

        var loginResp = await client.PostAsJsonAsync("/v1/auth/dev-login",
            new { role = "Customer", deviceFingerprint = fingerprint }, TestHelpers.Json);
        loginResp.EnsureSuccessStatusCode();
        var login = (await loginResp.Content.ReadFromJsonAsync<TestHelpers.DevLoginResponse>(TestHelpers.Json))!;
        client.SetBearer(login.AccessToken);

        var create = await PostCreateShopAsync(client, "Fresh Token Salonu");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // ESKİ token (role=Customer) ile owner endpoint'i hâlâ 403 — refresh'in NEDEN zorunlu olduğu.
        var staleCall = await client.GetAsync("/v1/owner/barber-shop/kyc/status");
        Assert.Equal(HttpStatusCode.Forbidden, staleCall.StatusCode);

        var refreshResp = await client.PostAsJsonAsync("/v1/auth/refresh",
            new { refreshToken = login.RefreshToken, deviceFingerprint = fingerprint }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);
        var refreshed = (await refreshResp.Content.ReadFromJsonAsync<TestHelpers.DevLoginResponse>(TestHelpers.Json))!;

        client.SetBearer(refreshed.AccessToken);
        var freshCall = await client.GetAsync("/v1/owner/barber-shop/kyc/status");
        Assert.Equal(HttpStatusCode.OK, freshCall.StatusCode);
    }

    /// <summary>
    /// Tek-shop unique index (ux_barber_shops_owner): çift-submit yarışında AnyAsync
    /// precheck'i iki isteği de geçirebilir — DB son savunma hattı, kaybeden 409 alır.
    /// Farklı adlar bilinçli: slug unique'ine değil owner unique'ine çarpması için.
    /// </summary>
    [Fact]
    public async Task Concurrent_create_shop_yields_single_shop()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        var task1 = PostCreateShopAsync(client, "Yarış Salonu A");
        var task2 = PostCreateShopAsync(client, "Yarış Salonu B");
        var responses = await Task.WhenAll(task1, task2);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Set<BarberShop>().CountAsync(s => s.OwnerId == login.UserId));

        // G5: kazanan istekte de kaybeden istekte de owner'ın oto-eklenen stylist'i yarıştan
        // etkilenmemeli — tam olarak 1 stylist (kaybedenin SaveChanges'i hiç commit olmadığı için).
        Assert.Equal(1, await db.Set<Stylist>().CountAsync(s => s.UserId == login.UserId));
    }

    /// <summary>
    /// O2 review M: ux_barber_shops_owner unique index'inin DB-seviyesi kanıtı —
    /// HTTP yarışı timing'e bağlı, bu test deterministik: raw DbContext ile aynı
    /// OwnerId'ye ikinci shop insert → 23505 unique_violation.
    /// </summary>
    [Fact]
    public async Task Owner_unique_index_rejects_second_shop_at_db_level()
    {
        await factory.ResetDatabaseAsync();

        var ownerId = Guid.NewGuid(); // owner_id'de users FK yok — plain column + unique index
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Set<BarberShop>().Add(BarberShop.Create(ownerId, "Index Kanıt Salonu 1", SalonGenderType.MaleOnly).Value);
        await db.SaveChangesAsync(default);

        db.Set<BarberShop>().Add(BarberShop.Create(ownerId, "Index Kanıt Salonu 2", SalonGenderType.MaleOnly).Value);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(default));

        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState); // 23505
        Assert.Equal("ux_barber_shops_owner", pg.ConstraintName);
    }

    // ---------------------------------------------------------------
    // GAPS.md G5 — owner tek-çalışan senaryosunda otomatik Stylist + WorkingHours
    // ---------------------------------------------------------------

    public sealed record SearchItemDto(Guid Id, string Name, int StylistCount, bool HasWalkInAvailable);
    public sealed record SearchRespDto(IReadOnlyList<SearchItemDto> Items, int Page, int PageSize, int TotalCount);

    /// <summary>
    /// G5 çekirdek doğrulama: shop oluşurken owner AYNI SaveChanges içinde aktif ilk Stylist
    /// olarak eklenir (adı DevLogin "Customer" persona'sının DisplayName'i, "Demo Müşteri")
    /// VE bu stylist için 7 günlük WorkingHours satırı da (Pzt-Cmt 09:00-19:00, Pazar kapalı)
    /// aynı transaction'da oluşur. Yalnız Stylist eklemek yetmezdi — WorkingHours olmadan
    /// ComputeAvailabilityQuery hiç slot üretmez, salon yine "randevu alınamaz" kalırdı.
    /// </summary>
    [Fact]
    public async Task CreateShop_AutoAddsOwnerAsActiveStylist_WithDefaultWorkingHours()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        var create = await PostCreateShopAsync(client, "Oto Stilist Salonu");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var shopId = (await create.Content.ReadFromJsonAsync<CreatedResp>(TestHelpers.Json))!.Id;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var stylists = await db.Set<Stylist>().AsNoTracking().Where(s => s.BarberShopId == shopId).ToListAsync();
        var stylist = Assert.Single(stylists);
        Assert.True(stylist.IsActive);
        Assert.Equal(login.UserId, stylist.UserId);
        Assert.Equal("Demo Müşteri", stylist.DisplayName); // DevLoginCommand'in Customer persona DisplayName'i

        var workingHours = await db.Set<WorkingHours>().AsNoTracking()
            .Where(w => w.StylistId == stylist.Id).ToListAsync();
        Assert.Equal(7, workingHours.Count);
        Assert.All(workingHours.Where(w => w.DayOfWeek != DayOfWeek.Sunday), w =>
        {
            Assert.False(w.IsClosed);
            Assert.Equal(new TimeOnly(9, 0), w.OpenTime);
            Assert.Equal(new TimeOnly(19, 0), w.CloseTime);
        });
        Assert.True(workingHours.Single(w => w.DayOfWeek == DayOfWeek.Sunday).IsClosed);
    }

    /// <summary>
    /// DevLogin OTP kayıtlarının tamamı DisplayName ile geliyor ama gerçek OTP-only kayıtta
    /// (RFC-005) müşteri hiç isim girmeden Customer olarak var olabilir — DisplayName null
    /// kalabilir. Bu durumda handler shop adını fallback olarak kullanır (owner adı yerine
    /// hiçbir ad göstermemek/Stylist.Create'i başarısız bırakmaktan iyi).
    /// </summary>
    [Fact]
    public async Task CreateShop_OwnerWithoutDisplayName_FallsBackToShopNameForStylist()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE users SET display_name = NULL WHERE id = {login.UserId}");
        }

        var create = await PostCreateShopAsync(client, "İsimsiz Owner Salonu");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var shopId = (await create.Content.ReadFromJsonAsync<CreatedResp>(TestHelpers.Json))!.Id;

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stylist = await verifyDb.Set<Stylist>().AsNoTracking().SingleAsync(s => s.BarberShopId == shopId);
        Assert.Equal("İsimsiz Owner Salonu", stylist.DisplayName);
    }

    /// <summary>
    /// Atomiklik kanıtı: farklı iki owner AYNI ismi kullanınca ikincisi ux_barber_shops_slug'a
    /// çarpar (DbUpdateException, SaveChanges'te). O ana kadar context'e eklenmiş olan
    /// BarberShop + ShopSubscription + Stylist + WorkingHours hiçbiri commit edilmemeli —
    /// tek SaveChangesAsync çağrısının hepsini kapsadığının kanıtı.
    /// </summary>
    [Fact]
    public async Task Failed_shop_creation_due_to_slug_conflict_leaves_no_orphan_stylist()
    {
        await factory.ResetDatabaseAsync();

        using var clientA = factory.CreateClient();
        using var clientB = factory.CreateClient();
        var loginA = await clientA.DevLoginAsync("Customer");
        clientA.SetBearer(loginA.AccessToken);
        var loginB = await clientB.DevLoginAsync("CustomerFresh");
        clientB.SetBearer(loginB.AccessToken);

        var respA = await PostCreateShopAsync(clientA, "Çakışan İsim Salonu");
        Assert.Equal(HttpStatusCode.Created, respA.StatusCode);

        var respB = await PostCreateShopAsync(clientB, "Çakışan İsim Salonu");
        Assert.Equal(HttpStatusCode.Conflict, respB.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Set<BarberShop>().CountAsync(s => s.OwnerId == loginB.UserId));
        Assert.Equal(0, await db.Set<Stylist>().CountAsync(s => s.UserId == loginB.UserId));

        // Kazanan tarafta yalnızca kendi tek shop/subscription/stylist'i var — kaybedenden
        // hiçbir satır sızmamış (toplamlar tam olarak "1 başarılı istek" kadar).
        Assert.Equal(1, await db.Set<BarberShop>().CountAsync());
        Assert.Equal(1, await db.Set<ShopSubscription>().CountAsync());
        var shopA = await db.Set<BarberShop>().AsNoTracking().SingleAsync(s => s.OwnerId == loginA.UserId);
        Assert.Equal(1, await db.Set<Stylist>().CountAsync(s => s.BarberShopId == shopA.Id));
    }

    /// <summary>
    /// G5'in "nihai hedef"i: KYC onayından geçip Active olan yeni bir salon, hiçbir manuel
    /// "+ Yeni çalışan" adımı olmadan arama sonuçlarında StylistCount≥1 ve (walk-in açılırsa)
    /// HasWalkInAvailable=true görünür. KYC belge akışını burada tekrarlamıyoruz (KycFlowTests
    /// zaten kapsıyor) — Draft→PendingKyc→Active geçişini doğrudan domain metoduyla yapıyoruz,
    /// bu testin odağı KYC değil "oto-stylist + search görünürlüğü" ilişkisi.
    /// </summary>
    [Fact]
    public async Task Newly_created_shop_is_bookable_in_search_after_activation()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        var create = await PostCreateShopAsync(client, "Aranabilir Yeni Salon");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var shopId = (await create.Content.ReadFromJsonAsync<CreatedResp>(TestHelpers.Json))!.Id;

        Guid stylistId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var shop = await db.Set<BarberShop>().SingleAsync(s => s.Id == shopId);
            Assert.True(shop.MarkPendingKyc().IsSuccess);
            Assert.True(shop.ApproveKyc("test-sub-merchant-key").IsSuccess);
            await db.SaveChangesAsync(default);

            stylistId = await db.Set<Stylist>().AsNoTracking()
                .Where(s => s.BarberShopId == shopId).Select(s => s.Id).SingleAsync();
        }

        var searchResp = await client.GetJson<SearchRespDto>("/v1/barber-shops/search?pageSize=50");
        var item = Assert.Single(searchResp.Items, i => i.Id == shopId);
        Assert.True(item.StylistCount >= 1, "Yeni aktive edilen salon en az 1 aktif stylist ile görünmeli.");

        // Walk-in açıldığında da görünür olmalı — G5'in tam kapsadığı davranış.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stylist = await db.Set<Stylist>().SingleAsync(s => s.Id == stylistId);
            Assert.True(stylist.ToggleWalkIn(true).IsSuccess);
            await db.SaveChangesAsync(default);
        }

        var walkInResp = await client.GetJson<SearchRespDto>("/v1/barber-shops/search?walkInOnly=true&pageSize=50");
        Assert.Contains(walkInResp.Items, i => i.Id == shopId && i.HasWalkInAvailable);
    }

    // ---------------------------------------------------------------
    // RFC-007 kapanış: adres adımı — mahalle OPSİYONEL (Erzurum pilot)
    // ---------------------------------------------------------------

    private sealed record GeoIds(Guid CityId, Guid DistrictId, Guid NeighborhoodId);

    /// <summary>Seed'li Erzurum/Yakutiye + ilk mahallesi. ResetDatabaseAsync geography'yi TRUNCATE ETMEZ.</summary>
    private async Task<GeoIds> GetErzurumGeoAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var erzurum = await db.Cities.AsNoTracking().FirstAsync(c => c.PlateCode == 25);
        var yakutiye = await db.Districts.AsNoTracking().FirstAsync(d => d.CityId == erzurum.Id && d.Name == "Yakutiye");
        var mahalle = await db.Neighborhoods.AsNoTracking().FirstAsync(n => n.DistrictId == yakutiye.Id);
        return new GeoIds(erzurum.Id, yakutiye.Id, mahalle.Id);
    }

    /// <summary>Customer → create shop → refresh (BarberOwner token) → (client, shopId).</summary>
    private async Task<(HttpClient Client, Guid ShopId)> CreateOwnerClientWithShopAsync(string shopName, string fingerprint)
    {
        var client = factory.CreateClient();
        var loginResp = await client.PostAsJsonAsync("/v1/auth/dev-login",
            new { role = "Customer", deviceFingerprint = fingerprint }, TestHelpers.Json);
        loginResp.EnsureSuccessStatusCode();
        var login = (await loginResp.Content.ReadFromJsonAsync<TestHelpers.DevLoginResponse>(TestHelpers.Json))!;
        client.SetBearer(login.AccessToken);

        var create = await PostCreateShopAsync(client, shopName);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var shopId = (await create.Content.ReadFromJsonAsync<CreatedResp>(TestHelpers.Json))!.Id;

        var refreshResp = await client.PostAsJsonAsync("/v1/auth/refresh",
            new { refreshToken = login.RefreshToken, deviceFingerprint = fingerprint }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);
        var refreshed = (await refreshResp.Content.ReadFromJsonAsync<TestHelpers.DevLoginResponse>(TestHelpers.Json))!;
        client.SetBearer(refreshed.AccessToken);

        return (client, shopId);
    }

    [Fact]
    public async Task Set_address_without_neighborhood_returns_204()
    {
        await factory.ResetDatabaseAsync();
        var geo = await GetErzurumGeoAsync();
        var (client, shopId) = await CreateOwnerClientWithShopAsync("Mahallesiz Salon", "addr-no-nb-fp");
        using var _ = client;

        var resp = await client.PutAsJsonAsync($"/v1/owner/barber-shop/{shopId}/address", new
        {
            cityId = geo.CityId,
            districtId = geo.DistrictId,
            neighborhoodId = (Guid?)null,
            line1 = "Cumhuriyet Cad. No:12",
            latitude = 39.9086,
            longitude = 41.2769,
        }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shop = await db.Set<BarberShop>().AsNoTracking().FirstAsync(s => s.Id == shopId);
        Assert.Null(shop.AddressNeighborhoodId);
        Assert.Equal("Cumhuriyet Cad. No:12", shop.AddressLine1);
    }

    [Fact]
    public async Task Set_address_with_seeded_erzurum_neighborhood_returns_204()
    {
        await factory.ResetDatabaseAsync();
        var geo = await GetErzurumGeoAsync();
        var (client, shopId) = await CreateOwnerClientWithShopAsync("Mahalleli Salon", "addr-nb-fp");
        using var _ = client;

        var resp = await client.PutAsJsonAsync($"/v1/owner/barber-shop/{shopId}/address", new
        {
            cityId = geo.CityId,
            districtId = geo.DistrictId,
            neighborhoodId = geo.NeighborhoodId,
            line1 = "Lalapaşa Mah. Berber Sok. 3",
            latitude = 39.9086,
            longitude = 41.2769,
        }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shop = await db.Set<BarberShop>().AsNoTracking().FirstAsync(s => s.Id == shopId);
        Assert.Equal(geo.NeighborhoodId, shop.AddressNeighborhoodId);
    }

    [Fact]
    public async Task Set_address_with_unknown_neighborhood_returns_400()
    {
        await factory.ResetDatabaseAsync();
        var geo = await GetErzurumGeoAsync();
        var (client, shopId) = await CreateOwnerClientWithShopAsync("Sahte Mahalle Salonu", "addr-bad-nb-fp");
        using var _ = client;

        var resp = await client.PutAsJsonAsync($"/v1/owner/barber-shop/{shopId}/address", new
        {
            cityId = geo.CityId,
            districtId = geo.DistrictId,
            neighborhoodId = Guid.NewGuid(), // var olmayan mahalle
            line1 = "Hayalet Mah. 1",
            latitude = 39.9086,
            longitude = 41.2769,
        }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shop = await db.Set<BarberShop>().AsNoTracking().FirstAsync(s => s.Id == shopId);
        Assert.Null(shop.AddressLine1); // adres yazılmamış olmalı
    }

    /// <summary>JWT payload'ını base64url decode edip claim okur — ek paket yok.</summary>
    private static string? ReadJwtClaim(string jwt, string claimName)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        switch (payload.Length % 4)
        {
            case 2: payload += "=="; break;
            case 3: payload += "="; break;
        }
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return doc.RootElement.TryGetProperty(claimName, out var value) ? value.GetString() : null;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

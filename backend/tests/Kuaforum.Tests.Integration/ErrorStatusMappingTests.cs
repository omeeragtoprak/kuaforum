using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// Hata türü → HTTP durum kodu eşlemesinin UÇTAN UCA kanıtı (G19, 2026-08-11).
///
/// NEDEN AYRI BİR DOSYA: eşleme eskiden her uçta elle yazılıyordu ve tutarsızdı —
/// 108 `Results.Problem` çağrısının 103'ü "forbidden" kodunu bilmiyor (sahiplik reddi
/// 400 dönüyordu), 24'ü ise statusCode'u hiç vermiyordu (ASP.NET varsayılanı 500 —
/// sıradan kullanıcı hataları SUNUCU HATASI olarak dönüyordu). Eşleme
/// `Api/Common/ResultHttp.cs`'te tek noktaya alındı; bu testler o noktanın gerçekten
/// tek nokta olduğunu ve uçların ona bağlı kaldığını sabitler.
///
/// Buradaki her test bir hata TÜRÜNÜ temsil eder, tek bir ucu değil. Yeni bir uç eski
/// ad-hoc kalıba dönerse bu testlerden biri kırılır.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ErrorStatusMappingTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);

    /// <summary>
    /// forbidden → 403. G19'un bildirilen kusuru tam olarak buydu: kuaför sahibi
    /// BAŞKASININ salonunu değiştirmeye kalktığında 400 dönüyordu ("gönderdiğin veri
    /// bozuk"), oysa veri doğru, yetki yok. 400 dönmesi hem istemciye yanlış mesaj
    /// gösterir hem de izlemede yetki ihlallerini doğrulama gürültüsüne gömer.
    /// </summary>
    [Fact]
    public async Task Updating_another_owners_shop_returns_403_not_400()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var ownerClient = factory.CreateClient();
        var ownerLogin = await ownerClient.DevLoginAsync("BarberOwner");
        ownerClient.SetBearer(ownerLogin.AccessToken);

        var ownShop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        // Kontrol: kendi salonunda aynı istek geçmeli. Bu olmadan 403 anlamsız —
        // isteğin kendisi bozuk olduğu için mi reddedildiğini ayırt edemezdik.
        var own = await ownerClient.PatchAsJsonAsync($"/v1/owner/barber-shop/{ownShop.Id}",
            new { name = "Kendi Salonum", description = "Güncellendi." });
        Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);

        // Var olan ama BAŞKASINA ait bir salon. Rastgele Guid kullanılsaydı 404 dönerdi
        // ve yetki yolu hiç çalışmazdı — testin anlamı kaybolurdu.
        var otherShopId = await FindShopNotOwnedByAsync(ownShop.OwnerId);
        var other = await ownerClient.PatchAsJsonAsync($"/v1/owner/barber-shop/{otherShopId}",
            new { name = "Başkasının Salonu", description = "Yetkim yok." });

        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    }

    /// <summary>
    /// not_found → 404. Var olmayan kaynak istendiğinde 400 dönmemeli.
    /// </summary>
    [Fact]
    public async Task Missing_resource_returns_404_not_400()
    {
        // ResetDatabaseAsync ÇAĞRILMIYOR: bu test seed'e ihtiyaç duymuyor ve reset
        // edip yeniden doldurmadığı için koleksiyondaki SONRAKİ testleri boş
        // veritabanıyla bırakırdı (GeographyTests bu şekilde kırılmıştı).
        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        var resp = await client.GetAsync($"/v1/bookings/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    /// <summary>
    /// validation → 400, ve KRİTİK OLARAK 500 DEĞİL. Refactor öncesi 24 uç statusCode
    /// vermiyordu; ASP.NET `Results.Problem(detail)` varsayılanı 500 olduğu için
    /// sıradan bir doğrulama hatası sunucu arızası gibi dönüyordu. Mobil istemci 5xx'i
    /// geçici arıza sayıp "tekrar dene" gösteriyor (mobile/src/lib/classify-error.ts),
    /// yani kullanıcı asla gerçek sebebi görmüyordu.
    /// </summary>
    [Fact]
    public async Task Validation_failure_returns_400_and_never_500()
    {
        // ResetDatabaseAsync ÇAĞRILMIYOR: bu test seed'e ihtiyaç duymuyor ve reset
        // edip yeniden doldurmadığı için koleksiyondaki SONRAKİ testleri boş
        // veritabanıyla bırakırdı (GeographyTests bu şekilde kırılmıştı).
        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        // 80 karakter sınırını aşan isim: kesin bir doğrulama hatası (validator
        // MaximumLength(80)). Kullanıcı hatasıdır, sunucu arızası değil.
        //
        // NOT: burada BOŞ isim ("") kullanılmıyor — o istek 204 dönüyor, çünkü
        // User.NormalizeDisplayName boş metni null'a çeviriyor (User.cs:204), yani
        // "adımı temizle" olarak yorumlanıyor. Bunun kasıtlı mı kusur mu olduğu bir
        // ÜRÜN kararı (kullanıcı adını silebilmeli mi? kuaför "isimsiz müşteri" görür)
        // — GAPS G20 olarak kaydedildi, burada varsayım yapılmadı.
        var resp = await client.PatchAsJsonAsync("/v1/users/me", new { displayName = new string('a', 81) });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, resp.StatusCode);
    }

    /// <summary>
    /// Kimliksiz istek → 401. Yetki politikası olan bir uca token'sız gidildiğinde
    /// 400/500 değil 401 dönmeli (bu yol ASP.NET'in kendi authentication katmanından
    /// geçer, ResultHttp'den değil — eşlemenin onu bozmadığını sabitler).
    /// </summary>
    [Fact]
    public async Task Anonymous_request_to_protected_endpoint_returns_401()
    {
        // ResetDatabaseAsync ÇAĞRILMIYOR: bu test seed'e ihtiyaç duymuyor ve reset
        // edip yeniden doldurmadığı için koleksiyondaki SONRAKİ testleri boş
        // veritabanıyla bırakırdı (GeographyTests bu şekilde kırılmıştı).
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/v1/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private async Task<Guid> FindShopNotOwnedByAsync(Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
        var shop = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.BarberShops.Where(s => s.OwnerId != ownerId));
        Assert.NotNull(shop);
        return shop!.Id;
    }
}

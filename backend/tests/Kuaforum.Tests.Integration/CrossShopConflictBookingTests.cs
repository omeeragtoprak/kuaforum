using System.Net;
using System.Net.Http.Json;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-014 §2 D4 / docs/GAPS.md G1 — müşterinin aynı zaman aralığına BAŞKA bir salondan
/// (cross-shop) çakışan randevusu varsa backend bunu <c>InitiateBookingResponse.ConflictingBooking</c>
/// alanında BİLDİRİR ama randevuyu REDDETMEZ (founder kararı — meşru senaryo: eş/çocuk adına
/// randevu, art arda iki farklı hizmet). Odak: (1) çakışma varken randevu YİNE oluşuyor + bilgi
/// dönüyor, (2) çakışma yokken alan null, (3) aynı salondaki (aynı stylist) çakışma davranışı
/// bu değişiklikten ETKİLENMEMİŞ — hâlâ 409/Conflict ile reddediliyor, (4) sınır durumu: bir
/// randevunun bitiş anı diğerinin başlangıç anına eşitse çakışma SAYILMAZ.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CrossShopConflictBookingTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record ConflictingBookingResp(Guid BookingId, string BarberShopName, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt);
    private sealed record InitiateResp(
        Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt,
        long TotalAmountMinor, string Currency, string Status, bool AutoConfirmEnabled,
        ConflictingBookingResp? ConflictingBooking);

    private sealed record ShopFixture(HttpClient CustomerClient, Guid ShopId, string ShopName, Guid StylistId, Guid ServiceId);

    // ── Fixture setup (BookingPolicyTests.SeedShopAndAuthAsync ile aynı desen) ──────────────

    private async Task<ShopFixture> SeedShopAndAuthAsync()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        var ownerClient = factory.CreateClient();
        var customerClient = factory.CreateClient();
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);

        var ownerShop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        using var scope2 = factory.Services.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stylist = await db.Set<Stylist>().AsNoTracking().FirstAsync(s => s.BarberShopId == ownerShop.Id);
        var service = await db.Set<ServiceCatalog>().AsNoTracking().FirstAsync(s => s.BarberShopId == ownerShop.Id);

        return new ShopFixture(customerClient, ownerShop.Id, ownerShop.Name, stylist.Id, service.Id);
    }

    /// <summary>
    /// İkinci, FARKLI bir Active demo shop (BookingPolicyTests.InitiateBooking_GlobalThreePendingCap_StillBindsAcrossDifferentShops
    /// ile aynı desen) — cross-shop senaryoyu kurmak için gerekli.
    /// </summary>
    private async Task<ShopFixture> GetOtherShopAsync(Guid excludeShopId, HttpClient customerClient)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var otherShop = await db.Set<BarberShop>().AsNoTracking()
            .FirstAsync(s => s.Id != excludeShopId && s.Status == BarberShopStatus.Active);
        var stylist = await db.Set<Stylist>().AsNoTracking().FirstAsync(s => s.BarberShopId == otherShop.Id);
        var service = await db.Set<ServiceCatalog>().AsNoTracking().FirstAsync(s => s.BarberShopId == otherShop.Id);
        return new ShopFixture(customerClient, otherShop.Id, otherShop.Name, stylist.Id, service.Id);
    }

    private sealed record CheckConflictResp(ConflictingBookingResp? ConflictingBooking);

    private static async Task<CheckConflictResp> CheckConflictAsync(
        HttpClient customer, Guid barberShopId, DateTimeOffset scheduledAt, DateTimeOffset scheduledEndsAt)
    {
        var resp = await customer.PostAsJsonAsync("/v1/bookings/check-conflict", new
        {
            barberShopId,
            scheduledAt,
            scheduledEndsAt,
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<CheckConflictResp>(TestHelpers.Json))!;
    }

    private static async Task<InitiateResp> InitiateAsync(HttpClient customer, Guid stylistId, Guid serviceId, DateTimeOffset scheduledAt)
    {
        var resp = await customer.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt,
            serviceCatalogIds = new[] { serviceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        if (resp.StatusCode != HttpStatusCode.Created)
        {
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Fail($"Initiate failed: {(int)resp.StatusCode} body={body}");
        }
        return (await resp.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json))!;
    }

    private static DateTimeOffset FutureWeekdayUtc(int minDaysAhead, int hourUtc = 10)
    {
        var date = DateTime.UtcNow.Date;
        var remaining = minDaysAhead;
        while (remaining > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek != DayOfWeek.Sunday) remaining--;
        }
        return new DateTimeOffset(date.Year, date.Month, date.Day, hourUtc, 0, 0, TimeSpan.Zero);
    }

    // ── (1) Çakışma varken bilgi döner VE randevu YİNE oluşur ────────────────────────────────

    [Fact]
    public async Task ConflictingCrossShopBooking_StillCreatesBooking_AndReturnsConflictInfo()
    {
        var shopA = await SeedShopAndAuthAsync();
        var shopB = await GetOtherShopAsync(shopA.ShopId, shopA.CustomerClient);

        var anchor = FutureWeekdayUtc(70);
        var first = await InitiateAsync(shopA.CustomerClient, shopA.StylistId, shopA.ServiceId, anchor);
        Assert.Null(first.ConflictingBooking);

        // Shop B'de TAM AYNI saatte ikinci bir randevu — zaman aralıkları kesinlikle kesişiyor.
        var second = await InitiateAsync(shopB.CustomerClient, shopB.StylistId, shopB.ServiceId, anchor);

        Assert.Equal("Pending", second.Status); // reddedilmedi, normal akış
        Assert.NotNull(second.ConflictingBooking);
        Assert.Equal(first.BookingId, second.ConflictingBooking!.BookingId);
        Assert.Equal(shopA.ShopName, second.ConflictingBooking.BarberShopName);
        Assert.Equal(first.ScheduledAt, second.ConflictingBooking.ScheduledAt);
        Assert.Equal(first.ScheduledEndsAt, second.ConflictingBooking.ScheduledEndsAt);

        // Server-side de doğrula — her iki randevu da gerçekten tabloda, biri diğerini iptal etmedi.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var count = await db.Set<Kuaforum.Domain.Modules.Booking.Booking>().AsNoTracking()
            .CountAsync(b => b.Id == first.BookingId || b.Id == second.BookingId);
        Assert.Equal(2, count);
    }

    // ── (2) Çakışma yokken alan null ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NonConflictingCrossShopBooking_ConflictingBookingIsNull()
    {
        var shopA = await SeedShopAndAuthAsync();
        var shopB = await GetOtherShopAsync(shopA.ShopId, shopA.CustomerClient);

        await InitiateAsync(shopA.CustomerClient, shopA.StylistId, shopA.ServiceId, FutureWeekdayUtc(70));

        // Açıkça farklı, kesişmeyen bir gün.
        var second = await InitiateAsync(shopB.CustomerClient, shopB.StylistId, shopB.ServiceId, FutureWeekdayUtc(75));

        Assert.Null(second.ConflictingBooking);
    }

    // ── (3) Aynı salon (aynı stylist) çakışması — REGRESYON, davranış değişmedi ─────────────

    [Fact]
    public async Task SameShopSameStylistConflict_StillRejectedWithConflict_Unaffected()
    {
        var shopA = await SeedShopAndAuthAsync();

        var anchor = FutureWeekdayUtc(70);
        var first = await shopA.CustomerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = shopA.StylistId,
            scheduledAt = anchor,
            serviceCatalogIds = new[] { shopA.ServiceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Aynı stylist'e aynı anda ikinci istek — yeni cross-shop mantığından ETKİLENMEMELİ,
        // hâlâ busy-range/EXCLUDE constraint tarafından reddedilmeli.
        var second = await shopA.CustomerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = shopA.StylistId,
            scheduledAt = anchor,
            serviceCatalogIds = new[] { shopA.ServiceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ── (4) Sınır durumu: bitiş anı == başlangıç anı çakışma SAYILMAZ ────────────────────────

    [Fact]
    public async Task BackToBack_EndEqualsStart_IsNotFlaggedAsConflict()
    {
        var shopA = await SeedShopAndAuthAsync();
        var shopB = await GetOtherShopAsync(shopA.ShopId, shopA.CustomerClient);

        var first = await InitiateAsync(shopA.CustomerClient, shopA.StylistId, shopA.ServiceId, FutureWeekdayUtc(70));

        // Shop B'deki randevu TAM shop A'nın bitiş anında başlıyor — arka arkaya, çakışma yok.
        var second = await InitiateAsync(shopB.CustomerClient, shopB.StylistId, shopB.ServiceId, first.ScheduledEndsAt);

        Assert.Null(second.ConflictingBooking);
    }

    // ── (5) Ön-kontrol ucu: randevu OLUŞTURULMADAN önce çakışma bilgisi dönüyor ──────────────
    // ── (coordinator review — RFC-014 §2 D4 madde 1: kapora tahsilat+iade sürtünmesini önler)

    [Fact]
    public async Task CheckConflict_BeforeBookingExists_ReturnsSameInfoAsInitiateWouldLaterReturn()
    {
        var shopA = await SeedShopAndAuthAsync();
        var shopB = await GetOtherShopAsync(shopA.ShopId, shopA.CustomerClient);

        var anchor = FutureWeekdayUtc(70);
        var first = await InitiateAsync(shopA.CustomerClient, shopA.StylistId, shopA.ServiceId, anchor);

        // Randevu HENÜZ oluşturulmadı (shop B'de) — yalnız ön-kontrol çağrılıyor.
        var preCheck = await CheckConflictAsync(shopB.CustomerClient, shopB.ShopId, anchor, anchor.AddMinutes(30));
        Assert.NotNull(preCheck.ConflictingBooking);
        Assert.Equal(first.BookingId, preCheck.ConflictingBooking!.BookingId);
        Assert.Equal(shopA.ShopName, preCheck.ConflictingBooking.BarberShopName);
        Assert.Equal(first.ScheduledAt, preCheck.ConflictingBooking.ScheduledAt);
        Assert.Equal(first.ScheduledEndsAt, preCheck.ConflictingBooking.ScheduledEndsAt);

        // Şimdi gerçekten oluştur — "son söz" olan InitiateBookingResponse.ConflictingBooking
        // AYNI bilgiyi taşımalı (aynı CrossShopConflictFinder çağrısı, iki yol arasında fark yok).
        var second = await InitiateAsync(shopB.CustomerClient, shopB.StylistId, shopB.ServiceId, anchor);
        Assert.NotNull(second.ConflictingBooking);
        Assert.Equal(preCheck.ConflictingBooking.BookingId, second.ConflictingBooking!.BookingId);
        Assert.Equal(preCheck.ConflictingBooking.BarberShopName, second.ConflictingBooking.BarberShopName);
        Assert.Equal(preCheck.ConflictingBooking.ScheduledAt, second.ConflictingBooking.ScheduledAt);
        Assert.Equal(preCheck.ConflictingBooking.ScheduledEndsAt, second.ConflictingBooking.ScheduledEndsAt);
    }

    [Fact]
    public async Task CheckConflict_NoConflict_ReturnsNull()
    {
        var shopA = await SeedShopAndAuthAsync();
        var shopB = await GetOtherShopAsync(shopA.ShopId, shopA.CustomerClient);

        await InitiateAsync(shopA.CustomerClient, shopA.StylistId, shopA.ServiceId, FutureWeekdayUtc(70));

        // Açıkça farklı, kesişmeyen bir gün.
        var precheck = await CheckConflictAsync(shopB.CustomerClient, shopB.ShopId, FutureWeekdayUtc(75), FutureWeekdayUtc(75).AddMinutes(30));

        Assert.Null(precheck.ConflictingBooking);
    }

    [Fact]
    public async Task CheckConflict_SameShopAsTarget_NotFlaggedAsConflict()
    {
        var shopA = await SeedShopAndAuthAsync();

        var anchor = FutureWeekdayUtc(70);
        var first = await InitiateAsync(shopA.CustomerClient, shopA.StylistId, shopA.ServiceId, anchor);

        // Hedef salon shop A'nın KENDİSİ — cross-shop mantığı bunu hariç tutar (aynı stylist
        // çakışması zaten oluşturma anında EXCLUDE constraint/busy-range ile ayrı ele alınıyor).
        var precheck = await CheckConflictAsync(shopA.CustomerClient, shopA.ShopId, first.ScheduledAt, first.ScheduledEndsAt);

        Assert.Null(precheck.ConflictingBooking);
    }

    [Fact]
    public async Task CheckConflict_DoesNotLeakOtherCustomersBooking()
    {
        var shopA = await SeedShopAndAuthAsync();
        var shopB = await GetOtherShopAsync(shopA.ShopId, shopA.CustomerClient);

        // "Customer" persona shop A'da randevu alıyor.
        var first = await InitiateAsync(shopA.CustomerClient, shopA.StylistId, shopA.ServiceId, FutureWeekdayUtc(70));

        // TAMAMEN FARKLI bir müşteri (CustomerFresh — ayrı telefon/kullanıcı) AYNI zaman
        // aralığı için shop B'de ön-kontrol çağırıyor. Kendi randevusu olmadığından null
        // dönmeli — "Customer"ın randevu bilgisi (salon adı + saat) sızmamalı.
        var otherCustomerClient = factory.CreateClient();
        otherCustomerClient.SetBearer((await otherCustomerClient.DevLoginAsync("CustomerFresh")).AccessToken);

        var precheck = await CheckConflictAsync(otherCustomerClient, shopB.ShopId, first.ScheduledAt, first.ScheduledEndsAt);

        Assert.Null(precheck.ConflictingBooking);
    }
}

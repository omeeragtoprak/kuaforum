using System.Net;
using System.Net.Http.Json;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-013 C3: per-shop kapora ayarı + dinamik anti-spam kuralları. Odak: (1) owner
/// GET/PUT round-trip + audit + admin-aralık doğrulaması, (2) global
/// Payment:OnlinePaymentsEnabled master-switch'in DepositEnabled'i SAKLAMASI ama
/// effectiveDepositActive'i FALSE yapması, (3) InitiateBookingCommand'ın per-shop
/// MaxPendingPerCustomer/MinDaysBetweenBookings/MaxBookingsPerMonth kurallarını mevcut
/// GLOBAL 3-Pending tabanının YANINDA (onun yerine değil) uygulaması, (4) sayım
/// semantiği — NoShow dahil, Cancelled hariç (koordinatörce onaylanan ara karar).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class BookingPolicyTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    private sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    private sealed record InitiateResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    private sealed record BookingPolicyDto(
        bool DepositEnabled, decimal? DepositPercent, bool EffectiveDepositActive,
        int MaxPendingPerCustomer, int? MinDaysBetweenBookings, int? MaxBookingsPerMonth,
        decimal DepositPercentMin, decimal DepositPercentMax);
    private sealed record PublicShopDetailRow(Guid Id, bool DepositEnabled, decimal? DepositPercent);

    private sealed record ShopFixture(
        HttpClient OwnerClient, HttpClient CustomerClient,
        Guid ShopId, Guid StylistId, Guid ServiceId, string ServiceName, int Duration, int Buffer, long Price,
        Guid CustomerId, string ShopSlug);

    // ── Fixture setup ───────────────────────────────────────────────────

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
        var customerLogin = await customerClient.DevLoginAsync("Customer");
        customerClient.SetBearer(customerLogin.AccessToken);

        var ownerShop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        Guid stylistId, serviceId;
        string serviceName, shopSlug;
        int duration, buffer;
        long price;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stylist = await db.Set<Stylist>().AsNoTracking().FirstAsync(s => s.BarberShopId == ownerShop.Id);
            var service = await db.Set<ServiceCatalog>().AsNoTracking().FirstAsync(s => s.BarberShopId == ownerShop.Id);
            var shop = await db.Set<BarberShop>().AsNoTracking().FirstAsync(s => s.Id == ownerShop.Id);
            stylistId = stylist.Id;
            serviceId = service.Id;
            serviceName = service.Name;
            duration = service.DurationMinutes;
            buffer = service.BufferMinutes;
            price = service.PriceMinor;
            shopSlug = shop.Slug;
        }

        return new ShopFixture(ownerClient, customerClient, ownerShop.Id, stylistId, serviceId, serviceName, duration, buffer, price, customerLogin.UserId, shopSlug);
    }

    /// <summary>Owner tarafından PUT /booking-policy — testlerin kendi aralarında bağımsız kalması için her senaryo kendi policy'sini set eder.</summary>
    private static async Task<HttpResponseMessage> PutPolicyAsync(
        HttpClient owner, bool depositEnabled, decimal? depositPercent,
        int maxPendingPerCustomer, int? minDaysBetweenBookings, int? maxBookingsPerMonth)
    {
        return await owner.PutAsJsonAsync("/v1/owner/barber-shop/booking-policy", new
        {
            depositEnabled,
            depositPercent,
            maxPendingPerCustomer,
            minDaysBetweenBookings,
            maxBookingsPerMonth,
        });
    }

    /// <summary>
    /// Direkt aggregate üretimi (CommissionBillingTests.CreateBookingAsync ile aynı desen) —
    /// InitiateBookingCommand'ın kendisini test ETMEDİĞİMİZ senaryolarda (yalnız "var olan
    /// geçmiş randevu" durumunu kurmak istediğimizde) gerçek HTTP akışını atlayıp doğrudan
    /// domain aggregate'i kaydeder. CustomerId gerçek dev-login customer'ı olmalı ki
    /// InitiateBookingCommand'ın sayım sorguları bu satırı görsün.
    /// </summary>
    private async Task<Guid> CreateBookingDirectAsync(ShopFixture f, BookingStatus targetStatus, DateTimeOffset scheduledAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var services = new (Guid, string, int, int, long)[] { (f.ServiceId, f.ServiceName, f.Duration, f.Buffer, f.Price) };
        var booking = BookingAggregate.Initiate(f.CustomerId, f.ShopId, f.StylistId, scheduledAt, services, BookingType.Scheduled, 0.10m).Value;

        switch (targetStatus)
        {
            case BookingStatus.Pending:
                break;
            case BookingStatus.Cancelled:
                Assert.True(booking.Cancel(BookingCancellationReason.CustomerRequested).IsSuccess);
                break;
            case BookingStatus.Confirmed:
                Assert.True(booking.OwnerConfirm().IsSuccess);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(targetStatus));
        }

        db.Set<BookingAggregate>().Add(booking);
        await db.SaveChangesAsync();

        if (targetStatus == BookingStatus.NoShow)
        {
            // Booking.MarkNoShow domain guard'ı ScheduledEndsAt'in geçmişte olmasını ister —
            // fixture yalnızca terminal Status'e ihtiyaç duyduğundan raw SQL ile set edilir
            // (CommissionBillingTests.CreateBookingAsync'in aynı deseni).
        }

        return booking.Id;
    }

    private async Task MarkNoShowDirectAsync(Guid bookingId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE bookings SET status = {(int)BookingStatus.NoShow} WHERE id = {bookingId}");
    }

    /// <summary>
    /// Pazar hariç, en az minDaysAhead gün sonrası — stylist'in kapalı gününe denk gelmez.
    /// hourUtc=10 (13:00 İstanbul) bilinçli seçim: fixture'lardaki servis DB sorguları
    /// (FirstAsync, ORDER BY'sız) hangi hizmeti döndüreceğini garanti etmez — demo veri
    /// setinde en uzun hizmet 150+5dk (Saç Boyama Röfle) sürüyor; 13:00 başlangıç 19:00
    /// kapanışa kadar bu süreyi rahatça sığdırır (14:00 başlangıçla arada bir flaky
    /// "çalışma saatleri dışında" hatası yakalandı — bkz. commit mesajı).
    /// </summary>
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

    /// <summary>
    /// Pazartesi (en az minDaysAhead gün sonra) — MinDaysBetweenBookings testlerinde +3/+8 gün
    /// eklendiğinde de Pazar'a denk gelmeyeceği garanti (Pzt+3=Perşembe, Pzt+8=sonraki Salı).
    /// </summary>
    private static DateTimeOffset FutureMondayUtc(int minDaysAhead)
    {
        var date = DateTime.UtcNow.Date.AddDays(minDaysAhead);
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return new DateTimeOffset(date.Year, date.Month, date.Day, 10, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// Ayın 5. veya 6. günü (Pazar'a denk gelirse kaydırılır), en az monthsAhead+1 ay sonra —
    /// MaxBookingsPerMonth testlerinde "+2 gün" eklendiğinde ay sınırını AŞMAYACAĞI garanti
    /// (FutureMondayUtc kullanılsaydı, ayın son günlerine denk gelen bir Pazartesi +2 gün
    /// eklendiğinde sessizce bir sonraki aya taşıp testi flaky yapardı).
    /// </summary>
    private static DateTimeOffset SafeMonthStartWeekdayUtc(int monthsAhead)
    {
        var target = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 5, 0, 0, 0, DateTimeKind.Utc).AddMonths(monthsAhead + 1);
        while (target.DayOfWeek == DayOfWeek.Sunday) target = target.AddDays(1);
        return new DateTimeOffset(target.Year, target.Month, target.Day, 10, 0, 0, TimeSpan.Zero);
    }

    // ── GET/PUT round-trip + audit ─────────────────────────────────────

    [Fact]
    public async Task GetBookingPolicy_NewShop_ReturnsDefaults()
    {
        var f = await SeedShopAndAuthAsync();

        var policy = await f.OwnerClient.GetJson<BookingPolicyDto>("/v1/owner/barber-shop/booking-policy");

        Assert.False(policy.DepositEnabled);
        Assert.Null(policy.DepositPercent);
        Assert.False(policy.EffectiveDepositActive);
        Assert.Equal(2, policy.MaxPendingPerCustomer);
        Assert.Null(policy.MinDaysBetweenBookings);
        Assert.Null(policy.MaxBookingsPerMonth);
        Assert.Equal(0.05m, policy.DepositPercentMin);
        Assert.Equal(0.50m, policy.DepositPercentMax);
    }

    [Fact]
    public async Task PutBookingPolicy_RoundTrips_AndWritesAuditLog()
    {
        var f = await SeedShopAndAuthAsync();

        var putResp = await PutPolicyAsync(f.OwnerClient, depositEnabled: true, depositPercent: 0.20m,
            maxPendingPerCustomer: 1, minDaysBetweenBookings: 7, maxBookingsPerMonth: 5);
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
        var putDto = (await putResp.Content.ReadFromJsonAsync<BookingPolicyDto>(TestHelpers.Json))!;

        Assert.True(putDto.DepositEnabled);
        Assert.Equal(0.20m, putDto.DepositPercent);
        // IntegrationTestFactory Development env'i Payment:OnlinePaymentsEnabled=true'ya
        // sabitliyor (RFC-006 suite'i regresyonsuz çalışsın diye) — bu yüzden burada true.
        Assert.True(putDto.EffectiveDepositActive);
        Assert.Equal(1, putDto.MaxPendingPerCustomer);
        Assert.Equal(7, putDto.MinDaysBetweenBookings);
        Assert.Equal(5, putDto.MaxBookingsPerMonth);

        var getDto = await f.OwnerClient.GetJson<BookingPolicyDto>("/v1/owner/barber-shop/booking-policy");
        Assert.Equal(putDto, getDto);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasAudit = await db.Set<AuditLog>().AsNoTracking()
            .AnyAsync(a => a.ActionType == "barber.shop.booking_policy.updated" && a.ResourceId == f.ShopId.ToString());
        Assert.True(hasAudit, "booking-policy PUT audit kaydı bulunamadı.");
    }

    [Fact]
    public async Task PutBookingPolicy_DepositPercentOutOfAdminRange_Returns400()
    {
        var f = await SeedShopAndAuthAsync();

        var resp = await PutPolicyAsync(f.OwnerClient, depositEnabled: true, depositPercent: 0.99m,
            maxPendingPerCustomer: 2, minDaysBetweenBookings: null, maxBookingsPerMonth: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PutBookingPolicy_MaxPendingPerCustomerAbovePlatformCeiling_Returns400()
    {
        var f = await SeedShopAndAuthAsync();

        var resp = await PutPolicyAsync(f.OwnerClient, depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: 4, minDaysBetweenBookings: null, maxBookingsPerMonth: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PutBookingPolicy_DepositEnabled_GlobalFlagOff_SavesButEffectiveDepositActiveFalse()
    {
        // RFC-013 madde 1: global Payment:OnlinePaymentsEnabled=false iken DepositEnabled=true
        // SET EDİLEBİLİR (owner ilerisi için hazırlanabilir) ama etkisizdir.
        var disabledFactory = new OnlinePaymentsDisabledFactory();
        await disabledFactory.InitializeAsync();
        try
        {
            await disabledFactory.ResetDatabaseAsync();
            using (var scope = disabledFactory.Services.CreateScope())
            {
                var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
                await seeder.SeedAsync(default);
            }

            using var ownerClient = disabledFactory.CreateClient();
            ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

            var putResp = await PutPolicyAsync(ownerClient, depositEnabled: true, depositPercent: 0.20m,
                maxPendingPerCustomer: 2, minDaysBetweenBookings: null, maxBookingsPerMonth: null);
            Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
            var putDto = (await putResp.Content.ReadFromJsonAsync<BookingPolicyDto>(TestHelpers.Json))!;

            Assert.True(putDto.DepositEnabled, "DepositEnabled kaydedilmeliydi (master-switch yalnız etkinliği durdurur, kaydı değil).");
            Assert.False(putDto.EffectiveDepositActive, "Global flag kapalıyken effectiveDepositActive true olmamalı.");

            var getDto = await ownerClient.GetJson<BookingPolicyDto>("/v1/owner/barber-shop/booking-policy");
            Assert.True(getDto.DepositEnabled);
            Assert.False(getDto.EffectiveDepositActive);
        }
        finally
        {
            await disabledFactory.DisposeAsync();
        }
    }

    // ── Public DTO ───────────────────────────────────────────────────────

    [Fact]
    public async Task PublicBarberShop_ShowsDepositFields_OnlyWhenEffectivelyActive()
    {
        var f = await SeedShopAndAuthAsync();

        var beforePut = await f.CustomerClient.GetJson<PublicShopDetailRow>($"/v1/public/barber-shops/{f.ShopSlug}");
        Assert.False(beforePut.DepositEnabled);
        Assert.Null(beforePut.DepositPercent);

        await PutPolicyAsync(f.OwnerClient, depositEnabled: true, depositPercent: 0.25m,
            maxPendingPerCustomer: 2, minDaysBetweenBookings: null, maxBookingsPerMonth: null);

        var afterPut = await f.CustomerClient.GetJson<PublicShopDetailRow>($"/v1/public/barber-shops/{f.ShopSlug}");
        Assert.True(afterPut.DepositEnabled);
        Assert.Equal(0.25m, afterPut.DepositPercent);
    }

    // ── InitiateBooking enforcement: per-shop MaxPendingPerCustomer ───────

    [Fact]
    public async Task InitiateBooking_ShopMaxPendingPerCustomer_RejectsBeyondPerShopLimit()
    {
        var f = await SeedShopAndAuthAsync();
        var putResp = await PutPolicyAsync(f.OwnerClient, depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: 1, minDaysBetweenBookings: null, maxBookingsPerMonth: null);
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        var first = await f.CustomerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = f.StylistId,
            scheduledAt = FutureWeekdayUtc(60),
            serviceCatalogIds = new[] { f.ServiceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await f.CustomerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = f.StylistId,
            scheduledAt = FutureWeekdayUtc(61),
            serviceCatalogIds = new[] { f.ServiceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("en fazla 1 bekleyen", body);
    }

    // ── InitiateBooking enforcement: GLOBAL 3-Pending tabanı hâlâ çalışıyor ─

    [Fact]
    public async Task InitiateBooking_GlobalThreePendingCap_StillBindsAcrossDifferentShops()
    {
        var f = await SeedShopAndAuthAsync();

        // İkinci, FARKLI bir Active demo shop (owner'ın kendi shop'u değil) — global tavanın
        // shop'tan BAĞIMSIZ saydığını kanıtlamak için gerekli. Her iki shop'ta da per-shop
        // MaxPendingPerCustomer varsayılan (2) bırakılıyor — global tavan (3) daha düşük
        // olduğundan ASIL bağlayıcı olan odur.
        Guid otherShopId, otherStylistId, otherServiceId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var otherShop = await db.Set<BarberShop>().AsNoTracking()
                .FirstAsync(s => s.Id != f.ShopId && s.Status == BarberShopStatus.Active);
            otherShopId = otherShop.Id;
            otherStylistId = (await db.Set<Stylist>().AsNoTracking().FirstAsync(s => s.BarberShopId == otherShopId)).Id;
            otherServiceId = (await db.Set<ServiceCatalog>().AsNoTracking().FirstAsync(s => s.BarberShopId == otherShopId)).Id;
        }

        async Task<(HttpStatusCode Status, string Body)> InitiateAsync(Guid stylistId, Guid serviceId, int daysAhead)
        {
            var resp = await f.CustomerClient.PostAsJsonAsync("/v1/bookings", new
            {
                stylistId,
                scheduledAt = FutureWeekdayUtc(daysAhead),
                serviceCatalogIds = new[] { serviceId },
                isWalkIn = false,
                customerNote = (string?)null,
            });
            return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
        }

        // Shop A: 2 Pending (kendi tavanına tam oturuyor).
        var a1 = await InitiateAsync(f.StylistId, f.ServiceId, 60);
        Assert.True(a1.Status == HttpStatusCode.Created, a1.Body);
        var a2 = await InitiateAsync(f.StylistId, f.ServiceId, 61);
        Assert.True(a2.Status == HttpStatusCode.Created, a2.Body);

        // Shop B: 1. Pending — global toplam artık 3 (henüz tavanda, geçmedi), kabul edilir.
        var b1 = await InitiateAsync(otherStylistId, otherServiceId, 62);
        Assert.True(b1.Status == HttpStatusCode.Created, b1.Body);

        // Shop B: 2. Pending — shop B'nin KENDİ tavanı (2) müsait olsa da global toplam
        // (3) tavanda olduğundan GLOBAL kontrol reddeder.
        var b2 = await InitiateAsync(otherStylistId, otherServiceId, 63);
        Assert.True(b2.Status == HttpStatusCode.BadRequest, b2.Body);
    }

    // ── InitiateBooking enforcement: MinDaysBetweenBookings ───────────────

    [Fact]
    public async Task InitiateBooking_MinDaysBetweenBookings_RejectsWithinWindow_AcceptsAfterWindow()
    {
        var f = await SeedShopAndAuthAsync();
        var putResp = await PutPolicyAsync(f.OwnerClient, depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: 3, minDaysBetweenBookings: 7, maxBookingsPerMonth: null);
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        var anchor = FutureMondayUtc(90);
        await CreateBookingDirectAsync(f, BookingStatus.Confirmed, anchor);

        // 3 gün sonra — pencere içinde, reddedilmeli.
        var tooSoon = await f.CustomerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = f.StylistId,
            scheduledAt = anchor.AddDays(3),
            serviceCatalogIds = new[] { f.ServiceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooSoon.StatusCode);
        var tooSoonBody = await tooSoon.Content.ReadAsStringAsync();
        Assert.Contains("en az 7 gün", tooSoonBody);

        // 8 gün sonra — pencere dışında, kabul edilmeli.
        var farEnough = await f.CustomerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = f.StylistId,
            scheduledAt = anchor.AddDays(8),
            serviceCatalogIds = new[] { f.ServiceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, farEnough.StatusCode);
    }

    [Fact]
    public async Task InitiateBooking_MinDaysBetweenBookings_CancelledAnchorDoesNotCount()
    {
        var f = await SeedShopAndAuthAsync();
        await PutPolicyAsync(f.OwnerClient, depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: 3, minDaysBetweenBookings: 7, maxBookingsPerMonth: null);

        var anchor = FutureMondayUtc(90);
        await CreateBookingDirectAsync(f, BookingStatus.Cancelled, anchor);

        // Tek mevcut randevu Cancelled — sayılmaz, dolayısıyla pencere hiç uygulanmaz.
        var resp = await f.CustomerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = f.StylistId,
            scheduledAt = anchor.AddDays(3),
            serviceCatalogIds = new[] { f.ServiceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task InitiateBooking_MinDaysBetweenBookings_NoShowAnchorCounts()
    {
        var f = await SeedShopAndAuthAsync();
        await PutPolicyAsync(f.OwnerClient, depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: 3, minDaysBetweenBookings: 7, maxBookingsPerMonth: null);

        var anchor = FutureMondayUtc(90);
        var bookingId = await CreateBookingDirectAsync(f, BookingStatus.Confirmed, anchor);
        await MarkNoShowDirectAsync(bookingId);

        // NoShow anchor — pencere HÂLÂ uygulanır (RFC-013 ara karar: NoShow dahil).
        var resp = await f.CustomerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = f.StylistId,
            scheduledAt = anchor.AddDays(3),
            serviceCatalogIds = new[] { f.ServiceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── InitiateBooking enforcement: MaxBookingsPerMonth ──────────────────

    [Fact]
    public async Task InitiateBooking_MaxBookingsPerMonth_RejectsSecondBookingSameMonth_AcceptsNextMonth()
    {
        var f = await SeedShopAndAuthAsync();
        await PutPolicyAsync(f.OwnerClient, depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: 3, minDaysBetweenBookings: null, maxBookingsPerMonth: 1);

        var first = SafeMonthStartWeekdayUtc(3);
        await CreateBookingDirectAsync(f, BookingStatus.Confirmed, first);

        // Aynı takvim ayı, farklı gün — reddedilmeli.
        var sameMonth = await f.CustomerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = f.StylistId,
            scheduledAt = first.AddDays(2), // Çarşamba, aynı hafta/ay
            serviceCatalogIds = new[] { f.ServiceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.BadRequest, sameMonth.StatusCode);
        var body = await sameMonth.Content.ReadAsStringAsync();
        Assert.Contains("aylık en fazla 1", body);

        // Bir sonraki ay (aynı gün numarası, +1 ay) — ayrı bucket, kabul edilmeli.
        var nextMonth = first.AddMonths(1);
        // Pazar'a denk gelirse bir gün kaydır (working hours kapalı).
        if (nextMonth.DayOfWeek == DayOfWeek.Sunday) nextMonth = nextMonth.AddDays(1);
        var nextMonthResp = await f.CustomerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = f.StylistId,
            scheduledAt = nextMonth,
            serviceCatalogIds = new[] { f.ServiceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, nextMonthResp.StatusCode);
    }

    [Fact]
    public async Task InitiateBooking_MaxBookingsPerMonth_CancelledExcluded_NoShowIncluded()
    {
        var f = await SeedShopAndAuthAsync();
        await PutPolicyAsync(f.OwnerClient, depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: 3, minDaysBetweenBookings: null, maxBookingsPerMonth: 1);

        var monthAnchor = SafeMonthStartWeekdayUtc(3);

        // Cancelled — sayılmaz, aynı ayda yeni randevu hâlâ kabul edilmeli.
        await CreateBookingDirectAsync(f, BookingStatus.Cancelled, monthAnchor);
        var afterCancelled = await f.CustomerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = f.StylistId,
            scheduledAt = monthAnchor.AddDays(2),
            serviceCatalogIds = new[] { f.ServiceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, afterCancelled.StatusCode);

        // Şimdi bu ayda 1 (Pending) var — limit doldu. Aynı ay içinde bir NoShow daha
        // eklensek de zaten dolu; NoShow'un SAYILDIĞINI göstermek için ayrı, taze bir ay kullan.
        var noShowMonthAnchor = monthAnchor.AddMonths(2);
        if (noShowMonthAnchor.DayOfWeek == DayOfWeek.Sunday) noShowMonthAnchor = noShowMonthAnchor.AddDays(1);
        var noShowBookingId = await CreateBookingDirectAsync(f, BookingStatus.Confirmed, noShowMonthAnchor);
        await MarkNoShowDirectAsync(noShowBookingId);

        var afterNoShow = await f.CustomerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = f.StylistId,
            scheduledAt = noShowMonthAnchor.AddDays(2),
            serviceCatalogIds = new[] { f.ServiceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.BadRequest, afterNoShow.StatusCode);
    }
}

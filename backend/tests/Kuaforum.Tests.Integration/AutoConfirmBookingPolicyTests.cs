using System.Net;
using System.Net.Http.Json;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-014 §2 D2 — salon başına otomatik onay (<c>auto_confirm</c>). Odak: (1) varsayılan KAPALI
/// davranış korunuyor, (2) auto_confirm açık + kapora YOK → randevu doğrudan Confirmed, (3) EN
/// KRİTİK etkileşim — auto_confirm açık + kapora VAR → randevu ödeme tamamlanana kadar Pending
/// kalır, ödeme geldiğinde kuaför onayı beklemeden Confirmed olur ("otomatik onay kuaför onayını
/// atlar, ödemeyi atlamaz"), (4) anti-spam kuralları auto_confirm tarafından bypass edilmez, (5)
/// otomatik onaylanan randevuda owner'a giden bildirim metni farklı.
///
/// PostPaymentApprovalTests'in kurduğu "önce kapora, sonra onay" temelinin üzerine — orada
/// owner'ın HER ZAMAN elle onaylaması gerektiği kilitleniyor; burada salonun bilinçli opt-in'iyle
/// bu adımın ne zaman/nasıl atlandığı kilitleniyor.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class AutoConfirmBookingPolicyTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    private sealed record CreatedResp(Guid Id);
    private sealed record InitiateBookingResp(
        Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt,
        long TotalAmountMinor, string Currency, string Status, bool AutoConfirmEnabled);
    private sealed record InitiatePaymentResp(
        Guid PaymentId, string CheckoutUrl, string? ProviderToken, DateTimeOffset ExpiresAt,
        long AmountMinor, long RemainingAtShopMinor, string Kind);
    private sealed record BookingStatusOnly(string Status);
    private sealed record NotificationItemDto(Guid Id, string TemplateKey, string Subject, string Body, bool IsRead, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt, Guid? RelatedBookingId);
    private sealed record NotificationFeedDto(int UnreadCount, IReadOnlyList<NotificationItemDto> Items);

    private const long KnownTotalMinor = 200_00;

    // ── (1) Varsayılan KAPALI — mevcut davranış korunuyor ────────────────────────────────────

    [Fact]
    public async Task AutoConfirmDisabled_Default_CreatesPendingBooking()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        // Ayar hiç değiştirilmiyor — shop varsayılan (AutoConfirmEnabled=false).

        var initiated = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        Assert.Equal("Pending", initiated.Status);
        Assert.False(initiated.AutoConfirmEnabled);
    }

    // ── (2) auto_confirm açık, kapora YOK → doğrudan Confirmed ───────────────────────────────

    [Fact]
    public async Task AutoConfirmEnabled_NoDeposit_CreatesConfirmedBookingImmediately()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        await SetBookingPolicyAsync(ownerClient, depositEnabled: false, depositPercent: null, autoConfirmEnabled: true);

        var initiated = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        Assert.Equal("Confirmed", initiated.Status);
        Assert.True(initiated.AutoConfirmEnabled);

        // Server-side de doğrula — API yanıtı yalnız komutun döndürdüğü snapshot değil.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == initiated.BookingId);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.NotNull(booking.ConfirmedAt);
    }

    // ── (3) EN KRİTİK: auto_confirm açık + kapora VAR → ödeme öncesi Pending, ─────────────────
    // ── ödeme sonrası owner onayı OLMADAN Confirmed ───────────────────────────────────────────

    [Fact]
    public async Task AutoConfirmEnabled_WithDeposit_StaysPendingUntilPayment_ThenAutoConfirmsWithoutOwnerAction()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        await SetBookingPolicyAsync(ownerClient, depositEnabled: true, depositPercent: 0.15m, autoConfirmEnabled: true);

        var initiated = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        // "Otomatik onay ödemeyi atlamaz" — kapora ödenene kadar hâlâ Pending.
        Assert.Equal("Pending", initiated.Status);
        Assert.True(initiated.AutoConfirmEnabled);

        var initiatePayment = await customerClient.PostAsJsonAsync("/v1/payments/initiate",
            new { bookingId = initiated.BookingId, kind = "Deposit" });
        Assert.Equal(HttpStatusCode.OK, initiatePayment.StatusCode);
        var pay = (await initiatePayment.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;

        // Owner HİÇBİR ŞEY YAPMADI — ne /confirm çağrısı ne başka bir aksiyon.
        var completeUrl = $"/v1/payments/mock/complete?token={Uri.EscapeDataString(pay.ProviderToken ?? "MOCK")}";
        var complete = await customerClient.GetAsync(completeUrl);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var detail = await customerClient.GetJson<BookingStatusOnly>($"/v1/bookings/{initiated.BookingId}");
        Assert.Equal("Confirmed", detail.Status);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == initiated.BookingId);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.NotNull(booking.DepositPaidAt);
        Assert.NotNull(booking.ConfirmedAt);
        Assert.Equal(pay.PaymentId, booking.PaymentId);

        // Zaten Confirmed olduğundan, owner'ın (gereksiz) /confirm çağrısı artık reddedilir —
        // OwnerConfirm() yalnız Pending'den geçiş yapar; ikinci kez tetiklenemeyeceğinin kanıtı.
        var redundantConfirm = await ownerClient.PostAsync($"/v1/bookings/{initiated.BookingId}/confirm", null);
        Assert.Equal(HttpStatusCode.BadRequest, redundantConfirm.StatusCode);
    }

    // ── (4) Anti-spam kuralları auto_confirm'i bypass ETMEZ ──────────────────────────────────

    [Fact]
    public async Task AutoConfirmEnabled_AntiSpamViolation_BookingIsStillRejected()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        await SetBookingPolicyAsync(ownerClient, depositEnabled: false, depositPercent: null,
            autoConfirmEnabled: true, minDaysBetweenBookings: 7);

        var f = await GetFixtureAsync(customerClient, ownerClient);

        var anchor = FutureWeekdayUtc(90);
        await CreateBookingDirectAsync(f, anchor);

        var bookingCountBefore = await CountBookingsAsync();

        // Anchor'ın 7 günlük penceresi içinde — auto_confirm açık olsa BİLE reddedilmeli,
        // randevu hiç oluşturulmamalı (bypass yok).
        var tooSoon = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = f.StylistId,
            scheduledAt = anchor.AddDays(2),
            serviceCatalogIds = new[] { f.ServiceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, tooSoon.StatusCode);
        var body = await tooSoon.Content.ReadAsStringAsync();
        Assert.Contains("en az 7 gün", body);

        var bookingCountAfter = await CountBookingsAsync();
        Assert.Equal(bookingCountBefore, bookingCountAfter); // hiçbir yeni satır eklenmedi
    }

    // ── (5) Bildirim metni farklılaşıyor ──────────────────────────────────────────────────────

    [Fact]
    public async Task AutoConfirmedBooking_NotifiesOwnerWithDifferentSubjectThanManualConfirm()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        await SetBookingPolicyAsync(ownerClient, depositEnabled: false, depositPercent: null, autoConfirmEnabled: true);

        var initiated = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 41);
        Assert.Equal("Confirmed", initiated.Status);

        var ownerFeed = await ownerClient.GetJson<NotificationFeedDto>("/v1/notifications");
        var notification = Assert.Single(ownerFeed.Items, i => i.RelatedBookingId == initiated.BookingId && i.TemplateKey == "booking.confirmed.barber");
        Assert.Equal("Yeni randevu — otomatik onaylandı", notification.Subject);
    }

    [Fact]
    public async Task ManuallyConfirmedBooking_NotifiesOwnerWithPlainSubject()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        // auto_confirm hiç açılmıyor — kontrast.

        var initiated = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 42);
        Assert.Equal("Pending", initiated.Status);

        var confirm = await ownerClient.PostAsync($"/v1/bookings/{initiated.BookingId}/confirm", null);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var ownerFeed = await ownerClient.GetJson<NotificationFeedDto>("/v1/notifications");
        var notification = Assert.Single(ownerFeed.Items, i => i.RelatedBookingId == initiated.BookingId && i.TemplateKey == "booking.confirmed.barber");
        Assert.Equal("Yeni randevu", notification.Subject);
    }

    // ---- helpers -----------------------------------------------------------

    private async Task<(HttpClient Customer, HttpClient Owner)> LoginClientsAsync()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        var customerClient = factory.CreateClient();
        var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);
        return (customerClient, ownerClient);
    }

    private static async Task SetBookingPolicyAsync(
        HttpClient owner, bool depositEnabled, decimal? depositPercent, bool autoConfirmEnabled,
        int maxPendingPerCustomer = 2, int? minDaysBetweenBookings = null, int? maxBookingsPerMonth = null)
    {
        var resp = await owner.PutAsJsonAsync("/v1/owner/barber-shop/booking-policy", new
        {
            depositEnabled,
            depositPercent,
            maxPendingPerCustomer,
            minDaysBetweenBookings,
            maxBookingsPerMonth,
            autoConfirmEnabled,
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    /// <summary>Owner API'siyle 200 TL'lik test servisi yaratır ve müşteri o servise booking açar — toplam tutar deterministik olarak 200_00 olur (PostPaymentApprovalTests kalıbı).</summary>
    private static async Task<InitiateBookingResp> InitiateKnownTotalBookingAsync(HttpClient customer, HttpClient owner, int daysAhead)
    {
        var ownerShop = await owner.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        var createService = await owner.PostAsJsonAsync($"/v1/owner/barber-shop/{ownerShop.Id}/services", new
        {
            name = "D2 Auto-Confirm Testi Servisi",
            gender = "Any",
            durationMinutes = 30,
            priceMinor = KnownTotalMinor,
            bufferMinutes = 5,
            description = (string?)null,
        }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.Created, createService.StatusCode);
        var serviceId = (await createService.Content.ReadFromJsonAsync<CreatedResp>(TestHelpers.Json))!.Id;

        var stylists = await customer.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{ownerShop.Id}/stylists");

        var scheduledAt = FutureWeekdayUtc(daysAhead);

        var initiate = await customer.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt,
            serviceCatalogIds = new[] { serviceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateBookingResp>(TestHelpers.Json))!;
        Assert.Equal(KnownTotalMinor, initiated.TotalAmountMinor);
        return initiated;
    }

    private sealed record ShopFixture(Guid ShopId, Guid StylistId, Guid ServiceId, string ServiceName, int Duration, int Buffer, long Price, Guid CustomerId);

    private async Task<ShopFixture> GetFixtureAsync(HttpClient customerClient, HttpClient ownerClient)
    {
        var ownerShop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var customerLogin = await customerClient.DevLoginAsync("Customer"); // aynı client zaten login — kullanıcı Id'sini almak için tekrar çağrı ucuz (idempotent dev-login)

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stylist = await db.Set<Stylist>().AsNoTracking().FirstAsync(s => s.BarberShopId == ownerShop.Id);
        var service = await db.Set<ServiceCatalog>().AsNoTracking().FirstAsync(s => s.BarberShopId == ownerShop.Id);

        return new ShopFixture(ownerShop.Id, stylist.Id, service.Id, service.Name, service.DurationMinutes, service.BufferMinutes, service.PriceMinor, customerLogin.UserId);
    }

    /// <summary>Direkt aggregate üretimi (BookingPolicyTests.CreateBookingAsync ile aynı desen) — anchor randevuyu HTTP akışını atlayıp doğrudan kaydeder.</summary>
    private async Task CreateBookingDirectAsync(ShopFixture f, DateTimeOffset scheduledAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var services = new (Guid, string, int, int, long)[] { (f.ServiceId, f.ServiceName, f.Duration, f.Buffer, f.Price) };
        var booking = BookingAggregate.Initiate(f.CustomerId, f.ShopId, f.StylistId, scheduledAt, services, BookingType.Scheduled, 0.10m).Value;
        Assert.True(booking.OwnerConfirm().IsSuccess);

        db.Set<BookingAggregate>().Add(booking);
        await db.SaveChangesAsync();
    }

    private async Task<int> CountBookingsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Set<BookingAggregate>().AsNoTracking().CountAsync();
    }

    /// <summary>Pazar hariç, en az minDaysAhead gün sonrası (BookingPolicyTests.FutureWeekdayUtc ile aynı desen).</summary>
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
}

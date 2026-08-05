using System.Net;
using System.Net.Http.Json;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Infrastructure.BackgroundJobs;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-009 ücretsiz büyüme modu — <c>Payment:OnlinePaymentsEnabled = false</c> davranışını
/// kilitler (bkz. <see cref="OnlinePaymentsDisabledFactory"/> için gerekçe: paylaşılan
/// <see cref="IntegrationTestFactory"/>'nin Development environment'ı bu flag'i appsettings.
/// Development.json üzerinden true'ya sabitliyor, dolayısıyla flag=false senaryosu ancak ayrı
/// bir factory ile test edilebilir).
///
/// Kapsam: (1) InitiatePaymentCommand → 409, (2) ExpirePendingBookingsJob'un flag kapalıyken
/// CreatedAt'e değil ScheduledAt'e bakması (RFC-009 "Açık soru → Karar": owner onay penceresi
/// = randevu saatine kadar, ayrı bir OwnerConfirmTimeout config'i yok), (3) OwnerConfirm'ün
/// ödeme hiç olmadan Pending → Confirmed akışının flag'den bağımsız çalışmaya devam etmesi.
///
/// Flag=true (mevcut RFC-006 davranışı) regresyonu ayrıca test EDİLMİYOR burada — bu zaten
/// paylaşılan <see cref="IntegrationTestFactory"/> üzerinde çalışan tüm RFC-006 test suite'i
/// (DepositPaymentTests, PaymentInitiationTests, PaytrWebhookTests, RefundFlowTests, ve
/// BookingSlotSafetyTests'teki ExpirePendingBookingsJob testleri) tarafından zaten kanıtlanıyor.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class OnlinePaymentsDisabledTests(OnlinePaymentsDisabledFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    private sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    private sealed record InitiateResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    private sealed record BookingStatusOnly(string Status);
    private sealed record ProfileResp(bool OnlinePaymentsEnabled);

    [Fact]
    public async Task Profile_reports_online_payments_disabled()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);

        var profile = await customerClient.GetJson<ProfileResp>("/v1/users/me");
        Assert.False(profile.OnlinePaymentsEnabled);
    }

    [Fact]
    public async Task Initiate_payment_returns_409_when_online_payments_disabled()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateBookingAsync(customerClient, ownerClient, 40);

        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/payments/initiate");
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        req.Content = JsonContent.Create(new { bookingId }, options: TestHelpers.Json);
        var response = await customerClient.SendAsync(req);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Online ödeme şu an aktif değil", body);
    }

    [Fact]
    public async Task ExpirePendingBookingsJob_does_not_cancel_pending_with_future_scheduled_at_even_if_stale_when_disabled()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateBookingAsync(customerClient, ownerClient, 60);

        // Backdate CreatedAt to 35 minutes ago — past the flag-ON 30-minute threshold — to prove
        // the flag-OFF branch ignores CreatedAt entirely. ScheduledAt stays in the future.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var staleCreatedAt = DateTimeOffset.UtcNow.AddMinutes(-35);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE bookings SET created_at = {staleCreatedAt} WHERE id = {bookingId}");
        }

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ExpirePendingBookingsJob>();
            await job.RunAsync(default);
        }

        var detail = await customerClient.GetJson<BookingStatusOnly>($"/v1/bookings/{bookingId}");
        Assert.Equal("Pending", detail.Status);
    }

    [Fact]
    public async Task ExpirePendingBookingsJob_cancels_pending_once_scheduled_at_has_passed_when_disabled()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateBookingAsync(customerClient, ownerClient, 60);

        // Booking stays freshly-created (CreatedAt ~= now, nowhere near a 30-min threshold) but
        // its ScheduledAt is pushed into the past — owner never confirmed in time. The job must
        // still sweep it because the flag-OFF branch keys off ScheduledAt, not CreatedAt.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pastScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-10);
            var pastScheduledEndsAt = pastScheduledAt.AddMinutes(35);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE bookings SET scheduled_at = {pastScheduledAt}, scheduled_ends_at = {pastScheduledEndsAt} WHERE id = {bookingId}");
        }

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ExpirePendingBookingsJob>();
            await job.RunAsync(default);
        }

        var detail = await customerClient.GetJson<BookingStatusOnly>($"/v1/bookings/{bookingId}");
        Assert.Equal("Cancelled", detail.Status);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            Assert.Equal(BookingCancellationReason.PendingTimeout, booking.CancellationReason);

            var hasPayment = await db.Set<Kuaforum.Domain.Modules.Payment.Payment>().AsNoTracking()
                .AnyAsync(p => p.BookingId == bookingId);
            Assert.False(hasPayment, "Flag kapalıyken hiç payment satırı oluşmamalı.");
        }
    }

    [Fact]
    public async Task OwnerConfirm_moves_pending_to_confirmed_without_any_payment_when_disabled()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateBookingAsync(customerClient, ownerClient, 60);

        var confirm = await ownerClient.PostAsync($"/v1/bookings/{bookingId}/confirm", null);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var detail = await customerClient.GetJson<BookingStatusOnly>($"/v1/bookings/{bookingId}");
        Assert.Equal("Confirmed", detail.Status);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasPayment = await db.Set<Kuaforum.Domain.Modules.Payment.Payment>().AsNoTracking()
            .AnyAsync(p => p.BookingId == bookingId);
        Assert.False(hasPayment, "OwnerConfirm ödeme olmadan çalışmalı (flag kapalı akışın tamamı).");
    }

    private async Task SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
        await seeder.SeedAsync(default);
    }

    private static async Task<Guid> InitiateBookingAsync(HttpClient customer, HttpClient owner, int daysAhead)
    {
        var ownerShop = await owner.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customer.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{ownerShop.Id}/stylists");
        var services = await customer.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{ownerShop.Id}/services");

        var initiate = await customer.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt = FutureWeekdayUtc(daysAhead),
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json))!;
        return initiated.BookingId;
    }

    /// <summary>
    /// The minDaysAhead-th non-Sunday day at 14:00 UTC (17:00 Istanbul) — inside the demo seed's
    /// Mon-Sat 09:00-19:00 hours. Same monotonic-safe implementation as
    /// BookingSlotSafetyTests.FutureWeekdayUtc — see that copy's XML doc for why the naive
    /// "AddDays then nudge off Sunday" version can collide for adjacent offsets.
    /// </summary>
    private static DateTimeOffset FutureWeekdayUtc(int minDaysAhead)
    {
        var date = DateTime.UtcNow.Date;
        var remaining = minDaysAhead;
        while (remaining > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek != DayOfWeek.Sunday) remaining--;
        }
        return new DateTimeOffset(date.Year, date.Month, date.Day, 14, 0, 0, TimeSpan.Zero);
    }
}

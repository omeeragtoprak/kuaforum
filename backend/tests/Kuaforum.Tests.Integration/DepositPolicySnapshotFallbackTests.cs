using System.Net;
using System.Net.Http.Json;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-013 M1 (adversarial review — "sessiz ceza"): InitiatePaymentCommand used to read the
/// shop's deposit policy LIVE for Kind=Deposit, while ExpirePendingBookingsJob branches on the
/// booking's own RequiresOnlinePayment SNAPSHOT (taken at InitiateBookingCommand time). If the
/// owner disables the shop's deposit policy AFTER a booking was created expecting one, the
/// customer got a 409 trying to pay the deposit they were promised, and 30 minutes later the job
/// silently cancelled the booking — a customer punished for an owner decision entirely outside
/// their control.
///
/// Fix: Booking now also snapshots the deposit PERCENT (Booking.DepositPercentSnapshot) at
/// initiate time. InitiatePaymentCommand still prefers the live shop value (so a normal
/// percent-only tweak works as before) but falls back to the booking's own snapshot when the shop
/// no longer offers deposit at all AND the booking's RequiresOnlinePayment snapshot says it should
/// have been available.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DepositPolicySnapshotFallbackTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    private sealed record CreatedResp(Guid Id);
    private sealed record InitiateBookingResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    private sealed record InitiatePaymentResp(
        Guid PaymentId, string CheckoutUrl, string? ProviderToken, DateTimeOffset ExpiresAt,
        long AmountMinor, long RemainingAtShopMinor, string Kind);

    private const long KnownTotalMinor = 200_00;
    private const long ExpectedDepositMinor = 30_00; // ceil(20000 × 0.15)

    [Fact]
    public async Task OwnerDisablesDepositAfterBookingCreated_CustomerCanStillPayDeposit_UsingBookingSnapshot()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        await SetDepositPolicyAsync(ownerClient, depositEnabled: true, depositPercent: 0.15m);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, daysAhead: 40);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            Assert.True(booking.RequiresOnlinePayment);
            Assert.Equal(0.15m, booking.DepositPercentSnapshot);
        }

        // Owner booking oluşturulduktan SONRA kaporayı kapatır.
        await SetDepositPolicyAsync(ownerClient, depositEnabled: false, depositPercent: null);

        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");

        // ESKİ davranış: 409 "Bu işletme kapora ödemesi kabul etmiyor." — müşteri hiç ödeyemezdi.
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;
        Assert.Equal(ExpectedDepositMinor, pay.AmountMinor); // snapshot'lanmış %15'ten hesaplandı
    }

    /// <summary>
    /// Control: if the shop still has deposit ENABLED but just changed the percent, the LIVE
    /// value wins (as before) — the fallback only kicks in when the live check fails entirely.
    /// </summary>
    [Fact]
    public async Task OwnerChangesDepositPercent_WithoutDisabling_LiveValueWins_NotStaleSnapshot()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        await SetDepositPolicyAsync(ownerClient, depositEnabled: true, depositPercent: 0.15m);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, daysAhead: 40);

        await SetDepositPolicyAsync(ownerClient, depositEnabled: true, depositPercent: 0.25m);

        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;

        var expectedAtNewRate = (long)Math.Ceiling(KnownTotalMinor * 0.25m);
        Assert.Equal(expectedAtNewRate, pay.AmountMinor); // canlı %25 kullanıldı, snapshot'taki %15 DEĞİL
    }

    /// <summary>
    /// Control: a booking created while the shop had NO deposit policy at all (RequiresOnlinePayment
    /// snapshot false) must still be rejected if the customer tries Deposit after the shop later
    /// enables one only for a LATER booking window — no, actually the live check passes here since
    /// shop.BookingPolicyDepositEnabled is what gates it once true. This test locks in that the
    /// fallback introduces no NEW restriction: once the shop currently offers deposit, Kind=Deposit
    /// works regardless of what the (now-irrelevant) booking snapshot says.
    /// </summary>
    [Fact]
    public async Task BookingCreatedWithoutDepositPolicy_ThenOwnerEnablesIt_CustomerCanPayUsingLiveValue()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        // Kapora hiç açılmadan booking oluşturulur.

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, daysAhead: 40);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            Assert.False(booking.RequiresOnlinePayment);
            Assert.Null(booking.DepositPercentSnapshot);
        }

        await SetDepositPolicyAsync(ownerClient, depositEnabled: true, depositPercent: 0.20m);

        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;
        Assert.Equal((long)Math.Ceiling(KnownTotalMinor * 0.20m), pay.AmountMinor);
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

    private static async Task SetDepositPolicyAsync(HttpClient owner, bool depositEnabled, decimal? depositPercent)
    {
        var resp = await owner.PutAsJsonAsync("/v1/owner/barber-shop/booking-policy", new
        {
            depositEnabled,
            depositPercent,
            maxPendingPerCustomer = 2,
            minDaysBetweenBookings = (int?)null,
            maxBookingsPerMonth = (int?)null,
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostInitiatePayment(
        HttpClient client, Guid bookingId, string idempotencyKey, string kind)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/payments/initiate");
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        req.Content = JsonContent.Create(new { bookingId, kind }, options: TestHelpers.Json);
        return await client.SendAsync(req);
    }

    private static async Task<Guid> InitiateKnownTotalBookingAsync(HttpClient customer, HttpClient owner, int daysAhead)
    {
        var ownerShop = await owner.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        var createService = await owner.PostAsJsonAsync($"/v1/owner/barber-shop/{ownerShop.Id}/services", new
        {
            name = "M1 Snapshot Testi Servisi",
            gender = "Any",
            durationMinutes = 30,
            priceMinor = KnownTotalMinor,
            bufferMinutes = 5,
            description = (string?)null,
        }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.Created, createService.StatusCode);
        var serviceId = (await createService.Content.ReadFromJsonAsync<CreatedResp>(TestHelpers.Json))!.Id;

        var stylists = await customer.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{ownerShop.Id}/stylists");

        var futureDate = DateTime.UtcNow.Date.AddDays(daysAhead);
        while (futureDate.DayOfWeek == DayOfWeek.Sunday) futureDate = futureDate.AddDays(1);
        var futureTime = new DateTimeOffset(futureDate.Year, futureDate.Month, futureDate.Day, 14, 0, 0, TimeSpan.Zero);

        var initiate = await customer.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt = futureTime,
            serviceCatalogIds = new[] { serviceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateBookingResp>(TestHelpers.Json))!;
        Assert.Equal(KnownTotalMinor, initiated.TotalAmountMinor);
        return initiated.BookingId;
    }
}

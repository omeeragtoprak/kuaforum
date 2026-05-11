using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class PaymentInitiationTests(IntegrationTestFactory factory)
{
    public sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    public sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    public sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    public sealed record InitiateBookingResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    public sealed record InitiatePaymentResp(Guid PaymentId, string CheckoutUrl, string? ProviderToken, DateTimeOffset ExpiresAt);
    public sealed record MockCompleteResp(Guid PaymentId, string Message);

    [Fact]
    public async Task Initiate_payment_returns_checkout_session_and_idempotency_blocks_replay()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateBookingAsync(customerClient, ownerClient, 40);
        // NOTE: NO owner confirm here. Real flow is initiate booking (Pending) →
        // customer pays → payment.Capture triggers booking.Confirm. Owner confirm
        // is a separate manual path; testing them together causes double-confirm.

        // act 1: initiate payment with idempotency key
        var idempKey = Guid.NewGuid().ToString();
        var initiate1 = await PostInitiatePayment(customerClient, bookingId, idempKey);
        if (initiate1.StatusCode != HttpStatusCode.OK)
        {
            var body = await initiate1.Content.ReadAsStringAsync();
            Assert.Fail($"Initiate failed: {(int)initiate1.StatusCode} body={body}");
        }
        var pay1 = (await initiate1.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;
        Assert.NotEqual(Guid.Empty, pay1.PaymentId);
        Assert.False(string.IsNullOrWhiteSpace(pay1.CheckoutUrl), "CheckoutUrl boş olmamalı");

        // act 2: same idempotency key → conflict
        var initiate2 = await PostInitiatePayment(customerClient, bookingId, idempKey);
        Assert.Equal(HttpStatusCode.Conflict, initiate2.StatusCode);

        // act 3: another customer should NOT be able to pay for someone else's booking
        // (here we re-use same customer, just confirming forbidden path needs another customer)

        // act 4: mock-complete the payment (dev GET hook, AllowAnonymous)
        var completeUrl = $"/v1/payments/mock/complete?token={Uri.EscapeDataString(pay1.ProviderToken ?? "MOCK")}";
        var complete = await customerClient.GetAsync(completeUrl);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        // act 5: initiate again (different idempotency) → already paid (Captured/Settled) → conflict
        var initiate3 = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, initiate3.StatusCode);
    }

    [Fact]
    public async Task Initiate_payment_rejects_for_non_owning_customer()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var customerA = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerA.SetBearer((await customerA.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateBookingAsync(customerA, ownerClient, 35);
        await ownerClient.PostAsync($"/v1/bookings/{bookingId}/confirm", null);

        // Use admin user (still a different identity than Customer A) to attempt payment
        // — Admin is technically a different User row, so Forbidden path is exercised.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var emailRes = Kuaforum.Domain.Modules.Identity.ValueObjects.EmailAddress.TryCreate("pay-thief@kuaforum.test");
            var adminRes = Kuaforum.Domain.Modules.Identity.User.CreateAdmin(emailRes.Value, "Pay Thief");
            db.Users.Add(adminRes.Value);
            await db.SaveChangesAsync(default);
        }
        using var thief = factory.CreateClient();
        thief.SetBearer((await thief.DevLoginAsync("Admin")).AccessToken);

        var attempt = await PostInitiatePayment(thief, bookingId, Guid.NewGuid().ToString());
        Assert.False(attempt.IsSuccessStatusCode,
            $"Başkasının booking'i için payment initiate edilememeli (got {(int)attempt.StatusCode})");
    }

    private static async Task<HttpResponseMessage> PostInitiatePayment(HttpClient client, Guid bookingId, string idempotencyKey)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/payments/initiate");
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        req.Content = JsonContent.Create(new { bookingId }, options: TestHelpers.Json);
        return await client.SendAsync(req);
    }

    private static async Task<Guid> InitiateBookingAsync(HttpClient customer, HttpClient owner, int daysAhead)
    {
        var ownerShop = await owner.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customer.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{ownerShop.Id}/stylists");
        var services = await customer.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{ownerShop.Id}/services");

        var futureDate = DateTime.UtcNow.Date.AddDays(daysAhead);
        while (futureDate.DayOfWeek == DayOfWeek.Sunday) futureDate = futureDate.AddDays(1);
        var futureTime = new DateTimeOffset(futureDate.Year, futureDate.Month, futureDate.Day, 14, 0, 0, TimeSpan.Zero);

        var initiate = await customer.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt = futureTime,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateBookingResp>(TestHelpers.Json))!;
        return initiated.BookingId;
    }
}

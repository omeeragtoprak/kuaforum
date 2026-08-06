using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// Covers the refund-endpoint IDOR fix (POST /v1/payments/refund now requires the "Admin"
/// policy and derives the refund amount from the linked booking's RefundPercentage instead of
/// trusting client-supplied AmountMinor/Reason — see RefundPaymentCommand) and the mock/complete
/// provider guard (CompletePaymentCommand rejects tokens for payments that aren't Mock-provider).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RefundFlowTests(IntegrationTestFactory factory)
{
    public sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    public sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    public sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    public sealed record InitiateBookingResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    public sealed record InitiatePaymentResp(Guid PaymentId, string CheckoutUrl, string? ProviderToken, DateTimeOffset ExpiresAt);
    public sealed record CancelResp(int RefundPercentage);

    /// <summary>
    /// Neither the paying customer nor the shop owner may call the refund endpoint — only
    /// Admin. Regression for the IDOR where any authenticated user could refund arbitrary
    /// payments with a self-chosen amount.
    /// </summary>
    [Fact]
    public async Task Non_admin_cannot_call_refund_endpoint_even_for_own_captured_payment()
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

        var (_, paymentId) = await InitiateAndPayAsync(customerClient, ownerClient, 40);

        var byCustomer = await customerClient.PostAsJsonAsync("/v1/payments/refund", new { paymentId });
        Assert.Equal(HttpStatusCode.Forbidden, byCustomer.StatusCode);

        var byOwner = await ownerClient.PostAsJsonAsync("/v1/payments/refund", new { paymentId });
        Assert.Equal(HttpStatusCode.Forbidden, byOwner.StatusCode);
    }

    /// <summary>
    /// Admin refund succeeds and the actual refunded amount is derived from the booking's
    /// RefundPercentage (12-24h cancel tier = 50%), NOT from anything the caller sent — the
    /// request body only carries PaymentId.
    ///
    /// RFC-013 C2 (adversarial review): the hour-based tier only applies to a CONFIRMED booking
    /// (Booking.ConfirmedAt set) — InitiateAndPayAsync deliberately does not auto-confirm
    /// (RFC-013 C4), so this test explicitly confirms before forcing the 12-24h window, otherwise
    /// the unconfirmed-cancel rule (always 100%) would fire instead of the tier this test targets.
    /// </summary>
    [Fact]
    public async Task Admin_refund_amount_is_derived_from_booking_refund_percentage()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);

            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var emailRes = Kuaforum.Domain.Modules.Identity.ValueObjects.EmailAddress.TryCreate("refund-admin@kuaforum.test");
            var adminRes = Kuaforum.Domain.Modules.Identity.User.CreateAdmin(emailRes.Value, "Refund Admin");
            db.Users.Add(adminRes.Value);
            await db.SaveChangesAsync(default);
        }

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        using var adminClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);
        adminClient.SetBearer((await adminClient.DevLoginAsync("Admin")).AccessToken);

        var (bookingId, paymentId) = await InitiateAndPayAsync(customerClient, ownerClient, 40);

        var confirm = await ownerClient.PostAsync($"/v1/bookings/{bookingId}/confirm", null);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        // Force the booking into the 12-24h band (50% refund tier) deterministically —
        // avoids relying on wall-clock timing at test-run time.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var newScheduledAt = DateTimeOffset.UtcNow.AddHours(18);
            var newEndsAt = newScheduledAt.AddMinutes(35);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE bookings SET scheduled_at = {newScheduledAt}, scheduled_ends_at = {newEndsAt} WHERE id = {bookingId}");
        }

        var cancel = await customerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var cancelResp = (await cancel.Content.ReadFromJsonAsync<CancelResp>(TestHelpers.Json))!;
        Assert.Equal(50, cancelResp.RefundPercentage);

        var refund = await adminClient.PostAsJsonAsync("/v1/payments/refund", new { paymentId });
        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var payment = await db.Set<Kuaforum.Domain.Modules.Payment.Payment>()
                .Include(p => p.Refunds)
                .FirstAsync(p => p.Id == paymentId);
            var refundRow = Assert.Single(payment.Refunds);
            Assert.Equal(payment.GrossAmountMinor * 50 / 100, refundRow.AmountMinor);
            Assert.Equal(Kuaforum.Domain.Modules.Payment.RefundReason.CustomerRequested, refundRow.Reason);
            Assert.Equal(Kuaforum.Domain.Modules.Payment.PaymentStatus.PartiallyRefunded, payment.Status);
        }
    }

    /// <summary>
    /// Admin refund is rejected when the booking hasn't been cancelled/no-showed yet — there is
    /// no RefundPercentage to derive an amount from, so the endpoint must not silently assume
    /// a full refund.
    /// </summary>
    [Fact]
    public async Task Admin_refund_rejected_when_booking_not_cancelled()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);

            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var emailRes = Kuaforum.Domain.Modules.Identity.ValueObjects.EmailAddress.TryCreate("refund-admin-2@kuaforum.test");
            var adminRes = Kuaforum.Domain.Modules.Identity.User.CreateAdmin(emailRes.Value, "Refund Admin 2");
            db.Users.Add(adminRes.Value);
            await db.SaveChangesAsync(default);
        }

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        using var adminClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);
        adminClient.SetBearer((await adminClient.DevLoginAsync("Admin")).AccessToken);

        var (_, paymentId) = await InitiateAndPayAsync(customerClient, ownerClient, 40);

        // booking still Confirmed — never cancelled.
        var refund = await adminClient.PostAsJsonAsync("/v1/payments/refund", new { paymentId });
        Assert.False(refund.IsSuccessStatusCode,
            $"İptal edilmemiş randevunun ödemesi iade edilememeli (got {(int)refund.StatusCode})");
    }

    /// <summary>
    /// Defense-in-depth: a payment that was never initiated through the Mock provider must not
    /// be completable via the dev-only mock/complete endpoint, even if an attacker guesses the
    /// "mock-{paymentId:N}" token format for its id.
    /// </summary>
    [Fact]
    public async Task Mock_complete_rejects_payment_that_is_not_mock_provider()
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

        var bookingId = await InitiateBookingAsync(customerClient, ownerClient, 41);

        Guid paymentId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var paymentResult = Kuaforum.Domain.Modules.Payment.Payment.Initiate(
                bookingId, Guid.NewGuid(), Guid.NewGuid(), 30000, 3000,
                Kuaforum.Domain.Modules.Payment.PaymentProviderName.PayTr);
            db.Set<Kuaforum.Domain.Modules.Payment.Payment>().Add(paymentResult.Value);
            await db.SaveChangesAsync(default);
            paymentId = paymentResult.Value.Id;
        }

        var token = $"mock-{paymentId:N}";
        var complete = await customerClient.GetAsync($"/v1/payments/mock/complete?token={Uri.EscapeDataString(token)}");
        Assert.False(complete.IsSuccessStatusCode,
            $"PayTr sağlayıcılı payment mock/complete ile tamamlanamamalı (got {(int)complete.StatusCode})");
    }

    private static async Task<(Guid BookingId, Guid PaymentId)> InitiateAndPayAsync(HttpClient customer, HttpClient owner, int daysAhead)
    {
        var bookingId = await InitiateBookingAsync(customer, owner, daysAhead);

        var idempKey = Guid.NewGuid().ToString();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/payments/initiate");
        req.Headers.Add("Idempotency-Key", idempKey);
        req.Content = JsonContent.Create(new { bookingId }, options: TestHelpers.Json);
        var initiate = await customer.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;

        var completeUrl = $"/v1/payments/mock/complete?token={Uri.EscapeDataString(pay.ProviderToken ?? "MOCK")}";
        var complete = await customer.GetAsync(completeUrl);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        return (bookingId, pay.PaymentId);
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

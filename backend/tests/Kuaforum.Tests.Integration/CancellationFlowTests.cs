using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class CancellationFlowTests(IntegrationTestFactory factory)
{
    public sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    public sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    public sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    public sealed record InitiateResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    public sealed record CancelResp(int RefundPercentage);
    public sealed record BookingDto(Guid Id, string Status, int? RefundPercentage);

    /// <summary>
    /// 60 days out → CustomerRequested cancel → 100% refund per
    /// ComputeRefundPercentage (>=24h band).
    /// </summary>
    [Fact]
    public async Task Customer_cancel_more_than_24h_before_returns_full_refund()
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

        var bookingId = await InitiateBookingAsync(customerClient, ownerClient, 60);

        // confirm so we exit Pending → confirm not strictly required for cancel,
        // but reflects real flow: customer cancels a confirmed booking.
        Assert.Equal(HttpStatusCode.NoContent,
            (await ownerClient.PostAsync($"/v1/bookings/{bookingId}/confirm", null)).StatusCode);

        // act: customer cancels. No body — Reason is derived server-side from actor role
        // (customer → CustomerRequested), the client cannot influence it (see IDOR regression
        // test below).
        var cancel = await customerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var resp = await cancel.Content.ReadFromJsonAsync<CancelResp>(TestHelpers.Json);
        Assert.Equal(100, resp!.RefundPercentage);

        // verify booking surface state
        var detail = await customerClient.GetJson<BookingDto>($"/v1/bookings/{bookingId}");
        Assert.Equal("Cancelled", detail.Status);
        Assert.Equal(100, detail.RefundPercentage);
    }

    /// <summary>
    /// Admin override cancel → 100% refund regardless of timing
    /// (ComputeRefundPercentage AdminOverride = 100).
    /// </summary>
    [Fact]
    public async Task Admin_override_cancel_returns_full_refund_and_blocks_double_cancel()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);

            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var emailRes = Kuaforum.Domain.Modules.Identity.ValueObjects.EmailAddress.TryCreate("admin-cancel@kuaforum.test");
            var adminRes = Kuaforum.Domain.Modules.Identity.User.CreateAdmin(emailRes.Value, "Test Admin");
            db.Users.Add(adminRes.Value);
            await db.SaveChangesAsync(default);
        }

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        using var adminClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);
        adminClient.SetBearer((await adminClient.DevLoginAsync("Admin")).AccessToken);

        var bookingId = await InitiateBookingAsync(customerClient, ownerClient, 45);
        await ownerClient.PostAsync($"/v1/bookings/{bookingId}/confirm", null);

        var cancel = await adminClient.PostAsJsonAsync(
            $"/v1/admin/bookings/{bookingId}/cancel",
            new { reason = "Şikayet üzerine işletme askıya alındı, randevu iptali zorunlu." });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var resp = await cancel.Content.ReadFromJsonAsync<CancelResp>(TestHelpers.Json);
        Assert.Equal(100, resp!.RefundPercentage);

        // double-cancel must reject
        var second = await adminClient.PostAsJsonAsync(
            $"/v1/admin/bookings/{bookingId}/cancel",
            new { reason = "Tekrar deneme — domain reddetmeli." });
        Assert.False(second.IsSuccessStatusCode,
            $"Cancelled durumundaki booking tekrar iptal edilemez (got {(int)second.StatusCode})");
    }

    /// <summary>
    /// Owner cancel via /v1/bookings/{id}/cancel → server derives BarberRequested for the
    /// shop-owner actor → 100% iade, zaman-bağımsız. No body sent — the client cannot pick
    /// its own reason (see IDOR fix: CancelBookingCommand no longer carries a Reason param).
    /// Regression: mobile booking detail eski 5 (NoShow) gönderiyordu, 0% iade
    /// hatasını yakalamak için (artık backend zaten reason'ı client'tan almıyor).
    /// </summary>
    [Fact]
    public async Task Owner_cancel_returns_full_refund_regardless_of_timing()
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

        // <12h ahead — eğer reason CustomerRequested olsaydı 0% iade gelirdi.
        var bookingId = await InitiateBookingAsync(customerClient, ownerClient, 60);
        await ownerClient.PostAsync($"/v1/bookings/{bookingId}/confirm", null);

        // act: owner cancels — no body, server derives BarberRequested from actor role.
        var cancel = await ownerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var resp = await cancel.Content.ReadFromJsonAsync<CancelResp>(TestHelpers.Json);
        Assert.Equal(100, resp!.RefundPercentage);

        var detail = await customerClient.GetJson<BookingDto>($"/v1/bookings/{bookingId}");
        Assert.Equal("Cancelled", detail.Status);
        Assert.Equal(100, detail.RefundPercentage);
    }

    /// <summary>
    /// IDOR regression: previously the client sent an arbitrary BookingCancellationReason in
    /// the cancel request body (e.g. AdminOverride=4 or BarberRequested=1), letting a customer
    /// force a 100% refund regardless of the &lt;12h cutoff. Reason is now derived purely from
    /// actor role server-side (see CancelBookingCommandHandler.ResolveReasonAsync) — a
    /// client-supplied "reason" field, even if the endpoint still accepted a body, must have
    /// zero effect. Booking is moved to &lt;6h out via direct DB write (deterministic, avoids
    /// wall-clock flakiness) so CustomerRequested would normally yield 0% refund.
    /// </summary>
    [Fact]
    public async Task Customer_cancel_ignores_client_supplied_reason_and_still_applies_less_than_12h_zero_percent()
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

        var bookingId = await InitiateBookingAsync(customerClient, ownerClient, 60);
        await ownerClient.PostAsync($"/v1/bookings/{bookingId}/confirm", null);

        // Force the booking to <12h out at the DB level (deterministic vs. wall-clock timing).
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var newScheduledAt = DateTimeOffset.UtcNow.AddHours(6);
            var newEndsAt = newScheduledAt.AddMinutes(35);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE bookings SET scheduled_at = {newScheduledAt}, scheduled_ends_at = {newEndsAt} WHERE id = {bookingId}");
        }

        // attack: customer sends AdminOverride(=4) in the (now-unused) body field, hoping to
        // force a 100% refund via a reason the endpoint no longer even reads.
        var cancel = await customerClient.PostAsJsonAsync($"/v1/bookings/{bookingId}/cancel", new { reason = 4 });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var resp = await cancel.Content.ReadFromJsonAsync<CancelResp>(TestHelpers.Json);
        Assert.Equal(0, resp!.RefundPercentage);

        var detail = await customerClient.GetJson<BookingDto>($"/v1/bookings/{bookingId}");
        Assert.Equal("Cancelled", detail.Status);
        Assert.Equal(0, detail.RefundPercentage);
    }

    /// <summary>
    /// Customer cancel after Completed → domain reject.
    /// </summary>
    [Fact]
    public async Task Cancel_rejected_after_completed()
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

        var bookingId = await InitiateBookingAsync(customerClient, ownerClient, 50);
        await ownerClient.PostAsync($"/v1/bookings/{bookingId}/confirm", null);
        await ownerClient.PostAsync($"/v1/bookings/{bookingId}/start", null);
        await ownerClient.PostAsync($"/v1/bookings/{bookingId}/complete", null);

        var cancel = await customerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);
        Assert.False(cancel.IsSuccessStatusCode,
            $"Completed durumundaki booking iptal edilemez (got {(int)cancel.StatusCode})");
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
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json))!;
        return initiated.BookingId;
    }
}

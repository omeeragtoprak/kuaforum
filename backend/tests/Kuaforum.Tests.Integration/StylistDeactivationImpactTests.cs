using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-015 §2 D2 — çalışan pasifleştirme ENGELLENMEZ; etkilenen gelecekteki Pending/Confirmed
/// randevular yanıtta döndürülür (kuaför görsün) ama otomatik devretme/iptal YAPILMAZ. Müşteriye
/// bildirim bu maddede YOK (D1'in aksine).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class StylistDeactivationImpactTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    private sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    private sealed record InitiateResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    private sealed record AffectedBookingRow(Guid BookingId, Guid CustomerId, string? CustomerName, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt);
    private sealed record UpdateStylistResult(IReadOnlyList<AffectedBookingRow> AffectedBookings);
    private sealed record BookingDto(Guid Id, Guid StylistId, string Status);

    private async Task<(HttpClient Customer, HttpClient Owner, Guid StylistId, Guid BookingId)> SeedConfirmedBookingAsync()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        var customer = factory.CreateClient();
        var owner = factory.CreateClient();
        customer.SetBearer((await customer.DevLoginAsync("Customer")).AccessToken);
        owner.SetBearer((await owner.DevLoginAsync("BarberOwner")).AccessToken);

        var shop = await owner.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customer.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var services = await customer.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{shop.Id}/services");
        var stylistId = stylists.First().Id;

        var futureDate = DateTime.UtcNow.Date.AddDays(10);
        while (futureDate.DayOfWeek == DayOfWeek.Sunday) futureDate = futureDate.AddDays(1);
        var scheduledAt = new DateTimeOffset(futureDate.Year, futureDate.Month, futureDate.Day, 14, 0, 0, TimeSpan.Zero);

        var initiate = await customer.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json))!;

        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PostAsync($"/v1/bookings/{initiated.BookingId}/confirm", null)).StatusCode);

        return (customer, owner, stylistId, initiated.BookingId);
    }

    [Fact]
    public async Task Deactivate_WithFutureConfirmedBooking_ReturnsAffectedList_AndStillDeactivates()
    {
        var (_, owner, stylistId, bookingId) = await SeedConfirmedBookingAsync();

        var resp = await owner.PatchAsJsonAsync($"/v1/stylists/{stylistId}", new { isActive = false }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // engellenmedi
        var result = (await resp.Content.ReadFromJsonAsync<UpdateStylistResult>(TestHelpers.Json))!;
        Assert.Contains(result.AffectedBookings, a => a.BookingId == bookingId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
        var stylist = await db.Set<Kuaforum.Domain.Modules.Barber.Stylist>().AsNoTracking().SingleAsync(s => s.Id == stylistId);
        Assert.False(stylist.IsActive); // pasifleştirme fiilen gerçekleşti
    }

    [Fact]
    public async Task Deactivate_DoesNotAutoCancelOrReassignBooking()
    {
        var (customer, owner, stylistId, bookingId) = await SeedConfirmedBookingAsync();

        await owner.PatchAsJsonAsync($"/v1/stylists/{stylistId}", new { isActive = false }, TestHelpers.Json);

        var booking = await customer.GetJson<BookingDto>($"/v1/bookings/{bookingId}");
        Assert.Equal("Confirmed", booking.Status); // otomatik iptal YOK
        Assert.Equal(stylistId, booking.StylistId); // otomatik devretme YOK
    }

    [Fact]
    public async Task Deactivate_WithNoFutureBookings_ReturnsEmptyList()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }
        var owner = factory.CreateClient();
        owner.SetBearer((await owner.DevLoginAsync("BarberOwner")).AccessToken);
        var shop = await owner.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await owner.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");

        var resp = await owner.PatchAsJsonAsync($"/v1/stylists/{stylists.First().Id}", new { isActive = false }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = (await resp.Content.ReadFromJsonAsync<UpdateStylistResult>(TestHelpers.Json))!;
        Assert.Empty(result.AffectedBookings);
    }

    [Fact]
    public async Task Reactivating_DoesNotReturnAffectedBookings()
    {
        var (_, owner, stylistId, _) = await SeedConfirmedBookingAsync();
        await owner.PatchAsJsonAsync($"/v1/stylists/{stylistId}", new { isActive = false }, TestHelpers.Json);

        var reactivate = await owner.PatchAsJsonAsync($"/v1/stylists/{stylistId}", new { isActive = true }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        var result = (await reactivate.Content.ReadFromJsonAsync<UpdateStylistResult>(TestHelpers.Json))!;
        Assert.Empty(result.AffectedBookings); // "etkilenen" yalnız aktif→pasif geçişte anlamlı
    }

    [Fact]
    public async Task ProfileOnlyUpdate_DoesNotReturnAffectedBookings()
    {
        var (_, owner, stylistId, _) = await SeedConfirmedBookingAsync();

        var resp = await owner.PatchAsJsonAsync($"/v1/stylists/{stylistId}", new { displayName = "Yeni İsim" }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = (await resp.Content.ReadFromJsonAsync<UpdateStylistResult>(TestHelpers.Json))!;
        Assert.Empty(result.AffectedBookings);
    }
}

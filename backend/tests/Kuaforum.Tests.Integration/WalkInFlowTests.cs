using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class WalkInFlowTests(IntegrationTestFactory factory)
{
    public sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    public sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName, bool IsActive);
    public sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    public sealed record InitiateResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    public sealed record SearchItem(Guid Id, string Name, bool HasWalkInAvailable);
    public sealed record SearchResp(IReadOnlyList<SearchItem> Items, int Page, int PageSize, int TotalCount);

    [Fact]
    public async Task Walkin_toggle_surfaces_shop_in_filter_and_booking_persists_as_walkin_type()
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

        var ownerShop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{ownerShop.Id}/stylists");
        var stylist = stylists.First();
        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{ownerShop.Id}/services");

        // pre: walk-in filter empty for owner shop initially
        var beforeToggle = await customerClient.GetJson<SearchResp>($"/v1/barber-shops/search?walkInOnly=true&pageSize=50");
        Assert.DoesNotContain(beforeToggle.Items, s => s.Id == ownerShop.Id);

        // act 1: owner toggles walk-in available
        var toggleOn = await ownerClient.PostAsJsonAsync($"/v1/stylists/{stylist.Id}/walk-in", new { available = true });
        Assert.Equal(HttpStatusCode.NoContent, toggleOn.StatusCode);

        // assert: filter now includes shop with HasWalkInAvailable=true
        var afterToggle = await customerClient.GetJson<SearchResp>($"/v1/barber-shops/search?walkInOnly=true&pageSize=50");
        var shopInFilter = Assert.Single(afterToggle.Items, s => s.Id == ownerShop.Id);
        Assert.True(shopInFilter.HasWalkInAvailable, "Walk-in toggle açıkken HasWalkInAvailable=true olmalı");

        // act 2: customer creates a walk-in booking — earliest free slot 30 minutes ahead
        // (avoid Sunday-closed days; iterate +1 day until a weekday).
        var slot = DateTime.UtcNow.AddMinutes(30);
        while (slot.DayOfWeek == DayOfWeek.Sunday) slot = slot.AddDays(1);
        // Cleaner: use the same 60-day-out pattern as other tests; walk-in flag is
        // independent of scheduling distance.
        var futureDate = DateTime.UtcNow.Date.AddDays(60);
        while (futureDate.DayOfWeek == DayOfWeek.Sunday) futureDate = futureDate.AddDays(1);
        var futureTime = new DateTimeOffset(futureDate.Year, futureDate.Month, futureDate.Day, 14, 0, 0, TimeSpan.Zero);

        var initiate = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylist.Id,
            scheduledAt = futureTime,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = true,
            customerNote = "Şu an müsait misiniz?",
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json))!;

        // assert DB: BookingType = WalkIn
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var booking = await db.Set<Kuaforum.Domain.Modules.Booking.Booking>()
                .FirstAsync(b => b.Id == initiated.BookingId);
            Assert.Equal(Kuaforum.Domain.Modules.Booking.BookingType.WalkIn, booking.BookingType);
        }

        // act 3: owner toggles walk-in off → filter clears
        var toggleOff = await ownerClient.PostAsJsonAsync($"/v1/stylists/{stylist.Id}/walk-in", new { available = false });
        Assert.Equal(HttpStatusCode.NoContent, toggleOff.StatusCode);

        var afterOff = await customerClient.GetJson<SearchResp>($"/v1/barber-shops/search?walkInOnly=true&pageSize=50");
        Assert.DoesNotContain(afterOff.Items, s => s.Id == ownerShop.Id);
    }
}

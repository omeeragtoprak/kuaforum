using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ShopSuspensionTests(IntegrationTestFactory factory)
{
    public sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    public sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    public sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    public sealed record SearchItemDto(Guid Id, string Name);
    public sealed record SearchResp(IReadOnlyList<SearchItemDto> Items, int Page, int PageSize, int TotalCount);

    [Fact]
    public async Task Suspended_shop_disappears_from_search_and_rejects_new_bookings_then_reactivated()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);

            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var emailRes = Kuaforum.Domain.Modules.Identity.ValueObjects.EmailAddress.TryCreate("admin-suspend@kuaforum.test");
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

        var ownerShop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{ownerShop.Id}/stylists");
        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{ownerShop.Id}/services");

        // pre: shop appears in search
        var before = await customerClient.GetJson<SearchResp>($"/v1/barber-shops/search?pageSize=50");
        Assert.Contains(before.Items, s => s.Id == ownerShop.Id);

        // act: admin suspends
        var suspend = await adminClient.PostAsJsonAsync($"/v1/admin/barber-shops/{ownerShop.Id}/suspend",
            new { reason = "Birden çok şikayet — soruşturma süresince askıda." });
        Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);

        // assert: hidden from search (Active-only filter)
        var afterSuspend = await customerClient.GetJson<SearchResp>($"/v1/barber-shops/search?pageSize=50");
        Assert.DoesNotContain(afterSuspend.Items, s => s.Id == ownerShop.Id);

        // assert: customer cannot initiate a booking on suspended shop
        var futureDate = DateTime.UtcNow.Date.AddDays(45);
        while (futureDate.DayOfWeek == DayOfWeek.Sunday) futureDate = futureDate.AddDays(1);
        var futureTime = new DateTimeOffset(futureDate.Year, futureDate.Month, futureDate.Day, 14, 0, 0, TimeSpan.Zero);

        var initiate = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt = futureTime,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.False(initiate.IsSuccessStatusCode,
            $"Askıda shop'a booking initiate edilememeli (got {(int)initiate.StatusCode})");

        // act: admin reactivates
        var reactivate = await adminClient.PostAsync($"/v1/admin/barber-shops/{ownerShop.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);

        // assert: visible in search again
        var afterReactivate = await customerClient.GetJson<SearchResp>($"/v1/barber-shops/search?pageSize=50");
        Assert.Contains(afterReactivate.Items, s => s.Id == ownerShop.Id);

        // assert: booking initiate now allowed
        var initiateOk = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt = futureTime,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiateOk.StatusCode);
    }
}

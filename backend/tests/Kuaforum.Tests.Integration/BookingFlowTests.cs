using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class BookingFlowTests(IntegrationTestFactory factory)
{
    public sealed record SearchResp(IReadOnlyList<ShopRow> Items, int Page, int PageSize, int TotalCount);
    public sealed record ShopRow(Guid Id, string Name, string Slug);
    public sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    public sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    public sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    public sealed record InitiateResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    public sealed record BookingDto(Guid Id, Guid CustomerId, Guid BarberShopId, Guid StylistId,
        DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, string Status, long TotalAmountMinor, string Currency, int? RefundPercentage, IReadOnlyList<BookingItemDto> Items);
    public sealed record BookingItemDto(Guid Id, Guid ServiceCatalogId, string ServiceName, int DurationMinutes, long PriceMinor);
    public sealed record NotificationFeedDto(int UnreadCount, IReadOnlyList<NotificationItemDto> Items);
    public sealed record NotificationItemDto(Guid Id, string TemplateKey, string Subject, string Body, bool IsRead, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt);

    [Fact]
    public async Task Owner_can_drive_full_lifecycle_and_receives_in_app_notification()
    {
        // arrange — fresh state
        await factory.ResetDatabaseAsync();

        // re-seed demo barber after reset
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();

        var customerLogin = await customerClient.DevLoginAsync("Customer");
        customerClient.SetBearer(customerLogin.AccessToken);

        var ownerLogin = await ownerClient.DevLoginAsync("BarberOwner");
        ownerClient.SetBearer(ownerLogin.AccessToken);

        // Use the OWNER's shop directly so booking belongs to them (search.First() may return a different demo shop).
        var ownerShop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{ownerShop.Id}/stylists");
        Assert.NotEmpty(stylists);
        var stylist = stylists.First();

        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{ownerShop.Id}/services");
        Assert.NotEmpty(services);
        var service = services.First();

        // act 1: customer initiates booking 60 days ahead at 14:00 UTC (avoids any conflict)
        var futureDate = DateTime.UtcNow.Date.AddDays(60);
        var futureTime = new DateTimeOffset(futureDate.Year, futureDate.Month, futureDate.Day, 14, 0, 0, TimeSpan.Zero);
        var initiateResp = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylist.Id,
            scheduledAt = futureTime,
            serviceCatalogIds = new[] { service.Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        if (initiateResp.StatusCode != HttpStatusCode.Created)
        {
            var body = await initiateResp.Content.ReadAsStringAsync();
            Assert.Fail($"Initiate failed: {(int)initiateResp.StatusCode} body={body}");
        }
        var initiated = await initiateResp.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json);
        Assert.NotNull(initiated);

        // act 2: owner sees pending booking
        var fromIso = DateTimeOffset.UtcNow.ToString("o");
        var toIso = DateTimeOffset.UtcNow.AddDays(70).ToString("o");
        var ownerBookings = await ownerClient.GetJson<IReadOnlyList<BookingDto>>(
            $"/v1/owner/barber-shop/bookings?from={Uri.EscapeDataString(fromIso)}&to={Uri.EscapeDataString(toIso)}");
        var bookingFromOwner = Assert.Single(ownerBookings);
        Assert.Equal("Pending", bookingFromOwner.Status);

        // initial: owner has 0 unread notifications
        var ownerUnreadInitial = await ownerClient.GetJson<UnreadCountDto>("/v1/notifications/unread-count");
        Assert.Equal(0, ownerUnreadInitial.UnreadCount);

        // act 3: owner confirms
        var confirmResp = await ownerClient.PostAsync($"/v1/bookings/{initiated!.BookingId}/confirm", null);
        Assert.Equal(HttpStatusCode.NoContent, confirmResp.StatusCode);

        // assert: owner now has 1 unread notification (Yeni randevu)
        var ownerFeed = await ownerClient.GetJson<NotificationFeedDto>("/v1/notifications");
        Assert.Equal(1, ownerFeed.UnreadCount);
        Assert.Single(ownerFeed.Items);
        Assert.StartsWith("booking.confirmed.barber", ownerFeed.Items[0].TemplateKey);
        Assert.False(ownerFeed.Items[0].IsRead);

        // assert: customer also got Randevun onaylandı + push log
        var customerFeed = await customerClient.GetJson<NotificationFeedDto>("/v1/notifications");
        Assert.Equal(1, customerFeed.UnreadCount);
        Assert.StartsWith("booking.confirmed.customer", customerFeed.Items[0].TemplateKey);

        // act 4: owner starts
        var startResp = await ownerClient.PostAsync($"/v1/bookings/{initiated.BookingId}/start", null);
        Assert.Equal(HttpStatusCode.NoContent, startResp.StatusCode);

        // act 5: owner completes
        var completeResp = await ownerClient.PostAsync($"/v1/bookings/{initiated.BookingId}/complete", null);
        Assert.Equal(HttpStatusCode.NoContent, completeResp.StatusCode);

        // assert: customer now has 3 unread (confirmed + started + completed)
        var customerFeedAfter = await customerClient.GetJson<NotificationFeedDto>("/v1/notifications");
        Assert.Equal(3, customerFeedAfter.UnreadCount);

        // act 6: customer marks the latest one as read
        var firstUnread = customerFeedAfter.Items.First(i => !i.IsRead);
        var markResp = await customerClient.PostAsync($"/v1/notifications/{firstUnread.Id}/read", null);
        Assert.Equal(HttpStatusCode.NoContent, markResp.StatusCode);

        var customerUnreadAfterRead = await customerClient.GetJson<UnreadCountDto>("/v1/notifications/unread-count");
        Assert.Equal(2, customerUnreadAfterRead.UnreadCount);

        // act 7: customer marks all as read
        var allReadResp = await customerClient.PostAsync("/v1/notifications/read-all", null);
        Assert.Equal(HttpStatusCode.OK, allReadResp.StatusCode);
        var allReadBody = await allReadResp.Content.ReadFromJsonAsync<MarkedReadDto>(TestHelpers.Json);
        Assert.Equal(2, allReadBody!.MarkedRead);

        var customerUnreadFinal = await customerClient.GetJson<UnreadCountDto>("/v1/notifications/unread-count");
        Assert.Equal(0, customerUnreadFinal.UnreadCount);

        // assert: customer booking detail shows Completed
        var bookingDetail = await customerClient.GetJson<BookingDto>($"/v1/bookings/{initiated.BookingId}");
        Assert.Equal("Completed", bookingDetail.Status);
    }

    public sealed record UnreadCountDto(int UnreadCount);
    public sealed record MarkedReadDto(int MarkedRead);
}

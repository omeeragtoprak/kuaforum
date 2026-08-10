using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-015 §2 D1 / §4 madde 1 — çalışma saati değişikliği ENGELLEMEZ ama etkilenen gelecekteki
/// randevuları hem önizleme ucunda hem kaydetme yanıtında döndürür + müşteriye idempotent
/// bildirim gönderir. Aynı zamanda paylaşılan sınır sorgusunun (<c>WorkingHoursImpactFinder</c> /
/// <c>StylistFutureBookingsFinder</c>) sınırda-eşitlik davranışını doğrular.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class WorkingHoursChangeImpactTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    private sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    private sealed record InitiateResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    private sealed record AffectedBookingRow(Guid BookingId, Guid CustomerId, string? CustomerName, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt);
    private sealed record SaveResult(IReadOnlyList<AffectedBookingRow> AffectedBookings);
    private sealed record NotificationItem(Guid Id, string TemplateKey, string Subject, string Body, bool IsRead, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt, Guid? RelatedBookingId);
    private sealed record NotificationFeed(int UnreadCount, IReadOnlyList<NotificationItem> Items);

    private async Task<(HttpClient Customer, HttpClient Owner, Guid StylistId, Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, DayOfWeek Day)>
        SeedConfirmedBookingAsync()
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

        // Seed default: Mon-Sat 09:00-19:00. Pick a non-Sunday day 10 days out, 17:00 Istanbul
        // local (=14:00 UTC, UTC+3 no-DST) — comfortably inside the default window.
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

        return (customer, owner, stylistId, initiated.BookingId, initiated.ScheduledAt, initiated.ScheduledEndsAt, futureDate.DayOfWeek);
    }

    [Fact]
    public async Task Preview_BookingExactlyAtNewCloseBoundary_IsNotAffected()
    {
        var (_, owner, stylistId, bookingId, _, endsAt, day) = await SeedConfirmedBookingAsync();
        var endsAtLocal = TimeZoneInfo.ConvertTime(endsAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));

        var resp = await owner.PostJsonAsync($"/v1/stylists/{stylistId}/working-hours/preview", new
        {
            dayOfWeek = day,
            openTime = new TimeOnly(9, 0),
            closeTime = new TimeOnly(endsAtLocal.Hour, endsAtLocal.Minute), // exactly the booking's end
            isClosed = false,
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var affected = (await resp.Content.ReadFromJsonAsync<List<AffectedBookingRow>>(TestHelpers.Json))!;
        Assert.DoesNotContain(affected, a => a.BookingId == bookingId);
    }

    [Fact]
    public async Task Preview_BookingJustPastNewCloseBoundary_IsAffected()
    {
        var (_, owner, stylistId, bookingId, _, endsAt, day) = await SeedConfirmedBookingAsync();
        var endsAtLocal = TimeZoneInfo.ConvertTime(endsAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));
        var justBeforeEnd = endsAtLocal.AddMinutes(-5);

        var resp = await owner.PostJsonAsync($"/v1/stylists/{stylistId}/working-hours/preview", new
        {
            dayOfWeek = day,
            openTime = new TimeOnly(9, 0),
            closeTime = new TimeOnly(justBeforeEnd.Hour, justBeforeEnd.Minute),
            isClosed = false,
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var affected = (await resp.Content.ReadFromJsonAsync<List<AffectedBookingRow>>(TestHelpers.Json))!;
        Assert.Contains(affected, a => a.BookingId == bookingId);
    }

    [Fact]
    public async Task Preview_PastBooking_IsExcludedEvenIfOutsideNewRange()
    {
        var (_, owner, stylistId, bookingId, _, _, day) = await SeedConfirmedBookingAsync();

        // Push the booking into the past directly (deterministic vs. wall-clock timing) —
        // same technique as CancellationFlowTests.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var pastStart = DateTimeOffset.UtcNow.AddDays(-1);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE bookings SET scheduled_at = {pastStart}, scheduled_ends_at = {pastStart.AddMinutes(35)} WHERE id = {bookingId}");
        }

        var resp = await owner.PostJsonAsync($"/v1/stylists/{stylistId}/working-hours/preview", new
        {
            dayOfWeek = day,
            isClosed = true, // fully closed — would trivially "affect" any same-day-of-week future booking
            openTime = new TimeOnly(9, 0),
            closeTime = new TimeOnly(19, 0),
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var affected = (await resp.Content.ReadFromJsonAsync<List<AffectedBookingRow>>(TestHelpers.Json))!;
        Assert.DoesNotContain(affected, a => a.BookingId == bookingId);
    }

    [Fact]
    public async Task SetWorkingHours_NarrowingHours_StillSaves_ReturnsAffected_AndNotifiesCustomerOnce()
    {
        var (customer, owner, stylistId, bookingId, _, endsAt, day) = await SeedConfirmedBookingAsync();
        var endsAtLocal = TimeZoneInfo.ConvertTime(endsAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));
        var newClose = endsAtLocal.AddMinutes(-5);

        var body = new
        {
            dayOfWeek = day,
            openTime = new TimeOnly(9, 0),
            closeTime = new TimeOnly(newClose.Hour, newClose.Minute),
            isClosed = false,
        };

        var save1 = await owner.PutAsJsonAsync($"/v1/stylists/{stylistId}/working-hours", body, TestHelpers.Json);
        // Not blocked — engellenmez (RFC-015 §2 D1).
        Assert.Equal(HttpStatusCode.OK, save1.StatusCode);
        var result1 = (await save1.Content.ReadFromJsonAsync<SaveResult>(TestHelpers.Json))!;
        Assert.Contains(result1.AffectedBookings, a => a.BookingId == bookingId);

        var feed1 = await customer.GetJson<NotificationFeed>("/v1/notifications");
        var matches1 = feed1.Items.Count(i => i.TemplateKey == "barber.working_hours.changed" && i.RelatedBookingId == bookingId);
        Assert.Equal(1, matches1);

        // Save the SAME values again — idempotency (RFC madde 4): no second notification.
        var save2 = await owner.PutAsJsonAsync($"/v1/stylists/{stylistId}/working-hours", body, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, save2.StatusCode);
        var result2 = (await save2.Content.ReadFromJsonAsync<SaveResult>(TestHelpers.Json))!;
        Assert.Empty(result2.AffectedBookings); // isActualChange=false → sorgu bile çalışmadı

        var feed2 = await customer.GetJson<NotificationFeed>("/v1/notifications");
        var matches2 = feed2.Items.Count(i => i.TemplateKey == "barber.working_hours.changed" && i.RelatedBookingId == bookingId);
        Assert.Equal(1, matches2); // tekrarlanmadı
    }

    [Fact]
    public async Task SetWorkingHours_NoActualChange_OnFirstEverSave_ProducesNoNotification()
    {
        // Sanity: saving the exact same values the seeder already wrote (Mon-Sat 09:00-19:00)
        // must not spuriously "affect" anything, since the booking is well inside the window.
        var (customer, owner, stylistId, bookingId, _, _, day) = await SeedConfirmedBookingAsync();

        var save = await owner.PutAsJsonAsync($"/v1/stylists/{stylistId}/working-hours", new
        {
            dayOfWeek = day,
            openTime = new TimeOnly(9, 0),
            closeTime = new TimeOnly(19, 0),
            isClosed = false,
        }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var result = (await save.Content.ReadFromJsonAsync<SaveResult>(TestHelpers.Json))!;
        Assert.Empty(result.AffectedBookings);

        var feed = await customer.GetJson<NotificationFeed>("/v1/notifications");
        Assert.DoesNotContain(feed.Items, i => i.TemplateKey == "barber.working_hours.changed" && i.RelatedBookingId == bookingId);
    }
}

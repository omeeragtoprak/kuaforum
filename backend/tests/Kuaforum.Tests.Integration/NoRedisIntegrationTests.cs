using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kuaforum.Domain.Modules.Barber;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// Koyeb free-tier deploy hazırlığı (2026-08): "Redis yoksa uygulama açılışta patlamamalı,
/// slot çakışması hâlâ 409 dönmeli" gereksinimini <see cref="NoRedisIntegrationTestFactory"/>
/// üzerinden kanıtlar — o factory'de <c>ConnectionStrings:Redis</c> hiç set edilmez, gerçek bir
/// boot üzerinden DependencyInjection.AddInfrastructure'ın in-memory fallback dalını tetikler.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class NoRedisIntegrationTests(NoRedisIntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    private sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    private sealed record InitiateResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);

    [Fact]
    public async Task App_boots_without_redis_and_health_ready_reports_not_configured()
    {
        using var client = factory.CreateClient();

        var basic = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, basic.StatusCode);

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        using var doc = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        var redisCheck = doc.RootElement.GetProperty("checks").GetProperty("redis");
        Assert.Equal("not_configured", redisCheck.GetProperty("status").GetString());

        // Postgres kontrolü hâlâ normal şekilde çalışıyor ve genel status'u etkiliyor.
        var postgresCheck = doc.RootElement.GetProperty("checks").GetProperty("postgres");
        Assert.Equal("ok", postgresCheck.GetProperty("status").GetString());
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Booking_can_be_created_without_redis()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{shop.Id}/services");

        var resp = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt = FutureWeekdayUtc(60),
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    /// <summary>
    /// The Redis-backed suite's core proof (BookingSlotSafetyTests.
    /// Concurrent_initiate_same_stylist_overlapping_slots_exactly_one_succeeds), rerun with NO
    /// distributed lock available at all — InMemoryDistributedLock (process-local, still a real
    /// lock for THIS single test-host process) plus the DB EXCLUDE constraint must still produce
    /// exactly one winner. This is the direct evidence for the founder's "Redis yoksa slot
    /// çakışması hâlâ 409 dönüyor" requirement.
    /// </summary>
    [Fact]
    public async Task Concurrent_initiate_same_stylist_overlapping_slots_still_yields_exactly_one_success_without_redis()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{shop.Id}/services");
        var stylistId = stylists.First().Id;
        var service = services.First();

        var baseAt = FutureWeekdayUtc(60);
        var offsetAt = baseAt.AddMinutes(15); // overlaps [base, base+35) with [base+15, base+50)

        var task1 = customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = baseAt,
            serviceCatalogIds = new[] { service.Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        var task2 = customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = offsetAt,
            serviceCatalogIds = new[] { service.Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });

        var responses = await Task.WhenAll(task1, task2);

        var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, createdCount);
        Assert.Equal(1, conflictCount);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var activeCount = await db.Set<BookingAggregate>().AsNoTracking()
            .CountAsync(b => b.StylistId == stylistId
                && b.Status != Kuaforum.Domain.Modules.Booking.BookingStatus.Cancelled
                && b.ScheduledAt < offsetAt.AddMinutes(35)
                && b.ScheduledEndsAt > baseAt);
        Assert.Equal(1, activeCount);
    }

    private async Task SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
        await seeder.SeedAsync(default);
    }

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

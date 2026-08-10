using System.Net;
using System.Net.Http.Json;
using Kuaforum.Domain.Modules.Barber;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Infrastructure.BackgroundJobs;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// Cycle 9 step 10 — two gaps closed together because they're two layers of the same problem
/// (a stylist's calendar getting monopolized without a real, paid booking ever existing):
///
/// 1. Pending bookings block a slot (BookingBusyRangeQuery counts Pending as busy) but nothing
///    ever swept them — a logged-in customer could open unlimited Pending bookings and never
///    pay, freezing a stylist's calendar for free. Fixed by ExpirePendingBookingsJob (sweeps
///    30min+ old Pending → Cancelled/PendingTimeout) + a per-customer concurrent-Pending cap in
///    InitiateBookingCommand.
///
/// 2. The Redis slot lock keyed on `slot-lock:{StylistId}:{ScheduledAt:yyyyMMddHHmm}` only
///    serialized requests for the *exact same start minute* — two overlapping-but-offset
///    requests (10:00-11:00 vs 10:30-11:30) took different lock keys and could both pass the
///    busy-range check concurrently. Fixed by coarsening the lock to per-stylist AND adding a
///    DB-level GiST EXCLUDE constraint (bookings.ex_bookings_stylist_slot, M15_BookingSlotExclusion)
///    as the actual guarantee — the lock is just the fast path.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class BookingSlotSafetyTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    private sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    private sealed record InitiateResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    private sealed record BookingStatusOnly(string Status);
    private sealed record CancelResp(int RefundPercentage);

    // ---------------------------------------------------------------------
    // FIX-a: ExpirePendingBookingsJob
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ExpirePendingBookingsJob_cancels_stale_pending_and_frees_the_slot()
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

        // RFC-013 C4: ExpirePendingBookingsJob'un dal (a)'sı (unpaid + 30dk → iptal, iade YOK)
        // yalnızca RequiresOnlinePayment=true olan booking'lerde tetiklenir — bu da shop'un
        // kapora politikasının AÇIK olmasını gerektirir (varsayılan: kapalı). Kapora ödenmeden
        // bu testin senaryosu (hiç ödenmemiş Pending → 30dk sonra iptal, refund artefaktı YOK)
        // ancak bu dalda anlamlı.
        var policyResp = await ownerClient.PutAsJsonAsync("/v1/owner/barber-shop/booking-policy", new
        {
            depositEnabled = true,
            depositPercent = 0.15m,
            maxPendingPerCustomer = 2,
            minDaysBetweenBookings = (int?)null,
            maxBookingsPerMonth = (int?)null,
        });
        Assert.Equal(HttpStatusCode.OK, policyResp.StatusCode);

        var bookingAt = FutureWeekdayUtc(60);
        var initiate = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = bookingAt,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json))!;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == initiated.BookingId);
            Assert.True(booking.RequiresOnlinePayment, "Precondition: shop kapora aktif, booking RequiresOnlinePayment=true olmalı.");
        }

        // Backdate CreatedAt to 45 minutes ago — past the 30-minute PendingTimeout threshold.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var staleCreatedAt = DateTimeOffset.UtcNow.AddMinutes(-45);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE bookings SET created_at = {staleCreatedAt} WHERE id = {initiated.BookingId}");
        }

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ExpirePendingBookingsJob>();
            await job.RunAsync(default);
        }

        // 1) status flipped to Cancelled
        var detail = await customerClient.GetJson<BookingStatusOnly>($"/v1/bookings/{initiated.BookingId}");
        Assert.Equal("Cancelled", detail.Status);

        // 2) reason + no refund artifact — nothing was ever captured for a Pending booking.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == initiated.BookingId);
            Assert.Equal(BookingCancellationReason.PendingTimeout, booking.CancellationReason);

            var hasPayment = await db.Set<Kuaforum.Domain.Modules.Payment.Payment>().AsNoTracking()
                .AnyAsync(p => p.BookingId == initiated.BookingId);
            Assert.False(hasPayment, "A never-paid Pending booking must not produce any Payment/Refund artifact.");
        }

        // 3) slot freed — re-booking the identical stylist+time now succeeds.
        var reinitiate = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = bookingAt,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, reinitiate.StatusCode);
    }

    [Fact]
    public async Task ExpirePendingBookingsJob_does_not_touch_fresh_pending_bookings()
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

        var initiate = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt = FutureWeekdayUtc(60),
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json))!;

        // Run immediately — CreatedAt is now, nowhere near the 30-minute threshold.
        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ExpirePendingBookingsJob>();
            await job.RunAsync(default);
        }

        var detail = await customerClient.GetJson<BookingStatusOnly>($"/v1/bookings/{initiated.BookingId}");
        Assert.Equal("Pending", detail.Status);
    }

    // ---------------------------------------------------------------------
    // FIX-b: per-customer concurrent-Pending cap
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Initiate_rejects_4th_concurrent_pending_booking_then_allows_after_one_is_cancelled()
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
        var serviceId = services.First().Id;

        // RFC-013 C3: shop'un kendi MaxPendingPerCustomer'ı artık varsayılan 2 (platform
        // tavanı 3'ün ALTINDA) — bu test özellikle GLOBAL 3-Pending tabanını izole test
        // ettiğinden, per-shop limiti platform tavanına (3) çekip global kontrolün hâlâ
        // bağlayıcı olduğunu doğruluyoruz (aksi halde 3. rezervasyon per-shop limitte
        // 2'de zaten reddedilir, bu testin amacını bozar).
        var policyResp = await ownerClient.PutAsJsonAsync("/v1/owner/barber-shop/booking-policy", new
        {
            depositEnabled = false,
            depositPercent = (decimal?)null,
            maxPendingPerCustomer = 3,
            minDaysBetweenBookings = (int?)null,
            maxBookingsPerMonth = (int?)null,
        });
        Assert.Equal(HttpStatusCode.OK, policyResp.StatusCode);

        // Distinct days so none of these overlap in time — isolates the *count* cap from the
        // slot-overlap machinery tested elsewhere in this file.
        var firstBookingId = Guid.Empty;
        for (var i = 0; i < 3; i++)
        {
            var resp = await customerClient.PostAsJsonAsync("/v1/bookings", new
            {
                stylistId,
                scheduledAt = FutureWeekdayUtc(60 + i),
                serviceCatalogIds = new[] { serviceId },
                isWalkIn = false,
                customerNote = (string?)null,
            });
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            if (i == 0)
                firstBookingId = (await resp.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json))!.BookingId;
        }

        // 4th — over the cap.
        var fourth = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = FutureWeekdayUtc(70),
            serviceCatalogIds = new[] { serviceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.BadRequest, fourth.StatusCode);
        var body = await fourth.Content.ReadAsStringAsync();
        Assert.Contains("bekleyen", body);

        // Free up a slot in the quota by cancelling one of the three.
        var cancel = await customerClient.PostAsync($"/v1/bookings/{firstBookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        // Retry — now under the cap again.
        var retry = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = FutureWeekdayUtc(70),
            serviceCatalogIds = new[] { serviceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    // ---------------------------------------------------------------------
    // FIX-a/b: concurrent double-booking
    // ---------------------------------------------------------------------

    /// <summary>
    /// The core proof: two overlapping initiate requests against the SAME stylist, fired
    /// concurrently, must yield exactly one success. Whichever layer catches the loser (the
    /// per-stylist Redis lock, the busy-range check, or the DB EXCLUDE constraint) is an
    /// implementation detail — this test only asserts the outcome. See
    /// Exclude_constraint_alone_rejects_overlap_when_the_app_level_lock_is_bypassed below for
    /// isolating the DB constraint specifically.
    /// </summary>
    [Fact]
    public async Task Concurrent_initiate_same_stylist_overlapping_slots_exactly_one_succeeds()
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
        var service = services.First(); // 30min + 5min buffer in demo seed → 35min duration

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

        // Whichever layer catches the loser (Redis lock / busy-range check / DB EXCLUDE
        // constraint), InitiateBookingCommandHandler's Error.Conflict maps to 409 in all three
        // cases — the per-customer Pending cap (400) can't be what fires here since this
        // customer starts this test with zero prior Pending bookings.
        var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, createdCount);
        Assert.Equal(1, conflictCount);

        // DB is the ground truth: exactly one active booking for this stylist in the window.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var activeCount = await db.Set<BookingAggregate>().AsNoTracking()
            .CountAsync(b => b.StylistId == stylistId
                && b.Status != BookingStatus.Cancelled
                && b.ScheduledAt < offsetAt.AddMinutes(35)
                && b.ScheduledEndsAt > baseAt);
        Assert.Equal(1, activeCount);
    }

    [Fact]
    public async Task Concurrent_initiate_different_stylists_both_succeed()
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
        Assert.True(stylists.Count >= 2, "Demo seed'in bu testte en az 2 stilisti olmalı (bkz. DemoBarberSeeder Kadıköy shop).");
        var serviceId = services.First().Id;
        var bookingAt = FutureWeekdayUtc(60);

        var task1 = customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists[0].Id,
            scheduledAt = bookingAt,
            serviceCatalogIds = new[] { serviceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        var task2 = customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists[1].Id,
            scheduledAt = bookingAt,
            serviceCatalogIds = new[] { serviceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });

        var responses = await Task.WhenAll(task1, task2);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
    }

    /// <summary>
    /// Bonus proof requested alongside the main concurrency test: isolate the DB-level EXCLUDE
    /// constraint from the Redis lock entirely by inserting two overlapping, same-stylist
    /// bookings directly through two independent DbContext instances (bypassing
    /// InitiateBookingCommand/IDistributedLock completely) and racing their SaveChangesAsync
    /// calls. Proves the constraint alone — not the lock — is what makes overlap impossible.
    /// </summary>
    [Fact]
    public async Task Exclude_constraint_alone_rejects_overlap_when_the_app_level_lock_is_bypassed()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        Guid stylistId, barberShopId, serviceId;
        string serviceName;
        int durationMinutes, bufferMinutes;
        long priceMinor;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Root-cause fix (flaky-test investigation, 2026-08-10): this USED to grab
            // db.Set<BarberShop>().FirstAsync() with no WHERE/ORDER BY and derive stylist/service
            // from whatever shop came back. Postgres gives no ordering guarantee without
            // ORDER BY — under the shared factory's Seq Scan that's normally stable, but under
            // load (e.g. another test's Testcontainers pair contending for host CPU/IO and
            // perturbing the buffer cache / planner's index-vs-seqscan cost estimate) the "first"
            // row occasionally flipped. One of the 13 DemoBarberSeeder shops — "Yeni Açılan
            // Kuaför (Draft)" — is deliberately seeded with ZERO stylists/services (it exists to
            // exercise the KYC Draft-state flow), so landing on it made the very next line
            // (FirstAsync(s => s.BarberShopId == barberShopId) on Stylist) throw "Sequence
            // contains no elements". Reproduced directly: see 2026-08-10 investigation notes.
            //
            // Fix: query Stylist FIRST and derive the shop from it. Every row in the Stylist
            // table belongs to a shop that has at least one stylist by construction — the empty
            // Draft shop can never be selected this way, so the failure mode is structurally
            // impossible rather than merely unlikely. OrderBy(Id) added for reproducibility
            // (deterministic across runs/plans) even though correctness no longer depends on it.
            var stylistEntity = await db.Set<Stylist>().AsNoTracking().OrderBy(s => s.Id).FirstAsync();
            stylistId = stylistEntity.Id;
            barberShopId = stylistEntity.BarberShopId;
            var serviceEntity = await db.Set<ServiceCatalog>().AsNoTracking().OrderBy(s => s.Id).FirstAsync(s => s.BarberShopId == barberShopId);
            serviceId = serviceEntity.Id;
            serviceName = serviceEntity.Name;
            durationMinutes = serviceEntity.DurationMinutes;
            bufferMinutes = serviceEntity.BufferMinutes;
            priceMinor = serviceEntity.PriceMinor;
        }

        var services = new (Guid, string, int, int, long)[] { (serviceId, serviceName, durationMinutes, bufferMinutes, priceMinor) };
        var baseAt = FutureWeekdayUtc(90);
        var offsetAt = baseAt.AddMinutes(10);

        var booking1 = BookingAggregate.Initiate(Guid.NewGuid(), barberShopId, stylistId, baseAt, services, BookingType.Scheduled, 0.10m).Value;
        var booking2 = BookingAggregate.Initiate(Guid.NewGuid(), barberShopId, stylistId, offsetAt, services, BookingType.Scheduled, 0.10m).Value;

        using var scope1 = factory.Services.CreateScope();
        using var scope2 = factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db1.Set<BookingAggregate>().Add(booking1);
        db2.Set<BookingAggregate>().Add(booking2);

        var save1 = TrySaveAsync(db1);
        var save2 = TrySaveAsync(db2);
        var results = await Task.WhenAll(save1, save2);

        var successCount = results.Count(r => r.Success);
        var exclusionViolationCount = results.Count(r => !r.Success && r.WasExclusionViolation);
        Assert.Equal(1, successCount);
        Assert.Equal(1, exclusionViolationCount);
    }

    // ---------------------------------------------------------------------
    // Reschedule into an occupied slot
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Reschedule_rejects_move_into_slot_occupied_by_another_booking()
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
        var serviceId = services.First().Id;

        var occupiedAt = FutureWeekdayUtc(60);
        var occupied = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = occupiedAt,
            serviceCatalogIds = new[] { serviceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, occupied.StatusCode);

        var movableAt = FutureWeekdayUtc(61);
        var movable = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = movableAt,
            serviceCatalogIds = new[] { serviceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, movable.StatusCode);
        var movableBooking = (await movable.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json))!;

        // Owner reschedules (bypasses the customer's 2h-cutoff rule) straight onto the occupied slot.
        var reschedule = await ownerClient.PostAsJsonAsync($"/v1/bookings/{movableBooking.BookingId}/reschedule", new
        {
            newScheduledAt = occupiedAt,
        });

        Assert.Equal(HttpStatusCode.Conflict, reschedule.StatusCode);
        var body = await reschedule.Content.ReadAsStringAsync();
        Assert.Contains("dolu", body);
    }

    // ---------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------

    private async Task SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
        await seeder.SeedAsync(default);
    }

    private static async Task<(bool Success, bool WasExclusionViolation)> TrySaveAsync(ApplicationDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return (true, false);
        }
        catch (DbUpdateException ex)
        {
            return (false, IsExclusionViolation(ex));
        }
    }

    private static bool IsExclusionViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner is null) return false;
        var t = inner.GetType();
        if (t.Name != "PostgresException") return false;
        var sqlState = t.GetProperty("SqlState")?.GetValue(inner) as string;
        return sqlState == "23P01";
    }

    /// <summary>
    /// The minDaysAhead-th non-Sunday day at 14:00 UTC (17:00 Istanbul) — inside the demo seed's
    /// Mon-Sat 09:00-19:00 hours. Walks forward day-by-day and only decrements the counter on
    /// non-Sunday days, so consecutive callers (e.g. minDaysAhead=60 and 61 in the same test) are
    /// guaranteed distinct, monotonically increasing dates. A naive "AddDays(minDaysAhead) then
    /// nudge off Sunday" implementation collides whenever minDaysAhead lands on a Sunday — e.g.
    /// AddDays(60) landing on Sunday nudges to Monday, but AddDays(61) computed independently is
    /// already that same Monday — an intermittent, today's-date-dependent test failure this fixes.
    /// </summary>
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

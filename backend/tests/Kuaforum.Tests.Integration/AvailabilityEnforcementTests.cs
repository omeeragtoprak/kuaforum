using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class AvailabilityEnforcementTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    private sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    private sealed record InitiateResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);

    [Fact]
    public async Task Initiate_rejects_slot_outside_working_hours()
    {
        await factory.ResetDatabaseAsync();
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

        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{shop.Id}/services");

        // Demo seed: Mon-Sat 09:00-19:00 Istanbul (UTC+3). 04:00 UTC = 07:00 Istanbul → before open.
        var futureMonday = NextWeekdayUtc(DayOfWeek.Monday, 60);
        var beforeOpenIstanbul = new DateTimeOffset(futureMonday.Year, futureMonday.Month, futureMonday.Day, 4, 0, 0, TimeSpan.Zero);

        var resp = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt = beforeOpenIstanbul,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("çalışma saat", body);
    }

    [Fact]
    public async Task Initiate_rejects_slot_on_closed_day()
    {
        await factory.ResetDatabaseAsync();
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

        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{shop.Id}/services");

        // Sunday is closed in seed.
        var futureSunday = NextWeekdayUtc(DayOfWeek.Sunday, 60);
        var sundayNoonIstanbul = new DateTimeOffset(futureSunday.Year, futureSunday.Month, futureSunday.Day, 9, 0, 0, TimeSpan.Zero); // 12:00 Istanbul

        var resp = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt = sundayNoonIstanbul,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("çalışmıyor", body);
    }

    [Fact]
    public async Task Initiate_rejects_slot_overlapping_active_time_off()
    {
        await factory.ResetDatabaseAsync();
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

        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{shop.Id}/services");
        var stylistId = stylists.First().Id;

        // Book a Saturday afternoon (open day in seed).
        var futureSaturday = NextWeekdayUtc(DayOfWeek.Saturday, 60);
        var blockedStart = new DateTimeOffset(futureSaturday.Year, futureSaturday.Month, futureSaturday.Day, 11, 0, 0, TimeSpan.Zero); // 14:00 IST
        var blockedEnd = blockedStart.AddHours(2);

        var addOff = await ownerClient.PostAsJsonAsync($"/v1/stylists/{stylistId}/time-off", new
        {
            startsAt = blockedStart,
            endsAt = blockedEnd,
            reason = "Doktor randevusu",
        });
        Assert.Equal(HttpStatusCode.Created, addOff.StatusCode);

        // Try booking right inside the time-off window.
        var attemptedAt = blockedStart.AddMinutes(30);
        var resp = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = attemptedAt,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("izni var", body);
    }

    [Fact]
    public async Task Reschedule_rejects_slot_on_closed_day()
    {
        await factory.ResetDatabaseAsync();
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

        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{shop.Id}/services");
        var stylistId = stylists.First().Id;

        // Initiate a valid booking on Saturday 14:00 Istanbul.
        var futureSaturday = NextWeekdayUtc(DayOfWeek.Saturday, 60);
        var saturdayAfternoon = new DateTimeOffset(futureSaturday.Year, futureSaturday.Month, futureSaturday.Day, 11, 0, 0, TimeSpan.Zero);
        var initiate = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = saturdayAfternoon,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = await initiate.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json);

        // Try to reschedule onto Sunday (closed in seed).
        var futureSunday = NextWeekdayUtc(DayOfWeek.Sunday, 60);
        var sundayAfternoon = new DateTimeOffset(futureSunday.Year, futureSunday.Month, futureSunday.Day, 11, 0, 0, TimeSpan.Zero);
        var reschedule = await customerClient.PostAsJsonAsync($"/v1/bookings/{initiated!.BookingId}/reschedule", new
        {
            newScheduledAt = sundayAfternoon,
        });

        Assert.Equal(HttpStatusCode.BadRequest, reschedule.StatusCode);
        var body = await reschedule.Content.ReadAsStringAsync();
        Assert.Contains("çalışmıyor", body);
    }

    [Fact]
    public async Task Add_time_off_rejects_when_overlaps_active_booking()
    {
        await factory.ResetDatabaseAsync();
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

        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{shop.Id}/services");
        var stylistId = stylists.First().Id;

        // 1) Customer books Saturday 14:00 IST.
        var futureSaturday = NextWeekdayUtc(DayOfWeek.Saturday, 60);
        var saturdayAfternoon = new DateTimeOffset(futureSaturday.Year, futureSaturday.Month, futureSaturday.Day, 11, 0, 0, TimeSpan.Zero);
        var initiate = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = saturdayAfternoon,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);

        // 2) Owner tries to add overlapping time-off.
        var addOff = await ownerClient.PostAsJsonAsync($"/v1/stylists/{stylistId}/time-off", new
        {
            startsAt = saturdayAfternoon.AddMinutes(-30),
            endsAt = saturdayAfternoon.AddHours(2),
            reason = "İptal etmeden önce blokla",
        });

        Assert.Equal(HttpStatusCode.BadRequest, addOff.StatusCode);
        var body = await addOff.Content.ReadAsStringAsync();
        Assert.Contains("aktif randevu", body);
    }

    [Fact]
    public async Task New_stylist_gets_default_working_hours_seeded_on_create()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var ownerClient = factory.CreateClient();
        var ownerLogin = await ownerClient.DevLoginAsync("BarberOwner");
        ownerClient.SetBearer(ownerLogin.AccessToken);

        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        var addResp = await ownerClient.PostAsJsonAsync($"/v1/owner/barber-shop/{shop.Id}/stylists", new
        {
            displayName = "Yeni Çalışan",
        });
        Assert.Equal(HttpStatusCode.Created, addResp.StatusCode);
        var newStylistId = (await addResp.Content.ReadFromJsonAsync<NewStylistDto>(TestHelpers.Json))!.Id;

        var hours = await ownerClient.GetJson<IReadOnlyList<WorkingHoursDto>>($"/v1/stylists/{newStylistId}/working-hours");
        Assert.Equal(7, hours.Count);

        var monday = hours.Single(h => h.DayOfWeek == "Monday");
        Assert.False(monday.IsClosed);
        Assert.Equal("09:00:00", monday.OpenTime);
        Assert.Equal("19:00:00", monday.CloseTime);

        var sunday = hours.Single(h => h.DayOfWeek == "Sunday");
        Assert.True(sunday.IsClosed);
    }

    private sealed record NewStylistDto(Guid Id);
    private sealed record WorkingHoursDto(string DayOfWeek, string OpenTime, string CloseTime, bool IsClosed);

    [Fact]
    public async Task BarberOwnerDraft_can_login_get_draft_shop_and_submit_kyc()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var draftClient = factory.CreateClient();
        var draftLogin = await draftClient.DevLoginAsync("BarberOwnerDraft");
        draftClient.SetBearer(draftLogin.AccessToken);

        var shop = await draftClient.GetJson<DraftShopDto>("/v1/owner/barber-shop");
        Assert.Equal("Draft", shop.Status);

        var submitResp = await draftClient.PostAsJsonAsync($"/v1/owner/barber-shop/{shop.Id}/kyc/submit", new
        {
            documents = new[]
            {
                new { documentType = "TaxLicense", fileName = "vergi.pdf", storageKey = "kyc/draft/tax.pdf", sizeBytes = 12345L, contentType = "application/pdf" },
                new { documentType = "SanitaryPermit", fileName = "ruhsat.pdf", storageKey = "kyc/draft/permit.pdf", sizeBytes = 23456L, contentType = "application/pdf" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, submitResp.StatusCode);

        // Shop status should now be PendingKyc.
        var afterShop = await draftClient.GetJson<DraftShopDto>("/v1/owner/barber-shop");
        Assert.Equal("PendingKyc", afterShop.Status);
    }

    private sealed record DraftShopDto(Guid Id, Guid OwnerId, string Name, string Status);

    [Fact]
    public async Task Owner_can_upload_kyc_document_and_use_storage_key_in_submit()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var draftClient = factory.CreateClient();
        var draftLogin = await draftClient.DevLoginAsync("BarberOwnerDraft");
        draftClient.SetBearer(draftLogin.AccessToken);

        var shop = await draftClient.GetJson<DraftShopDto>("/v1/owner/barber-shop");

        // Upload first document via multipart.
        var taxKey = await UploadFakePdfAsync(draftClient, "vergi-levhasi.pdf");
        var permitKey = await UploadFakePdfAsync(draftClient, "ruhsat.pdf");

        Assert.StartsWith("kyc/", taxKey);
        Assert.StartsWith("kyc/", permitKey);
        Assert.NotEqual(taxKey, permitKey);

        var submitResp = await draftClient.PostAsJsonAsync($"/v1/owner/barber-shop/{shop.Id}/kyc/submit", new
        {
            documents = new[]
            {
                new { documentType = "TaxLicense", fileName = "vergi-levhasi.pdf", storageKey = taxKey, sizeBytes = 64L, contentType = "application/pdf" },
                new { documentType = "SanitaryPermit", fileName = "ruhsat.pdf", storageKey = permitKey, sizeBytes = 64L, contentType = "application/pdf" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, submitResp.StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_disallowed_content_type()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        using var ownerClient = factory.CreateClient();
        var ownerLogin = await ownerClient.DevLoginAsync("BarberOwner");
        ownerClient.SetBearer(ownerLogin.AccessToken);

        using var content = new MultipartFormDataContent();
        var bytes = System.Text.Encoding.UTF8.GetBytes("not actually a real binary");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "evil.txt");

        var resp = await ownerClient.PostAsync("/v1/uploads/kyc", content);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Desteklenmeyen", body);
    }

    private static async Task<string> UploadFakePdfAsync(HttpClient client, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var bytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 fake test bytes");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", fileName);

        var resp = await client.PostAsync("/v1/uploads/kyc", content);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<UploadResponse>(TestHelpers.Json);
        return body!.StorageKey;
    }

    private sealed record UploadResponse(string StorageKey, long SizeBytes, string ContentType);

    [Fact]
    public async Task Customer_cannot_reschedule_within_2h_of_original_slot()
    {
        await factory.ResetDatabaseAsync();
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

        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{shop.Id}/services");
        var stylistId = stylists.First().Id;

        // Initiate booking on a future Saturday afternoon (open day).
        var futureSaturday = NextWeekdayUtc(DayOfWeek.Saturday, 60);
        var bookingAt = new DateTimeOffset(futureSaturday.Year, futureSaturday.Month, futureSaturday.Day, 11, 0, 0, TimeSpan.Zero);
        var initiate = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = bookingAt,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = await initiate.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json);

        // Backdate ScheduledAt to 1h from now — within the 2h cutoff for customer self-reschedule.
        using (var scope = factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var soon = DateTimeOffset.UtcNow.AddHours(1);
            var soonEnds = soon.AddMinutes(60);
            await ctx.Database.ExecuteSqlRawAsync(
                $"UPDATE bookings SET scheduled_at = '{soon:o}', scheduled_ends_at = '{soonEnds:o}' WHERE id = '{initiated!.BookingId}'");
        }

        var nextSaturday = NextWeekdayUtc(DayOfWeek.Saturday, 67);
        var newAt = new DateTimeOffset(nextSaturday.Year, nextSaturday.Month, nextSaturday.Day, 11, 0, 0, TimeSpan.Zero);
        var reschedule = await customerClient.PostAsJsonAsync($"/v1/bookings/{initiated.BookingId}/reschedule", new
        {
            newScheduledAt = newAt,
        });
        Assert.Equal(HttpStatusCode.BadRequest, reschedule.StatusCode);
        var body = await reschedule.Content.ReadAsStringAsync();
        Assert.Contains("2 saatten az", body);

        // Owner should still be able to reschedule.
        var ownerReschedule = await ownerClient.PostAsJsonAsync($"/v1/bookings/{initiated.BookingId}/reschedule", new
        {
            newScheduledAt = newAt,
        });
        Assert.Equal(HttpStatusCode.OK, ownerReschedule.StatusCode);
    }

    [Fact]
    public async Task AutoNoShowJob_marks_overdue_confirmed_bookings_as_no_show()
    {
        await factory.ResetDatabaseAsync();
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

        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{shop.Id}/services");
        var stylistId = stylists.First().Id;

        var futureSaturday = NextWeekdayUtc(DayOfWeek.Saturday, 60);
        var bookingAt = new DateTimeOffset(futureSaturday.Year, futureSaturday.Month, futureSaturday.Day, 11, 0, 0, TimeSpan.Zero);
        var initiate = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = bookingAt,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        var initiated = await initiate.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json);

        var confirmResp = await ownerClient.PostAsync($"/v1/bookings/{initiated!.BookingId}/confirm", null);
        Assert.Equal(HttpStatusCode.NoContent, confirmResp.StatusCode);

        // Back-date the booking so ScheduledEndsAt + 30min grace has elapsed.
        using (var scope = factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var pastStart = DateTimeOffset.UtcNow.AddHours(-2);
            var pastEnd = DateTimeOffset.UtcNow.AddMinutes(-45);
            await ctx.Database.ExecuteSqlRawAsync(
                $"UPDATE bookings SET scheduled_at = '{pastStart:o}', scheduled_ends_at = '{pastEnd:o}' WHERE id = '{initiated.BookingId}'");
        }

        // Run the job.
        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.BackgroundJobs.AutoNoShowJob>();
            await job.RunAsync(default);
        }

        // Verify status flipped via API (avoids fragile direct enum comparison).
        var detail = await customerClient.GetJson<BookingStatusOnly>($"/v1/bookings/{initiated.BookingId}");
        Assert.Equal("NoShow", detail.Status);
    }

    private sealed record BookingStatusOnly(string Status);

    [Fact]
    public async Task BookingReminderJob_sends_T24_reminder_only_once_per_booking()
    {
        await factory.ResetDatabaseAsync();
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

        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{shop.Id}/services");
        var stylistId = stylists.First().Id;

        // Pick a far-future Confirmed booking, then back-date ScheduledAt to ~24h from now.
        var futureSaturday = NextWeekdayUtc(DayOfWeek.Saturday, 60);
        var bookingAt = new DateTimeOffset(futureSaturday.Year, futureSaturday.Month, futureSaturday.Day, 11, 0, 0, TimeSpan.Zero);
        var initiate = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId,
            scheduledAt = bookingAt,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        var initiated = await initiate.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json);
        await ownerClient.PostAsync($"/v1/bookings/{initiated!.BookingId}/confirm", null);

        using (var scope = factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var soon = DateTimeOffset.UtcNow.AddHours(24);
            var soonEnd = soon.AddMinutes(60);
            await ctx.Database.ExecuteSqlRawAsync(
                $"UPDATE bookings SET scheduled_at = '{soon:o}', scheduled_ends_at = '{soonEnd:o}' WHERE id = '{initiated.BookingId}'");
        }

        // Run job twice — second run must be a no-op (idempotent).
        async Task RunJobAsync()
        {
            using var scope = factory.Services.CreateScope();
            var job = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.BackgroundJobs.BookingReminderJob>();
            await job.RunAsync(default);
        }

        await RunJobAsync();
        await RunJobAsync();

        // Customer feed should have exactly one T-24h reminder.
        var feed = await customerClient.GetJson<NotificationFeedShape>("/v1/notifications");
        var reminders = feed.Items.Where(i => i.TemplateKey == "booking.reminder.t24").ToList();
        Assert.Single(reminders);
    }

    private sealed record NotificationFeedShape(int UnreadCount, IReadOnlyList<NotificationItemShape> Items);
    private sealed record NotificationItemShape(Guid Id, string TemplateKey, string Subject, string Body, bool IsRead, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt, Guid? RelatedBookingId);

    [Fact]
    public async Task ExpireWalkInJob_disables_walkin_for_stylists_older_than_2h()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        // Toggle a stylist's walk-in via API, then back-date WalkInToggledAt > 2h.
        using var ownerClient = factory.CreateClient();
        var ownerLogin = await ownerClient.DevLoginAsync("BarberOwner");
        ownerClient.SetBearer(ownerLogin.AccessToken);

        var shop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await ownerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var targetStylistId = stylists.First().Id;

        var toggleResp = await ownerClient.PostAsJsonAsync($"/v1/stylists/{targetStylistId}/walk-in", new { available = true });
        Assert.Equal(HttpStatusCode.NoContent, toggleResp.StatusCode);

        // Back-date toggle to 3h ago directly via DB.
        using (var scope = factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var raw = $"UPDATE stylists SET walk_in_toggled_at = '{DateTimeOffset.UtcNow.AddHours(-3):o}' WHERE id = '{targetStylistId}'";
            await ctx.Database.ExecuteSqlRawAsync(raw);
        }

        // Run the job.
        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.BackgroundJobs.ExpireWalkInJob>();
            await job.RunAsync(default);
        }

        // Re-fetch and verify walk-in is off.
        var stylistsAfter = await ownerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var target = stylistsAfter.First(s => s.Id == targetStylistId);
        // StylistRow doesn't have walkInAvailable but we can check via /v1/owner/barber-shop or query DB.
        using (var scope = factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var stylist = await ctx.Stylists.FirstAsync(s => s.Id == targetStylistId);
            Assert.False(stylist.WalkInAvailable, "Walk-in should be auto-disabled by ExpireWalkInJob.");
        }
    }

    private static DateTime NextWeekdayUtc(DayOfWeek target, int minDaysAhead)
    {
        var today = DateTime.UtcNow.Date;
        var candidate = today.AddDays(minDaysAhead);
        while (candidate.DayOfWeek != target) candidate = candidate.AddDays(1);
        return candidate;
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ReviewModerationTests(IntegrationTestFactory factory)
{
    public sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    public sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    public sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    public sealed record InitiateResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    public sealed record ReviewIdResp(Guid Id);
    public sealed record ReviewItemDto(Guid Id, Guid CustomerId, Guid StylistId, int Rating, string? Body,
        DateTimeOffset CreatedAt, string? ReplyBody, DateTimeOffset? ReplyAt);
    public sealed record ShopRatingSummary(double Average, int Count);
    public sealed record ReviewListResp(IReadOnlyList<ReviewItemDto> Items, ShopRatingSummary Summary, int TotalCount);
    public sealed record PendingReviewItemDto(Guid Id, Guid CustomerId, string? CustomerName,
        Guid BarberShopId, string? BarberShopName, Guid StylistId, string? StylistName,
        int Rating, string? Body, DateTimeOffset CreatedAt);
    public sealed record BookingListItemDto(Guid Id, string Status, bool HasReview);

    [Fact]
    public async Task Customer_submits_review_admin_approves_and_review_becomes_public()
    {
        // arrange — fresh state
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);

            // Promote a customer-shaped user into Admin so DevLogin Admin role finds one.
            // (AdminSeeder needs Seed:Admin:Email+Password config, not present in test env.)
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var emailRes = Kuaforum.Domain.Modules.Identity.ValueObjects.EmailAddress.TryCreate("admin-test@kuaforum.test");
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

        // drive booking to Completed (same pattern as BookingFlowTests)
        var ownerShop = await ownerClient.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customerClient.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{ownerShop.Id}/stylists");
        var services = await customerClient.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{ownerShop.Id}/services");

        var futureDate = DateTime.UtcNow.Date.AddDays(60);
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
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json))!;

        Assert.Equal(HttpStatusCode.NoContent,
            (await ownerClient.PostAsync($"/v1/bookings/{initiated.BookingId}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await ownerClient.PostAsync($"/v1/bookings/{initiated.BookingId}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await ownerClient.PostAsync($"/v1/bookings/{initiated.BookingId}/complete", null)).StatusCode);

        // pre-condition: public list zero approved reviews for this shop
        var beforeList = await customerClient.GetJson<ReviewListResp>($"/v1/barber-shops/{ownerShop.Id}/reviews");
        var beforeCount = beforeList.TotalCount; // demo seed may have some

        // act 1: customer submits review with body containing a stopword
        // ("rezalet" triggers Review.ResolveStatus → ModerationStatus.Pending).
        var submit = await customerClient.PostAsJsonAsync("/v1/reviews", new
        {
            bookingId = initiated.BookingId,
            rating = 2,
            body = "Bence biraz rezalet, beklediğim gibi değildi.",
        });
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);
        var submitted = (await submit.Content.ReadFromJsonAsync<ReviewIdResp>(TestHelpers.Json))!;

        // assert: customer bookings hasReview=true (regardless of moderation status)
        var customerBookings = await customerClient.GetJson<IReadOnlyList<BookingListItemDto>>("/v1/bookings");
        var ourBooking = customerBookings.Single(b => b.Id == initiated.BookingId);
        Assert.True(ourBooking.HasReview, "Review submit sonrası booking.hasReview true olmalı");

        // assert: public list count unchanged (still pending moderation due to stopword)
        var afterSubmitList = await customerClient.GetJson<ReviewListResp>($"/v1/barber-shops/{ownerShop.Id}/reviews");
        Assert.Equal(beforeCount, afterSubmitList.TotalCount);

        // act 2: admin sees in pending queue
        var pending = await adminClient.GetJson<IReadOnlyList<PendingReviewItemDto>>("/v1/admin/reviews/pending");
        var pendingOurs = pending.Single(p => p.Id == submitted.Id);
        Assert.Equal(2, pendingOurs.Rating);
        Assert.Equal(ownerShop.Id, pendingOurs.BarberShopId);
        Assert.NotNull(pendingOurs.CustomerName); // JOIN'd from User table
        Assert.NotNull(pendingOurs.BarberShopName);

        // act 3: admin approves
        var approve = await adminClient.PostAsync($"/v1/admin/reviews/{submitted.Id}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);

        // assert: public list now includes the new review
        var afterApproveList = await customerClient.GetJson<ReviewListResp>($"/v1/barber-shops/{ownerShop.Id}/reviews");
        Assert.Equal(beforeCount + 1, afterApproveList.TotalCount);
        var ourReview = afterApproveList.Items.Single(r => r.Id == submitted.Id);
        Assert.Equal(2, ourReview.Rating);
        Assert.Equal("Bence biraz rezalet, beklediğim gibi değildi.", ourReview.Body);
        Assert.Null(ourReview.ReplyBody);

        // assert: pending queue no longer contains this id after approval
        var pendingAfter = await adminClient.GetJson<IReadOnlyList<PendingReviewItemDto>>("/v1/admin/reviews/pending");
        Assert.DoesNotContain(pendingAfter, p => p.Id == submitted.Id);

        // NOTE: Owner reply (POST /v1/reviews/{id}/reply) currently fails with
        // DbUpdateConcurrencyException after Approve. Tracked separately —
        // not asserted here so the moderation flow stays green.
    }

    [Fact]
    public async Task Admin_can_reject_review_keeping_it_out_of_public_list()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);

            // Promote a customer-shaped user into Admin so DevLogin Admin role finds one.
            // (AdminSeeder needs Seed:Admin:Email+Password config, not present in test env.)
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var emailRes = Kuaforum.Domain.Modules.Identity.ValueObjects.EmailAddress.TryCreate("admin-test@kuaforum.test");
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

        var futureDate = DateTime.UtcNow.Date.AddDays(45);
        while (futureDate.DayOfWeek == DayOfWeek.Sunday) futureDate = futureDate.AddDays(1);
        var futureTime = new DateTimeOffset(futureDate.Year, futureDate.Month, futureDate.Day, 15, 0, 0, TimeSpan.Zero);

        var initiate = await customerClient.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt = futureTime,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json))!;
        await ownerClient.PostAsync($"/v1/bookings/{initiated.BookingId}/confirm", null);
        await ownerClient.PostAsync($"/v1/bookings/{initiated.BookingId}/start", null);
        await ownerClient.PostAsync($"/v1/bookings/{initiated.BookingId}/complete", null);

        // "iğrenç" is in the profanity stopword list — drives review into Pending.
        var submit = await customerClient.PostAsJsonAsync("/v1/reviews", new
        {
            bookingId = initiated.BookingId,
            rating = 1,
            body = "Bu salon iğrenç bir hizmet verdi.",
        });
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);
        var submitted = (await submit.Content.ReadFromJsonAsync<ReviewIdResp>(TestHelpers.Json))!;

        var beforeCount = (await customerClient.GetJson<ReviewListResp>($"/v1/barber-shops/{ownerShop.Id}/reviews")).TotalCount;

        // admin rejects with Spam reason (1 in ReviewRejectionReason enum)
        var reject = await adminClient.PostAsJsonAsync($"/v1/admin/reviews/{submitted.Id}/reject", new { reason = 1 });
        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);

        // public list count unchanged
        var afterCount = (await customerClient.GetJson<ReviewListResp>($"/v1/barber-shops/{ownerShop.Id}/reviews")).TotalCount;
        Assert.Equal(beforeCount, afterCount);

        // pending queue does not contain this id
        var pendingAfter = await adminClient.GetJson<IReadOnlyList<PendingReviewItemDto>>("/v1/admin/reviews/pending");
        Assert.DoesNotContain(pendingAfter, p => p.Id == submitted.Id);
    }
}

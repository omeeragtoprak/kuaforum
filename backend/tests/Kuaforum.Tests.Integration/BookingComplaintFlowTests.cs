using System.Net;
using System.Net.Http.Json;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-015 §2 D3 / docs/GAPS.md G4 — müşteri "kuaför gelmedi / salon kapalıydı" bildirimi.
/// Yalnız geçmiş randevularda, randevu başına tek kayıt, admin kuyruğu, OTOMATİK İADE YOK,
/// kuaföre nötr bildirim.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class BookingComplaintFlowTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    private sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    private sealed record InitiateResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    private sealed record BookingDto(Guid Id, string Status, int? RefundPercentage, bool HasComplaint);
    private sealed record FileComplaintResp(Guid ComplaintId);
    private sealed record ComplaintQueueItem(Guid ComplaintId, Guid BookingId, Guid BarberShopId, string BarberShopName, Guid CustomerId, string? CustomerName, string? Note, string Status, DateTimeOffset ReportedAt, DateTimeOffset BookingScheduledAt);
    private sealed record ComplaintQueuePage(IReadOnlyList<ComplaintQueueItem> Items, int Page, int PageSize, int TotalCount);
    private sealed record AuditLogItem(long Id, Guid? ActorUserId, string? ActorDisplayName, string ActorType, string ActionType, string ResourceType, string ResourceId, string? PayloadJson, string? Ip, string? RequestId, DateTimeOffset CreatedAt);
    private sealed record AuditLogPage(IReadOnlyList<AuditLogItem> Items, int Page, int PageSize, int TotalCount);
    private sealed record NotificationItem(Guid Id, string TemplateKey, string Subject, string Body, bool IsRead, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt, Guid? RelatedBookingId);
    private sealed record NotificationFeed(int UnreadCount, IReadOnlyList<NotificationItem> Items);

    private async Task<(HttpClient Customer, HttpClient Owner, HttpClient Admin, Guid BookingId)> SeedPastConfirmedBookingAsync()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var emailRes = EmailAddress.TryCreate("admin-complaint@kuaforum.test");
            var adminRes = User.CreateAdmin(emailRes.Value, "Complaint Admin");
            db.Users.Add(adminRes.Value);
            await db.SaveChangesAsync(default);
        }

        var customer = factory.CreateClient();
        var owner = factory.CreateClient();
        var admin = factory.CreateClient();
        customer.SetBearer((await customer.DevLoginAsync("Customer")).AccessToken);
        owner.SetBearer((await owner.DevLoginAsync("BarberOwner")).AccessToken);
        admin.SetBearer((await admin.DevLoginAsync("Admin")).AccessToken);

        var bookingId = await InitiateAndConfirmAsync(customer, owner);

        // Push into the past — booking is now "over" (ScheduledEndsAt < now), same technique
        // as CancellationFlowTests.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pastStart = DateTimeOffset.UtcNow.AddDays(-1);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE bookings SET scheduled_at = {pastStart}, scheduled_ends_at = {pastStart.AddMinutes(35)} WHERE id = {bookingId}");
        }

        return (customer, owner, admin, bookingId);
    }

    private async Task<Guid> InitiateAndConfirmAsync(HttpClient customer, HttpClient owner)
    {
        var shop = await owner.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customer.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{shop.Id}/stylists");
        var services = await customer.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{shop.Id}/services");

        var futureDate = DateTime.UtcNow.Date.AddDays(10);
        while (futureDate.DayOfWeek == DayOfWeek.Sunday) futureDate = futureDate.AddDays(1);
        var scheduledAt = new DateTimeOffset(futureDate.Year, futureDate.Month, futureDate.Day, 14, 0, 0, TimeSpan.Zero);

        var initiate = await customer.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateResp>(TestHelpers.Json))!;

        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PostAsync($"/v1/bookings/{initiated.BookingId}/confirm", null)).StatusCode);

        return initiated.BookingId;
    }

    [Fact]
    public async Task FileComplaint_OnPastConfirmedBooking_Succeeds_AndAppearsInAdminQueue()
    {
        var (customer, _, admin, bookingId) = await SeedPastConfirmedBookingAsync();

        var resp = await customer.PostAsJsonAsync($"/v1/bookings/{bookingId}/complaint",
            new { note = "Randevu saatinde salon kapalıydı." });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<FileComplaintResp>(TestHelpers.Json))!;
        Assert.NotEqual(Guid.Empty, body.ComplaintId);

        var queue = await admin.GetJson<ComplaintQueuePage>("/v1/admin/booking-complaints");
        Assert.Contains(queue.Items, i => i.BookingId == bookingId && i.ComplaintId == body.ComplaintId);

        var detail = await customer.GetJson<BookingDto>($"/v1/bookings/{bookingId}");
        Assert.True(detail.HasComplaint);
    }

    [Fact]
    public async Task FileComplaint_OnFutureBooking_Fails()
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

        var bookingId = await InitiateAndConfirmAsync(customer, owner); // still in the future

        var resp = await customer.PostAsJsonAsync($"/v1/bookings/{bookingId}/complaint", new { note = (string?)null });
        Assert.False(resp.IsSuccessStatusCode,
            $"Gelecekteki randevu için şikayet bildirilebilmemeli (got {(int)resp.StatusCode})");
    }

    [Fact]
    public async Task FileComplaint_Twice_IsIdempotent_NoOpOnSecondCall()
    {
        var (customer, owner, admin, bookingId) = await SeedPastConfirmedBookingAsync();

        var first = await customer.PostAsJsonAsync($"/v1/bookings/{bookingId}/complaint", new { note = "İlk bildirim." });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = (await first.Content.ReadFromJsonAsync<FileComplaintResp>(TestHelpers.Json))!;

        var second = await customer.PostAsJsonAsync($"/v1/bookings/{bookingId}/complaint", new { note = "İkinci deneme, farklı metin." });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = (await second.Content.ReadFromJsonAsync<FileComplaintResp>(TestHelpers.Json))!;
        Assert.Equal(firstBody.ComplaintId, secondBody.ComplaintId); // no-op — aynı kayıt

        var queue = await admin.GetJson<ComplaintQueuePage>("/v1/admin/booking-complaints");
        Assert.Single(queue.Items.Where(i => i.BookingId == bookingId));

        var auditLog = await admin.GetJson<AuditLogPage>(
            $"/v1/admin/audit-logs?actionType=booking.complaint.filed&resourceId={firstBody.ComplaintId}&pageSize=10");
        Assert.Equal(1, auditLog.TotalCount); // audit ikinci kez YAZILMADI

        var feed = await owner.GetJson<NotificationFeed>("/v1/notifications");
        var matches = feed.Items.Count(i => i.TemplateKey == "booking.complaint.filed.barber" && i.RelatedBookingId == bookingId);
        Assert.Equal(1, matches); // owner bildirimi de tekrarlanmadı
    }

    [Fact]
    public async Task FileComplaint_ByNonOwningCustomer_IsForbidden()
    {
        var (_, _, _, bookingId) = await SeedPastConfirmedBookingAsync();

        Guid otherCustomerId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var phoneRes = PhoneE164.TryCreate("+905009999997");
            var otherRes = User.CreateCustomerByPhone(phoneRes.Value, "Başka Müşteri");
            db.Users.Add(otherRes.Value);
            await db.SaveChangesAsync();
            otherCustomerId = otherRes.Value.Id;
        }

        using var authScope = factory.Services.CreateScope();
        var authDb = authScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtService = authScope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var otherUser = await authDb.Set<User>().AsNoTracking().FirstAsync(u => u.Id == otherCustomerId);
        var tokens = jwtService.Issue(otherUser);

        using var otherCustomer = factory.CreateClient();
        otherCustomer.SetBearer(tokens.AccessToken);

        var resp = await otherCustomer.PostAsJsonAsync($"/v1/bookings/{bookingId}/complaint", new { note = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task FileComplaint_SendsNeutralNotificationToOwner_AndDoesNotAutoRefund()
    {
        var (customer, owner, _, bookingId) = await SeedPastConfirmedBookingAsync();

        var beforeDetail = await customer.GetJson<BookingDto>($"/v1/bookings/{bookingId}");
        Assert.Equal("Confirmed", beforeDetail.Status);
        Assert.Null(beforeDetail.RefundPercentage);

        var resp = await customer.PostAsJsonAsync($"/v1/bookings/{bookingId}/complaint", new { note = "Kuaför randevuda değildi." });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Otomatik iade TETİKLENMEDİ — booking hâlâ Confirmed, refund yüzdesi hâlâ null
        // (Cancel() çağrılsaydı Status Cancelled'a geçer ve RefundPercentage set edilirdi).
        var afterDetail = await customer.GetJson<BookingDto>($"/v1/bookings/{bookingId}");
        Assert.Equal("Confirmed", afterDetail.Status);
        Assert.Null(afterDetail.RefundPercentage);

        var feed = await owner.GetJson<NotificationFeed>("/v1/notifications");
        var notif = feed.Items.Single(i => i.TemplateKey == "booking.complaint.filed.barber" && i.RelatedBookingId == bookingId);
        // Nötr dil: suçlayıcı kelimeler YOK ("gelmedi", "kapalıydı", "suç").
        Assert.DoesNotContain("gelmedi", notif.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kapalıydı", notif.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inceliyor", notif.Body, StringComparison.OrdinalIgnoreCase);
    }
}

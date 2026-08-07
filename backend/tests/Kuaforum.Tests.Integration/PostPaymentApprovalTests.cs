using System.Net;
using System.Net.Http.Json;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Payment;
using Kuaforum.Infrastructure.BackgroundJobs;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-013 C4 — "önce kapora, sonra onay": ödeme capture'ı artık booking'i Confirm ETMİYOR,
/// yalnızca DepositPaidAt/PaymentId işaretliyor; her randevu (kapora'lı/kaporasız,
/// Deposit/FullOnline fark etmeksizin) Pending → Confirmed geçişini yalnız owner'ın
/// OwnerConfirm() çağrısıyla yapıyor. Reddet/timeout'ta kapora ödenmişse otomatik %100 iade
/// (mevcut BookingCancelledEvent → BookingRefundService → ProcessBookingRefundJob zinciri).
/// Komisyon artık ödeme anında SIFIR (RFC-014 §2 D1 — gelir modeli sabit aylık aboneliğe geçti, SubscriptionInvoice).
///
/// DepositPaymentTests/PaytrWebhookTests bu davranışın "ödeme mekaniği" tarafını (checkout,
/// webhook, reinitiate) kilitliyor; bu dosya "onay sırası" tarafını — RFC-013'ün asıl riskli
/// parçasını — kilitliyor.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PostPaymentApprovalTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record CreatedResp(Guid Id);
    private sealed record InitiateBookingResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    private sealed record InitiatePaymentResp(
        Guid PaymentId, string CheckoutUrl, string? ProviderToken, DateTimeOffset ExpiresAt,
        long AmountMinor, long RemainingAtShopMinor, string Kind);
    private sealed record BookingStatusOnly(string Status);
    private sealed record CancelResp(int RefundPercentage);
    private sealed record NotificationItemDto(Guid Id, string TemplateKey, string Subject, string Body, bool IsRead, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt, Guid? RelatedBookingId);
    private sealed record NotificationFeedDto(int UnreadCount, IReadOnlyList<NotificationItemDto> Items);

    private const long KnownTotalMinor = 200_00;

    // ── (1) Kapora ödendi → Pending kalır → OwnerConfirm → Confirmed + bildirim ─────────────

    [Fact]
    public async Task DepositPaid_KeepsBookingPending_ThenOwnerConfirm_MovesToConfirmed_AndNotifiesCustomer()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        await EnableDepositAsync(ownerClient, depositPercent: 0.15m);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);
        var pay = await InitiateAndCompleteDepositAsync(customerClient, bookingId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            Assert.Equal(BookingStatus.Pending, booking.Status);
            Assert.NotNull(booking.DepositPaidAt);
            Assert.Equal(pay.PaymentId, booking.PaymentId);
        }

        // owner "ödenmiş talep onayınızı bekliyor" bildirimi aldı.
        var ownerFeed = await ownerClient.GetJson<NotificationFeedDto>("/v1/notifications");
        Assert.Contains(ownerFeed.Items, i => i.TemplateKey == "booking.payment_received.barber" && i.RelatedBookingId == bookingId);

        var confirm = await ownerClient.PostAsync($"/v1/bookings/{bookingId}/confirm", null);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var detail = await customerClient.GetJson<BookingStatusOnly>($"/v1/bookings/{bookingId}");
        Assert.Equal("Confirmed", detail.Status);

        var customerFeed = await customerClient.GetJson<NotificationFeedDto>("/v1/notifications");
        Assert.Contains(customerFeed.Items, i => i.TemplateKey == "booking.confirmed.customer" && i.RelatedBookingId == bookingId);
    }

    // ── (2) Ödendi → owner reddeder → %100 otomatik iade (tutar = kapora) ───────────────────

    [Fact]
    public async Task DepositPaid_OwnerRejects_TriggersAutomaticFullRefundOfDepositAmount()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        await EnableDepositAsync(ownerClient, depositPercent: 0.15m);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);
        var pay = await InitiateAndCompleteDepositAsync(customerClient, bookingId);

        // Owner reddeder — POST .../cancel çağrılan actor owner olduğu için server-side reason
        // BarberRequested türetilir (CancelBookingCommand.ResolveReasonAsync), %100 iade.
        var reject = await ownerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        var rejectResp = (await reject.Content.ReadFromJsonAsync<CancelResp>(TestHelpers.Json))!;
        Assert.Equal(100, rejectResp.RefundPercentage);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            Assert.Equal(BookingCancellationReason.BarberRequested, booking.CancellationReason);
        }

        // Hangfire testte kapalı — refund job'ı elle çalıştır (AutoRefundFlowTests kalıbı).
        await RunRefundJobAsync(bookingId);

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db2.Set<Payment>().Include(p => p.Refunds).AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
        var refund = Assert.Single(payment.Refunds);
        Assert.Equal(pay.AmountMinor, refund.AmountMinor); // ödenen KAPORA tutarı — booking toplamı değil
        Assert.Equal(RefundStatus.Succeeded, refund.Status);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    // ── (3) Ödendi → ScheduledAt geçti (job dal b) → iptal + %100 iade ──────────────────────

    [Fact]
    public async Task DepositPaid_ScheduledAtPassesWithoutOwnerConfirm_JobCancelsAndAutoRefunds()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        await EnableDepositAsync(ownerClient, depositPercent: 0.15m);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 60);
        var pay = await InitiateAndCompleteDepositAsync(customerClient, bookingId);

        // Owner hiç onaylamadı — randevu saati geçti.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pastScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-10);
            var pastScheduledEndsAt = pastScheduledAt.AddMinutes(35);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE bookings SET scheduled_at = {pastScheduledAt}, scheduled_ends_at = {pastScheduledEndsAt} WHERE id = {bookingId}");
        }

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ExpirePendingBookingsJob>();
            await job.RunAsync(default);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            Assert.Equal(BookingStatus.Cancelled, booking.Status);
            Assert.Equal(BookingCancellationReason.PendingTimeout, booking.CancellationReason);
            Assert.Equal(100, booking.RefundPercentage);
        }

        await RunRefundJobAsync(bookingId);

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db2.Set<Payment>().Include(p => p.Refunds).AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
        var refund = Assert.Single(payment.Refunds);
        Assert.Equal(pay.AmountMinor, refund.AmountMinor);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    // ── (4) Ödenmemiş 30dk (job dal a) → iptal, refund YOK (para hiç alınmadı) ──────────────

    [Fact]
    public async Task DepositUnpaid_StaleThirtyMinutes_JobCancelsWithoutAnyRefundArtifact()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        await EnableDepositAsync(ownerClient, depositPercent: 0.15m);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 60);

        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        // Bilerek mock-complete ÇAĞRILMIYOR — ödeme hiç tamamlanmadı (terk edilen checkout).

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            Assert.True(booking.RequiresOnlinePayment);
            Assert.Null(booking.DepositPaidAt);

            var staleCreatedAt = DateTimeOffset.UtcNow.AddMinutes(-45);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE bookings SET created_at = {staleCreatedAt} WHERE id = {bookingId}");
        }

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ExpirePendingBookingsJob>();
            await job.RunAsync(default);
        }

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var booking2 = await db2.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
        Assert.Equal(BookingStatus.Cancelled, booking2.Status);
        Assert.Equal(BookingCancellationReason.PendingTimeout, booking2.CancellationReason);
        Assert.Null(booking2.PaymentId); // MarkPaymentReceived hiç çağrılmadı

        // Refund job'ı çalıştırsak bile verecek bir şey yok (para hiç alınmadı).
        await RunRefundJobAsync(bookingId);
        var payment = await db2.Set<Payment>().Include(p => p.Refunds).AsNoTracking().FirstAsync(p => p.BookingId == bookingId);
        Assert.Empty(payment.Refunds);
        Assert.Equal(PaymentStatus.Initiated, payment.Status);
    }

    // ── (5) Kaporasız shop → ödeme adımı yok, OwnerConfirm ile doğrudan Confirmed ───────────

    [Fact]
    public async Task NoDepositShop_HasNoPaymentStep_OwnerConfirmMovesDirectlyToConfirmed()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        // Kapora ayarı hiç açılmıyor — shop varsayılan (DepositEnabled=false).

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            Assert.False(booking.RequiresOnlinePayment);
            Assert.Null(booking.DepositPaidAt);

            var hasPayment = await db.Set<Payment>().AsNoTracking().AnyAsync(p => p.BookingId == bookingId);
            Assert.False(hasPayment, "Kaporasız shop'ta hiç ödeme satırı oluşmamalı.");
        }

        var confirm = await ownerClient.PostAsync($"/v1/bookings/{bookingId}/confirm", null);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var detail = await customerClient.GetJson<BookingStatusOnly>($"/v1/bookings/{bookingId}");
        Assert.Equal("Confirmed", detail.Status);
    }

    // ── (6) FullOnline ödeme → yine onay zorunlu, otomatik Confirmed OLMAZ ──────────────────

    [Fact]
    public async Task FullOnlinePayment_StillRequiresOwnerConfirm_DoesNotAutoConfirm()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        await EnableDepositAsync(ownerClient, depositPercent: 0.15m); // kapora aktif olsa BİLE FullOnline seçilebiliyor

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "FullOnline");
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;
        Assert.Equal(KnownTotalMinor, pay.AmountMinor);

        var completeUrl = $"/v1/payments/mock/complete?token={Uri.EscapeDataString(pay.ProviderToken ?? "MOCK")}";
        var complete = await customerClient.GetAsync(completeUrl);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            Assert.Equal(PaymentStatus.Captured, payment.Status);
            Assert.Equal(BookingStatus.Pending, booking.Status); // RFC-006'nın eski "tam ödeme = anında onay" varsayımı KALKTI
            Assert.NotNull(booking.DepositPaidAt);
        }

        var confirm = await ownerClient.PostAsync($"/v1/bookings/{bookingId}/confirm", null);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var detail = await customerClient.GetJson<BookingStatusOnly>($"/v1/bookings/{bookingId}");
        Assert.Equal("Confirmed", detail.Status);
    }

    // ── (7) Komisyon: payment.PlatformCommissionMinor == 0 (para doğruluğu — kritik) ────────

    [Fact]
    public async Task Payment_PlatformCommissionMinor_IsAlwaysZero_RegardlessOfKind()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        await EnableDepositAsync(ownerClient, depositPercent: 0.15m);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);
        var pay = await InitiateAndCompleteDepositAsync(customerClient, bookingId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
        var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);

        Assert.Equal(0, payment.PlatformCommissionMinor);
        Assert.Equal(payment.GrossAmountMinor, payment.NetToBarberMinor + payment.PspMdrMinor); // komisyon kesilmedi — tamamı kuaföre (PSP MDR hariç)
        // Review fix (2026-08-07): Payment:CommissionRate config'i kaldırıldı — Booking.
        // PlatformCommissionMinor vestigial alanı (M18/M23 faturalama bundan hiç beslenmedi)
        // artık her zaman 0 yazılır (önceden sıfırdan farklıydı, payment'a hiç taşınmasa da
        // "platform geliri" gibi görünen yanıltıcı bir kolon üretiyordu).
        Assert.Equal(0, booking.PlatformCommissionMinor);
    }

    // ── (8) Per-shop yüzde: %25 ayarlı shop'ta kapora = ceil(total×0.25) ────────────────────

    [Fact]
    public async Task PerShopDepositPercent_TwentyFivePercent_ComputesDepositFromShopRate_NotGlobalDefault()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        await EnableDepositAsync(ownerClient, depositPercent: 0.25m);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;

        var expectedDeposit = (long)Math.Ceiling(KnownTotalMinor * 0.25m); // ceil(20000 × 0.25) = 5000
        Assert.Equal(expectedDeposit, pay.AmountMinor);
        Assert.NotEqual((long)Math.Ceiling(KnownTotalMinor * 0.15m), pay.AmountMinor); // global fallback %15 KULLANILMADI
    }

    [Fact]
    public async Task DepositKind_RejectedWhenShopHasNoDepositPolicy()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        // Kapora ayarı hiç açılmıyor.

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");

        Assert.Equal(HttpStatusCode.Conflict, initiate.StatusCode);
        var body = await initiate.Content.ReadAsStringAsync();
        Assert.Contains("kapora ödemesi kabul etmiyor", body);
    }

    // ---- helpers -----------------------------------------------------------

    private async Task<(HttpClient Customer, HttpClient Owner)> LoginClientsAsync()
    {
        await factory.ResetDatabaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
            await seeder.SeedAsync(default);
        }

        var customerClient = factory.CreateClient();
        var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);
        return (customerClient, ownerClient);
    }

    private static async Task EnableDepositAsync(HttpClient owner, decimal depositPercent)
    {
        var resp = await owner.PutAsJsonAsync("/v1/owner/barber-shop/booking-policy", new
        {
            depositEnabled = true,
            depositPercent,
            maxPendingPerCustomer = 2,
            minDaysBetweenBookings = (int?)null,
            maxBookingsPerMonth = (int?)null,
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostInitiatePayment(
        HttpClient client, Guid bookingId, string idempotencyKey, string kind)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/payments/initiate");
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        req.Content = JsonContent.Create(new { bookingId, kind }, options: TestHelpers.Json);
        return await client.SendAsync(req);
    }

    private static async Task<InitiatePaymentResp> InitiateAndCompleteDepositAsync(HttpClient customer, Guid bookingId)
    {
        var initiate = await PostInitiatePayment(customer, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;

        var completeUrl = $"/v1/payments/mock/complete?token={Uri.EscapeDataString(pay.ProviderToken ?? "MOCK")}";
        var complete = await customer.GetAsync(completeUrl);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        return pay;
    }

    private async Task RunRefundJobAsync(Guid bookingId)
    {
        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ProcessBookingRefundJob>();
        await job.RunAsync(bookingId, CancellationToken.None);
    }

    /// <summary>
    /// Owner API'siyle 200 TL'lik test servisi yaratır ve müşteri o servise booking açar —
    /// toplam tutar deterministik olarak 200_00 olur (DepositPaymentTests kalıbı).
    /// </summary>
    private static async Task<Guid> InitiateKnownTotalBookingAsync(HttpClient customer, HttpClient owner, int daysAhead)
    {
        var ownerShop = await owner.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        var createService = await owner.PostAsJsonAsync($"/v1/owner/barber-shop/{ownerShop.Id}/services", new
        {
            name = "C4 Onay Testi Servisi",
            gender = "Any",
            durationMinutes = 30,
            priceMinor = KnownTotalMinor,
            bufferMinutes = 5,
            description = (string?)null,
        }, TestHelpers.Json);
        Assert.Equal(HttpStatusCode.Created, createService.StatusCode);
        var serviceId = (await createService.Content.ReadFromJsonAsync<CreatedResp>(TestHelpers.Json))!.Id;

        var stylists = await customer.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{ownerShop.Id}/stylists");

        var futureDate = DateTime.UtcNow.Date.AddDays(daysAhead);
        while (futureDate.DayOfWeek == DayOfWeek.Sunday) futureDate = futureDate.AddDays(1);
        var futureTime = new DateTimeOffset(futureDate.Year, futureDate.Month, futureDate.Day, 14, 0, 0, TimeSpan.Zero);

        var initiate = await customer.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt = futureTime,
            serviceCatalogIds = new[] { serviceId },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateBookingResp>(TestHelpers.Json))!;
        Assert.Equal(KnownTotalMinor, initiated.TotalAmountMinor);
        return initiated.BookingId;
    }

    private sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
}

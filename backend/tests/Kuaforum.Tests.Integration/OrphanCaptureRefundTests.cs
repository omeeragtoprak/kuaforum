using System.Net;
using System.Net.Http.Json;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Payment;
using Kuaforum.Infrastructure.BackgroundJobs;
using Kuaforum.Infrastructure.Payments;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-013 C1 (adversarial review — "para sızıntısı", KANITLI): capture (webhook or mock-complete)
/// can land AFTER the booking has already left Pending — the owner rejected while the customer
/// was mid-checkout, or ExpirePendingBookingsJob's 30-minute sweep already cancelled it. Reviewer
/// proved with two probes that the OLD PaymentCompletionService produced a captured payment with
/// booking.PaymentId still NULL (webhook path, confirmOnlyIfPending=true silently skipped the
/// attach) or dropped the whole command (mock path, confirmOnlyIfPending=false let
/// MarkPaymentReceived's Status guard fail the transaction) — either way BookingRefundService could
/// never see the money (booking.PaymentId is its only entry point), so it was never refunded and
/// nothing was logged.
///
/// This file locks in the fix: PaymentCompletionService.CaptureAndConfirmAsync now attaches an
/// "orphan capture" via Booking.AttachOrphanPayment + always enqueues the refund job + always
/// records a payment.orphan_capture audit entry + always logs an error, regardless of which path
/// (webhook vs mock-complete) or which terminal booking status (Cancelled vs NoShow) triggered it.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class OrphanCaptureRefundTests(IntegrationTestFactory factory)
{
    private sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    private sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    private sealed record CreatedResp(Guid Id);
    private sealed record InitiateBookingResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    private sealed record InitiatePaymentResp(
        Guid PaymentId, string CheckoutUrl, string? ProviderToken, DateTimeOffset ExpiresAt,
        long AmountMinor, long RemainingAtShopMinor, string Kind);
    private sealed record CancelResp(int RefundPercentage);

    private const long KnownTotalMinor = 200_00;
    private const long ExpectedDepositMinor = 30_00; // ceil(20000 × 0.15)

    // ── Probe A: owner rejects mid-checkout, THEN the webhook capture lands ─────────────────

    /// <summary>
    /// Reviewer probe A: customer is on the PayTR screen (payment Initiated, checkout token
    /// issued) when the owner rejects the booking. Booking → Cancelled/BarberRequested/100%
    /// BEFORE the payment ever captures. When the (delayed) success webhook finally arrives, the
    /// old code captured the payment but left booking.PaymentId null — money taken, no refund
    /// path, no alarm. Fixed behavior: capture succeeds, orphan-attaches to the booking, and an
    /// audit + refund-job enqueue happen; running the job actually refunds the deposit.
    /// </summary>
    [Fact]
    public async Task OwnerRejectsMidCheckout_LateWebhookCapture_AttachesOrphanPaymentAndRefunds()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, daysAhead: 40);
        var pay = await InitiatePaymentAsync(customerClient, bookingId, kind: "Deposit");
        Assert.Equal(ExpectedDepositMinor, pay.AmountMinor);

        // Owner reddeder — payment HENÜZ capture edilmedi (Initiated).
        var reject = await ownerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        var rejectResp = (await reject.Content.ReadFromJsonAsync<CancelResp>(TestHelpers.Json))!;
        Assert.Equal(100, rejectResp.RefundPercentage);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bookingBeforeWebhook = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            Assert.Equal(BookingStatus.Cancelled, bookingBeforeWebhook.Status);
            Assert.Null(bookingBeforeWebhook.PaymentId); // capture henüz gelmedi
        }

        // Webhook GEÇ gelir — PayTR sunucu tarafında ödeme az önce tamamlandı.
        using var webhookClient = factory.CreateClient();
        var resp = await PostWebhookAsync(webhookClient, pay.PaymentId.ToString("N"), "success", ExpectedDepositMinor.ToString());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("OK", await resp.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            Assert.Equal(PaymentStatus.Captured, payment.Status); // para PSP'de tahsil edildi
            Assert.Equal(pay.PaymentId, booking.PaymentId); // ARTIK yetim DEĞİL — booking'e bağlandı
            Assert.Equal(BookingStatus.Cancelled, booking.Status); // Status'e dokunulmadı

            var orphanAudit = await db.Set<AuditLog>().AsNoTracking()
                .Where(a => a.ActionType == "payment.orphan_capture" && a.ResourceId == payment.Id.ToString())
                .ToListAsync();
            Assert.NotEmpty(orphanAudit);
        }

        // Hangfire testte kapalı — refund job'ı elle çalıştır (AutoRefundFlowTests kalıbı).
        await RunRefundJobAsync(bookingId);

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var finalPayment = await db2.Set<Payment>().Include(p => p.Refunds).AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
        var refund = Assert.Single(finalPayment.Refunds);
        Assert.Equal(ExpectedDepositMinor, refund.AmountMinor);
        Assert.Equal(RefundReason.BarberRequested, refund.Reason);
        Assert.Equal(PaymentStatus.Refunded, finalPayment.Status);
    }

    // ── Probe B: 30dk expire iptali, THEN late webhook capture lands ────────────────────────

    /// <summary>
    /// Reviewer probe B: same orphan outcome via the other cancellation path —
    /// ExpirePendingBookingsJob's 30-minute unpaid-Pending sweep cancels the booking
    /// (PendingTimeout/100%) while a checkout session is still technically open, and the customer
    /// completes 3DS just as (or right after) that happens. The delayed webhook must not create a
    /// silent, unrecoverable orphan capture.
    /// </summary>
    [Fact]
    public async Task ExpireJobCancelsPending_LateWebhookCapture_AttachesOrphanPaymentAndRefunds()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, daysAhead: 40);
        var pay = await InitiatePaymentAsync(customerClient, bookingId, kind: "Deposit");

        // CreatedAt'i 30dk+ geriye çekerek job'un unpaid-stale eşiğini deterministik tetikle.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE bookings SET created_at = {DateTimeOffset.UtcNow.AddMinutes(-31)} WHERE id = {bookingId}");
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
            Assert.Null(booking.PaymentId); // capture henüz gelmedi
        }

        // Webhook GEÇ gelir.
        using var webhookClient = factory.CreateClient();
        var resp = await PostWebhookAsync(webhookClient, pay.PaymentId.ToString("N"), "success", ExpectedDepositMinor.ToString());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            Assert.Equal(PaymentStatus.Captured, payment.Status);
            Assert.Equal(pay.PaymentId, booking.PaymentId);
        }

        await RunRefundJobAsync(bookingId);

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var finalPayment = await db2.Set<Payment>().Include(p => p.Refunds).AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
        var refund = Assert.Single(finalPayment.Refunds);
        Assert.Equal(ExpectedDepositMinor, refund.AmountMinor);
        Assert.Equal(RefundReason.AdminOverride, refund.Reason); // BookingRefundService.MapRefundReason: PendingTimeout → AdminOverride
        Assert.Equal(PaymentStatus.Refunded, finalPayment.Status);
    }

    // ── Mock-complete path: same orphan-capture fix must apply there too ────────────────────

    /// <summary>
    /// The mock-complete path (CompletePaymentCommand, dev-only) previously had a WORSE bug than
    /// the webhook path: confirmOnlyIfPending=false meant MarkPaymentReceived's Status guard
    /// FAILED the whole command when the booking wasn't Pending — the real-provider analogy is
    /// "PSP charged the card, but our own capture write never committed" (no Payment.Captured row
    /// at all). Fixed behavior: mock-complete now succeeds (200) exactly like the webhook path.
    /// </summary>
    [Fact]
    public async Task OwnerRejectsMidCheckout_LateMockComplete_SucceedsAndAttachesOrphanPayment()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, daysAhead: 40);
        var pay = await InitiatePaymentAsync(customerClient, bookingId, kind: "Deposit");

        var reject = await ownerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        // Müşteri az önce reddedildiğini bilmeden mock-complete'i tetikler (PayTR sürecinin son adımı).
        var completeUrl = $"/v1/payments/mock/complete?token={Uri.EscapeDataString(pay.ProviderToken ?? "MOCK")}";
        var complete = await customerClient.GetAsync(completeUrl);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode); // ESKİ davranış: komut düşerdi (500/400)

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            Assert.Equal(PaymentStatus.Captured, payment.Status);
            Assert.Equal(pay.PaymentId, booking.PaymentId);

            var orphanAudit = await db.Set<AuditLog>().AsNoTracking()
                .Where(a => a.ActionType == "payment.orphan_capture" && a.ResourceId == payment.Id.ToString())
                .ToListAsync();
            Assert.NotEmpty(orphanAudit);
        }

        await RunRefundJobAsync(bookingId);

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var finalPayment = await db2.Set<Payment>().Include(p => p.Refunds).AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
        var refund = Assert.Single(finalPayment.Refunds);
        Assert.Equal(ExpectedDepositMinor, refund.AmountMinor);
        Assert.Equal(PaymentStatus.Refunded, finalPayment.Status);
    }

    // ── Idempotent webhook retry after orphan attach must not double-log/double-enqueue harm ──

    /// <summary>
    /// A PayTR retry of the SAME successful webhook after the orphan attach already happened must
    /// stay idempotent: capture is already terminal (Captured), so ProcessPaytrWebhookCommand's
    /// own idempotent-status short-circuit (Status is Captured/Settled/...) returns success without
    /// touching PaymentCompletionService again — this test proves that early-return path still
    /// applies cleanly on top of an orphan-attached booking.
    /// </summary>
    [Fact]
    public async Task DuplicateWebhookAfterOrphanAttach_StaysIdempotent()
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, daysAhead: 40);
        var pay = await InitiatePaymentAsync(customerClient, bookingId, kind: "Deposit");
        await ownerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);

        using var webhookClient = factory.CreateClient();
        var first = await PostWebhookAsync(webhookClient, pay.PaymentId.ToString("N"), "success", ExpectedDepositMinor.ToString());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await PostWebhookAsync(webhookClient, pay.PaymentId.ToString("N"), "success", ExpectedDepositMinor.ToString());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
        var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(pay.PaymentId, booking.PaymentId);

        var orphanAuditCount = await db.Set<AuditLog>().AsNoTracking()
            .CountAsync(a => a.ActionType == "payment.orphan_capture" && a.ResourceId == payment.Id.ToString());
        Assert.Equal(1, orphanAuditCount); // ikinci webhook ProcessPaytrWebhookCommand'ın kendi Status guard'ında duruyor
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

        var policyResp = await ownerClient.PutAsJsonAsync("/v1/owner/barber-shop/booking-policy", new
        {
            depositEnabled = true,
            depositPercent = 0.15m,
            maxPendingPerCustomer = 2,
            minDaysBetweenBookings = (int?)null,
            maxBookingsPerMonth = (int?)null,
        });
        Assert.Equal(HttpStatusCode.OK, policyResp.StatusCode);

        return (customerClient, ownerClient);
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, string merchantOid, string status, string totalAmount)
    {
        var cfg = factory.Services.GetRequiredService<IConfiguration>();
        var hash = PaytrWebhookSignature.Compute(
            merchantOid, cfg["PayTr:MerchantSalt"] ?? "", status, totalAmount, cfg["PayTr:MerchantKey"] ?? "");
        return await client.PostAsync("/v1/webhooks/paytr", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["merchant_oid"] = merchantOid,
            ["status"] = status,
            ["total_amount"] = totalAmount,
            ["hash"] = hash,
        }));
    }

    private async Task RunRefundJobAsync(Guid bookingId)
    {
        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ProcessBookingRefundJob>();
        await job.RunAsync(bookingId, CancellationToken.None);
    }

    private static async Task<InitiatePaymentResp> InitiatePaymentAsync(HttpClient client, Guid bookingId, string kind)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/payments/initiate");
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        req.Content = JsonContent.Create(new { bookingId, kind }, options: TestHelpers.Json);
        var resp = await client.SendAsync(req);
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Fail($"Payment initiate failed: {(int)resp.StatusCode} body={body}");
        }
        return (await resp.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;
    }

    /// <summary>
    /// Owner API'siyle 200 TL'lik test servisi yaratır ve müşteri o servise booking açar —
    /// toplam tutar deterministik 200_00 (DepositPaymentTests/PaytrWebhookTests kalıbı).
    /// </summary>
    private static async Task<Guid> InitiateKnownTotalBookingAsync(HttpClient customer, HttpClient owner, int daysAhead)
    {
        var ownerShop = await owner.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        var createService = await owner.PostAsJsonAsync($"/v1/owner/barber-shop/{ownerShop.Id}/services", new
        {
            name = "Orphan Capture Testi Servisi",
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
}

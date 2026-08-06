using System.Net;
using System.Net.Http.Json;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Payment;
using Kuaforum.Domain.Modules.Payment;
using Kuaforum.Infrastructure.BackgroundJobs;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// Covers the cancel → real refund automation (Cycle 9 step 2): previously a cancelled booking
/// only sent a "%X iade işleme alındı" notification — nothing ever touched the Payment
/// aggregate. Booking.Cancel() → BookingCancelledEvent → BookingCancelledRefundHandler →
/// IBookingRefundJobEnqueuer.EnqueueRefund → (Hangfire, disabled in tests) →
/// ProcessBookingRefundJob.RunAsync → BookingRefundService.
///
/// Hangfire is disabled in this test host (Hangfire:Enabled=false — see
/// IntegrationTestFactory), so EnqueueRefund is a logging no-op (NullBookingRefundJobEnqueuer)
/// and the job never runs automatically. That's intentional and is the documented, legitimate
/// way to test this path: instantiate ProcessBookingRefundJob directly and call RunAsync,
/// exactly like the pattern already used for the other Hangfire jobs (ExpireWalkInJob etc. are
/// only exercised by directly calling RunAsync in tests too).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class AutoRefundFlowTests(IntegrationTestFactory factory)
{
    public sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    public sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    public sealed record ServiceRow(Guid Id, string Name, int DurationMinutes, int BufferMinutes, long PriceMinor);
    public sealed record InitiateBookingResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    public sealed record InitiatePaymentResp(Guid PaymentId, string CheckoutUrl, string? ProviderToken, DateTimeOffset ExpiresAt);
    public sealed record CancelResp(int RefundPercentage);

    /// <summary>
    /// >=24h customer cancel on a fully-paid booking → running the job produces a 100% Refund
    /// row and flips the payment to Refunded. This is the core "money actually moves" case the
    /// notification-only flow was missing.
    /// </summary>
    [Fact]
    public async Task Customer_cancel_more_than_24h_before_auto_refunds_full_amount_via_job()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var (bookingId, paymentId) = await InitiateAndPayAsync(customerClient, ownerClient, 60);

        var cancel = await customerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var cancelResp = (await cancel.Content.ReadFromJsonAsync<CancelResp>(TestHelpers.Json))!;
        Assert.Equal(100, cancelResp.RefundPercentage);

        await RunRefundJobAsync(bookingId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<Payment>().Include(p => p.Refunds).FirstAsync(p => p.Id == paymentId);
        var refund = Assert.Single(payment.Refunds);
        Assert.Equal(payment.GrossAmountMinor, refund.AmountMinor);
        Assert.Equal(RefundReason.CustomerRequested, refund.Reason);
        Assert.Equal(RefundStatus.Succeeded, refund.Status);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    /// <summary>
    /// Admin override cancel (AdminCancelBookingCommand) raises the same BookingCancelledEvent
    /// as the customer/owner path — one handler covers all three cancel entry points. Confirms
    /// the admin path also produces a real refund, not just the 100%-RefundPercentage booking
    /// field.
    /// </summary>
    [Fact]
    public async Task Admin_cancel_auto_refunds_full_amount_via_job()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var emailRes = Kuaforum.Domain.Modules.Identity.ValueObjects.EmailAddress.TryCreate("auto-refund-admin@kuaforum.test");
            var adminRes = Kuaforum.Domain.Modules.Identity.User.CreateAdmin(emailRes.Value, "Auto Refund Admin");
            db.Users.Add(adminRes.Value);
            await db.SaveChangesAsync(default);
        }

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        using var adminClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);
        adminClient.SetBearer((await adminClient.DevLoginAsync("Admin")).AccessToken);

        var (bookingId, paymentId) = await InitiateAndPayAsync(customerClient, ownerClient, 45);

        var cancel = await adminClient.PostAsJsonAsync(
            $"/v1/admin/bookings/{bookingId}/cancel",
            new { reason = "Test — admin override tetikli otomatik iade." });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        await RunRefundJobAsync(bookingId);

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db2.Set<Payment>().Include(p => p.Refunds).FirstAsync(p => p.Id == paymentId);
        var refund = Assert.Single(payment.Refunds);
        Assert.Equal(payment.GrossAmountMinor, refund.AmountMinor);
        Assert.Equal(RefundReason.AdminOverride, refund.Reason);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    /// <summary>
    /// Booking cancelled before payment was ever completed (payment stays Initiated,
    /// booking.PaymentId is never set — see Booking.Confirm, only called from
    /// CompletePaymentCommand). Running the job must be a silent no-op: no Refund row, no
    /// exception. There is nothing to give back.
    /// </summary>
    [Fact]
    public async Task Cancel_of_never_completed_payment_produces_no_refund_and_no_error()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateBookingAsync(customerClient, ownerClient, 60);

        // Initiate payment but never complete it — payment stays Initiated, booking stays
        // Pending (booking.PaymentId is only set by CompletePaymentCommand → Booking.Confirm).
        var idempKey = Guid.NewGuid().ToString();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/payments/initiate");
        req.Headers.Add("Idempotency-Key", idempKey);
        req.Content = JsonContent.Create(new { bookingId }, options: TestHelpers.Json);
        var initiate = await customerClient.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;

        var cancel = await customerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var cancelResp = (await cancel.Content.ReadFromJsonAsync<CancelResp>(TestHelpers.Json))!;
        Assert.Equal(100, cancelResp.RefundPercentage); // >=24h → domain still computes 100%, just nothing was ever paid

        // must not throw
        await RunRefundJobAsync(bookingId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<Payment>().Include(p => p.Refunds).FirstAsync(p => p.Id == pay.PaymentId);
        Assert.Empty(payment.Refunds);
        Assert.Equal(PaymentStatus.Initiated, payment.Status);
    }

    /// <summary>
    /// &lt;12h customer cancel of a CONFIRMED booking → RefundPercentage 0 → no refund owed. The
    /// job must not create a Refund row (and must not even call the PSP).
    ///
    /// RFC-013 C2 (adversarial review): the hour-based scale only applies once the owner has
    /// confirmed (Booking.ConfirmedAt set) — an unconfirmed booking's customer-cancel is always
    /// 100% regardless of timing (see BookingTests.Cancel_CustomerRequested_NeverConfirmed_*).
    /// This test's payment-completion helper (InitiateAndPayAsync) deliberately does NOT
    /// auto-confirm (RFC-013 C4 — capture no longer confirms), so an explicit OwnerConfirm call
    /// is required here to exercise the tier this test is actually about.
    /// </summary>
    [Fact]
    public async Task Cancel_less_than_12h_before_produces_no_refund()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var (bookingId, paymentId) = await InitiateAndPayAsync(customerClient, ownerClient, 60);

        var confirm = await ownerClient.PostAsync($"/v1/bookings/{bookingId}/confirm", null);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        // Force <12h out deterministically (avoids wall-clock flakiness).
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var newScheduledAt = DateTimeOffset.UtcNow.AddHours(6);
            var newEndsAt = newScheduledAt.AddMinutes(35);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE bookings SET scheduled_at = {newScheduledAt}, scheduled_ends_at = {newEndsAt} WHERE id = {bookingId}");
        }

        var cancel = await customerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var cancelResp = (await cancel.Content.ReadFromJsonAsync<CancelResp>(TestHelpers.Json))!;
        Assert.Equal(0, cancelResp.RefundPercentage);

        await RunRefundJobAsync(bookingId);

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db2.Set<Payment>().Include(p => p.Refunds).FirstAsync(p => p.Id == paymentId);
        Assert.Empty(payment.Refunds);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
    }

    /// <summary>
    /// Idempotency: running the job twice for the same fully-refunded booking must not create
    /// a second Refund row or double the refunded amount. This is what makes it safe for
    /// Hangfire to retry (or for the job to be enqueued twice) without a duplicate payout.
    /// </summary>
    [Fact]
    public async Task Refund_job_run_twice_does_not_double_refund()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var (bookingId, paymentId) = await InitiateAndPayAsync(customerClient, ownerClient, 60);

        var cancel = await customerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        await RunRefundJobAsync(bookingId);
        await RunRefundJobAsync(bookingId); // simulates a Hangfire retry / duplicate enqueue

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<Payment>().Include(p => p.Refunds).FirstAsync(p => p.Id == paymentId);
        var refund = Assert.Single(payment.Refunds);
        Assert.Equal(payment.GrossAmountMinor, refund.AmountMinor);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    /// <summary>
    /// PSP failure resilience: the cancel HTTP call must succeed regardless of PSP health (see
    /// the other tests — cancel always returns 200 before the job ever runs, since the job is
    /// enqueued post-commit and never runs inline). This test covers the second half: when the
    /// job itself hits a failing PSP, it must throw (so Hangfire's [AutomaticRetry(Attempts=5)]
    /// on ProcessBookingRefundJob.RunAsync retries it) rather than silently swallowing the
    /// failure, and it must leave the payment untouched (no partial Refund row) so a retry
    /// starts from a clean idempotent state.
    ///
    /// Exercised at the service/job level with a hand-rolled failing IPaymentProvider — there's
    /// no DI seam in this test host to swap the registered PSP mid-test (MockPaymentProvider is
    /// wired at startup), and this project has no established pattern for mocking
    /// IApplicationDbContext (handlers cast it to a real DbContext), so a real Testcontainers-
    /// backed DbContext + a fake PSP is the pragmatic option here.
    /// </summary>
    [Fact]
    public async Task Refund_job_throws_on_psp_failure_and_leaves_payment_untouched()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var (bookingId, paymentId) = await InitiateAndPayAsync(customerClient, ownerClient, 60);

        var cancel = await customerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode); // cancel succeeds regardless of PSP health

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ProcessBookingRefundJob>>();

        var failingRegistry = new SingleProviderRegistry(new FailingPaymentProvider());
        var refundServiceWithFailingPsp = new BookingRefundService(db, failingRegistry, audit);
        var job = new ProcessBookingRefundJob(refundServiceWithFailingPsp, db, logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.RunAsync(bookingId, CancellationToken.None));

        var payment = await db.Set<Payment>().Include(p => p.Refunds).AsNoTracking().FirstAsync(p => p.Id == paymentId);
        Assert.Empty(payment.Refunds);
        Assert.Equal(PaymentStatus.Captured, payment.Status); // untouched — safe to retry
    }

    private sealed class FailingPaymentProvider : IPaymentProvider
    {
        public PaymentProviderName ProviderName => PaymentProviderName.Mock;

        public Task<CheckoutSession> InitiateAsync(CheckoutRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException("Test double — not used by the refund path.");

        public Task<PaymentVerification> VerifyAsync(string providerToken, CancellationToken ct = default) =>
            throw new NotSupportedException("Test double — not used by the refund path.");

        public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct = default) =>
            Task.FromResult(new RefundResult(false, null, "PSP geçici olarak kullanılamıyor (test)."));
    }

    private sealed class SingleProviderRegistry(IPaymentProvider provider) : IPaymentProviderRegistry
    {
        public IPaymentProvider GetProvider(PaymentProviderName name) => provider;
        public PaymentProviderName DefaultProvider => provider.ProviderName;
    }

    private async Task SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
        await seeder.SeedAsync(default);
    }

    /// <summary>
    /// Hangfire is disabled in this test host, so EnqueueRefund never actually schedules
    /// ProcessBookingRefundJob — see the class doc. This resolves the real job + real
    /// registered services from DI (MockPaymentProvider, which auto-succeeds) and runs it
    /// directly, standing in for "the Hangfire worker picked up the enqueued job".
    /// </summary>
    private async Task RunRefundJobAsync(Guid bookingId)
    {
        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ProcessBookingRefundJob>();
        await job.RunAsync(bookingId, CancellationToken.None);
    }

    private static async Task<(Guid BookingId, Guid PaymentId)> InitiateAndPayAsync(HttpClient customer, HttpClient owner, int daysAhead)
    {
        var bookingId = await InitiateBookingAsync(customer, owner, daysAhead);

        var idempKey = Guid.NewGuid().ToString();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/payments/initiate");
        req.Headers.Add("Idempotency-Key", idempKey);
        req.Content = JsonContent.Create(new { bookingId }, options: TestHelpers.Json);
        var initiate = await customer.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;

        var completeUrl = $"/v1/payments/mock/complete?token={Uri.EscapeDataString(pay.ProviderToken ?? "MOCK")}";
        var complete = await customer.GetAsync(completeUrl);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        return (bookingId, pay.PaymentId);
    }

    private static async Task<Guid> InitiateBookingAsync(HttpClient customer, HttpClient owner, int daysAhead)
    {
        var ownerShop = await owner.GetJson<BarberShopRow>("/v1/owner/barber-shop");
        var stylists = await customer.GetJson<IReadOnlyList<StylistRow>>($"/v1/barber-shops/{ownerShop.Id}/stylists");
        var services = await customer.GetJson<IReadOnlyList<ServiceRow>>($"/v1/barber-shops/{ownerShop.Id}/services");

        var futureDate = DateTime.UtcNow.Date.AddDays(daysAhead);
        while (futureDate.DayOfWeek == DayOfWeek.Sunday) futureDate = futureDate.AddDays(1);
        var futureTime = new DateTimeOffset(futureDate.Year, futureDate.Month, futureDate.Day, 14, 0, 0, TimeSpan.Zero);

        var initiate = await customer.PostAsJsonAsync("/v1/bookings", new
        {
            stylistId = stylists.First().Id,
            scheduledAt = futureTime,
            serviceCatalogIds = new[] { services.First().Id },
            isWalkIn = false,
            customerNote = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var initiated = (await initiate.Content.ReadFromJsonAsync<InitiateBookingResp>(TestHelpers.Json))!;
        return initiated.BookingId;
    }
}

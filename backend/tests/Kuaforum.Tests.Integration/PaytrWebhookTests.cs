using System.Net;
using System.Net.Http.Json;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Payment;
using Kuaforum.Infrastructure.Payments;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

/// <summary>
/// RFC-006 Task 4: POST /v1/webhooks/paytr — PayTR sunucudan sunucuya bildirim endpoint'i.
/// Güvenlik HMAC imzasıyla (auth yok); işleme idempotent; PayTR "OK" gövdesini görmezse
/// bildirimi tekrarladığı için bilinmeyen merchant_oid dahil 200 "OK" dönülür.
///
/// Tutar doğrulaması ReinitiateCheckout stale-session koruması: müşteri Deposit→FullOnline
/// switch yaptığında payment satırının Gross'u değişir; ESKİ checkout session'ının callback'i
/// eski tutarla gelirse capture edilmez (audit: payment.webhook_amount_mismatch).
///
/// Hash, implementasyondaki PaytrWebhookSignature.Compute ile üretilir (tek kaynak).
/// Beklenenler DepositPaymentTests ile aynı: 200 TL booking → deposit ceil(20000×0.15)=3000.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PaytrWebhookTests(IntegrationTestFactory factory)
{
    public sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    public sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    public sealed record CreatedResp(Guid Id);
    public sealed record InitiateBookingResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    public sealed record InitiatePaymentResp(
        Guid PaymentId, string CheckoutUrl, string? ProviderToken, DateTimeOffset ExpiresAt,
        long AmountMinor, long RemainingAtShopMinor, string Kind);

    private const long KnownTotalMinor = 200_00;
    private const long ExpectedDepositMinor = 30_00; // ceil(20000 × 0.15)

    /// <summary>
    /// Known-answer (golden value) testi — external contract pin'i. PayTR bildirim hash'i
    /// base64(HMAC-SHA256(merchant_oid + merchant_salt + status + total_amount, key)) olmalı;
    /// şema resmi C# örneğiyle doğrulandı (https://dev.paytr.com/en/iframe-api/iframe-api-2-adim,
    /// 2026-07-30) ve bu sabit çıktı o formülü kilitler. Compute içindeki concat sırası/algoritma
    /// bir refactor'de sessizce değişirse diğer testler (aynı fonksiyonla hash ürettikleri için)
    /// yeşil kalır ama BU test kırılır — PayTR ile uyumsuzluk canlıda değil burada yakalanır.
    /// </summary>
    [Fact]
    public void Compute_matches_known_answer_from_official_formula()
    {
        var hash = PaytrWebhookSignature.Compute(
            merchantOid: "abc123", merchantSalt: "SALT", status: "success", totalAmount: "3000", merchantKey: "KEY");
        Assert.Equal("bw6tbwNxb+eLO7XdNvE5Hs3g7qBsz0+fEOdwNfBw65c=", hash);
    }

    /// <summary>
    /// Fail-closed guard: MerchantKey yapılandırılmamışsa (boş) endpoint imza doğrulamasına hiç
    /// girmeden 503 döner. Boş key ile HMAC herkesçe hesaplanabilir olurdu — saldırgan kendi
    /// paymentId'siyle ödemesiz booking confirm forge edebilirdi.
    /// </summary>
    [Fact]
    public async Task Missing_merchant_key_returns_503_and_payment_untouched()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();
        var (_, paymentId) = await CreateDepositInitiatedBookingAsync(daysAhead: 46);

        // Aynı Testcontainers altyapısını paylaşan, PayTr:MerchantKey'i boş override eden host.
        await using var unconfiguredFactory = factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PayTr:MerchantKey"] = "",
            })));
        using var client = unconfiguredFactory.CreateClient();

        // Saldırı senaryosu: boş key/salt ile "geçerli" görünen hash üretilebilirdi.
        var oid = paymentId.ToString("N");
        var amount = ExpectedDepositMinor.ToString();
        var forgedHash = PaytrWebhookSignature.Compute(oid, "", "success", amount, "");
        var resp = await client.PostAsync("/v1/webhooks/paytr", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["merchant_oid"] = oid,
            ["status"] = "success",
            ["total_amount"] = amount,
            ["hash"] = forgedHash,
        }));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == paymentId);
        Assert.Equal(PaymentStatus.Initiated, payment.Status); // forge işe yaramadı
    }

    /// <summary>
    /// RFC-013 C4: webhook capture artık booking'i Confirm ETMİYOR — yalnızca "ödeme alındı,
    /// onay bekliyor" işaretliyor (PaymentId + DepositPaidAt), Status Pending kalıyor. Onay her
    /// zaman ayrı, owner-tetikli bir adım (OwnerConfirmBookingCommand).
    /// </summary>
    [Fact]
    public async Task Success_webhook_captures_deposit_and_marks_payment_received_without_confirming()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();
        var (bookingId, paymentId) = await CreateDepositInitiatedBookingAsync(daysAhead: 40);

        using var client = factory.CreateClient();
        var oid = paymentId.ToString("N");
        var resp = await PostWebhookAsync(client, oid, "success", ExpectedDepositMinor.ToString());

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("OK", await resp.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == paymentId);
        var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.StartsWith("paytr-", payment.ProviderTransactionId);
        Assert.NotNull(payment.CapturedAt);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(paymentId, booking.PaymentId);
        Assert.NotNull(booking.DepositPaidAt);
    }

    [Fact]
    public async Task Duplicate_webhook_is_idempotent_and_captured_at_unchanged()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();
        var (_, paymentId) = await CreateDepositInitiatedBookingAsync(daysAhead: 41);

        using var client = factory.CreateClient();
        var oid = paymentId.ToString("N");

        var first = await PostWebhookAsync(client, oid, "success", ExpectedDepositMinor.ToString());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        DateTimeOffset? capturedAtAfterFirst;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            capturedAtAfterFirst = (await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == paymentId)).CapturedAt;
        }
        Assert.NotNull(capturedAtAfterFirst);

        // PayTR aynı bildirimi tekrarlayabilir — ikinci çağrı da OK, ama İKİNCİ capture yok.
        var second = await PostWebhookAsync(client, oid, "success", ExpectedDepositMinor.ToString());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("OK", await second.Content.ReadAsStringAsync());

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db2.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == paymentId);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(capturedAtAfterFirst, payment.CapturedAt); // değişmemiş — tek capture
    }

    [Fact]
    public async Task Invalid_hash_returns_400_and_payment_untouched()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();
        var (_, paymentId) = await CreateDepositInitiatedBookingAsync(daysAhead: 42);

        using var client = factory.CreateClient();
        var resp = await client.PostAsync("/v1/webhooks/paytr", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["merchant_oid"] = paymentId.ToString("N"),
            ["status"] = "success",
            ["total_amount"] = ExpectedDepositMinor.ToString(),
            ["hash"] = "kesinlikle-gecersiz-imza",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == paymentId);
        Assert.Equal(PaymentStatus.Initiated, payment.Status); // dokunulmamış
    }

    [Fact]
    public async Task Unknown_merchant_oid_returns_200_ok_and_changes_nothing()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();
        var (_, paymentId) = await CreateDepositInitiatedBookingAsync(daysAhead: 43);

        using var client = factory.CreateClient();
        var unknownOid = Guid.NewGuid().ToString("N");
        var resp = await PostWebhookAsync(client, unknownOid, "success", ExpectedDepositMinor.ToString());

        // PayTR "OK" görmezse bildirimi tekrar eder — bilinmeyen oid için retry fırtınası yaratma.
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("OK", await resp.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == paymentId);
        Assert.Equal(PaymentStatus.Initiated, payment.Status);
        Assert.Equal(0, await db.Set<Payment>().CountAsync(p => p.Status == PaymentStatus.Captured));
    }

    [Fact]
    public async Task Failed_webhook_marks_payment_failed_and_booking_stays_pending()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();
        var (bookingId, paymentId) = await CreateDepositInitiatedBookingAsync(daysAhead: 44);

        using var client = factory.CreateClient();
        var oid = paymentId.ToString("N");
        var resp = await PostWebhookAsync(client, oid, "failed", ExpectedDepositMinor.ToString());

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("OK", await resp.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == paymentId);
        var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.False(string.IsNullOrWhiteSpace(payment.FailureReason));
        Assert.Equal(BookingStatus.Pending, booking.Status);
    }

    /// <summary>
    /// ReinitiateCheckout stale-session koruması: Deposit (3000) initiate edilir, müşteri
    /// FullOnline'a geçer (aynı payment satırı, Gross=20000). ESKİ checkout session'ının
    /// callback'i eski tutarla (3000) doğru hash'le gelir → OK dönülür ama capture edilmez;
    /// audit'e payment.webhook_amount_mismatch yazılır.
    /// </summary>
    [Fact]
    public async Task Amount_mismatch_after_reinitiate_returns_ok_but_does_not_capture()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, daysAhead: 45);
        var depositPay = await InitiatePaymentAsync(customerClient, bookingId, kind: "Deposit");
        Assert.Equal(ExpectedDepositMinor, depositPay.AmountMinor);

        // Müşteri türü değiştirdi: aynı payment satırı FullOnline'a reinitiate edilir (Gross=20000).
        var fullPay = await InitiatePaymentAsync(customerClient, bookingId, kind: "FullOnline");
        Assert.Equal(depositPay.PaymentId, fullPay.PaymentId);
        Assert.Equal(KnownTotalMinor, fullPay.AmountMinor);

        using var client = factory.CreateClient();
        var oid = depositPay.PaymentId.ToString("N");
        var resp = await PostWebhookAsync(client, oid, "success", ExpectedDepositMinor.ToString()); // STALE tutar

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("OK", await resp.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == depositPay.PaymentId);
        Assert.Equal(PaymentStatus.Initiated, payment.Status); // capture YOK
        Assert.Null(payment.CapturedAt);

        var mismatchAudit = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.ActionType == "payment.webhook_amount_mismatch" && a.ResourceId == payment.Id.ToString())
            .ToListAsync();
        Assert.NotEmpty(mismatchAudit);
    }

    // ---- helpers -----------------------------------------------------------

    private async Task SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
        await seeder.SeedAsync(default);
    }

    /// <summary>Webhook form POST — hash implementasyondaki PaytrWebhookSignature.Compute ile (tek kaynak).</summary>
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

    private async Task<(HttpClient Customer, HttpClient Owner)> LoginClientsAsync()
    {
        var customerClient = factory.CreateClient();
        var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        // RFC-013 C4: Kind=Deposit artık shop.BookingPolicy.DepositEnabled/DepositPercent şart
        // koşuyor (per-shop oran InitiatePaymentCommand'ı yönetiyor, global PaymentOptions.
        // DepositRate fallback'i artık kullanılmıyor) — bu dosyanın tüm testleri Deposit kind
        // kullandığından politika burada, tek yerde açılıyor (%15, ExpectedDepositMinor sabitiyle hizalı).
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

    private async Task<(Guid BookingId, Guid PaymentId)> CreateDepositInitiatedBookingAsync(int daysAhead)
    {
        var (customerClient, ownerClient) = await LoginClientsAsync();
        using var _c = customerClient;
        using var _o = ownerClient;
        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, daysAhead);
        var pay = await InitiatePaymentAsync(customerClient, bookingId, kind: "Deposit");
        Assert.Equal(ExpectedDepositMinor, pay.AmountMinor);
        return (bookingId, pay.PaymentId);
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
    /// toplam tutar deterministik 200_00 (DepositPaymentTests kalıbı).
    /// </summary>
    private static async Task<Guid> InitiateKnownTotalBookingAsync(HttpClient customer, HttpClient owner, int daysAhead)
    {
        var ownerShop = await owner.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        var createService = await owner.PostAsJsonAsync($"/v1/owner/barber-shop/{ownerShop.Id}/services", new
        {
            name = "Webhook Test Servisi",
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

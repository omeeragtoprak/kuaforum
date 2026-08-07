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
/// RFC-006 Task 3: POST /v1/payments/initiate artık opsiyonel bir "kind" alanı alıyor.
/// kind=Deposit → online tahsilat ceil(total × DepositRate) kapora, kalan salonda ödenir;
/// kind gönderilmezse davranış birebir eski FullOnline akışı. Deposit iptal iadesi
/// BookingRefundService üzerinden payment.GrossAmountMinor'dan (yani kaporadan) türediği
/// için booking toplamını DEĞİL kaporayı iade eder — üçüncü test bunu kilitliyor.
///
/// Bilinen tutar (200 TL) için owner API'siyle test-özel bir servis yaratılır — seed
/// servislerinin fiyat/sıralamasına bağımlılık yok. Beklenen (DepositRate 0.15):
/// deposit = ceil(20000 × 0.15) = 3000.
///
/// Review fix (2026-08-07): Payment:CommissionRate config'i KALDIRILDI (bkz. Infrastructure.
/// DependencyInjection.AddInfrastructure yorumu) — InitiateBookingCommand artık booking.
/// PlatformCommissionMinor'a sabit 0 yazıyor (vestigial alan, hiçbir faturaya taşınmıyordu zaten).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DepositPaymentTests(IntegrationTestFactory factory)
{
    public sealed record BarberShopRow(Guid Id, Guid OwnerId, string Name);
    public sealed record StylistRow(Guid Id, Guid BarberShopId, string DisplayName);
    public sealed record CreatedResp(Guid Id);
    public sealed record InitiateBookingResp(Guid BookingId, DateTimeOffset ScheduledAt, DateTimeOffset ScheduledEndsAt, long TotalAmountMinor, string Currency);
    public sealed record InitiatePaymentResp(
        Guid PaymentId, string CheckoutUrl, string? ProviderToken, DateTimeOffset ExpiresAt,
        long AmountMinor, long RemainingAtShopMinor, string Kind);
    public sealed record CancelResp(int RefundPercentage);

    private const long KnownTotalMinor = 200_00;
    private const long ExpectedDepositMinor = 30_00;   // ceil(20000 × 0.15)

    [Fact]
    public async Task Deposit_initiate_charges_ceil_15_percent_and_persists_kind()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");
        if (initiate.StatusCode != HttpStatusCode.OK)
        {
            var body = await initiate.Content.ReadAsStringAsync();
            Assert.Fail($"Deposit initiate failed: {(int)initiate.StatusCode} body={body}");
        }
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;
        Assert.NotEqual(Guid.Empty, pay.PaymentId);
        Assert.Equal(ExpectedDepositMinor, pay.AmountMinor);
        Assert.Equal(KnownTotalMinor - ExpectedDepositMinor, pay.RemainingAtShopMinor); // 170_00
        Assert.Equal("Deposit", pay.Kind);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
        var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
        Assert.Equal(PaymentKind.Deposit, payment.Kind);
        Assert.Equal(ExpectedDepositMinor, payment.GrossAmountMinor);
        Assert.Equal(KnownTotalMinor, booking.TotalAmountMinor);
        Assert.Equal(0, booking.PlatformCommissionMinor); // vestigial alan, artık her zaman 0 — bkz. sınıf XML doc (Payment:CommissionRate kaldırıldı)
        Assert.Equal(0, payment.PlatformCommissionMinor); // RFC-012: komisyon artık ödeme anında kesilmiyor, kapora tamamen kuaföre akar
    }

    [Fact]
    public async Task Full_initiate_unchanged_and_kind_defaults_to_full()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        // kind alanı YOK — eski istek gövdesi aynen çalışmalı (backward compat).
        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: null);
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;
        Assert.Equal(KnownTotalMinor, pay.AmountMinor);
        Assert.Equal(0, pay.RemainingAtShopMinor);
        Assert.Equal("FullOnline", pay.Kind);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
        var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
        Assert.Equal(PaymentKind.FullOnline, payment.Kind);
        Assert.Equal(booking.TotalAmountMinor, payment.GrossAmountMinor);
    }

    /// <summary>
    /// Deposit ödenmiş booking'in &gt;24h iptali: iade tutarı KAPORA (payment.GrossAmountMinor)
    /// üzerinden hesaplanır — booking toplamı değil. Aksi durumda müşteriye ödemediği parayı
    /// iade etmiş olurduk.
    /// </summary>
    [Fact]
    public async Task Deposit_cancel_more_than_24h_refunds_full_deposit_not_booking_total()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 60);

        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;

        // mock-complete (dev GET hook) — PaymentInitiationTests kalıbı, token = mock-{paymentId:N}
        var completeUrl = $"/v1/payments/mock/complete?token={Uri.EscapeDataString(pay.ProviderToken ?? "MOCK")}";
        var complete = await customerClient.GetAsync(completeUrl);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Set<BookingAggregate>().AsNoTracking().FirstAsync(b => b.Id == bookingId);
            // RFC-013 C4: kapora tahsilatı artık booking'i Confirm ETMEZ — yalnızca "ödeme
            // alındı" işaretler (Pending kalır, DepositPaidAt dolar). Onay ayrı bir adım
            // (OwnerConfirm). Cancel() Pending durumda da çalışır (yalnız Cancelled/Completed/
            // NoShow'da reddedilir), bu yüzden bu testin asıl amacı (iade tutarının kapora
            // üzerinden hesaplanması) owner hiç onaylamasa da geçerli kalır.
            Assert.Equal(BookingStatus.Pending, booking.Status);
            Assert.NotNull(booking.DepositPaidAt);
        }

        var cancel = await customerClient.PostAsync($"/v1/bookings/{bookingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var cancelResp = (await cancel.Content.ReadFromJsonAsync<CancelResp>(TestHelpers.Json))!;
        Assert.Equal(100, cancelResp.RefundPercentage);

        // Hangfire testte kapalı — job'ı elle çalıştır (AutoRefundFlowTests kalıbı).
        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ProcessBookingRefundJob>();
            await job.RunAsync(bookingId, CancellationToken.None);
        }

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db2.Set<Payment>().Include(p => p.Refunds).AsNoTracking().FirstAsync(p => p.Id == pay.PaymentId);
        var refund = Assert.Single(payment.Refunds);
        Assert.Equal(ExpectedDepositMinor, refund.AmountMinor); // 30_00 — booking toplamı (200_00) DEĞİL
        Assert.Equal(RefundStatus.Succeeded, refund.Status);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    /// <summary>
    /// RFC-006 resume/switch: tamamlanmamış (Initiated) payment varken aynı booking'e farklı
    /// kind ile ikinci initiate 409 DEĞİL — mevcut satır ReinitiateCheckout ile yeniden
    /// kullanılır (aynı paymentId, booking başına tek satır invariantı korunur).
    /// </summary>
    [Fact]
    public async Task Reinitiate_switches_deposit_to_full_and_reuses_single_payment_row()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        var depositInit = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");
        Assert.Equal(HttpStatusCode.OK, depositInit.StatusCode);
        var depositPay = (await depositInit.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;

        // Müşteri fikir değiştirdi: tamamı online — YENİ Idempotency-Key ile (mobile her denemede yeni key üretir).
        var fullInit = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "FullOnline");
        if (fullInit.StatusCode != HttpStatusCode.OK)
        {
            var body = await fullInit.Content.ReadAsStringAsync();
            Assert.Fail($"Reinitiate failed: {(int)fullInit.StatusCode} body={body}");
        }
        var fullPay = (await fullInit.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;
        Assert.Equal(depositPay.PaymentId, fullPay.PaymentId); // AYNI satır — yeni payment yaratılmadı
        Assert.Equal(KnownTotalMinor, fullPay.AmountMinor);
        Assert.Equal(0, fullPay.RemainingAtShopMinor);
        Assert.Equal("FullOnline", fullPay.Kind);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Set<Payment>().CountAsync(p => p.BookingId == bookingId)); // tek satır invariantı
        var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == fullPay.PaymentId);
        Assert.Equal(PaymentKind.FullOnline, payment.Kind);
        Assert.Equal(KnownTotalMinor, payment.GrossAmountMinor);
        Assert.Equal(PaymentStatus.Initiated, payment.Status);
    }

    [Fact]
    public async Task Reinitiate_after_capture_conflicts_and_payment_untouched()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 45);

        var depositInit = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");
        Assert.Equal(HttpStatusCode.OK, depositInit.StatusCode);
        var depositPay = (await depositInit.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;

        var completeUrl = $"/v1/payments/mock/complete?token={Uri.EscapeDataString(depositPay.ProviderToken ?? "MOCK")}";
        var complete = await customerClient.GetAsync(completeUrl);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        // Para hareketi başladı (Captured) — tür değiştirme/yeniden deneme reddedilir.
        var retry = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "FullOnline");
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Set<Payment>().CountAsync(p => p.BookingId == bookingId));
        var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == depositPay.PaymentId);
        Assert.Equal(PaymentKind.Deposit, payment.Kind);
        Assert.Equal(ExpectedDepositMinor, payment.GrossAmountMinor);
        Assert.Equal(PaymentStatus.Captured, payment.Status); // dokunulmamış
    }

    /// <summary>
    /// RFC-006 Task 5: müşteri booking detayı ve owner randevu listesi DTO'larına eklenen
    /// paymentKind / paidOnlineMinor / remainingAtShopMinor alanlarının doğru doldurulduğunu
    /// (veya ödeme yoksa/henüz tamamlanmadıysa null/null/0 kaldığını) pinler.
    /// </summary>
    public sealed record BookingDetailPaymentResp(
        Guid Id, long TotalAmountMinor, string? PaymentKind, long? PaidOnlineMinor, long RemainingAtShopMinor);

    public sealed record OwnerBookingPaymentResp(
        Guid Id, string? PaymentKind, long? PaidOnlineMinor, long RemainingAtShopMinor);

    [Fact]
    public async Task Detail_shows_deposit_payment_fields_after_mock_complete()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;

        var completeUrl = $"/v1/payments/mock/complete?token={Uri.EscapeDataString(pay.ProviderToken ?? "MOCK")}";
        var complete = await customerClient.GetAsync(completeUrl);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var detail = await customerClient.GetJson<BookingDetailPaymentResp>($"/v1/bookings/{bookingId}");
        Assert.Equal("Deposit", detail.PaymentKind);
        Assert.Equal(ExpectedDepositMinor, detail.PaidOnlineMinor);
        Assert.Equal(KnownTotalMinor - ExpectedDepositMinor, detail.RemainingAtShopMinor); // 170_00
    }

    [Fact]
    public async Task Detail_has_null_payment_fields_when_no_payment_initiated()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        var detail = await customerClient.GetJson<BookingDetailPaymentResp>($"/v1/bookings/{bookingId}");
        Assert.Null(detail.PaymentKind);
        Assert.Null(detail.PaidOnlineMinor);
        Assert.Equal(0, detail.RemainingAtShopMinor);
    }

    [Fact]
    public async Task Detail_shows_full_online_payment_fields_after_mock_complete()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "FullOnline");
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;

        var completeUrl = $"/v1/payments/mock/complete?token={Uri.EscapeDataString(pay.ProviderToken ?? "MOCK")}";
        var complete = await customerClient.GetAsync(completeUrl);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var detail = await customerClient.GetJson<BookingDetailPaymentResp>($"/v1/bookings/{bookingId}");
        Assert.Equal("FullOnline", detail.PaymentKind);
        Assert.Equal(KnownTotalMinor, detail.PaidOnlineMinor);
        Assert.Equal(0, detail.RemainingAtShopMinor);
    }

    [Fact]
    public async Task Owner_list_shows_remaining_at_shop_for_deposit_booking()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var pay = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentResp>(TestHelpers.Json))!;

        var completeUrl = $"/v1/payments/mock/complete?token={Uri.EscapeDataString(pay.ProviderToken ?? "MOCK")}";
        var complete = await customerClient.GetAsync(completeUrl);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var fromIso = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("o"));
        var toIso = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(70).ToString("o"));
        var ownerBookings = await ownerClient.GetJson<IReadOnlyList<OwnerBookingPaymentResp>>(
            $"/v1/owner/barber-shop/bookings?from={fromIso}&to={toIso}");
        var ownerBooking = Assert.Single(ownerBookings, b => b.Id == bookingId);
        Assert.Equal("Deposit", ownerBooking.PaymentKind);
        Assert.Equal(ExpectedDepositMinor, ownerBooking.PaidOnlineMinor);
        Assert.Equal(KnownTotalMinor - ExpectedDepositMinor, ownerBooking.RemainingAtShopMinor); // 170_00
    }

    /// <summary>
    /// Yok-sayma kuralının pin'i: Initiated (henüz ödenmemiş) bir payment satırı var olsa bile
    /// booking detayı "salonda ödenecek" bilgisini bu payment'tan TÜRETMEMELİ — müşteri parayı
    /// henüz göndermedi (T8'deki "Ödemeyi tamamla" CTA'sı ayrıca status==Pending üzerinden çalışır).
    /// </summary>
    [Fact]
    public async Task Detail_ignores_initiated_unpaid_payment()
    {
        await factory.ResetDatabaseAsync();
        await SeedAsync();

        using var customerClient = factory.CreateClient();
        using var ownerClient = factory.CreateClient();
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);
        ownerClient.SetBearer((await ownerClient.DevLoginAsync("BarberOwner")).AccessToken);

        var bookingId = await InitiateKnownTotalBookingAsync(customerClient, ownerClient, 40);

        // Initiate ama HİÇ mock-complete etme — payment Initiated durumunda kalır.
        var initiate = await PostInitiatePayment(customerClient, bookingId, Guid.NewGuid().ToString(), kind: "Deposit");
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payment = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.BookingId == bookingId);
            Assert.Equal(PaymentStatus.Initiated, payment.Status); // ön koşul teyidi
        }

        var detail = await customerClient.GetJson<BookingDetailPaymentResp>($"/v1/bookings/{bookingId}");
        Assert.Null(detail.PaymentKind);
        Assert.Null(detail.PaidOnlineMinor);
        Assert.Equal(0, detail.RemainingAtShopMinor);
    }

    private async Task SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
        await seeder.SeedAsync(default);
    }

    private static async Task<HttpResponseMessage> PostInitiatePayment(
        HttpClient client, Guid bookingId, string idempotencyKey, string? kind)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/payments/initiate");
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        req.Content = kind is null
            ? JsonContent.Create(new { bookingId }, options: TestHelpers.Json)
            : JsonContent.Create(new { bookingId, kind }, options: TestHelpers.Json);
        return await client.SendAsync(req);
    }

    /// <summary>
    /// Owner API'siyle 200 TL'lik test servisi yaratır ve müşteri o servise booking açar —
    /// toplam tutar deterministik olarak 200_00 olur. RFC-013 C4: shop'un kapora politikası
    /// (%15, ExpectedDepositMinor sabitiyle hizalı) burada da açılır — InitiatePaymentCommand
    /// artık Kind=Deposit için shop.BookingPolicy.DepositEnabled/DepositPercent şart koşuyor
    /// (global PaymentOptions.DepositRate fallback'i yalnız config'te kalıyor, kullanılmıyor).
    /// </summary>
    private static async Task<Guid> InitiateKnownTotalBookingAsync(HttpClient customer, HttpClient owner, int daysAhead)
    {
        var ownerShop = await owner.GetJson<BarberShopRow>("/v1/owner/barber-shop");

        var policyResp = await owner.PutAsJsonAsync("/v1/owner/barber-shop/booking-policy", new
        {
            depositEnabled = true,
            depositPercent = 0.15m,
            maxPendingPerCustomer = 2,
            minDaysBetweenBookings = (int?)null,
            maxBookingsPerMonth = (int?)null,
        });
        Assert.Equal(HttpStatusCode.OK, policyResp.StatusCode);

        var createService = await owner.PostAsJsonAsync($"/v1/owner/barber-shop/{ownerShop.Id}/services", new
        {
            name = "Kapora Test Servisi",
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

# Kapora Checkout (RFC-006) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Müşteri randevu ödemesinde %15 kapora / tam ödeme seçimi + mock-uçtan-uca WebView checkout + PayTR webhook iskeleti (RFC-006, founder onaylı).

**Architecture:** `Payment` aggregate'ine `Kind` (FullOnline|Deposit) eklenir; kapora tutarı `ceil(total × Payment:DepositRate)` ile Application katmanında hesaplanır ve `Payment.GrossAmountMinor` = online ödenen tutar olur (refund matrisi/oto-refund zinciri DEĞİŞMEZ — hepsi payment.Gross üzerinden zaten çalışıyor). Webhook idempotent completion'ı mevcut `CompletePaymentCommand` zincirini yeniden kullanır. Mobile'da confirm ekranına seçim kartı, yeni `booking/payment.tsx` checkout ekranı (mock simülasyon + gerçek WebView iskeleti) eklenir.

**Tech Stack:** .NET 8 + EF Core (migration M16) + MediatR + FluentValidation + xUnit/Testcontainers · Expo RN + react-native-webview + TanStack Query yok (mevcut elle-fetch kalıbı) · Hangfire (değişiklik yok)

**Ön koşullar:** Working tree temiz, `main` 10 commit ileride, 221/221 backend + 35 mobile test yeşil. Docker açık (Testcontainers). Testler proje bazında koşulur (`dotnet test` slnx sorunu — bkz. commit b277b30 sonrası pratik).

---

### Task 1: Domain — PaymentKind + Initiate genişletme

**Files:**
- Create: `backend/src/Kuaforum.Domain/Modules/Payment/PaymentKind.cs`
- Modify: `backend/src/Kuaforum.Domain/Modules/Payment/Payment.cs` (Initiate imzası + Kind property)
- Test: `backend/tests/Kuaforum.Tests.Domain/Payment/PaymentTests.cs` (mevcut dosyaya ekle)

- [ ] **Step 1: Failing testleri yaz** — `PaymentTests.cs`'e ekle (mevcut `Initiate` çağrılarının named-argument stilini koru; dosyadaki mevcut helper'ları incele):

```csharp
[Fact]
public void Initiate_deposit_kind_persists_kind_and_accepts_deposit_at_least_commission()
{
    var r = Payment.Initiate(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        grossAmountMinor: 15_00, platformCommissionMinor: 10_00,
        PaymentProviderName.Mock, kind: PaymentKind.Deposit);
    Assert.True(r.IsSuccess);
    Assert.Equal(PaymentKind.Deposit, r.Value.Kind);
}

[Fact]
public void Initiate_deposit_below_commission_fails()
{
    var r = Payment.Initiate(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        grossAmountMinor: 9_00, platformCommissionMinor: 10_00,
        PaymentProviderName.Mock, kind: PaymentKind.Deposit);
    Assert.True(r.IsFailure);
}

[Fact]
public void Initiate_defaults_to_full_online_kind()
{
    var r = Payment.Initiate(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        grossAmountMinor: 100_00, platformCommissionMinor: 10_00, PaymentProviderName.Mock);
    Assert.True(r.IsSuccess);
    Assert.Equal(PaymentKind.FullOnline, r.Value.Kind);
}
```

DİKKAT: mevcut `Initiate(grossAmountMinor: 9_00, platformCommissionMinor: 10_00)` FullOnline'da zaten `komisyon > gross` validasyonuna takılır — Deposit testinde bu validasyonun Deposit'e özel mesajla değil genel kuralla yakalanması kabul (Deposit'te gross = kapora; komisyon ≤ kapora invariant'ı mevcut `platformCommissionMinor > grossAmountMinor` kuralının TA KENDİSİ). Yani domain'de EK kural gerekmiyor; testler mevcut kuralın Deposit semantiğini kilitler.

- [ ] **Step 2: Testleri koştur, FAIL gör** — `cd backend && dotnet test tests/Kuaforum.Tests.Domain --filter "FullyQualifiedName~PaymentTests" -v minimal` → beklenen: `PaymentKind` tanımsız derleme hatası.

- [ ] **Step 3: Implementasyon** — `PaymentKind.cs`:

```csharp
namespace Kuaforum.Domain.Modules.Payment;

/// <summary>RFC-006: FullOnline = tamamı online; Deposit = %DepositRate kapora online, kalan salonda.</summary>
public enum PaymentKind
{
    FullOnline = 0,
    Deposit = 1,
}
```

`Payment.cs` değişiklikleri: property ekle (Status'un altına):

```csharp
public PaymentKind Kind { get; private set; }
```

`Initiate` imzasına parametre ekle (`string? idempotencyKey = null`'dan ÖNCE, default'lu):

```csharp
public static Result<Payment> Initiate(
    Guid bookingId, Guid customerId, Guid subMerchantBarberShopId,
    long grossAmountMinor, long platformCommissionMinor,
    PaymentProviderName provider,
    PaymentKind kind = PaymentKind.FullOnline,
    string? idempotencyKey = null)
```

ve nesne başlatıcıya `Kind = kind,` ekle. Mevcut çağrı yerleri (InitiatePaymentCommand + testler) named-argument kullandığı için kırılmaz; positional kullanan varsa derleyici gösterir — düzelt.

- [ ] **Step 4: Testler PASS** — aynı komut → 3 yeni test dahil yeşil.

- [ ] **Step 5: Migration** — `cd backend && dotnet ef migrations add M16_PaymentKind --project src/Kuaforum.Infrastructure --startup-project src/Kuaforum.Api`. Üretilen migration'da `kind` int kolonu default 0 olmalı; `PaymentConfiguration`'da açık mapping gerekiyorsa (`builder.Property(p => p.Kind)`) mevcut enum kalıbını izle (BookingConfiguration'daki Status mapping'ine bak).

- [ ] **Step 6: Tüm domain + integration smoke** — `dotnet test tests/Kuaforum.Tests.Domain -v minimal` yeşil; `dotnet build src/Kuaforum.Api` temiz.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Kuaforum.Domain/Modules/Payment/ backend/src/Kuaforum.Infrastructure/Persistence/ backend/tests/Kuaforum.Tests.Domain/Payment/PaymentTests.cs
git commit -m "feat(payment): PaymentKind (FullOnline|Deposit) + M16 migration (RFC-006 Task 1)"
```

---

### Task 2: Config — PaymentOptions (CommissionRate + DepositRate) + ceil helper

**Files:**
- Create: `backend/src/Kuaforum.Application/Common/PaymentOptions.cs`
- Create: `backend/src/Kuaforum.Application/Common/PaymentAmounts.cs`
- Modify: `backend/src/Kuaforum.Infrastructure/DependencyInjection.cs` (options kaydı + startup validasyonu)
- Modify: `backend/src/Kuaforum.Api/appsettings.json` (`"Payment": { "CommissionRate": 0.10, "DepositRate": 0.15 }`)
- Modify: `backend/src/Kuaforum.Application/Modules/Booking/InitiateBookingCommand.cs` (`DefaultCommissionRate` const → options; GAP P2 "komisyon hard-coded" kısmi kapanış)
- Test: `backend/tests/Kuaforum.Tests.Application/PaymentAmountsTests.cs` (yeni)

- [ ] **Step 1: Failing test**

```csharp
using Kuaforum.Application.Common;
using Xunit;

namespace Kuaforum.Tests.Application;

public class PaymentAmountsTests
{
    [Theory]
    [InlineData(100_00, 0.15, 15_00)]   // düz
    [InlineData(333, 0.15, 50)]         // 49.95 → ceil 50
    [InlineData(1, 0.15, 1)]            // min 1 kuruş
    [InlineData(100_00, 0.10, 10_00)]
    public void Deposit_is_ceiled(long total, decimal rate, long expected)
        => Assert.Equal(expected, PaymentAmounts.CalculateDepositMinor(total, rate));

    [Fact]
    public void Deposit_never_below_commission_when_rate_gte_commission_rate()
    {
        // ceil garantisi: rate ≥ commissionRate ise deposit ≥ round(commission) her tutar için
        for (long total = 1; total <= 5_000; total++)
        {
            var deposit = PaymentAmounts.CalculateDepositMinor(total, 0.15m);
            var commission = PaymentAmounts.CalculateCommissionMinor(total, 0.10m);
            Assert.True(deposit >= commission, $"total={total}: deposit {deposit} < commission {commission}");
        }
    }
}
```

- [ ] **Step 2: FAIL gör** — `dotnet test tests/Kuaforum.Tests.Application --filter PaymentAmounts -v minimal` → derleme hatası.

- [ ] **Step 3: Implementasyon** — `PaymentAmounts.cs`:

```csharp
namespace Kuaforum.Application.Common;

/// <summary>RFC-006 tutar hesapları. Deposit YUKARI yuvarlanır (kapora ≥ komisyon garantisi).</summary>
public static class PaymentAmounts
{
    public static long CalculateDepositMinor(long totalMinor, decimal depositRate)
        => (long)Math.Ceiling(totalMinor * depositRate);

    public static long CalculateCommissionMinor(long totalMinor, decimal commissionRate)
        => (long)Math.Round(totalMinor * commissionRate, MidpointRounding.AwayFromZero);
}
```

`PaymentOptions.cs`:

```csharp
namespace Kuaforum.Application.Common;

public sealed class PaymentOptions
{
    public const string SectionName = "Payment";
    /// <summary>Platform komisyonu (toplam hizmet bedeli üzerinden). RFC-006.</summary>
    public decimal CommissionRate { get; set; } = 0.10m;
    /// <summary>Kapora oranı. Invariant: >= CommissionRate (startup'ta doğrulanır).</summary>
    public decimal DepositRate { get; set; } = 0.15m;
}
```

DI kaydı (`DependencyInjection.cs` — mevcut options kalıbını izle, ör. NetGsm options nasıl bind ediliyorsa):

```csharp
services.AddOptions<PaymentOptions>()
    .Bind(configuration.GetSection(PaymentOptions.SectionName))
    .Validate(o => o.DepositRate >= o.CommissionRate,
        "Payment:DepositRate, Payment:CommissionRate'ten küçük olamaz (RFC-006 kapora ≥ komisyon invariantı).")
    .Validate(o => o.CommissionRate > 0 && o.CommissionRate < 1 && o.DepositRate > 0 && o.DepositRate <= 1,
        "Payment oranları (0,1] aralığında olmalı.")
    .ValidateOnStart();
```

`InitiateBookingCommand.cs`: `private const decimal DefaultCommissionRate = 0.10m;` satırını sil; handler ctor'una `IOptions<PaymentOptions> paymentOptions` ekle; komisyon hesabını `PaymentAmounts.CalculateCommissionMinor(totalMinor, paymentOptions.Value.CommissionRate)` yap (mevcut hesap satırını bul: `grep -n "DefaultCommissionRate" backend/src/Kuaforum.Application/Modules/Booking/InitiateBookingCommand.cs`). Eski hesap `(long)(total * rate)` truncation ise davranış farkını testle kilitle: mevcut integration testlerinde komisyon asserti varsa güncelle (round vs truncate yalnız küsuratlı toplamda farklı — testlerde 100_00 gibi düz tutarlar kullanılıyor, fark çıkmamalı; çıkarsa raporla).

- [ ] **Step 4: PASS + tüm Application testleri** — `dotnet test tests/Kuaforum.Tests.Application -v minimal` yeşil.

- [ ] **Step 5: Commit** — `git add ... && git commit -m "feat(payment): PaymentOptions (config-driven komisyon+kapora oranı) + ceil helper (RFC-006 Task 2)"`

---

### Task 3: InitiatePaymentCommand `kind` + endpoint

**Files:**
- Modify: `backend/src/Kuaforum.Application/Modules/Payment/InitiatePaymentCommand.cs`
- Modify: `backend/src/Kuaforum.Api/Endpoints/PaymentEndpoints.cs` (`InitiateRequest`)
- Test: `backend/tests/Kuaforum.Tests.Integration/DepositPaymentTests.cs` (yeni)

- [ ] **Step 1: Failing integration testleri** — mevcut `PaymentInitiationTests.cs` kalıbını (factory, auth helper, booking seed) birebir izleyerek yeni dosya:

```csharp
// DepositPaymentTests.cs — kalıp: PaymentInitiationTests'ten factory/login/booking-oluşturma helper'larını kopyala/uyarla.
[Fact]
public async Task Deposit_initiate_charges_ceil_15_percent_and_persists_kind()
{
    // 1) müşteri login + booking oluştur (toplamı bilinen bir hizmetle, ör. 200_00)
    // 2) POST /v1/payments/initiate body: { bookingId, kind: "Deposit" }
    // 3) 200; response.amountMinor == 30_00
    // 4) DB: payment.Kind == Deposit, GrossAmountMinor == 30_00,
    //    PlatformCommissionMinor == booking.PlatformCommissionMinor (20_00)
}

[Fact]
public async Task Full_initiate_unchanged_and_kind_defaults_to_full()
{
    // body kind alanı YOK → payment.Kind == FullOnline, Gross == booking.TotalAmountMinor
}

[Fact]
public async Task Deposit_cancel_more_than_24h_refunds_full_deposit_not_booking_total()
{
    // deposit initiate + mock complete → booking Confirmed
    // customer cancel (>24h) → ProcessBookingRefundJob'ı elle çalıştır (AutoRefundFlowTests kalıbı)
    // DB: tek Refund, AmountMinor == 30_00 (kapora), payment.Status == Refunded
}
```

(Yorum satırları pseudo değil — gerçek kod, mevcut test dosyalarındaki somut helper çağrılarıyla doldurulacak; `AutoRefundFlowTests.cs` ve `PaymentInitiationTests.cs`'teki mevcut akışlar aynen kopyalanıp `kind` eklenir. Bu iki dosya bu görevde ZORUNLU okuma.)

- [ ] **Step 2: FAIL gör** — `dotnet test tests/Kuaforum.Tests.Integration --filter DepositPayment -v minimal`.

- [ ] **Step 3: Implementasyon** — `InitiatePaymentCommand`:

```csharp
public sealed record InitiatePaymentCommand(
    Guid CustomerId, Guid BookingId, PaymentKind Kind, string? IdempotencyKey) : ICommand<InitiatePaymentResponse>;

public sealed record InitiatePaymentResponse(
    Guid PaymentId, string CheckoutUrl, string? ProviderToken, DateTimeOffset ExpiresAt,
    long AmountMinor, long RemainingAtShopMinor, string Kind);
```

Validator'a `RuleFor(x => x.Kind).IsInEnum();`. Handler'da (booking yüklendikten sonra, `PaymentAggregate.Initiate` çağrısından önce):

```csharp
var opts = paymentOptions.Value; // ctor'a IOptions<PaymentOptions> ekle
var onlineAmountMinor = request.Kind == PaymentKind.Deposit
    ? PaymentAmounts.CalculateDepositMinor(booking.TotalAmountMinor, opts.DepositRate)
    : booking.TotalAmountMinor;
```

`PaymentAggregate.Initiate(...)` çağrısında `booking.TotalAmountMinor` yerine `onlineAmountMinor`, ve `kind: request.Kind` geç. `CheckoutRequest.GrossAmountMinor` da `onlineAmountMinor`. Response'a `onlineAmountMinor`, `booking.TotalAmountMinor - onlineAmountMinor`, `request.Kind.ToString()` ekle. Audit payload'ına `kind` ekle.

`PaymentEndpoints.cs`: `public sealed record InitiateRequest(Guid BookingId, PaymentKind Kind = PaymentKind.FullOnline);` ve `InitiateAsync`'te command'a `body.Kind` geç (JsonStringEnumConverter global — "Deposit"/"FullOnline" string body kabul edilir).

- [ ] **Step 4: PASS** — Task 3 testleri + `dotnet test tests/Kuaforum.Tests.Integration -v minimal` TAMAMI yeşil (mevcut 57 + yeniler).

- [ ] **Step 5: Commit** — `git commit -m "feat(payment): initiate kind parametresi — kapora tutarı ceil(total×rate) (RFC-006 Task 3)"`

---

### Task 4: PayTR webhook iskeleti — POST /v1/webhooks/paytr

**Files:**
- Create: `backend/src/Kuaforum.Api/Endpoints/WebhookEndpoints.cs`
- Create: `backend/src/Kuaforum.Application/Modules/Payment/ProcessPaytrWebhookCommand.cs`
- Create: `backend/src/Kuaforum.Infrastructure/Payments/PaytrWebhookSignature.cs`
- Modify: `backend/src/Kuaforum.Api/Program.cs` (`app.MapWebhookEndpoints();` — mevcut MapXxx zincirine)
- Modify: `backend/src/Kuaforum.Api/appsettings.Development.json` (`"PayTr": { "MerchantKey": "test-merchant-key", "MerchantSalt": "test-merchant-salt" }` — dev/test değerleri; prod env'den)
- Test: `backend/tests/Kuaforum.Tests.Integration/PaytrWebhookTests.cs` (yeni)

**ÖNEMLİ `[unverified 2026-07-29]`:** PayTR bildirim şeması public dokümana göre form-urlencoded `merchant_oid, status ("success"|"failed"), total_amount, hash` + HMAC-SHA256(base64, key=merchant_key, data=merchant_oid+merchant_salt+status+total_amount) ve **yanıt gövdesi literal "OK"** olmalı. İmplementasyonda context7/https://dev.paytr.com üzerinden alan adlarını TEYİT ET; farklıysa DTO'yu güncelle ve RFC-006'ya not düş. Mimari (HMAC doğrula → idempotent complete → "OK") şemadan bağımsız ayakta kalır.

- [ ] **Step 1: Failing testler**

```csharp
// PaytrWebhookTests.cs — senaryolar (her biri gerçek Testcontainers DB'siyle):
// 1) Happy path: deposit initiate → webhook(status=success, doğru HMAC) → 200 "OK",
//    payment Captured, booking Confirmed.
// 2) İdempotent: aynı webhook 2. kez → 200 "OK", DB'de değişiklik yok (CapturedAt aynı).
// 3) Yanlış HMAC → 400, payment Initiated kalır.
// 4) Bilinmeyen merchant_oid → 200 "OK" (PayTR retry fırtınası önlenir) + payment yok.
// 5) status=failed → payment.Status == Failed, booking Pending kalır, yanıt 200 "OK".
// HMAC hesabı testte PaytrWebhookSignature.Compute(...) ile — prod kodu ile aynı fonksiyon.
```

- [ ] **Step 2: FAIL gör.**

- [ ] **Step 3: Implementasyon** — `PaytrWebhookSignature.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Kuaforum.Infrastructure.Payments;

public static class PaytrWebhookSignature
{
    /// <summary>[unverified 2026-07-29] PayTR bildirim hash formülü — gerçek entegrasyonda resmi doc ile teyit edilecek.</summary>
    public static string Compute(string merchantOid, string merchantSalt, string status, string totalAmount, string merchantKey)
    {
        var data = string.Concat(merchantOid, merchantSalt, status, totalAmount);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(merchantKey));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }
}
```

`ProcessPaytrWebhookCommand.cs` (Application; imza doğrulaması API katmanında — command saf iş mantığı):

```csharp
public sealed record ProcessPaytrWebhookCommand(string MerchantOid, bool Succeeded, long TotalAmountMinor)
    : ICommand<Guid>;

// Handler:
// 1) Guid.TryParseExact(MerchantOid, "N", out var paymentId) değilse NotFound.
// 2) Payment'ı yükle (yoksa NotFound — endpoint bunu 200 "OK"a çevirir, log Warning).
// 3) Zaten Captured/Settled/Refunded → Result.Success(payment.Id)  (İDEMPOTENT no-op).
// 4) Succeeded: payment.Capture($"paytr-{MerchantOid}", pspMdrMinor: 0) → booking'i yükle,
//    Pending ise booking.Confirm() (CompletePaymentCommand'daki mevcut confirm bloğunu AYNEN
//    yeniden kullan — kod tekrarına düşme: ortak private helper'a çıkarmak yerine
//    CompletePaymentCommand'daki bloğu incele; 10 satırın altındaysa kopya kabul, üstündeyse
//    PaymentCompletionService'e çıkar ve İKİ handler'ı da ona bağla).
// 5) !Succeeded: payment.Fail("PayTR bildirim: başarısız") — Initiated değilse yine idempotent no-op.
// 6) Audit: "payment.webhook_processed" (payload: oid, succeeded, amount).
// 7) SaveChangesAsync.
```

`WebhookEndpoints.cs`:

```csharp
public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        // PayTR sunucudan sunucuya çağırır — auth YOK, güvenlik HMAC ile.
        app.MapPost("/v1/webhooks/paytr", HandlePaytrAsync)
            .WithTags("Webhooks").AllowAnonymous().DisableAntiforgery();
        return app;
    }

    private static async Task<IResult> HandlePaytrAsync(
        HttpRequest req, IMediator m, IConfiguration cfg, ILoggerFactory lf, CancellationToken ct)
    {
        var log = lf.CreateLogger("PaytrWebhook");
        var form = await req.ReadFormAsync(ct);
        string oid = form["merchant_oid"].ToString(), status = form["status"].ToString(),
               amount = form["total_amount"].ToString(), hash = form["hash"].ToString();

        var expected = PaytrWebhookSignature.Compute(oid,
            cfg["PayTr:MerchantSalt"] ?? "", status, amount, cfg["PayTr:MerchantKey"] ?? "");
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(hash)))
            return Results.Problem("Geçersiz imza.", statusCode: 400);

        var r = await m.Send(new ProcessPaytrWebhookCommand(
            oid, status == "success", long.TryParse(amount, out var a) ? a : 0), ct);
        if (r.IsFailure && r.Error.Code == "not_found")
            log.LogWarning("PayTR webhook bilinmeyen merchant_oid {Oid}", oid);
        return Results.Text("OK"); // PayTR "OK" görmezse bildirimi tekrar eder — bilinmeyen oid dahil OK.
    }
}
```

(`MockCompletePayment` dev endpoint'i aynen kalır — mock akış onu kullanmaya devam eder.)

- [ ] **Step 4: PASS** — webhook testleri + tüm Integration suite yeşil.
- [ ] **Step 5: Commit** — `git commit -m "feat(payment): PayTR webhook iskeleti — HMAC + idempotent completion (RFC-006 Task 4)"`

---

### Task 5: Booking DTO'larına ödeme alanları (customer detay + owner)

**Files:**
- Modify: booking detay query (customer) — bul: `grep -rn "refundPercentage\|RefundPercentage" backend/src/Kuaforum.Application/Modules/Booking/*.cs` (mobile `b.refundPercentage` kullandığı DTO)
- Modify: owner bugün/randevu listesi query'si — bul: `grep -rn "OwnerId" backend/src/Kuaforum.Application/Modules/Booking/GetShopBookingsQuery.cs` (dosya adı farklıysa grep ile bul)
- Test: mevcut booking detail integration testine alan assert'leri ekle

- [ ] **Step 1: Test genişlet** — deposit ödemeli booking'in detay endpoint'i şu alanları dönmeli: `paymentKind: "Deposit"`, `paidOnlineMinor: 30_00`, `remainingAtShopMinor: 170_00`; ödemesiz booking'de üçü de null/0. FAIL gör.

- [ ] **Step 2: Implementasyon** — DTO'lara üç alan ekle; sorguya payment LEFT JOIN (`Captured|Settled|PartiallyRefunded|Refunded` durumundaki payment'ı al):

```csharp
// Projeksiyona (mevcut Select içine):
PaymentKind = (string?)payment.Kind.ToString(),      // payment null ise null
PaidOnlineMinor = (long?)payment.GrossAmountMinor,
RemainingAtShopMinor = payment != null && payment.Kind == PaymentKind.Deposit
    ? b.TotalAmountMinor - payment.GrossAmountMinor : 0,
```

(EF Core'da nullable-join projeksiyonunda mevcut dosyanın kalıbını izle — booking detail zaten payment'a benzer JOIN'ler yapıyor olabilir; yapmıyorsa `ctx.Set<Payment>().Where(p => p.BookingId == b.Id && aktifDurumlar.Contains(p.Status)).OrderByDescending(p => p.CreatedAt).FirstOrDefault()` alt-sorgusu.)

- [ ] **Step 3: PASS + commit** — `git commit -m "feat(booking): detay DTO'larına paymentKind/paidOnline/remainingAtShop (RFC-006 Task 5)"`

---

### Task 6: Mobile — PaymentApi + confirm.tsx seçim kartı

**Files:**
- Create: `mobile/src/api/payment.ts`
- Modify: `mobile/app/booking/confirm.tsx` (💳 Ödeme kartı + handleConfirm akışı)
- Test: `mobile/src/lib/__tests__/deposit.test.ts` (yeni; helper `mobile/src/lib/deposit.ts`)

- [ ] **Step 1: Helper + failing jest**

`mobile/src/lib/deposit.ts`:

```ts
/** RFC-006 — backend Payment:DepositRate ile senkron tutulmalı (kaynak: appsettings Payment:DepositRate).
 *  Yalnız ÖN İZLEME için; gerçek tutar initiate response'undan (amountMinor) gelir. */
export const DEPOSIT_RATE = 0.15;

export function estimateDepositMinor(totalMinor: number): number {
  return Math.ceil(totalMinor * DEPOSIT_RATE);
}
export function remainingAtShopMinor(totalMinor: number, depositMinor: number): number {
  return totalMinor - depositMinor;
}
```

Test (`deposit.test.ts`): `estimateDepositMinor(20000) === 3000`, `estimateDepositMinor(333) === 50`, `remainingAtShopMinor(20000, 3000) === 17000`. `npx jest src/lib/__tests__/deposit.test.ts` önce FAIL (dosya yok) → implement → PASS.

- [ ] **Step 2: PaymentApi**

```ts
// mobile/src/api/payment.ts
import { apiClient } from './client';

export type PaymentKind = 'FullOnline' | 'Deposit';
export type InitiatePaymentResponse = {
  paymentId: string; checkoutUrl: string; providerToken?: string; expiresAt: string;
  amountMinor: number; remainingAtShopMinor: number; kind: PaymentKind;
};

export const PaymentApi = {
  async initiate(bookingId: string, kind: PaymentKind): Promise<InitiatePaymentResponse> {
    const res = await apiClient.post<InitiatePaymentResponse>('/v1/payments/initiate', { bookingId, kind });
    return res.data;
  },
};
```

- [ ] **Step 3: confirm.tsx** — state `const [payKind, setPayKind] = useState<PaymentKind>('Deposit');`. "💳 Ödeme" kartındaki placeholder metni kaldır, yerine iki seçenekli kart (mevcut Card/Pressable stiliyle; `totalAmount` param'ı varken tutarlar gösterilir):

```tsx
<Card style={{ gap: Spacing.sm }}>
  <Text style={styles.policyTitle}>💳 Ödeme</Text>
  <PayOption
    selected={payKind === 'Deposit'} onPress={() => setPayKind('Deposit')}
    title="Kapora ile ayırt (önerilen)"
    detail={total ? `Şimdi ${formatTry(estimateDepositMinor(total))} · Salonda ${formatTry(remainingAtShopMinor(total, estimateDepositMinor(total)))}` : 'Hizmet bedelinin %15’i şimdi, kalanı salonda'}
  />
  <PayOption
    selected={payKind === 'FullOnline'} onPress={() => setPayKind('FullOnline')}
    title="Tamamını şimdi öde"
    detail={total ? `${formatTry(total)} — salonda ödeme kalmaz` : 'Tamamı online'}
  />
  <Text style={styles.policySub}>Kesin tutar ödeme adımında gösterilir. 30 dk içinde ödenmeyen randevu otomatik iptal olur.</Text>
</Card>
```

(`const total = params.totalAmount ? parseInt(params.totalAmount, 10) : undefined;` — `PayOption` bileşeni aynı dosyada: seçili durumda `Colors.primary` border, `accessibilityRole="radio"`, `accessibilityState={{ selected }}`, min 44pt.) `handleConfirm` yeni akış:

```ts
const booking = await BookingApi.initiate(...);           // res.data — id alanını logla/teyit et
const pay = await PaymentApi.initiate(booking.id, payKind);
Haptics.success();
router.replace({ pathname: '/booking/payment',
  params: { paymentId: pay.paymentId, checkoutUrl: pay.checkoutUrl,
            amountMinor: String(pay.amountMinor), remainingAtShopMinor: String(pay.remainingAtShopMinor),
            bookingId: booking.id, expiresAt: pay.expiresAt } });
```

`PaymentApi.initiate` hatasında booking oluşmuş durumda — Alert: "Randevun oluştu ama ödeme başlatılamadı; Randevularım'dan ödemeyi tamamlayabilirsin" → `/(tabs)/bookings`. (Booking detayından "Ödemeyi tamamla" butonu Task 8'de.)

- [ ] **Step 4: Typecheck + jest** — `npx tsc --noEmit` temiz, jest yeşil. Commit: `feat(mobile): ödeme seçim kartı + PaymentApi (RFC-006 Task 6)`

---

### Task 7: Mobile — booking/payment.tsx checkout ekranı

**Files:**
- Create: `mobile/app/booking/payment.tsx`
- Modify: `mobile/package.json` (`npx expo install react-native-webview` — SDK 52 uyumlu sürümü expo belirler)

- [ ] **Step 1: WebView kur** — `cd mobile && npx expo install react-native-webview` → package.json + lock değişir.

- [ ] **Step 2: Ekran** — tam kod (mevcut ekran stillerini `confirm.tsx`'ten uyarPla):

```tsx
// Davranış özeti (kod tam yazılacak, iskelet şu):
// - checkoutUrl '/payments/mock/complete' içeriyorsa → MOCK MODU:
//   Card: "🧪 Test ödemesi" + tutar + "Ödemeyi simüle et" Button →
//   apiClient.get(checkoutUrl'in path+query'si) → başarıda success state.
// - Değilse → GERÇEK MOD: <WebView source={{ uri: checkoutUrl }}
//     onShouldStartLoadWithRequest={(req) => {
//       if (req.url.startsWith('kuaforum://payment/success')) { setPhase('verifying'); return false; }
//       if (req.url.startsWith('kuaforum://payment/cancel'))  { setPhase('cancelled'); return false; }
//       return true; }} />
// - phase 'verifying': BookingApi.get(bookingId)'yi 2 sn arayla max 8 kez yokla —
//   status Confirmed → success; süre dolarsa 'pending-webhook' bilgi ekranı
//   ("Ödemen işleniyor, onaylanınca bildirim alacaksın").
// - Success ekranı: ✅ + kapora modunda "Salonda ödenecek: X TL" satırı + "Randevuya git"
//   → router.replace(`/booking/${bookingId}`).
// - Hata/cancel: "Tekrar dene" (WebView reload / mock retry) + "Vazgeç" → bookings.
// - Üstte geri sayım: expiresAt'e kalan (30dk Pending timeout) — dakikada bir güncelle.
// - Tüm durumlarda loading/empty/error state ve accessibilityLabel'lar (CLAUDE.md §7).
```

- [ ] **Step 3: Uçtan uca elle doğrulama (mock)** — backend dev + expo başlat; keşfet → randevu → kapora seç → simüle et → booking Confirmed + detayda "Salonda ödenecek" görünecek (Task 8 sonrası). En azından payment ekranı akışının çalıştığını Android emülatörde bir kez gör (belgeleyen kısa not yeterli — screenshot şart değil).

- [ ] **Step 4: Typecheck + commit** — `feat(mobile): WebView checkout ekranı — mock simülasyon + PayTR iskeleti (RFC-006 Task 7)`

---

### Task 8: Detay ekranları — "salonda ödenecek" + owner rozeti + ödemesi eksik booking

**Files:**
- Modify: `mobile/app/booking/[id].tsx` (satır ~276 "Toplam" DetailRow civarı)
- Modify: `mobile/app/(barber)/today.tsx` (randevu kartına rozet)
- Modify: `mobile/src/api/booking.ts` (`Booking` tipine `paymentKind?/paidOnlineMinor?/remainingAtShopMinor?`)

- [ ] **Step 1: [id].tsx** — "Toplam" satırının altına:

```tsx
{b.paymentKind === 'Deposit' && b.remainingAtShopMinor ? (
  <>
    <Divider />
    <DetailRow icon="💵" label="Salonda ödenecek" value={formatTry(b.remainingAtShopMinor)} />
  </>
) : null}
{!b.paymentKind && b.status === 0 ? ( // Pending + hiç ödeme yok → tamamlama CTA
  <>
    <Divider />
    <Pressable onPress={() => router.push({ pathname: '/booking/pay-redirect', params: { bookingId: b.id } })}>
      {/* pay-redirect yerine: PaymentApi.initiate(b.id,'Deposit'|seçim) yeniden başlatan basit bir Alert-akışı da kabul —
          minimal çözüm: Alert ile kapora/tam seçtir → PaymentApi.initiate → /booking/payment push. Ayrı ekran YAZMA (YAGNI). */}
    </Pressable>
  </>
) : null}
```

(İkinci blok minimal biçimde: buton "💳 Ödemeyi tamamla" → `Alert.alert` iki seçenek → initiate → payment ekranı. Status enum karşılaştırmasında mevcut `isBookingCancelled`/status helper kalıbını kullan, ham `0` literal'i yerine mevcut sabitleri tercih et.)

- [ ] **Step 2: today.tsx** — randevu kartında (owner müşteri bilgisi bölümünün yanına): `b.remainingAtShopMinor ? <Badge label={`Salonda: ${formatTry(b.remainingAtShopMinor)}`} tone="warning" /> : null` (owner listesi DTO'su Task 5'te alanları aldı; `Badge` bileşeni today.tsx'te importlu mu kontrol et, değilse mevcut rozet kalıbını izle).

- [ ] **Step 3: Typecheck + jest + commit** — `feat(mobile): salonda-ödenecek satırı + owner rozeti + ödeme tamamlama CTA (RFC-006 Task 8)`

---

### Task 9: Kapanış — tam doğrulama + docs

- [ ] **Step 1:** Backend: `dotnet build` + 3 test projesi ayrı ayrı → TAMAMI yeşil (beklenen: 221 + ~14 yeni). Mobile: `npx tsc --noEmit` + `npx jest` (35 + yeniler).
- [ ] **Step 2:** `docs/04-modules/07-payment.md`'ye "RFC-006 implementasyonu (2026-07-29)" bölümü: Kind, DepositRate config, webhook endpoint'i, mobile akış — 10-15 satır, as-built formatta. GAP-ANALYSIS P0-5 maddesine "mock uçtan uca + webhook iskeleti tamam; gerçek PayTR founder hesabı bekliyor" notu.
- [ ] **Step 3:** Commit `docs: RFC-006 implementasyon notları` + kısa final rapor (test sayıları, riskler, PayTR şema [unverified] hatırlatması).

---

## Self-Review Notları (plan yazarı)

- **Spec kapsaması:** RFC-006'nın tüm bölümleri görevlere eşlendi — domain Kind (T1), config+ceil (T2), initiate kind (T3), webhook (T4), DTO'lar (T5), mobile seçim (T6), WebView (T7), detay+owner rozeti (T8), test/doc (T9). Walk-in istisnası: değişiklik yok (kapora yalnız normal initiate akışında — walk-in confirm.tsx'ten geçmiyor, `isWalkIn` ayrı path).
- **Bilinen riskler:** (1) PayTR şeması `[unverified]` — Task 4 mimarisi şemadan bağımsız; (2) mobile `DEPOSIT_RATE` sabiti backend config ile elle senkron — önizleme amaçlı, gerçek tutar response'tan; drift olursa yalnız önizleme yanılır; (3) `BookingApi.initiate` dönüşünde `id` alanı varsayımı — Task 6 Step 3'te teyit adımı var; (4) Task 3'te komisyon truncate→round değişimi mevcut testlerde fark yaratırsa raporlanacak.
- **Tip tutarlılığı:** `PaymentKind` string'leri her yerde `"FullOnline" | "Deposit"` (JsonStringEnumConverter); `InitiatePaymentResponse` alanları Task 3 ↔ Task 6 `payment.ts` birebir aynı adlarla.

# Ödeme Mimarisi

## Üst Düzey Karar

- **Pazaryeri modeli:** Iyzico/PayTR Pazaryeri ürünü kullanılır. **Platformun (Kuaforum) hiçbir an fonu elinde tutmaması** sayesinde 6493 sayılı Kanun **Madde 12/2-(b) "ticari temsilci" istisnası** uygulanır. **TCMB Ödeme Hizmeti Sağlayıcısı lisansı GEREKLİ DEĞİL.**
- **Primary PSP:** **PayTR Pazaryeri** (T+1 settlement) — kuaför cash-flow dostu.
- **Fallback PSP:** **Iyzico Pazaryeri** (T+3 hafta holdback dezavantajı, ama API + dokümantasyon zenginliği için yedek).
- **Abstraction:** `IPaymentProvider` arayüzü, PSP swap maliyetini düşürür ve pazarlık avantajı sağlar.
- **Komisyon hesabı:** Default %10 (negotiable kuaför tier'ına göre %5-15 aralığı).
- **E-Arşiv:** Çift fatura modeli (kuaför→müşteri, platform→kuaför), Nilvera entegrasyonu.

> Detaylı regülasyon analizi: [`docs/01-research/payment-infrastructure.md`](../01-research/payment-infrastructure.md), [`docs/01-research/turkey-compliance.md`](../01-research/turkey-compliance.md).

---

## 1. Akış Diyagramı (Müşteri Ödeme)

```
[Müşteri seçer hizmet + zaman]
         │
         ▼
[POST /api/bookings/initiate]
         │ (Booking pending state, Availability slot reserve)
         ▼
[POST /api/payments/checkout]
         │
         ▼
[PaymentService.Initiate(provider="paytr", subMerchantKey, amount, commission)]
         │
         ▼
[PayTR Pazaryeri token + iframe URL]
         │
         ▼
[Müşteri 3DS akışında kart bilgisi girer]
         │
         ▼
[PSP webhook → POST /api/payments/webhook]
         │
         ▼
[Outbox: PaymentSucceededEvent → BookingConfirmEvent + EArchiveQueueEvent]
         │
         ▼
[Booking confirmed · E-Arşiv fatura kuyruğa · Push: kuaföre + müşteriye]
```

**State machine (`Payment` aggregate):**
```
Initiated ──► Authorized ──► Captured ──► Settled
    │            │              │
    ▼            ▼              ▼
  Failed     Cancelled      Refunded (kademeli)
```

---

## 2. `IPaymentProvider` Soyutlaması

```csharp
public interface IPaymentProvider
{
    Task<CheckoutSession> InitiateAsync(InitiateCheckoutRequest request, CancellationToken ct);
    Task<PaymentVerification> VerifyAsync(string paymentId, string transactionId, CancellationToken ct);
    Task<RefundResult> RefundAsync(string paymentId, decimal amount, RefundReason reason, CancellationToken ct);
    Task<SubMerchantOnboardResult> OnboardSubMerchantAsync(SubMerchantData data, CancellationToken ct);
    Task<PayoutSummary> GetPayoutAsync(string subMerchantKey, DateRange range, CancellationToken ct);
    
    string ProviderName { get; }
    PaymentProviderCapabilities Capabilities { get; }
}

public record PaymentProviderCapabilities(
    bool SupportsMarketplace,
    bool SupportsInstallments,
    bool SupportsApplePay,
    bool SupportsGooglePay,
    bool Supports3DS,
    decimal MinTransactionAmount,
    decimal MaxTransactionAmount,
    string SettlementSchedule  // "T+1", "T+3w" vb.
);
```

**Implementations:**
- `PayTrPaymentProvider`
- `IyzicoPaymentProvider`
- `MockPaymentProvider` (test/dev/CI)

`IPaymentProvider` registration (DI):
```csharp
services.AddScoped<IPaymentProvider>(sp => 
    sp.GetRequiredKeyedService<IPaymentProvider>(
        configuration["Payments:DefaultProvider"]));
services.AddKeyedScoped<IPaymentProvider, PayTrPaymentProvider>("paytr");
services.AddKeyedScoped<IPaymentProvider, IyzicoPaymentProvider>("iyzico");
```

---

## 3. Sub-merchant Onboarding (Kuaför)

### 3.1 Bilgiler
| Alan | Tipi | Bireysel | Şahıs | Limited / A.Ş. |
|---|---|---|---|---|
| Ad-soyad / Ünvan | string | ✅ | ✅ | ✅ |
| TCKN / VKN | string | TCKN | VKN | VKN |
| IBAN | string | ✅ | ✅ | ✅ |
| Vergi dairesi | string | — | ✅ | ✅ |
| Adres | string | ✅ | ✅ | ✅ |
| Vergi levhası (PDF/JPG) | file | — | ✅ | ✅ |
| İmza sirküleri | file | — | — | (PayTR'de gerekli değil) |

### 3.2 KYC Akışı
1. **Kuaför mobil panelde "Pazaryeri başvurusu"** ekranına girer.
2. Form doldurur, belgeleri yükler (R2'ye, encrypted).
3. **Kuaforum admin paneli kuyruğuna** düşer (`KycSubmission` entity).
4. Admin manuel kontrol — belge OCR + ESBİS web doğrulama (manuel).
5. Onaylanırsa: `IPaymentProvider.OnboardSubMerchantAsync()` çağrılır.
6. PSP'den `subMerchantKey` döner, `BarberShop` entity'sinde saklanır.
7. Kuaföre push + e-mail: "Pazaryeri başvurun onaylandı."

**KYC SLA:**
- Kuaforum admin onayı: **24-48 saat** (solo dönem).
- PSP onayı: **24-72 saat** (PayTR/Iyzico SLA).

---

## 4. Komisyon Modeli

### 4.1 Hesaplama
```
hizmet_tutar = 500 TL  (kuaförün belirlediği)
kdv = 500 * 0.20 = 100 TL  (zaten dahil)
brüt_işlem = 500 TL  (müşterinin ödediği — ek bedel yok)
platform_komisyon = 500 * 0.10 = 50 TL  (kuaförün kazancından kesilir)
psp_mdr = 500 * 0.0299 = 14.95 TL  (PayTR ortalama)
kuaför_net = 500 - 50 - 14.95 = 435.05 TL
```

> **Önemli not:** KDV kuaförün hizmet faturasında dahildir; kuaför kendi muhasebesinde KDV'yi GİB'e öder. Platform sadece komisyon faturasını kuaföre keser.

### 4.2 Tier
- **Standart:** %10 komisyon, default.
- **Premium:** %5 komisyon + ₺499/ay sabit ücret. (V1.5'a sonraki versiyona)
- **Verified Pro:** %8 komisyon, ek görünürlük. (V1.5'a)

MVP'de **tek tier %10**.

### 4.3 Split Payment
- PSP `Pazaryeri` ürünü ile checkout sırasında split otomatik:
  - Tutar: 500 TL → kuaför sub-merchant'a 450 TL, platform main account'a 50 TL.
  - PSP MDR brüt'ten kesilir, kuaförün payına yansır (PayTR/Iyzico bu modele uygun).

---

## 5. Çift Fatura (E-Arşiv)

```
┌──────────────────────────────────────────────────────────────┐
│  Randevu tamamlanmış (state=Completed)                        │
└────────────────────┬─────────────────────────────────────────┘
                     │
                     ▼
       ┌─────────────────────────────────┐
       │ FATURA #1: Kuaför → Müşteri      │
       │ Hizmet: Saç Kesim                │
       │ Tutar: 500 TL (KDV dahil)        │
       │ KDV: %20 (~83.33 TL)             │
       │ Mükellef: Kuaför VKN              │
       │ Alıcı: Müşteri TCKN/VKN          │
       └─────────────────────────────────┘
                     │ (Nilvera API)
                     ▼
       ┌─────────────────────────────────┐
       │ FATURA #2: Platform → Kuaför     │
       │ Hizmet: Aracılık Komisyonu       │
       │ Tutar: 50 TL (KDV dahil)         │
       │ KDV: %20 (8.33 TL)               │
       │ Mükellef: Kuaforum Ltd. VKN       │
       │ Alıcı: Kuaför VKN                │
       └─────────────────────────────────┘
                     │
                     ▼
        Her iki fatura da Nilvera üzerinden GİB'e iletilir
        Kuaför ve müşteri faturayı e-mail + uygulamadan görür
```

### 5.1 Kuyruk
- `EArchiveOutbox` tablosu: `bookingId`, `payload`, `status`, `attempts`, `lastError`.
- Hangfire job 1 dakikada bir çeker, Nilvera'ya gönderir.
- VUK 509 SLA: **7 gün**. 5. güne kadar otomatik retry; 6. günde admin alert; 7. günde fail-stop ve manuel müdahale.

### 5.2 Hata Modları
- Nilvera 429 (rate limit) → exponential backoff.
- Nilvera 4xx (data hatası) → admin alert, manuel düzeltme.
- Nilvera 5xx → retry.
- Müşteri TCKN doğrulanamaz → fatura "vatandaşa" mod (TCKN: 11111111111 — GİB istisna kuralı). [unverified — needs CPA confirm]

---

## 6. Refund (Kademeli İade)

### 6.1 Politika (Yasal Uyumlu)
| Süre | İade |
|---|---|
| Randevu zamanından **24 saat+ önce** | %100 |
| Randevu zamanından **12-24 saat önce** | %50 |
| Randevu zamanından **0-12 saat önce** | %0 |
| **Kuaför iptal etti** | %100 (kuaföre uyarı + 3 strike sonrası geçici askı) |
| **No-show (müşteri gelmedi)** | %0 + müşteriye 1 strike (3 strike = 30 gün ban) |

### 6.2 Akış
1. Müşteri "İptal Et" → backend kademe hesabı.
2. Eligible refund tutarı hesaplanır.
3. `IPaymentProvider.RefundAsync()` çağrılır.
4. PSP'den ack gelir → `Payment.Refunded` state.
5. **Refund fatura iadesi** Nilvera'ya: orijinal faturanın iptal/iade işlemi (e-Arşiv iade fişi).
6. Müşteriye e-mail + push: "İade işlendi, 3-7 iş gününde kartınıza yansır."

---

## 7. Performans & Hata Hedefi

| Metrik | Hedef |
|---|---|
| Checkout init P95 | < 800ms (PSP roundtrip dahil) |
| Webhook ack P95 | < 200ms |
| E-Arşiv kesim SLA | < 7 gün (yasal), < 1 saat (operasyonel) |
| Refund SLA | < 5 dakika (PSP'ye komut), 3-7 iş günü (banka) |
| Payment success rate | > %92 |
| Webhook delivery success | > %99.5 |

---

## 8. Test Stratejisi

- **Birim test:** `IPaymentProvider` mock + edge case'ler.
- **Entegrasyon test:** PayTR sandbox + Iyzico sandbox + Nilvera sandbox.
- **E2E test:** Detox ile mobil checkout akışı, sandbox PSP.
- **Load test:** k6 ile 100 RPS checkout simulation.

---

## 9. Risk

| Risk | Mitigasyon |
|---|---|
| PSP webhook gelmezse | 2 dakika sonra polling fallback (`VerifyAsync`) |
| Çift fatura'da müşteri TCKN yanlış | Form validation Luhn + GİB API check |
| Nilvera entegratör değişikliği | `IEArchiveProvider` abstraction |
| Refund banka 30 gün üstü | Müşteriye SLA notice + manuel takip |
| Iyzico T+3w'a düşersek | Kuaföre proaktif iletişim + alternatif PSP swap UI |

---

## 10. Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/01-research/payment-infrastructure.md`](../01-research/payment-infrastructure.md)
- [`docs/01-research/turkey-compliance.md`](../01-research/turkey-compliance.md)
- [`docs/09-payment/02-commission-model.md`](02-commission-model.md) (yazılacak)

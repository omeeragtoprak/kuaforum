# RFC-006 — Ödeme Modeli: Kapora (%15) + Tam Ödeme + Salonda Kalan

**Status:** ✅ Accepted — founder onayı 2026-07-24 (brainstorming döngüsü, üç aşamalı karar zinciri)
**İmplementasyon:** tamamlandı 2026-07-30 (mock uçtan uca; canlıya alma checklist'i için [GAP-ANALYSIS P0-5 güncellemesine](../GAP-ANALYSIS.md) bakınız; as-built kayıt: [`04-modules/07-payment.md`](../04-modules/07-payment.md) §14)
**Tarih:** 2026-07-29 (yazım); kararlar 2026-07-24
**Kapsam:** `Kuaforum.Domain/Modules/Payment`, `Kuaforum.Application/Modules/Payment`, `Kuaforum.Api` (checkout + webhook), `mobile/app/booking/` (confirm + payment ekranları), owner Bugün ekranı
**İlgili:** [RFC-005](RFC-005-otp-only-launch.md) (launch scope), `docs/04-modules/07-payment.md` (as-built temel), GAP-ANALYSIS P0-5 (gerçek ödeme zinciri)

---

## Karar

MVP ödeme akışı **iki online ödeme modu** sunar; her **önden rezervasyonlu (scheduled)** randevu online bir ödemeyle başlar, "tamamen salonda öde" seçeneği YOKTUR (tek istisna: walk-in — aşağıda):

1. **Kapora (varsayılan seçili):** Müşteri hizmet bedelinin **%15'ini** online öder, kalanı salonda öder. Oran config-driven: `Payment:DepositRate = 0.15` (hard-code yasak).
2. **Tam ödeme:** Müşteri tamamını online öder (mevcut as-built davranış).

Komisyon (%10) her iki modda da **toplam hizmet bedeli üzerinden** hesaplanır ve online tahsil edilen tutardan karşılanır. Domain invariant'ı: **kapora tutarı ≥ komisyon tutarı** (DepositRate < CommissionRate konfigürasyonu reddedilir).

## Karar zinciri (founder, 2026-07-24)

1. "Ön ödeme zorunlu / kuaför onayı sonrası ödeme / dükkanda ödeme seçeneği" sorusuna founder **dükkanda ödeme seçeneğini** istedi (TR'de "salonda öderim" alışkanlığı — dönüşüm kaygısı).
2. Push-back: salonda ödenen parada komisyon tahsil edilemez + kuaför komisyondan kaçmak için müşteriyi salona yönlendirir (online ödemeyi yamyamlama teşviki). Üç çözümden founder **kapora modelini** seçti (ay sonu komisyon faturası ve komisyonsuz model reddedildi).
3. Oran sorusu: founder **%15 kapora + "tamamını öde" seçeneği** varyantını onayladı (%10'luk varyant 12-24h iptal diliminde — %50 iade — platformu zarara sokabildiği için önerilmedi).

## Alternatifler ve Trade-off

| Alternatif | Artı | Eksi | Sonuç |
|---|---|---|---|
| Yalnız ön ödeme (as-built) | En basit, komisyon garantili | "Salonda öderim" müşterisini kaçırır | Reddedildi |
| Kuaför onayı → ödeme | Kuaföre kontrol | +1 adım, +1-2 gün iş, OwnerConfirm yeniden kurgusu | Reddedildi |
| Salonda ödeme + ay sonu komisyon faturası | Dönüşüm en yüksek | Tahsilat riski, eksik beyan teşviki, muhasebe katmanı | Reddedildi |
| Salonda ödemede komisyon yok | En hızlı | Gelir modeli fiilen ölür | Reddedildi |
| **Kapora %15 + tam ödeme seçeneği** | Komisyon garantili, no-show caydırıcı, TR'de bilinen kalıp | Ödeme UI'da 2 seçenek, kalan-tutar iletişimi gerekir | **Kabul** |

## Tasarım

### Domain (minimal değişiklik)

- `Payment.Kind` enum alanı: `FullOnline | Deposit`. Kapora senaryosunda `Payment.AmountMinor` = kapora tutarı; toplam hizmet bedeli booking'ten okunur.
- **"Salonda ödenecek kalan" türetilir** (`bookingTotal − onlinePaid`) — yeni kolon YOK. Salon ödemesi platformdan akmadığı için ayrıca modellenmez (YAGNI; tek Payment kaydı).
- Komisyon: `CommissionMinor = round(bookingTotal × 0.10)` her iki Kind'da aynı. Kapora tutarı **yukarı yuvarlanır**: `DepositMinor = ceil(bookingTotal × DepositRate)` — küçük tutarlarda yuvarlama kaynaklı invariant ihlali imkânsızlaşır. Invariant yine de domain'de kontrol edilir: `Deposit` kind'da `AmountMinor ≥ CommissionMinor`.
- Refund: mevcut matris (>24h %100 · 12-24h %50 · <12h %0 · reason-bazlı override'lar) **online ödenen tutar üzerinden** aynen işler — `BookingRefundService` + `ProcessBookingRefundJob` zinciri değişmez.
- Walk-in: mevcut ödemesiz davranışında kalır; kapora walk-in'e uygulanmaz.

### API

- `InitiatePaymentCommand`'a `kind` parametresi (validator: enum + Deposit'te invariant kontrolü). Idempotency key mekanizması aynen.
- **`POST /v1/webhooks/paytr`** (yeni, AllowAnonymous + HMAC): imza doğrulama, idempotent completion (aynı callback 2× → tek `CompletePayment`), out-of-order/duplicate tolerans, ham payload'ın audit kaydı. PayTR gerçek callback şeması `[unverified 2026-07-29 — implementasyonda PayTR resmi doc/context7 teyidi]`; mock provider ile uçtan uca çalışır, merchant hesabı gelince config + HMAC secret bağlanır.

### Mobile (müşteri)

- `booking/confirm.tsx`: "Ödeme adımı geliştirme aşamasında" placeholder'ı yerine ödeme seçim kartı — kapora varsayılan seçili, iki tutar net: "Şimdi X TL · Salonda Y TL" / "Tamamını öde: Z TL".
- `booking/payment.tsx` (yeni): WebView checkout iskeleti (PayTR iFrame'e hazır); mock provider'da yerel simülasyon; başarı / hata / zaman aşımı state'leri; 30dk Pending timeout geri sayımı görünür; dönüşte booking detayına.
- Booking detayı: kapora modunda "Salonda ödenecek: Y TL" satırı.

### Owner

- Bugün ekranı + randevu detayında "Salonda tahsil edilecek: Y TL" rozeti. Dashboard "Ciro" tanımı bu RFC'nin kapsamı dışında (ayrı iş, GAP P1).

### Test

- Integration: kapora tutar/komisyon hesabı + invariant reddi; kapora iptal → matris iadesi (kapora üzerinden); webhook idempotency (2× callback → tek completion) + out-of-order; tam ödeme regresyonu.
- Mobile: tutar hesaplama helper'ları jest.

## Why

Komisyon tahsilatını PSP akışına bağlamak (kapora ≥ komisyon) tahsilat riskini sıfırlar, kuaförün "salona yönlendir" teşvikini kırar (kapora her durumda ödenir), müşteriye tam-tutar-önden bariyerini kaldırır. Refund matrisinin online tutar üzerinden değişmeden işlemesi bugünkü oto-refund altyapısını aynen yeniden kullanır.

## How to apply

Implementasyon planı bu RFC'den türetilir (writing-plans). Sıra: domain `Kind` + config + invariant → InitiatePayment genişletme → webhook iskeleti + testler → mobile seçim + WebView ekranları → owner rozeti. `DepositRate` değişiklikleri yalnız config ile; oran değişimi mevcut Pending payment'ları etkilemez (initiate anındaki oran payment'a yazılır).

## Bilinçli dışarıda bırakılanlar

Kayıtlı kart / tek tık (V1.5) · e-Arşiv faturalanabilir kayıt tablosu (ayrı iş, founder Nilvera hesabı) · salon POS entegrasyonu (platform dışı) · kuaför onayı akışı (reddedildi) · İyzico fallback aktivasyonu (V1.5).

# Kullanıcı Akışları (User Journeys)

> 3 ana akış: Müşteri Booking, Kuaför Onboarding, Admin KYC. Detaylı state machine'ler modül spec'lerinde.

---

## 1. Müşteri Booking Akışı (Happy Path)

```mermaid
sequenceDiagram
    actor M as Müşteri
    participant App as Mobile App
    participant API as Backend API
    participant Avail as Availability Engine
    participant PSP as PayTR
    participant Nilv as Nilvera (e-Arşiv)
    participant FCM as FCM Push
    actor K as Kuaför

    M->>App: Aç + giriş yap
    App->>API: GET /barbers?lat=..&lng=..&filter=..
    API-->>App: Yakındaki kuaförler (Meilisearch)
    M->>App: Kuaför seç + hizmet seç + zaman seç
    App->>API: GET /barbers/{id}/availability
    API->>Avail: GetSlots(stylistId, date, services)
    Avail-->>API: Available slots
    API-->>App: Slots
    M->>App: Slot seç + onayla
    App->>API: POST /bookings/initiate
    API->>Avail: ReserveSlot (Redis lock)
    Avail-->>API: ReservationToken
    API-->>App: BookingId + checkoutUrl

    App->>PSP: 3DS iframe (kart bilgisi)
    M->>PSP: Kart + 3DS doğrulama
    PSP-->>App: callback (success/fail)

    PSP->>API: POST /webhooks/payment (signed)
    API->>API: Verify webhook signature + idempotency check
    API->>API: Tx: Booking.Confirm + Payment.Capture + Outbox
    API-->>PSP: 200 OK

    Note over API: Outbox processor 5s sonra
    API->>FCM: Push: kuaföre "Yeni randevu"
    API->>Nilv: Çift fatura kuyruğa
    API->>FCM: Push: müşteriye "Randevu onaylandı"
    
    K->>App: Push notification görür
    K->>App: Detay ekranı, hazırlık
    
    M->>App: Ana ekrandan "Yol tarifi" → native maps deep link
    M->>K: Salon'a varır, hizmet alır
    K->>App: "Hizmet tamamlandı" işaretler
    API->>API: Booking state: Completed
    API->>FCM: Push: müşteriye "Yorum yap"
    M->>App: Yorum + puan
    API->>API: Review created (moderation queue)
```

**Edge case'ler:**
- Slot rezervasyon çakışması (concurrent) → Redis lock retry, başarısızsa "slot kapandı".
- 3DS başarısız → Booking state Pending kalır, 15 dk sonra Availability slot release.
- Webhook gelmezse → 2 dakika sonra polling fallback.
- Kuaför reddederse → Auto refund (kademeli — randevuya 24h+ ise %100).
- Müşteri iptal → Kademeli refund.

---

## 2. Kuaför Onboarding Akışı

```mermaid
sequenceDiagram
    actor K as Kuaför
    participant App as Mobile App / Web
    participant API as Backend API
    participant Admin as Admin (Founder)
    participant PSP as PayTR Pazaryeri

    K->>App: "Kuaför olarak kayıt ol"
    App->>API: POST /auth/register
    API-->>App: User created (BarberOwner pending)
    K->>App: BarberShop bilgileri (ünvan, adres, çalışma saati, hizmet katalog)
    App->>API: POST /barber-shops
    API-->>App: BarberShop created (status=draft)
    K->>App: Belgeleri yükler (vergi levhası, sıhhi ruhsat, MYK belgesi)
    App->>API: POST /barber-shops/{id}/documents
    API-->>App: Documents uploaded (R2 encrypted)
    K->>App: "Pazaryeri için başvur"
    App->>API: POST /barber-shops/{id}/kyc-submit
    API->>Admin: Email + Slack notification (KYC kuyruğa düştü)
    Note over Admin: Admin manuel inceler (24-48h)
    Admin->>API: PATCH /admin/kyc/{id} {status: approved}
    API->>PSP: OnboardSubMerchant
    PSP-->>API: subMerchantKey + status
    API->>API: BarberShop.subMerchantKey saved + status=active
    API->>K: Push + email "Pazaryeri aktif!"
    K->>App: Ana panele girer, ilk randevuyu bekler
```

**Edge case'ler:**
- Belge eksikse: admin reddeder, kuaföre "şu belgeyi tekrar yükle" mesajı.
- PSP red → admin alert, manuel kanal denenir (Iyzico fallback).
- Belge OCR yanlış okuma (V1.5'tan itibaren OCR yardımcısı): manuel düzeltme.

---

## 3. Admin KYC İnceleme Akışı

```mermaid
flowchart TD
    A[Admin paneline gir] --> B[KYC kuyruğu]
    B --> C{Belge tam mı?}
    C -- Hayır --> D[Reject + sebep + push]
    C -- Evet --> E[Belgeleri görüntüle]
    E --> F{ESBİS web doğrulama}
    F -- Eşleşmiyor --> D
    F -- Eşleşiyor --> G{Vergi levhası geçerli mi?}
    G -- Hayır --> D
    G -- Evet --> H{Sıhhi ruhsat süresi?}
    H -- Süresi geçmiş --> D
    H -- Geçerli --> I[KYC onayla]
    I --> J[PSP sub-merchant başvuru otomatik]
    J --> K{PSP cevabı}
    K -- Onay --> L[BarberShop aktif]
    K -- Red --> M[Admin alert + manuel kanal]
    D --> N[Reject reason logged + audit]
```

---

## 4. İptal & İade Akışı

```mermaid
flowchart TD
    A[Müşteri 'İptal' tıklar] --> B[Saat farkı hesabı]
    B --> C{Randevuya kalan süre}
    C -- "> 24h" --> D[100% iade hak]
    C -- "12-24h" --> E[50% iade hak]
    C -- "< 12h" --> F[0% iade hak — uyarı]
    D --> G[Onay ekranı: 100%]
    E --> H[Onay ekranı: 50%]
    F --> I[Onay ekranı: 0% — devam? veya kuaföre ulaş?]
    G --> J[POST /bookings/cancel]
    H --> J
    I --> J
    J --> K[Booking.Cancel + Slot release]
    J --> L[Payment refund — IPaymentProvider.RefundAsync]
    L --> M{PSP ack?}
    M -- Success --> N[Booking state=Cancelled, Payment state=Refunded]
    M -- Fail --> O[Hangfire retry queue + admin alert]
    N --> P[Nilvera: iade fişi/iptal fatura]
    N --> Q[Push + email müşteriye + kuaföre]
```

---

## 5. Walk-in (Simplified) Akışı

```mermaid
sequenceDiagram
    actor K as Kuaför
    actor M as Müşteri
    participant App as Mobile App
    participant API as Backend API

    K->>App: Panelde "Şu an boşum" toggle ON
    App->>API: PATCH /stylists/{id}/walk-in {available: true}
    API->>API: Stylist.walkInAvailable = true (TTL: 60dk auto-off)
    
    M->>App: Keşif ekranında "Açık + Boş" filtresi
    App->>API: GET /barbers?openNow=true&walkInOnly=true
    API-->>App: Yakın + boş kuaförler
    M->>App: Bir kuaför seç + "Hemen yürüyorum"
    App->>API: POST /bookings/initiate (walkIn=true, services=[..])
    API->>API: Booking pending + slot 30dk sonrası rezerve
    Note over M: Müşteri yürür / araçla gider
    M->>K: Salonda görünür
    K->>App: "Müşteri geldi, başlıyorum"
    App->>API: PATCH /bookings/{id}/start
    API->>API: Booking.Start + Stylist.walkInAvailable = false (otomatik)
    K->>K: Hizmet
    K->>App: "Tamamlandı" + ödeme
    [Sonra normal Booking + Payment + Review akışı]
```

**Edge case'ler:**
- 60 dakika walk-in toggle açık kaldıysa otomatik OFF.
- Walk-in slot rezervasyonu 30 dakika içinde müşteri gelmezse → release.
- Kuaför "müşteri geldi" işaretlerse otomatik off (ilk gelen ilk hizmet).

---

## 6. Eksik Akışlar (Modül Spec'lerinde detaylanır)

- **OAuth login (Apple/Google)** — M1 spec.
- **Telefon değişikliği akışı** — M1 spec.
- **KVKK silme talebi** — M2 spec.
- **Yorum moderasyon** — M8 spec.
- **Promo kod kullanımı** (V1.5) — M14 spec.
- **Multi-stylist randevu** (aynı booking'de birden fazla stilist) — M5+M6 spec.

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/04-modules/00-module-list.md`](../04-modules/00-module-list.md)
- [`docs/09-payment/01-payment-architecture.md`](../09-payment/01-payment-architecture.md)

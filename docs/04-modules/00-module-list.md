# Modül Listesi (16 Modül)

> Her modül kendi brainstorming → spec → implementation plan → kod döngüsünden geçer.
> Bu doküman özet/skelet niteliğindedir; detaylı modül spec'leri Phase 1+ döngülerinde `04-modules/<NN>-<modul>.md` olarak yazılır.

---

## Modül Özet Tablosu

| # | Modül | Faz | Bağımlılık | MVP Önceliği |
|---|---|---|---|---|
| M1 | Auth & Authorization | P1 | — | MUST |
| M2 | User & Role Management | P1 | M1 | MUST |
| M3 | Geographic Data | P1 | — | MUST |
| M4 | Barber Profile & Verification | P1 | M2, M3 | MUST |
| M5 | Availability Engine | P1 | M4 | MUST (en kritik) |
| M6 | Booking Lifecycle | P2 | M5 | MUST |
| M7 | Payment & Commission | P2 | M6 | MUST |
| M8 | Review & Moderation | P2 | M6 | MUST |
| M9 | Search & Discovery | P2 | M3, M4, M8 | MUST |
| M10 | Maps & Navigation | P2 | M3 | MUST |
| M11 | Notification | P2 | M1, M6 | MUST |
| M12 | Admin Panel | P2 (minimal) → P3 (full) | All | MUST minimal, SHOULD full |
| M13 | Analytics & Reporting | P3 | M6, M7 | SHOULD |
| M14 | Promo & Referral | P3 | M7 | SHOULD |
| M15 | Loyalty & Rewards | P3 (V2) | M6 | COULD |
| M16 | Internationalization (i18n) | P3 (V2) | All | COULD |

---

## Modül Detayları

### M1 — Auth & Authorization
- **Kapsam:** Telefon + e-posta kayıt, OTP, Apple/Google OAuth, JWT (15dk) + Refresh (30g) + device-bound, RBAC (Customer/BarberOwner/Stylist/Admin).
- **Stack:** ASP.NET Identity + JwtBearer + OAuth handlers + NetGSM SMS.
- **Kritik konular:** Token rotation, refresh token theft detection, multi-device sign-in, KVKK aydınlatma metni + parçalı açık rıza akışı.
- **Edge case:** OTP brute-force protection, telefon numarası değişimi (yeniden doğrulama), hesap silme + 30 günlük bekleme.

### M2 — User & Role Management
- **Kapsam:** Kullanıcı profili (ad, foto, tercihler), rol atama, profil silme/anonimleştirme (KVKK).
- **Critical:** **Hassas veri** (alerji notu, saç-cilt durumu) için özel m.6 (KVKK) işleme akışı.

### M3 — Geographic Data
- **Kapsam:** TR il/ilçe/mahalle hierarchy bootstrap (NVI), PostGIS yapı, geocoding entegrasyonu.
- **Veri kaynağı:** Nüfus İdaresi açık veri seti (ücretsiz). Manuel düzeltme paneli (admin).
- **Critical:** Spatial index (`GIST`), nearest-neighbor sorgu performansı.

### M4 — Barber Profile & Verification
- **Kapsam:** İşletme (BarberShop) + stilist (Stylist) profil ayrımı, hizmet katalog, çalışma saatleri.
- **Verification:** Belge upload (vergi levhası, sıhhi müessese ruhsatı, MYK Seviye 4 belgesi), admin manuel onay.
- **Edge case:** Çalışan ekleme/çıkarma, hizmet saat farklılığı (stilist bazlı override).

### M5 — Availability Engine
- **Kapsam:** Slot rezervasyonu (concurrency-safe), izin/tatil/blok, hizmet süresi bazlı slot dolduruculuk.
- **Critical:** Race condition kontrolü (Redis distributed lock), takvim performansı (3 ay önden cache), yarım slot/buffer time (10 dk hazırlık).
- **En zor modül.** İçindeki algoritma:
  - Stilistin haftalık working hours
  - Bu haftadaki onaylı izin/tatil
  - Bu zamanda var olan randevular
  - Hizmet süresi (örn. 30dk kesim, 60dk boya, 90dk iki birden) → slot toplam süre
  - **Çıktı:** mevcut slot listesi (dakika hassasiyetli).

### M6 — Booking Lifecycle
- **Kapsam:** State machine (pending/confirmed/started/completed/cancelled/no-show), multi-service booking, kademeli iptal politikası.
- **Critical:** Booking + Payment + Availability transaction atomicity (Outbox pattern).

### M7 — Payment & Commission
- **Kapsam:** PayTR Pazaryeri primary (T+1), Iyzico fallback, kademeli iade, e-Arşiv çift fatura (Nilvera).
- **Critical:** `IPaymentProvider` abstraction, webhook idempotency, refund idempotency.
- **Detay:** [`docs/09-payment/01-payment-architecture.md`](../09-payment/01-payment-architecture.md).

### M8 — Review & Moderation
- **Kapsam:** 5 yıldız + metin yorum, moderasyon (otomatik küfür filtresi + manuel review queue), kuaför reply (her yoruma 1).
- **Critical:** Sahte yorum tespiti, fake-rating manipulation (örn. yeni hesap çoğunluk).
- **Edge case:** Kuaför kendi profilini yorumlayamaz, müşteri sadece tamamlanmış randevuyu yorumlayabilir.

### M9 — Search & Discovery
- **Kapsam:** Meilisearch (Türkçe tokenizer), filtre (cinsiyet, mahalle, fiyat, açık, walk-in, rating, mesafe, hizmet türü), liste ↔ harita view.
- **Walk-in simplified mode:** kuaför "şu an boşum" toggle, kullanıcı tarafında "açık+boş" filtresi.
- **Performans:** P95 < 500ms.

### M10 — Maps & Navigation
- **Kapsam:** Geocoding (Google Maps), yol tarifi handoff (Apple Maps / Google Maps deep link), harita üzerinde keşif.
- **Critical:** Maps SDK quota yönetimi (geocoding ücretli).

### M11 — Notification
- **Kapsam:** FCM push, NetGSM SMS, Postmark e-mail, kullanıcı tercih merkezi.
- **Critical:** Bildirim tercihi parçalı (randevu hatırlatma vs marketing ayrı toggle), GDPR/KVKK opt-in.
- **Akışlar:** Randevu kabul/red, randevu hatırlatma (24h, 2h, 30dk), refund, KYC sonucu, marketing campaign.

### M12 — Admin Panel
- **MVP minimal:** Kullanıcı arama, KYC kuyruğu, randevu listesi, refund onayı.
- **V1.5 full:** Gelişmiş ihtilaf yönetimi, finansal rapor, OCR otomasyonu, KVKK silme talebi yönetimi, audit log explorer.
- **Stack:** Next.js 15, shadcn/ui, TanStack Query.

### M13 — Analytics & Reporting (V1.5)
- **Kapsam:** Kuaför dashboard (gelir, popüler hizmet, müşteri retention), admin dashboard (GMV, MAU, churn, NPS).
- **Stack:** Aggregated metrics in Postgres + Metabase (self-hosted) veya custom dashboard.

### M14 — Promo & Referral (V1.5)
- **Kapsam:** Promo kod (kullanım sayısı, indirim oranı/sabit, geçerlilik), referral (X arkadaşına gönder, ilk randevuda Y TL kredi).
- **Critical:** Anti-abuse (aynı kart, aynı device fingerprint), audit trail.

### M15 — Loyalty & Rewards (V2)
- **Kapsam:** Puan biriktirme (her randevuda), seviye (bronze/silver/gold), ödül (indirim, ücretsiz hizmet).
- **Beklemede:** MVP sonrası 6+ ay.

### M16 — Internationalization (V2)
- **Kapsam:** EN, AR (RTL), RU, opsiyonel DE.
- **Beklemede:** TR-only MVP yeter, EN ilk tutucu (English UI tourist için).

---

## Modül Boundary Discipline

- Her modülün **public contract**ı sadece `Kuaforum.Modules.{Module}.Contracts` projesinde tanımlı (interface + DTO).
- Modüller arası bağımlılık **MediatR INotification** ile in-process event.
- Cross-module domain leakage YASAK.
- Bir modülün içsel entity'si başka modülde import EDİLMEZ.

---

## Modül Spec Şablonu

Her modül için `04-modules/<NN>-<modul>.md` aşağıdaki yapıda:

```
1. Amaç (Purpose)
2. Stakeholder'lar
3. Kullanıcı Hikâyeleri (User Stories)
4. Functional Requirements
5. Non-functional Requirements
6. Veri Modeli (Entity, Value Object, Domain Event)
7. API Endpoint'leri
8. State Machine (varsa)
9. Edge Case'ler
10. Test Stratejisi
11. Risk
12. Bağımlılık
13. Acceptance Criteria
```

Şablon dosyası: `docs/04-modules/_template.md` (Phase 1 başında oluşturulacak).

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/02-requirements/01-mvp-scope.md`](../02-requirements/01-mvp-scope.md)
- [`docs/13-project-management/01-roadmap.md`](../13-project-management/01-roadmap.md)

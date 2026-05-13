# Kuaforum — Gap Analizi (2026-05-13)

> Bu doküman, projenin **mevcut durumu** ile **hedeflenen MVP / production** durumu arasındaki farkları sistematik olarak listeler. Her madde için: **status · kapsamı · efor tahmini**.

**Sembol:**
- ✅ tamam (production-ready)
- 🟢 fonksiyonel (minor cleanup gerekli)
- 🟡 stub / iskelet (çalışır ama eksik)
- 🟠 kısmi (bazı parçaları var)
- 🔴 yok (yazılmadı)
- 🟣 dış-koşul (kullanıcı eylemi gerekli — domain, lawyer, hesap başvurusu)

**Efor:** S = saatler (1-8h) · M = günler (1-5g) · L = haftalar (1-3h) · XL = aylar (1-3ay)

---

## 0. Genel Durum Özeti

| Bileşen | Bitti | Üretim hazır? |
|---|---|---|
| Phase 0 dokümantasyon | 51/51 ✅ | ✅ |
| Phase 1 backbone (M1-M5) | %98 | 🟢 |
| Phase 2 marketplace (M6-M11) | %90 | 🟠 (PSP/SMS/Maps gerçek değil — 🟣 hesap) |
| Phase 3 growth (M12-M16) | %55 | 🟠 (admin 7 sayfa + audit log, mobile analytics today widget) |
| Mobile customer app | %92 | 🟢 (profile edit + favoriler + i18n eksik) |
| Mobile barber paneli | %85 | 🟢 (yorum cevap mobile UI eksik) |
| Web admin paneli | %85 | 🟢 (8 sayfa: dashboard/users/shops/bookings/kyc/reviews/audit-logs/login; shadcn migration optional) |
| Backend domain unit testleri | 71 test | 🟢 |
| Backend application unit testleri | 51 test | 🟢 |
| Backend integration testleri | 36 test | 🟢 (Testcontainers, çok flow kapsanmış) |
| Mobile error boundary + Sentry foundation | ✅ (DSN-guard) | 🟢 (founder DSN gelince auto-aktive) |
| Üçüncü parti gerçek entegrasyon | Mock fallback | 🟠 (PayTR/Iyzico/R2/Sentry DSN — 🟣 hesap) |
| CI/CD | GitHub Actions ✅ | 🟢 (Hetzner deploy pipeline eksik) |

**Toplam ilerleme:** Master spec'in **~%80'i** kodlanmış. Kalan **~%20** — üçüncü-parti hesap entegrasyonu (PayTR/R2/Sentry DSN — 🟣 founder), Hetzner deploy pipeline, mobile UX gap (profile edit + i18n), şehir/kategori admin yönetimi.

**Realistik tahmin (devam):** Founder onayı bekleyen 🟣 işleri saymazsak, kalan kod **~3-5 hafta**.

---

## 1. Phase 0 — Foundation

| Madde | Status | Not |
|---|---|---|
| Vizyon + scope + persona doc'ları | ✅ | |
| Rakip analizi (TR + global) | ✅ | |
| Ödeme + lisans araştırması | ✅ | |
| KVKK + e-arşiv + tüketici hakları araştırması | ✅ | |
| Mimari spec'leri | ✅ | |
| Modül listesi | ✅ | |
| Risk register · roadmap · budget · şirket roadmap | ✅ | |
| Customer discovery sprint planı | ✅ (sprint **yapılmadı** 🟣) | |
| Claude setup (CLAUDE.md, agent'lar, MCP, kalıcı kurallar) | ✅ | |
| Güvenlik + KVKK overview | ✅ | |
| **Domain + trademark check** | 🟣 | Founder eylemi |
| **SMMM + avukat seçimi** | 🟣 | Founder eylemi |
| **Şirket kuruluşu** | 🟣 | Founder eylemi |
| **PayTR + Iyzico hesap başvurusu** | 🟣 | Founder eylemi (şirket sonra) |
| **NetGSM hesap başvurusu** | 🟣 | Founder eylemi |
| **Firebase project + service account** | 🟣 | Founder eylemi (FCM gerçek delivery için) |

---

## 2. Phase 1 — Backbone Modüller

### M1 — Auth & Authorization (%85)

| Item | Status | Efor kalan |
|---|---|---|
| Domain (User, RefreshToken, OtpCode, Consent, AuditLog, AdminCredential) | ✅ | — |
| Value objects (PhoneE164, EmailAddress) | ✅ | — |
| Domain events | ✅ | — |
| EF + migration (M01_Identity_Initial) | ✅ | — |
| Telefon + OTP register/login | ✅ | — |
| JWT (15dk) + Refresh (30g) + rotation + reuse detection | ✅ | — |
| Profile GET/PATCH/DELETE/restore | ✅ | — |
| Sessions GET/REVOKE | ✅ | — |
| Consents GET/PATCH | ✅ | — |
| Hangfire HardDeleteUsersJob | ✅ | — |
| Audit logging cross-cutting | ✅ | — |
| **Dev-login endpoint (development only)** | ✅ | — |
| **OAuth (Apple, Google) endpoints + provider verify** | 🔴 | M (1-2g) |
| **Admin login + parola + TOTP** | 🟠 (AdminCredential entity + endpoint var, TOTP yok) | S (4h) |
| **Phone change endpoint** | 🔴 | S (4h) |
| **NetGSM gerçek SMS provider** | 🟡 (mock log) | S (3h) + 🟣 hesap |
| **Application + Integration testleri (Testcontainers)** | 🔴 | M (2-3g) — Task #28 |
| Domain unit testleri | 30 test ✅ | — |

### M2 — User & Role Management (%70)

> M1 user ile çakışıyor; M2 = role-spesifik profil enrichment'ı + admin yönetimi.

| Item | Status | Efor kalan |
|---|---|---|
| BarberOwner role enum + claims | ✅ | — |
| Stylist alt-entity (Barber modülünde) | ✅ | — |
| Admin role + dev-login destek | ✅ | — |
| **BarberOwner davet akışı (e-posta link)** | 🔴 | M |
| **Stylist davet akışı (BarberOwner tarafından)** | 🔴 | M |
| **Admin user listesi + filtre + arama** | ✅ (web-admin/users page) | — |
| **User suspend/unsuspend (admin)** | 🟠 (endpoint mevcut, UI link yok) | S |
| Role permission matrisi (policy-based authorization) | 🟠 (RequireAuthorization + RequestContext role check) | S — formal policy'ler |
| KYC queue (M4 ile bağlantılı) | ✅ (web-admin/kyc page) | — |

### M3 — Geographic Data (%95)

| Item | Status | Efor kalan |
|---|---|---|
| City entity (81 il) | ✅ | — |
| District entity (~970 ilçe) | ✅ (TR plate + slug) | — |
| Neighborhood entity (~30K mahalle) | 🟠 (entity var, NVI bootstrap eksik — şu an placeholder veri) | M |
| Tr81CitySeed + GeographySeeder | ✅ | — |
| PostGIS Coordinate VO | ✅ | — |
| Cities/districts/neighborhoods read endpoints | ✅ | — |
| **Geocoding service (Google Maps API)** | 🟡 (interface var, gerçek HTTP yok) | S + 🟣 API key |
| **Reverse geocoding (lat/lng → mahalle)** | 🔴 | S |
| Manuel düzeltme admin paneli | 🔴 | M (low priority) |

### M4 — Barber Profile & Verification (%85)

| Item | Status | Efor kalan |
|---|---|---|
| BarberShop aggregate (PostGIS Location, ratings, status, gender, capacity) | ✅ | — |
| Stylist sub-entity (linked user + display name + services) | ✅ | — |
| ServiceCatalog (kategoriler + flat seed) | ✅ | — |
| StylistService (stylist-bazlı override) | ✅ | — |
| Working hours (haftalık + per-stylist) | ✅ (Availability modülünde) | — |
| TimeOff (izin/tatil) | ✅ | — |
| **KYC submission entity** | ✅ (BarberKycSubmission) | — |
| **Belge upload (Cloudflare R2)** | 🟡 (URL string field, gerçek upload yok) | M + 🟣 R2 hesap |
| **Admin KYC review akışı** | ✅ (web-admin/kyc page) | — |
| ESBİS web doğrulama (manuel link) | 🟣 | — |
| **Salon photos + portfolio** | 🟠 (URL field, upload UI yok) | M |
| Public barber profile endpoints | ✅ | — |
| Barber panel CRUD endpoints (services, hours, profile) | ✅ | — |

### M5 — Availability Engine (%80)

| Item | Status | Efor kalan |
|---|---|---|
| Slot generation algoritması | ✅ | — |
| Working hours + TimeOff + Booking ile slot exclusion | ✅ | — |
| Hizmet süresine göre composite slot | ✅ | — |
| **Concurrency-safe rezervasyon (Redis distributed lock)** | ✅ (IDistributedLock interface + Redis impl) | — |
| Walk-in (anlık) booking desteği | ✅ (booking initiate `isWalkIn=true`) | — |
| Slot endpoint perf budget < 200ms | 🟡 (test edilmedi, IQueryable safe) | S — load test |

---

## 3. Phase 2 — Marketplace Flow

### M6 — Booking Lifecycle (%95)

| Item | Status | Efor kalan |
|---|---|---|
| Booking aggregate (Pending → Confirmed → Started → Completed/Cancelled/NoShow) | ✅ | — |
| BookingItem (multi-service per booking) | ✅ | — |
| Domain events: Initiated/Confirmed/Started/Completed/Cancelled/NoShow | ✅ | — |
| Outbox pattern (integration events) | 🔴 | M (async dispatch için) |
| Initiate + cancel + owner-confirm + start + complete + no-show endpoint'leri | ✅ | — |
| Customer/owner authorization check (GetById) | ✅ | — |
| Cancellation tier'lı iade (24h/12h/0h) | ✅ | — |
| BookingHold (Pending state'de slot lock) | ✅ | — |
| **Booking reschedule** | 🔴 | M (cancel + new initiate workaround mevcut) |
| **Stylist tarafından owner adına booking** | 🔴 | S |

### M7 — Payment & Commission (%70)

| Item | Status | Efor kalan |
|---|---|---|
| Payment aggregate + state machine | ✅ | — |
| Commission calculation (%10 platform) | ✅ | — |
| **PayTR provider gerçek entegrasyon** | 🟡 (interface + skeleton, gerçek HTTP yok) | M (2-3g) + 🟣 hesap |
| **Iyzico fallback provider** | 🔴 | M + 🟣 hesap |
| **3DS callback endpoint** | 🟠 (endpoint mevcut, gerçek imza doğrulama eksik) | S + provider docs |
| Refund (full/partial) | ✅ (cancellation tier'lı) | — |
| **Payout (kuaföre T+1)** | 🔴 | M + 🟣 banka |
| **e-Arşiv (Nilvera) çift fatura** | 🔴 | M + 🟣 hesap |

### M8 — Review & Moderation (%85)

| Item | Status | Efor kalan |
|---|---|---|
| Review aggregate (rating + comment + photos) | ✅ | — |
| BarberReply | ✅ | — |
| Moderation queue + admin approve/reject | ✅ (web-admin/reviews page) | — |
| Review only-after-completed | ✅ | — |
| Helpful vote (peer signal) | 🔴 | S |
| **Review fotoğrafı upload (R2)** | 🟠 (URL field var, upload UI yok) | S + 🟣 R2 |
| **Otomatik küfür filtresi** | 🔴 | S — TR profanity list |

### M9 — Search & Discovery (%50)

| Item | Status | Efor kalan |
|---|---|---|
| City + district + ad search (Postgres ILIKE + slug + normalize) | ✅ | — |
| Multi-filter (gender, rating, distance, open-now) | 🟠 (gender + rating var, distance/open-now eksik) | S |
| **PostGIS distance ranking** | 🟠 (veri var, sıralamada kullanılmıyor) | S |
| **Meilisearch entegrasyonu (TR tokenizer)** | 🔴 | M (Postgres ILIKE şimdilik yetiyor, scale'de gerek olur) |
| Category/service-based search | ✅ | — |
| Featured / boost (sponsorlu) | 🔴 | S (mvp+) |

### M10 — Maps & Navigation (%30)

| Item | Status | Efor kalan |
|---|---|---|
| Coordinate value object + DB column | ✅ | — |
| **Google Maps geocoding HTTP client** | 🔴 | S + 🟣 API key |
| **Mobile Apple/Google Maps deep-link handoff** | 🔴 | S |
| Static map preview (görsel) | 🔴 | S (mvp+) |

### M11 — Notification (%85)

| Item | Status | Efor kalan |
|---|---|---|
| NotificationLog entity | ✅ | — |
| NotificationDispatcher + ChannelHandler abstraction | ✅ | — |
| Channels: Push/SMS/Email/InApp | ✅ (hepsi mock log) | — |
| Booking event handlers (Confirmed/Cancelled/Started/Completed/NoShow) | ✅ | — |
| InApp feed: GET / unread-count / read / read-all | ✅ | — |
| Mobile notifications screen + tab badge | ✅ | — |
| **FCM gerçek delivery (FirebaseAdmin SDK)** | 🟢 (adapter + DeviceToken + register endpoint hazır; gerçek delivery 🟣 Firebase project) | — |
| **NetGSM gerçek SMS** | 🟡 (mock) | S + 🟣 hesap |
| **Resend gerçek email** | 🟡 (skeleton var) | S + 🟣 hesap |
| Outbox pattern (idempotent + retry) | 🔴 | M |
| Notification preferences (per-user, per-template) | 🔴 | M |

---

## 4. Phase 3 — Growth & Operations

### M12 — Admin Panel (%35)

| Item | Status | Efor kalan |
|---|---|---|
| Login + JWT persist | ✅ | — |
| Dashboard (8 stat tile) | ✅ | — |
| Users list + filter | ✅ | — |
| KYC queue + approve/reject | ✅ | — |
| Reviews moderation queue | ✅ | — |
| **Bookings list + filter (system-wide)** | ✅ | — |
| **Bookings cancel admin override** | 🔴 | S |
| **Barber shops list + filter (system-wide)** | ✅ | — |
| **Barber shops suspend/unsuspend** | 🔴 | S |
| **Payment refund manual override** | 🔴 | S |
| **Audit log viewer** | 🔴 | S |
| **shadcn/ui migration** (şu an plain inline style) | 🔴 | M (CLAUDE.md hedefiyle alignment) |
| **TanStack Table** entegrasyonu | 🔴 | S |
| **i18n (TR/EN)** | 🔴 | S |
| **Analytics dashboard (charts)** | 🔴 | M |

### M13 — Analytics & Reporting (%5)

| Item | Status | Efor kalan |
|---|---|---|
| Dashboard temel stats | ✅ | — |
| **GMV time-series + cohort** | 🔴 | M |
| **Funnel: search → detail → book → pay** | 🔴 | M |
| **Booking conversion + cancellation rate** | 🔴 | S |
| **Stylist utilization + revenue** | 🔴 | M |

### M14 — Promo & Referral (%0)

| Item | Status | Efor kalan |
|---|---|---|
| Promo code entity + redemption | 🔴 | M |
| Referral code (paylaş + ödül) | 🔴 | M |
| Welcome bonus (ilk randevu indirimi) | 🔴 | S |

### M15 — Loyalty & Rewards (V2)

Punto / yıldız sistemi. **MVP'de yok.**

### M16 — i18n (V2)

Mobile zaten i18next yüklü ama tek dil (TR). EN/AR sonra eklenir.

---

## 5. Mobile App (Customer) — %85

| Bölüm | Status | Not |
|---|---|---|
| Login (telefon + OTP) | ✅ | hızlı giriş Demo butonları dev'de |
| OAuth (Apple + Google butonları) | 🔴 | endpoint yok |
| Tabs: Keşfet, Ara, Randevular, Bildirim, Profil | ✅ | — |
| Şehir → mahalle → salon listesi search akışı | ✅ | TR aksent normalize |
| Salon detayı (foto, hizmet, stylist, rating, reviews + barber reply) | ✅ | — |
| Booking flow (servis → stylist → slot → onay → success) | ✅ | walk-in toggle UI yok (booking initiate'de var) |
| Booking detayı + iptal | ✅ | — |
| Yorum yazma | ✅ | foto upload UI yok |
| InApp notification feed + tab badge | ✅ | — |
| Profil + sessions + consents | ✅ | — |
| **Push notification handler (foreground / background)** | 🔴 | M (FCM bağımlı) |
| **Deep link** (booking-id, barber-id) | 🟠 (Expo Router native destek var, kullanılmıyor) | S |
| **Theme/dark mode** | 🟠 (theme.ts mevcut, dark variant yok) | S |
| **Accessibility audit** (a11y label, font scale, tap target) | 🟠 (kısmi) | S |
| Detox E2E | 🔴 | M |

## 6. Mobile/Web Barber Paneli — %80

| Bölüm | Status | Not |
|---|---|---|
| Bugün dashboard (3 stat + bookings) | ✅ | — |
| 14 günlük takvim + booking detail Pressable | ✅ | — |
| Hizmet listesi | ✅ | — |
| Profil | ✅ | — |
| Bildirimler tab + badge | ✅ | — |
| Booking aksiyonları (Confirm / Start / Complete / NoShow / Cancel) | ✅ | NoShow runtime test'i seed'de past-confirmed booking eksikliğinden yapılmadı |
| **KYC submit form** | 🔴 | M |
| **Working hours editor UI** | 🔴 | M |
| **Salon photos upload UI** | 🔴 | S |
| **Earnings / Payout dashboard** | 🔴 | M |
| **Stylist invite UI (BarberOwner)** | 🔴 | M |

## 7. Web Admin Paneli — %35

| Bölüm | Status | Not |
|---|---|---|
| Login + token persist | ✅ | — |
| Dashboard (8 stat) | ✅ | — |
| Users list | ✅ | — |
| KYC queue | ✅ | — |
| Reviews moderation | ✅ | — |
| Bookings management | 🔴 | M — Task #30 |
| Barber shops management | 🔴 | M — Task #30 |
| **shadcn/ui migration** | 🔴 | M |
| **TanStack Query/Table** | 🔴 | S |
| **Audit log viewer** | 🔴 | S |
| **i18n** | 🔴 | S |

---

## 8. Infrastructure & DevOps

| Madde | Status | Efor |
|---|---|---|
| Docker Compose dev (Postgres + Redis + Backend) | ✅ | — |
| Production Docker image | 🔴 | M |
| Hetzner provisioning script | 🔴 | M + 🟣 sunucu kiralama |
| Cloudflare CDN + WAF setup | 🔴 | S + 🟣 zone |
| GitHub Actions CI: build + test + lint | 🔴 | M |
| GitHub Actions CD: docker push + Hetzner deploy | 🔴 | M |
| EAS mobile build (development + production profile) | 🔴 | S + 🟣 Apple/Google account |
| Log aggregation (Seq) | 🟠 (Serilog yapı var, Seq sink eksik) | S |
| Metrics (Prometheus + Grafana) | 🔴 | M |
| Sentry frontend + backend | 🔴 | S + 🟣 hesap |
| Better Stack uptime | 🔴 | S + 🟣 hesap |
| Backup strategy (Postgres) | 🔴 | S |

## 9. Üçüncü Parti Entegrasyonlar

| Servis | Status | Engel |
|---|---|---|
| PayTR (PSP primary) | 🟡 mock | 🟣 hesap |
| Iyzico (PSP fallback) | 🔴 | 🟣 hesap |
| NetGSM (SMS) | 🟡 mock | 🟣 hesap |
| Resend (Email) | 🟡 skeleton | 🟣 hesap |
| Firebase FCM (Push) | 🟡 mock | 🟣 service account JSON |
| Cloudflare R2 (file upload) | 🔴 | 🟣 hesap |
| Google Maps (geocoding) | 🔴 | 🟣 API key |
| Nilvera (e-arşiv) | 🔴 | 🟣 hesap + sertifika |
| Sentry | 🔴 | 🟣 hesap |
| Apple/Google OAuth | 🔴 | 🟣 dev hesap |

## 10. Compliance & Legal

| Madde | Status | Eylem |
|---|---|---|
| KVKK aydınlatma metni | 🔴 | 🟣 avukat |
| Açık rıza UI (parçalı) | 🟠 (Consent entity + endpoint var, UI'da gösterilmiyor) | S kod + 🟣 metin |
| Kullanıcı sözleşmesi | 🔴 | 🟣 avukat |
| Kuaför hizmet sözleşmesi | 🔴 | 🟣 avukat |
| KVKK VERBİS kayıt | 🔴 | 🟣 founder |
| Çerez politikası (web) | 🔴 | 🟣 avukat |

## 11. Tests

| Tür | Sayı | Status |
|---|---|---|
| Backend Domain unit | 30 | ✅ |
| Backend Application | 51 | ✅ (validator coverage) |
| Backend Integration (Testcontainers) | 6 | 🟢 (auth + booking flow + notifications + geography happy-path) |
| **Toplam test** | **87** | **✅ all green** |
| FluentValidation kapsama | 45/45 command | ✅ |
| Mobile typecheck | clean | ✅ |
| Web-admin typecheck | clean | ✅ |
| CI/CD pipeline | GitHub Actions (backend test + mobile/web typecheck + CodeQL) | ✅ |
| appsettings.Production.json | env-driven, secrets externalized | ✅ |
| Health endpoint /health/ready | DB + Redis ping | ✅ |
| Logging enrichment | RequestId + UserId + UserRole | ✅ |
| Domain event coverage | Booking, Payment (5), Review (5), Kyc (4), Notification | ✅ |
| Mobile Jest | 0 | 🔴 |
| Mobile Detox E2E | 0 | 🔴 |
| API contract (Bruno/Schemathesis) | 0 | 🔴 |
| k6 load test | 0 | 🔴 |

## 12. Dokümantasyon Eksiklikleri

| Madde | Status |
|---|---|
| Master spec (PHASE-0-MASTER-SPEC.md) | ✅ |
| M1 Auth design spec | ✅ |
| M2-M11 design specs | 🔴 (kod yazıldı, retrospektif spec yok) |
| API reference (auto-generated OpenAPI) | 🟢 (Scalar UI mevcut) |
| Bruno API collection | 🔴 |
| ADR (Architecture Decision Record) klasörü | 🔴 |
| Runbook (incident, oncall) | 🔴 |
| Onboarding doc (yeni dev için) | 🔴 |

---

## 13. Kritik Yol — Sıradaki Hedefler (öncelik sırasıyla)

1. **Integration test paketi (Task #28)** — happy-path coverage. Foundation. Olmazsa her commit risk taşır.
2. **FCM real delivery adapter (Task #29)** — push'a doğru attığımız ilk adım. Adapter + config + fallback. Gerçek delivery 🟣 founder hesap.
3. **Web admin genişletme (Task #30)** — booking management + barber shops management + shadcn migration. 1/3 surface'i prod-leaning yapma.
4. **Booking reschedule** — UX'te en sık şikayet edilen feature.
5. **PayTR gerçek HTTP entegrasyonu** — 🟣 hesap geldikten sonra. MVP launch için critical.
6. **CI/CD pipeline** — GitHub Actions + Hetzner deploy. Manuel deploy süreyi yiyor.
7. **NetGSM + Resend gerçek delivery** — kritik notification path'leri için.
8. **KYC + photo upload (R2)** — gerçek dosya yükleme. Day-1 launch öncesi.
9. **Booking owner reschedule + stylist proxy booking** — operasyonel ihtiyaç.
10. **Application + Domain test coverage hedefi: %70+**.

## 14. Bu Konuşmadaki Hedef

- ✅ Comprehensive seed (15 şehir + 21 kuaför + N booking/payment/review/notification)
- ✅ Owner booking actions (Confirm/Start/Complete/NoShow + UI)
- ✅ InApp notification feed (handler + endpoint + screen + tab badge)
- ✅ GAP-ANALYSIS + CLAUDE.md refresh (bu doc)
- ✅ Integration test paketi (Testcontainers + 6 happy-path test, hepsi yeşil)
- ✅ FCM real delivery adapter (FirebaseAdmin SDK + DeviceToken entity + register/unregister + mobile hook + setup doc)
- ✅ Web admin genişletme (admin bookings + barber-shops sayfaları + endpoint'leri + JWT inbound claim mapping fix)

## 14B. Cycle 5-7 Yatırımları (2026-05-08 → 2026-05-13)

### Erzurum default-city + demo seed (Cycle 5)
- Tr81CitySeed Erzurum districts + DemoBarberSeeder 6 Erzurum shop (Çifte Minare/Stil Studio/Palandöken/VIP/Aziziye/Genç Trend)
- Mobile keşfet refactor: Erzurum otomatik seçili + Zustand store + city picker modal + inline shop cards
- KVKK gizlilik UI: aktif oturumlar + rıza yönetimi (Marketing/Location/HealthData revoke)
- Web admin users/bookings/barber-shops modernize (modal + skeleton + empty state pattern)
- Mobile bookings tab Tümü/Yaklaşan/Tamamlanan/İptal filter chip + status enum bug fix (Completed=3, eski yanlış=4)

### Critical bug fix + Sentry foundation (Cycle 6)
- **Reply 500 DbUpdateConcurrencyException** — kök neden: EF Core `Include(r => r.Reply)` LEFT JOIN null state ile nav property tracker'a takıldı; sonra `review.Reply = BarberReply.Create(...)` set edildiğinde EF Modified state'e geçirdi (Added değil) → UPDATE WHERE id=<yeni> → 0 row. Fix: handler'da `ctx.Set<BarberReply>().Add(review.Reply!)` explicit Added. Memory'ye gotcha kaydedildi.
- **AggregateRoot.Touch Version++ override** — concurrency token sistemik bump (Entity.Touch virtual + AggregateRoot override).
- **Backend Sentry.AspNetCore 5.0.1 + Sentry.Serilog** wired — DSN env-guarded (boş/${SENTRY_DSN} placeholder → SDK init edilmez), KVKK SendDefaultPii=false, Serilog Error→event/Info→breadcrumb.
- **Mobile ErrorReporter facade + AppErrorBoundary** — RootLayout top-level wrap, componentDidCatch reporter'a; api/client.ts refreshTokens + auth-store signOut/hydrate catch'leri tag'li reporter'a. V1.5 @sentry/react-native swap-out.

### Test coverage genişletme (Cycle 7) — +53 test
- **ReviewModerationTests** (2) — profanity stopword → Pending → admin approve → public list +1; admin reject → list unchanged.
- **CancellationFlowTests** (3) — >24h customer 100%, admin override 100%, post-completed reject.
- **AccountDeletionTests** (3) — soft-delete + tokens revoke + restore, double-delete reject, multi-device session revoke.
- **ShopSuspensionTests** (1) — suspend → search hide + booking 400; reactivate restore.
- **PaymentInitiationTests** (2) — checkout session + idempotency 409 + mock complete auto-confirm; ownership 400.
- **WalkInFlowTests** (1) — toggle on/off + filter + BookingType.WalkIn DB assertion.
- **AuditLogReadTests** (2) — admin suspend → log entry görünür (actor name + payload JSON); customer 401/403.
- **ReviewTests** domain (17) — AutoModerate profanity, Approve/Reject/Hide state, AddReply rules.
- **BookingTests** domain (24) — refund matrix 100/50/0 + reason override (BarberRequested/PaymentFailed/PendingTimeout/AdminOverride hep 100, NoShow=0), state transitions, reschedule, MarkNoShow guards.

**Sonuç:** Backend 105 → **158 test** (36 integration + 51 application + 71 domain).

### Audit log transparency (Cycle 8 başlangıcı)
- **GET /v1/admin/audit-logs** — actor/action/resource/dateRange filter + paging (max 200), actor display name JOIN, integration test (suspend action surfaced + admin-only).
- **Web admin /audit-logs sayfası** — expandable JSON payload dark pre, top-5 action chip filter, shimmer skeleton, empty state.

## 14C. Kalan Kritik Gap'ler (öncelik sırasıyla)

1. **Mobile customer profile düzenleme** — `UpdateMyProfile` endpoint var; display name + avatar düzenleme UI'ı eksik.
2. **Phone change OTP flow mobile UI** — backend `InitiatePhoneChange` + `ConfirmPhoneChange` var, mobile UI yok.
3. **Owner için yorum cevap mobile UI** — backend `POST /v1/reviews/{id}/reply` çalışıyor, mobile listeleme + cevap ekranı yok.
4. **Mobile i18n** — i18next şartına rağmen sabit Türkçe string kullanılıyor. M14 Internationalization spec'e ihtiyaç.
5. **Customer "Hesap bilgilerini indir"** — KVKK m.11 veri alma hakkı, endpoint+UI yok.
6. **Stylist/Payment/Notification domain unit tests** — coverage genişletme.
7. **Modül spec'leri (M2-M12)** — sadece M1 Auth spec yazılmış; geri kalan 11 modül tek satır.
8. **Hetzner deploy pipeline** — GitHub Actions build var, deploy yok.
9. **PayTR / Iyzico gerçek HTTP** — 🟣 founder hesap.
10. **R2 production storage** — 🟣 founder bucket/keys.
11. **Sentry DSN** — 🟣 founder Sentry organization.
12. **NetGSM gerçek SMS** — 🟣 founder hesap.

## 15. Bağlantılar

- Master spec: `docs/PHASE-0-MASTER-SPEC.md`
- Module list: `docs/04-modules/00-module-list.md`
- M1 Auth spec: `docs/04-modules/01-auth.md`
- CLAUDE.md: project root

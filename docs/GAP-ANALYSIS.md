# Kuaforum — Gap Analizi (2026-05-03)

> Bu doküman, projenin **mevcut durumu** ile **hedeflenen MVP / production** durumu arasındaki farkları sistematik olarak listeler. Her madde için: **status · kapsamı · efor tahmini · kritik yol önceliği**.

**Sembol:**
- ✅ tamam (production-ready)
- 🟢 fonksiyonel (minor cleanup gerekli)
- 🟡 stub / iskelet (çalışır ama eksik)
- 🟠 kısmi (bazı parçaları var)
- 🔴 yok (yazılmadı)
- 🟣 dış-koşul (kullanıcı eylemi gerekli — domain, lawyer, vs.)

**Efor:** S = saatler (1-8h) · M = günler (1-5g) · L = haftalar (1-3h) · XL = aylar (1-3ay).

---

## 0. Genel Durum Özeti

| Bileşen | Bitti / Toplam | Production Hazır? |
|---|---|---|
| Phase 0 dokümantasyon | 51/51 ✅ | ✅ |
| Phase 1 backbone modülleri | M1 %75 · M2-M5 %0 | 🔴 |
| Phase 2 marketplace flow | M6-M11 %0 | 🔴 |
| Phase 3 growth modülleri | M12-M16 %0 | 🔴 |
| Mobile app (Customer) | %5 (şablon) | 🔴 |
| Mobile/web Barber paneli | %5 (şablon) | 🔴 |
| Web admin paneli | %5 (şablon) | 🔴 |
| Test (xUnit) | 30 unit test (Domain) | 🟠 |
| Test (Integration, Detox, k6) | 0 | 🔴 |
| Üçüncü parti entegrasyonlar | Mock/skeleton | 🔴 |
| CI/CD | Doc'lar var, pipeline yok | 🔴 |
| Kurumsal hazırlık | Doc'lar var, hiçbiri yapılmadı | 🟣 |

**Toplam ilerleme:** Master spec'in **~%8'i** kodlanmış durumda. Geriye **~%92** kalıyor.

**Realistik tahmin:** Mevcut hızla solo founder + Claude ile kalan iş **~9-13 ay** sürer. Bu konuşmadaki turn sayısı (~10-20 ek turn) ile foundation tarafı (M1-M5 + temel mobile + temel admin) hedeflenebilir; gerisi devam eden konuşmalardan oluşacak proje.

---

## 1. Phase 0 — Foundation

| Madde | Status | Not |
|---|---|---|
| Vizyon + scope + persona doc'ları | ✅ | |
| Rakip analizi (TR + global) | ✅ | 1 araştırma raporu |
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

---

## 2. Phase 1 — Backbone Modüller

### M1 — Auth & Authorization (~%75)

| Item | Status | Efor kalan |
|---|---|---|
| Domain entities (User, RefreshToken, OtpCode, Consent, AuditLog, AdminCredential) | ✅ | — |
| Value objects (PhoneE164, EmailAddress) | ✅ | — |
| Domain events (8) | ✅ | — |
| EF configurations + migration | ✅ | — |
| Telefon + OTP register/login | ✅ | — |
| JWT + Refresh + rotation + reuse detection | ✅ | — |
| Profile GET/PATCH/DELETE/restore | ✅ | — |
| Sessions GET/REVOKE | ✅ | — |
| Consents GET/PATCH | ✅ | — |
| Hangfire HardDeleteUsersJob | ✅ | — |
| Audit logging cross-cutting | ✅ | — |
| **OAuth (Apple, Google) endpoints + provider verify** | 🔴 | M (1-2g) |
| **Admin login + parola + TOTP** | 🔴 | M (1-2g) |
| **Phone change endpoint** | 🔴 | S (4h) |
| **NetGSM gerçek SMS provider** | 🟡 (mock) | S (3h) + lisans |
| **Application + Integration testleri (Testcontainers)** | 🔴 | M (2-3g) |
| **OpenAPI doc + Bruno collection** | 🟡 (auto-OpenAPI) | S |
| Domain unit testleri | 30 test ✅ | — |

### M2 — User & Role Management (%0)

> M1 user ile çakışıyor; M2 = role-spesifik profil enrichment'ı.

| Item | Status | Efor |
|---|---|---|
| BarberOwner profile invite akışı | 🔴 | M |
| Stylist profile (BarberOwner tarafından davet) | 🔴 | M |
| Admin user yönetim endpoint'leri | 🔴 | M |
| Role permission matrisi | 🔴 | S |
| User suspend/unsuspend (admin) | 🔴 | S |
| KYC queue (cross-modül M4 ile bağlantı) | 🔴 | M |
| Kullanıcı arama / filtre (admin) | 🔴 | S |

### M3 — Geographic Data (%0)

| Item | Status | Efor |
|---|---|---|
| City entity (81 il) | 🔴 | S |
| District entity (~970 ilçe) | 🔴 | S |
| Neighborhood entity (~30K mahalle) | 🔴 | S |
| NVI veri seti bootstrap script | 🔴 | M (veri kaynağı + import) |
| PostGIS Coordinate value object | 🔴 | S |
| Geocoding service (Google Maps API) | 🔴 | S |
| Reverse geocoding (lat/lng → mahalle) | 🔴 | S |
| Manuel düzeltme admin paneli | 🔴 | M |
| Cities/districts/neighborhoods read endpoints | 🔴 | S |

### M4 — Barber Profile & Verification (%0)

| Item | Status | Efor |
|---|---|---|
| BarberShop aggregate | 🔴 | M |
| Stylist sub-entity | 🔴 | S |
| ServiceCatalog (kesim, sakal, boya, ...) | 🔴 | M |
| StylistService (stylist-bazlı override) | 🔴 | S |
| Working hours (haftalık takvim) | 🔴 | M |
| TimeOff (izin/tatil) | 🔴 | S |
| KYC submission entity | 🔴 | M |
| Belge upload (Cloudflare R2 entegrasyonu) | 🔴 | M |
| Admin KYC review akışı | 🔴 | M |
| ESBİS web doğrulama (manuel link) | 🟣 | — |
| Salon photos + portfolio | 🔴 | M |
| Public barber profile endpoints | 🔴 | M |
| Barber panel CRUD endpoints | 🔴 | L (5-7g) |

### M5 — Availability Engine ⚠️ (%0)

> **En kritik + en zor modül.** Slot rezervasyon mantığı, concurrency, izin/tatil, hizmet süresi.

| Item | Status | Efor |
|---|---|---|
| Slot generation algoritması (working hours - timeoff - bookings - hizmet süresi) | 🔴 | L |
| Concurrency-safe rezervasyon (Redis distributed lock) | 🔴 | M |
| Slot caching (3 ay önden) | 🔴 | M |
| Slot expiration (15dk pending → release) | 🔴 | S |
| Multi-service booking total süre + buffer | 🔴 | M |
| Timezone (Europe/Istanbul) handling | 🔴 | S |
| Endpoint: `GET /barbers/{id}/availability` | 🔴 | S |
| Comprehensive unit tests (race conditions, edge cases) | 🔴 | M |

---

## 3. Phase 2 — Marketplace Flow

### M6 — Booking Lifecycle (%0)

| Item | Status | Efor |
|---|---|---|
| Booking aggregate + state machine (pending/confirmed/started/completed/cancelled/no-show) | 🔴 | M |
| BookingItem (multi-service desteği) | 🔴 | S |
| Cancellation policy (kademeli iade 24/12/0) | 🔴 | M |
| No-show handling | 🔴 | S |
| Booking endpoints (initiate, confirm, cancel, complete) | 🔴 | M |
| Outbox event integration | 🔴 | S |
| Tests | 🔴 | M |

### M7 — Payment & Commission (%0)

> **Gerçek para. En riskli modül.**

| Item | Status | Efor |
|---|---|---|
| `IPaymentProvider` abstraction | 🟡 (interface yok) | S |
| `PayTrPaymentProvider` (sandbox + prod) | 🔴 | L |
| `IyzicoPaymentProvider` (fallback) | 🔴 | M |
| `MockPaymentProvider` (dev/CI) | 🔴 | S |
| Sub-merchant onboarding KYC akışı | 🔴 | M |
| Checkout init + 3DS iframe | 🔴 | M |
| Webhook handler (signed) | 🔴 | M |
| Commission split logic | 🔴 | M |
| Refund (kademeli) | 🔴 | M |
| `IEArchiveProvider` + Nilvera impl (çift fatura) | 🔴 | L |
| Reconciliation günlük job | 🔴 | M |
| PCI-DSS compliance check | 🟣 | — |
| Tests (sandbox PSP + Testcontainers) | 🔴 | L |

### M8 — Review & Moderation (%0)

| Item | Status | Efor |
|---|---|---|
| Review aggregate (5 yıldız + metin) | 🔴 | S |
| Moderation queue (admin) | 🔴 | M |
| Otomatik küfür filtresi (TR-aware) | 🔴 | M |
| Kuaför reply (1 cevap/yorum) | 🔴 | S |
| Sahte yorum tespit (heuristic) | 🔴 | M |
| Endpoint: review CRUD | 🔴 | S |
| Tests | 🔴 | S |

### M9 — Search & Discovery (%0)

| Item | Status | Efor |
|---|---|---|
| Meilisearch entegrasyonu + DI | 🔴 | M |
| Türkçe tokenizer + stopword config | 🔴 | S |
| Indexer service (BarberShop → Meili) | 🔴 | M |
| Real-time index update (domain event subscriber) | 🔴 | M |
| Filters: cinsiyet, mahalle, fiyat, açık, walk-in, rating, mesafe, hizmet | 🔴 | M |
| Walk-in **simplified** toggle endpoint | 🔴 | S |
| Endpoint: `GET /barbers/search` | 🔴 | M |
| Map view veri formatı | 🔴 | S |
| Tests | 🔴 | M |

### M10 — Maps & Navigation (%0)

| Item | Status | Efor |
|---|---|---|
| Google Maps geocoding entegrasyon | 🔴 | S |
| Reverse geocoding | 🔴 | S |
| Native deep link (Apple Maps + Google Maps) | 🔴 | S |
| Mobile harita component | 🔴 | M |
| Quota monitoring + alert | 🔴 | S |

### M11 — Notification (%0)

| Item | Status | Efor |
|---|---|---|
| `INotificationChannel` abstraction (FCM, SMS, Email) | 🔴 | S |
| FCM Firebase Admin SDK entegrasyon | 🔴 | M |
| NetGSM SMS provider (gerçek) | 🔴 | S |
| Postmark e-mail provider | 🔴 | S |
| Notification template engine | 🔴 | M |
| Kullanıcı tercih merkezi (parçalı opt-in) | 🔴 | M |
| Notification log + audit | 🔴 | S |
| 8 akış: randevu kabul/red, hatırlatma 24h/2h/30dk, refund, KYC, marketing | 🔴 | M |

---

## 4. Phase 3 — Growth & Operations

### M12 — Admin Panel (full) (%0)

| Item | Status | Efor |
|---|---|---|
| Dashboard (GMV, MAU, randevu) | 🔴 | M |
| KYC kuyruğu UI + filtreleme | 🔴 | M |
| Refund queue + manuel onay | 🔴 | M |
| Audit log explorer (filtreleme + export) | 🔴 | M |
| KVKK silme talebi yönetimi | 🔴 | M |
| Finansal rapor (komisyon, fatura, tahsilat) | 🔴 | M |
| Kullanıcı suspend/ban + audit | 🔴 | M |
| Sistem health + Hangfire jobs UI | 🔴 | S |

### M13 — Analytics & Reporting (%0)

| Item | Status | Efor |
|---|---|---|
| Kuaför dashboard (gelir, müşteri retention, popüler hizmet) | 🔴 | L |
| Admin dashboard (GMV, MAU, churn, NPS) | 🔴 | L |
| Materialized views (haftalık/aylık agregat) | 🔴 | M |
| Export CSV/Excel | 🔴 | S |
| Metabase entegrasyonu (opsiyonel) | 🔴 | M |

### M14 — Promo & Referral (%0)

| Item | Status | Efor |
|---|---|---|
| PromoCode entity + validation rules | 🔴 | M |
| Referral entity + double-sided ödül | 🔴 | M |
| Anti-abuse (device fingerprint + IP rate) | 🔴 | M |
| Endpoint'ler | 🔴 | S |
| Promo creation admin UI | 🔴 | M |

### M15 — Loyalty & Rewards (V2)

→ **MVP'de YOK.** Phase 3 ileri.

### M16 — i18n (V2)

→ **MVP'de TR-only.** Phase 3 ileri.

---

## 5. Mobile App (Customer)

| Ekran/Özellik | Status | Efor |
|---|---|---|
| Auth: telefon + OTP ekranı | 🟡 (placeholder) | M |
| Auth: Apple/Google OAuth | 🔴 | M |
| Onboarding (KVKK rıza ekranı) | 🔴 | M |
| Ana sayfa (keşif + harita) | 🔴 | L |
| Filter sheet (cinsiyet/mahalle/fiyat/açık/rating) | 🔴 | M |
| Kuaför detay + portföy | 🔴 | M |
| Hizmet seçimi + slot picker | 🔴 | M |
| Randevu özet + ödeme akışı | 🔴 | L |
| 3DS iframe görünümü | 🔴 | M |
| Randevularım listesi + iptal | 🔴 | M |
| Yorum yapma | 🔴 | M |
| Profil + ayarlar + KVKK | 🔴 | M |
| Push notification handling | 🔴 | M |
| Konum izni + reverse geocode | 🔴 | M |
| Walk-in modu UI | 🔴 | M |
| i18n yapısı (boş ama kurulu) | 🔴 | S |
| Detox E2E (kritik akış) | 🔴 | L |

## 6. Mobile/Web Barber Paneli

| Ekran | Status | Efor |
|---|---|---|
| Login | 🔴 | M |
| KYC submit (belge upload) | 🔴 | M |
| Bugünün randevuları | 🔴 | M |
| Takvim (haftalık) | 🔴 | M |
| Hizmet katalog yönetimi | 🔴 | M |
| Çalışma saatleri + izin | 🔴 | M |
| Stilist yönetimi | 🔴 | M |
| Walk-in toggle + müşteri akışı | 🔴 | S |
| Gelir dashboard (V1.5) | 🔴 | M |
| E-Arşiv fatura görüntüleme | 🔴 | S |

## 7. Web Admin Paneli

| Sayfa | Status | Efor |
|---|---|---|
| Login (admin OAuth + TOTP) | 🔴 | M |
| Dashboard | 🔴 | M |
| Kullanıcı listesi + arama | 🔴 | M |
| Kuaför listesi + KYC | 🔴 | M |
| KYC kuyruğu (pending review) | 🔴 | L |
| Randevu listesi + filtre | 🔴 | M |
| Refund queue | 🔴 | M |
| Audit log explorer | 🔴 | M |
| KVKK silme talepleri | 🔴 | M |
| Finansal rapor | 🔴 | L |
| Hangfire jobs (embed dashboard) | 🟢 (Hangfire dashboard) | — |

## 8. Infrastructure & DevOps

| Item | Status | Efor |
|---|---|---|
| Docker compose dev env | ✅ | — |
| Postgres + Redis + Meilisearch + Seq | ✅ (compose) | — |
| Hetzner provisioning playbook | 🔴 | M |
| Production docker-compose | 🔴 | M |
| TLS + Cloudflare DNS + SSL | 🔴 | S |
| Backup automation (WAL-G) | 🔴 | M |
| Monitoring (Sentry, Better Stack) | 🔴 | S (config + key) |
| GitHub Actions CI (backend test/build) | 🔴 | M |
| GitHub Actions CD (deploy staging/prod) | 🔴 | M |
| EAS Build pipeline (mobile) | 🔴 | M |
| Cloudflare Pages (web admin) | 🔴 | S |
| Secret management (Doppler) | 🔴 | S |

## 9. Üçüncü Parti Entegrasyonlar

| Servis | Status | Efor + 🟣 |
|---|---|---|
| PayTR Pazaryeri (sandbox + prod) | 🔴 | L + 🟣 (hesap başvurusu) |
| Iyzico Pazaryeri (fallback) | 🔴 | M + 🟣 |
| Nilvera e-Arşiv | 🔴 | M + 🟣 (abonelik) |
| NetGSM SMS | 🔴 | S + 🟣 (hesap) |
| Firebase Cloud Messaging | 🔴 | M + 🟣 (proje) |
| Apple OAuth | 🔴 | M + 🟣 (Developer hesap) |
| Google OAuth | 🔴 | M + 🟣 (Cloud project) |
| Google Maps Platform | 🔴 | M + 🟣 (key + billing) |
| Sentry | 🔴 | S + 🟣 (DSN) |
| Postmark / Resend e-mail | 🔴 | S + 🟣 (hesap + DKIM) |
| Cloudflare R2 (file storage) | 🔴 | S + 🟣 (bucket) |

## 10. Compliance & Legal

| Item | Status | Efor + 🟣 |
|---|---|---|
| Şirket kuruluş (Limited Şti.) | 🟣 | 2-4 hafta |
| KVKK aydınlatma metni (avukat) | 🟣 | 1-2 hafta |
| Kullanım sözleşmesi + gizlilik | 🟣 | 1-2 hafta |
| ETBİS kayıt | 🟣 | 1-2 gün |
| KVKK VERBİS (eşik aşılınca) | 🟣 | — |
| Marka tescili (TPMK) | 🟣 | 6-12 ay |
| Sigorta poliçesi (siber + meslek) | 🟣 | 1 hafta |
| Pen-test (3rd party) | 🟣 | 1-2 hafta + |
| E-Arşiv mükellefi tescil (GİB) | 🟣 | 1-2 gün |
| Hetzner Standart Sözleşme + KVK Kurum bildirim | 🟣 | 1 hafta |
| Sözleşmeli SMMM (aylık) | 🟣 | yıllık |

## 11. Tests

| Tip | Mevcut | Hedef | Efor |
|---|---|---|---|
| Domain unit | 30 ✅ | 80+ | M |
| Application unit | 0 | 60+ | M |
| Application integration (Testcontainers) | 0 | 40+ | L |
| API contract (Bruno) | 0 | 1 koleksiyon | S |
| Mobile component | 0 | 30+ | M |
| Mobile E2E (Detox) | 0 | 8-10 senaryo | L |
| Load test (k6) | 0 | 3 senaryo | S |
| Security pen-test | 0 | 1 rapor | 🟣 |

## 12. Dokümantasyon Eksiklikleri

| Doc | Status | Efor |
|---|---|---|
| API endpoint spec'i (OpenAPI auto + manual) | 🟡 (Swagger auto) | S |
| Module spec'leri M2-M16 | 🔴 (sadece M1) | L |
| Operasyon runbook'ları | 🔴 | M |
| Incident response SOP | 🔴 | M |
| Disaster recovery playbook | 🔴 | S |
| Onboarding doc (yeni dev için) | 🔴 | S |

---

## 13. Kritik Yol — Sıradaki 30 Günlük Yapılacaklar

```
Hafta 1   Şirket kuruluş başlat 🟣 + M1 OAuth+Admin+Phone close + M2 User
Hafta 2   M3 Geography (NVI bootstrap) + M4 Barber profile/verification
Hafta 3   M5 Availability Engine (en zor) + M5 testleri
Hafta 4   M6 Booking lifecycle + M11 Notification skeleton
```

Sonraki 30 gün:
```
Hafta 5-6 M7 Payment (PayTR sandbox) + Nilvera e-Arşiv
Hafta 7   M8 Review + M9 Search (Meilisearch)
Hafta 8   Mobile auth + keşif + booking flow
```

Sonraki 30 gün: M10 Maps, M12 admin paneli, mobile devam.

---

## 14. Bu Konuşmada Hedef

Realistik:
- **Bu turn (şu an):** Gap analizi (✅ bu doc) + M1 closure (OAuth, Admin, Phone) + M2 User enrichment + M3 Geography
- **Sonraki ~5-10 turn:** M4 + M5 + M6 + temel mobile auth + admin KYC ekranı
- **Conversation sonu hedef:** "Marketplace iskeleti çalışır halde" (tam değil ama gösterilebilir)

**Tam launch için:** ~9-13 ay daha (mevcut hızla, solo + Claude). Bu konuşma **foundation tarafını ileri taşır**, full launch'a götürmez.

---

## 15. Bağlantılar

- [`PHASE-0-MASTER-SPEC.md`](PHASE-0-MASTER-SPEC.md)
- [`13-project-management/01-roadmap.md`](13-project-management/01-roadmap.md)
- [`13-project-management/02-risk-register.md`](13-project-management/02-risk-register.md)

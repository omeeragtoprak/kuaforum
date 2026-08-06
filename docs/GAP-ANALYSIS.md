# Kuaforum — Gap Analizi (2026-07-23)

> Bu doküman, 10 agent'lık paralel fullstack kod taramasının (backend domain/application, API + infrastructure, mobile müşteri, owner paneli, web admin, infra/devops, spec-izlenebilirlik, test envanteri) ve bunu takip eden senkron sentez + critic (çapraz doğrulama) turunun sonucudur; her madde dosya:satır kanıtına dayanır. 2026-05-13 tarihli önceki analiz iki yönde bayattı: kapanmış işleri (mobile profile edit, phone-change OTP UI, owner review reply, KVKK veri export) hâlâ eksik gösteriyordu ve var olmayan özellikleri ("3DS callback endpoint mevcut", gerçek Meilisearch/PayTR entegrasyonu gibi) tamamlanmış varsayıyordu — bu doküman o hatalı temeli tamamen değiştirir.

**Sembol (M1-M16 modül tablosu ve alan özetlerinde):**
- ✅ tamam (production-ready) · 🟢 fonksiyonel (minor cleanup gerekli) · 🟡 stub/iskelet (çalışır ama eksik) · 🟠 kısmi (bazı parçaları var) · 🔴 yok (yazılmadı) · 🟣 dış-koşul (founder eylemi gerekli — hesap başvurusu, sözleşme, hukukçu)

**Öncelik (P0/P1/P2 matrisinde):**
- **P0** — launch blocker: üretime çıkmadan mutlaka kapanmalı (güvenlik açığı, yasal risk, deploy imkânsızlığı)
- **P1** — yüksek risk: launch öncesi veya hemen sonrasında kapanmalı (operasyonel/finansal itibar riski)
- **P2** — backlog / kalite borcu: bilinçli scope kararıyla ertelenebilir, ama karar kayıt altına alınmalı

---

## 0. Genel Durum Özeti

### 0.1 Yönetici Özeti

Proje MVP'ye doğru **~%55-60** seviyesinde: backend çekirdeği (Auth/OTP, Booking state machine + refund matrisi, Availability, Review, Admin/Audit) **201 testle** sağlam, üç UI yüzeyi de gerçek endpoint'lere bağlı, lokal dev ortamı + CI olgun. Ancak platform bu haliyle **ne deploy edilebilir ne gerçek para alabilir**. Üç risk kümesi:

**(1) Para akışı hem yok hem delik**
- Gerçek ödeme zinciri yok: PayTR/Iyzico stub, webhook/3DS callback endpoint'i hiç yazılmamış, mobile ödeme UI'ı bağlanmamış (→ P0-5)
- Refund endpoint'inde tam IDOR — herhangi bir login kullanıcı herhangi bir payment'ı keyfi tutarla iade ettirebilir (→ P0-1)
- `AllowAnonymous` mock/complete endpoint'i env-gate'siz — production'da ödemesiz booking confirm mümkün (→ P0-2)
- Cancel `Reason` client-controlled — müşteri refund matrisini bypass edip %100 iade yazdırabilir (→ P0-3)
- İptal sonrası gerçek para iadesi otomasyonu yok; "iade işleme alındı" bildirimi gidiyor ama iade yapılmıyor (→ P0-4)
- e-Arşiv (Nilvera) katmanında sıfır kod — gerçek tahsilat başladığı an vergi denetiminde doğrudan ceza kalemi (→ P0-13)

**(2) Deploy edilebilirlik fiilen sıfır**
- Repo'da tek Dockerfile yok, deploy workflow yok; `appsettings.Production.json` içindeki `${VAR}` placeholder'ları .NET tarafından expand edilmiyor — prod boot'ta crash/tam kesinti (→ P0-7)
- Base `appsettings.json`'daki commit'li admin şifresi (`ChangeMe!Admin2026`) prod'da ilk boot'ta seed edilir (→ P0-8)
- NetGSM config key mismatch — prod'da OTP asla SMS'le gitmez, OTP-only auth'ta hiçbir kullanıcı kayıt olamaz (→ P0-6)
- Mobile production build store'a gönderilemez: EAS projectId yok, icon/splash asset'i hiç yok, API URL localhost'a düşer (→ P0-12)

**(3) Yasal/onboarding zinciri kopuk**
- KVKK aydınlatma metni + ToS uygulamada hiçbir yerde görüntülenemiyor — bundled consent riski (→ P0-10)
- Admin, KYC belgelerini hiç görmeden onaylıyor (→ P0-11)
- KYC reddedilen kuaför backend'de sonsuza dek kilitleniyor; owner mobile'dan salon oluşturamıyor + gerçek belge yükleyemiyor (→ P0-9)

İyi haber: P0'ların yaklaşık yarısı 1 günün altında efor isteyen guard/config işleridir. Büyük kalemler (PayTR, Nilvera, NetGSM gerçek entegrasyon) founder hesap açılışlarına bloklu (🟣) olduğundan başvurular **bugün** başlamalı (bkz. §10).

### 0.2 Alan Skorları

| Alan | % | Not |
|---|---|---|
| Backend Domain + Application | **%65** | Çekirdek modüller + 201 test yeşil (113 domain + 51 application + 37 integration). Para akışı bütünlüğü kırık: refund authz yok, iptal→iade otomasyonu yok, Outbox yok, StylistService enforce edilmiyor, PendingTimeout job yok, KYC reject kilidi. |
| API Yüzeyi + Infrastructure | **%55** | ~78 endpoint tutarlı, JWT rotation + reuse-detection gerçek; FCM/Resend/Sentry/Redis/Hangfire GERÇEK ama PayTR/Iyzico/NetGSM açıkça stub, Nilvera/Meilisearch/R2 kodda hiç yok, rol policy'leri tanımlı ama kullanılmıyor. |
| Mobile Müşteri Uygulaması | **%60** | Keşfet→randevu→yorum uçtan uca çalışıyor, loading/empty/error disiplini örnek seviyede. Ödeme UI yok, KVKK/ToS metinleri görüntülenemiyor, walk-in CTA'sız, konum kullanılmıyor, icon/splash asset'leri hiç yok. |
| Kuaför (Owner) Paneli | **%58** | Günlük operasyon (bugün/takvim/hizmet/müsaitlik/walk-in/yorum cevabı/KYC takip) çalışıyor. Onboarding uçtan uca yürümüyor: salon oluşturma disabled, KYC upload placeholder; kazanç/komisyon ekranı yok, "Ciro" gerçekleşmemiş randevuları sayıyor. |
| Web Admin Paneli | **%55** | 8 sayfa gerçek endpoint'lere bağlı, admin login (parola+TOTP+lockout) tam — GAP-ANALYSIS'in önceki iddiasından çok ileride. KYC kör onay, admin iptal iade tetiklemiyor, token refresh yok (15dk'da oturum ölüyor), finansal rapor sıfır, shadcn migration yapılmadı. |
| Infra + DevOps + Deploy | **%30** | Dev compose + CI olgun; production hikayesi fiilen sıfır: Dockerfile yok, deploy workflow yok, `${VAR}` placeholder'lar prod boot'u kırar, commit'li admin şifresi, backup yok, EAS projectId bağlanmamış. |
| Spec / İzlenebilirlik | **%40** | Kod amaçla uyumlu, MVP MUST ~%70 çalışıyor; ama 16 modülden 15'i spec'siz, `docs/rfc` hiç açılmamış, .NET10→8 / RS256→HS256 / Meilisearch→ILIKE sapmaları RFC'siz, eski GAP-ANALYSIS iki yönde bayat ve güvenilmezdi. |
| Test Envanteri + Kalite Kapıları | **%40** | Backend 201 test ama Application suite CI'da hiç koşmuyor, gerçek domain coverage %54.5 (hedef %70), refund akışını hiçbir test çağırmıyor, concurrent double-booking testi yok, mobile/web-admin/contract test sıfır. |

### 0.3 Toplam Değerlendirme

Önceki analiz "master spec'in ~%80'i kodlanmış" diyordu — bu iddia **bayattı ve yanıltıcıydı**: hem tamamlanmış işi eksik gösteriyor hem eksik/tehlikeli olan para akışını "hazır" gibi sunuyordu. Gerçekçi durum: **kod hacmi büyük ve UI yüzeyleri gerçek endpoint'lere bağlı**, ama üç kritik eksen (para akışı güvenliği, deploy edilebilirlik, yasal onboarding) MVP launch'ını fiilen bloklamaktadır. Founder onayı bekleyen 🟣 işleri saymazsak (PayTR/Nilvera/NetGSM/R2/Sentry/Hetzner/Apple-Google hesapları), **P0 kümesinin kod tarafı ~2-3 hafta**, P0+P1 birlikte **~5-6 hafta**.

---

## 1. Modül İzleri (M1-M16) — Niteliksel Durum

> Bu cycle'ın kaynak verisi **alan bazlı** toplandı (bkz. §0.2), modül bazlı kesin yüzdeler bu veriden güvenilir türetilemez — fabrike edilmiş bir M1-M16 % kırılımı yanıltıcı olur. Bunun yerine her modülün niteliksel durumu + kritik kanıt aşağıda; kesin efor/skor için ilgili P0/P1/P2 madde numarasına bakın.

| Modül | Durum | Not |
|---|---|---|
| **M1 — Auth & Authorization** | 🟠 Kod tamam, üretime kilitli | OTP/JWT rotation+reuse-detection/OAuth JWKS/Admin login+TOTP gerçek implement. NetGSM config mismatch → prod'da OTP hiç gitmiyor (P0-6). Mobile'da OAuth butonu yok (P2-4). |
| **M2 — User & Role Management** | 🟠 Kısmi | KVKK self-servis (silme/export/rıza/session) tam. Rol policy'leri tanımlı ama hiçbir endpoint grubunda kullanılmıyor — Customer rolü owner endpoint'lerine erişebiliyor (P1-4). |
| **M3 — Geographic Data** | 🟠 Kısmi | 81 il + 16 il örnek ilçe seed'li, PostGIS Coordinate VO çalışıyor. Neighborhood 0 satır (mahalle differentiator yarım, P1-14). Geocoding gerçek HTTP çağrısı yok. |
| **M4 — Barber Profile & Verification** | 🟠 Kısmi / kritik nokta blok | BarberShop/Stylist/ServiceCatalog/WorkingHours/TimeOff domain tam. KYC: reddedilen kuaför sonsuza dek kilitli (P0-9), admin belgeleri görmeden onaylıyor (P0-11), mobile upload placeholder (P0-9), R2 yok — yalnız local disk (P1-6). |
| **M5 — Availability Engine** | 🟢 Kod tamam, concurrency güvenliği eksik | Slot generation + working hours/time-off enforcement 13 integration testle kanıtlı. Redis lock granülaritesi hatalı + DB exclusion constraint yok (P1-1). |
| **M6 — Booking Lifecycle** | 🟠 State machine tamam, güvenlik delikli | Pending→Confirmed→Started→Completed/Cancelled/NoShow + refund matrisi + reschedule test kapsamlı. Cancel Reason client-controlled (P0-3), PendingTimeout job yok (P1-3), no-show zaman guard'ı yok (P1-5). |
| **M7 — Payment & Commission** | 🔴 Kritik blok — projenin en riskli modülü | Payment aggregate + refund matematiği domain'de sağlam ama: refund endpoint IDOR (P0-1), mock/complete anonim (P0-2), PayTR/Iyzico stub + webhook yok (P0-5), e-Arşiv sıfır kod (P0-13), komisyon oranı hardcoded (P2-1). |
| **M8 — Review & Moderation** | ✅ Kod tamam | Submit/moderasyon/reply/TR profanity heuristiği çalışıyor, domain testli. Helpful vote + foto upload küçük eksikler (P2 seviyesi). |
| **M9 — Search & Discovery** | 🟠 Kısmi | Şehir/ilçe/cinsiyet/walk-in/rating/fiyat filtreleri SQL ile çalışıyor. PostGIS mesafe sıralaması + Meilisearch hiç entegre değil, rating her sorguda korelasyonlu subquery (ölçek riski, P2-2). |
| **M10 — Maps & Navigation** | 🟡 Minimal | Yalnızca Apple/Google Maps deep-link handoff var. Geocoding/reverse-geocoding yok, mobile'da harita view (liste↔harita toggle) yok. |
| **M11 — Notification** | 🟠 Kısmi/blok | Dispatcher + InApp feed + FCM adapter (gerçek, hesap bekliyor) + Resend gerçek çalışıyor. SMS koşulsuz mock (NetGSM ile aynı kök neden, P0-6); Outbox yok (P1-2); tercih merkezi (opt-out) yok. |
| **M12 — Admin Panel** | 🟠 GAP-ANALYSIS'in eski iddiasından çok ileride, finansal olarak tehlikeli | 8 sayfa + login+TOTP tam çalışıyor. KYC kör onay (P0-11), admin iptal PSP refund tetiklemiyor (P0-4), tek "Admin" rolü (süper admin/operatör ayrımı yok), token 15dk'da ölüyor (P1-9). |
| **M13 — Analytics & Reporting** | 🟡 Minimal | Sadece dashboard temel stat kartları. GMV time-series/funnel/cohort/stylist utilization yok. |
| **M14 — Promo & Referral** | 🔴 Yok | Domain'de promo/coupon/campaign/referral kavramı hiç yok (grep 0 sonuç). |
| **M15 — Loyalty & Rewards** | — V2 | Kapsam dışı, MVP'de planlanmıyor. |
| **M16 — i18n** | 🟡 Askıda | i18next kurulu, 0 kullanım (P2-3) — TR-only mı yoksa retrofit mi kararı verilmemiş. |

---

## 2. Alan Bazlı Detay

### 2.1 Backend Domain + Application (%65)

Çekirdek modüller (Auth/OTP, User+KVKK, Geography, BarberShop/Stylist/KYC, Availability, Booking state machine, Payment+Refund, Review+moderasyon, Notification, Admin/Audit) domain entity + CQRS handler + FluentValidation validator seviyesinde büyük oranda yazılmış ve 201 test yeşil.

**Çalışıyor:**
- Identity/Auth: User aggregate (suspend/soft-delete/30g restore/anonymize), OtpCode (hash'li kod, device fingerprint, 5 cihaz limiti), RefreshToken rotation+reuse-detection, OAuth, AdminLogin, PhoneChange, DevLogin
- KVKK/Consent: Consent entity (ToS/Marketing/Location/HealthData), DeleteMyAccount/RestoreMyAccount, ExportMyDataQuery, session yönetimi, HardDeleteUsersJob
- Geography: City/District/Neighborhood/Address/Coordinate + Tr81 + Erzurum seed
- BarberShop: Create/UpdateBasics/SetAddress + status makinesi Draft→PendingKyc→Active→Suspended, ServiceCatalog CRUD, Stylist CRUD + walk-in toggle + ExpireWalkInJob, WorkingHours/TimeOff, KYC submit + admin ReviewKyc
- Availability: `ComputeAvailabilityQuery` (15dk slot grid, Europe/Istanbul TZ) + `StylistScheduleChecker` Initiate ve Reschedule'da enforce
- Booking: tam state machine, refund yüzde matrisi (>24h %100, 12-24h %50, <12h %0, BarberRequested/PaymentFailed/PendingTimeout/AdminOverride %100, NoShow %0), Redis distributed lock, Reschedule (2h cutoff), AutoNoShowJob, BookingReminderJob
- Payment: Payment aggregate Initiated→Authorized→Captured→Settled/Failed/Refunded + kısmi iade birikimi ve aşım koruması, idempotency key
- Review: Submit (yalnız Completed booking, tek review/booking, AutoModerate profanity→Pending), Approve/Reject/Hide, tek seferlik BarberReply
- Notification: NotificationLog + 6 booking event handler, DeviceToken kayıt, notification feed
- Admin/Audit: `IAuditLogger` tüm kritik komutlarda, `GET /v1/admin/audit-logs`, SuspendBarberShop, AdminCancelBooking

**Kritik gap'ler:** refund IDOR (→P0-1), cancel reason bypass (→P0-3), iptal→iade otomasyonu yok (→P0-4), Outbox yok (→P1-2), StylistService enforce edilmiyor (→P1-16), PendingTimeout job yok (→P1-3), KYC reject sonsuz kilit (→P0-9), PayTR/Iyzico/webhook stub (→P0-5), Nilvera sıfır kod (→P0-13), komisyon hardcoded (→P2-1), arama ölçek riski (→P2-2).

### 2.2 API Yüzeyi + Infrastructure Entegrasyon Gerçekliği (%55)

API yüzeyi geniş ve tutarlı: 15 grup altında ~78 endpoint, `Result`→`ProblemDetails` eşlemesi, FluentValidation pipeline, JWT refresh rotation + device-binding + reuse detection gerçekten implement edilmiş. Entegrasyon gerçekliği ikiye ayrılıyor:

**Gerçek entegrasyonlar:** FCM push (FirebaseAdmin 3.4.0, multicast, UNREGISTERED token deaktivasyonu), Resend email (gerçek HTTP), Sentry (DSN env-guarded, `SendDefaultPii=false`), Redis rate limiter (OTP send), Hangfire (Postgres storage, 4 recurring job), JWT/refresh (HS256, SHA256 hash saklama, rotation+reuse-detection, device fingerprint).

**Üçüncü Parti Entegrasyon Durumu:**

| Servis | Durum | Not |
|---|---|---|
| PayTR (PSP primary) | 🟡 stub | `PayTrPaymentProvider.cs:12` "SCAFFOLDED stub"; 🟣 merchant hesabı |
| Iyzico (PSP fallback) | 🟡 stub | Aynı durumda; 🟣 hesap |
| NetGSM (SMS) | 🟡 stub + config mismatch | `UserCode` hiç bind olmuyor → prod'da her zaman mock fallback (→P0-6); 🟣 hesap + originator onayı |
| Resend (Email) | ✅ gerçek | ApiKey yoksa log-fallback; config key mismatch küçük tutarsızlık (→P2-9) |
| Firebase FCM (Push) | ✅ gerçek (adapter hazır) | 🟣 service account JSON gelince aktif |
| Cloudflare R2 (file storage) | ✅ kod hazır | `S3ObjectStorage` MinIO'da test edilip kanıtlandı, default hâlâ `LocalFileObjectStorage`; 🟣 hesap (→P1-6) |
| Google Maps (geocoding) | 🔴 yok | Interface var, HTTP çağrısı yok; 🟣 API key |
| Nilvera (e-Arşiv) | 🔴 yok | Sıfır kod (→P0-13); 🟣 hesap + mali müşavir |
| Sentry | ✅ kod hazır | Yalnızca DSN bekliyor; 🟣 organizasyon |
| Apple/Google OAuth | 🟢 backend gerçek, mobile ölü kod | JWKS doğrulamalı ama mobile'da buton yok (→P2-4) |
| Meilisearch | 🔴 kodda yok | Compose'da container çalışıyor ama backend'de 0 referans (→P2-2) |

**Kritik gap'ler:** refund IDOR (→P0-1), anonim mock/complete (→P0-2), PayTR/Iyzico/webhook (→P0-5), `${VAR}` placeholder substitution yok (→P0-7), NetGSM (→P0-6), rol policy'leri kullanılmıyor (→P1-4), R2/local storage (→P1-6), Hangfire Host-header bypass (→P1-16), küçük tutarsızlıklar — Resend config key, kök route 404, rate limiter atomik değil (→P2-9).

### 2.3 Mobile Müşteri Uygulaması (%60)

Keşfet→randevu→yorum döngüsü uçtan uca çalışır durumda; loading/empty/error state disiplini örnek seviyede (EmptyState + Skeleton + RefreshControl neredeyse her liste ekranında). TanStack Query v5 app genelinde gerçek kullanılıyor, axios interceptor'da single-flight token refresh + SecureStore + device fingerprint var.

**Çalışıyor:** OTP login + axios interceptor refresh; Keşfet (Erzurum default city + city picker); Arama (server-side filtreler); Kuaför detay (native harita handoff, yorum+cevap gösterimi); Randevu akışı (initiate, filter chips, cancel/reschedule, owner aksiyonları, CountdownBadge); Yorum yazma; Hesap (edit-profile, change-phone, sessions, consents, veri indirme, 2 kademeli hesap silme); Bildirim feed + FCM token register; AppErrorBoundary + ErrorReporter; Design system (Button 44pt+ tap target, accessibility props, haptics).

**Kritik gap'ler:** ödeme UI hiç bağlanmamış (→P0-5), KVKK/ToS görüntülenemiyor (→P0-10), demo login `__DEV__` guard'sız (→P0-12), walk-in akışı tıklanamıyor (→P1-12), konum izinleri declare'li ama kullanılmıyor (→P1-14), push production config eksik (→P0-12), i18n askıda (→P2-3), accessibility kapsamı düşük / reschedule elle metin girişi / error state eksikleri / favoriler yok / mobile test sıfır (→P2-6, P2-8).

**Alan-özel ek notlar (matriste ayrı madde olmayan):**
- Kuaför detayında `stylistsQuery`/`servicesQuery` hata verirse bölüm sessizce boş render olur — kullanıcı "hizmet yok" sanır (`barber/[id].tsx:126-145`).
- Deep-link kapsamı dar: universal/app link yok, cold-start push tıklaması kaybolur (`usePushNotifications.ts:84-106`).
- Yorum onay metni backend davranışıyla çelişiyor: "Onaylanınca görünür" diyor ama backend default-Approved (`review/submit.tsx:27-28`).

### 2.4 Kuaför (Owner) Mobile Paneli (%58)

Günlük operasyon çekirdeği (bugün ekranı, 14 günlük takvim, hizmet listesi, stylist + çalışma saati + izin yönetimi, walk-in toggle, yorum cevaplama, bildirimler, KYC durum takibi) gerçek backend endpoint'lerine bağlı ve çalışır durumda.

**Çalışıyor:** `today.tsx` (bugünün randevuları + start/complete/no-show + haftalık ciro widget); `calendar.tsx`; owner confirm/reschedule/cancel; `services.tsx` (hizmet CRUD tam); `availability.tsx` (stylist ekleme, çalışma saati, izin — ÇALIŞIYOR); `profile.tsx` (salon özeti, walk-in toggle); `kyc.tsx` (durum takibi tam); `reviews.tsx` (yorum listesi + cevap); notifications tab.

**Kritik gap'ler:** owner onboarding uçtan uca kopuk — salon oluşturma disabled + KYC upload placeholder + KYC reject sonsuz kilit (→P0-9); kazanç/komisyon görünürlüğü sıfır + "Ciro" yanıltıcı (→P1-11); no-show server-side zaman guard'ı yok (→P1-5); KYC onay/red bildirimi gönderilmiyor (→P1-10); walk-in owner-side kasada kayıt yok, salon profili düzenleme mobile'da yok, shop galerisi yok, takvim geçmişe bakamıyor (→P2-8).

**Alan-özel ek not:** Pending randevu onay/red akışına Bugün ekranından erişilemiyor — `today.tsx` BookingRow Pressable değil, owner en sık baktığı ekrandan bekleyen talebi onaylayamıyor (`today.tsx:262-272`, sadece calendar→booking/[id] rotasından ulaşılabiliyor).

### 2.5 Web Admin Paneli (%55)

7 sayfa (Dashboard, Kullanıcılar, Kuaförler, Randevular, KYC, Yorum Moderasyonu, Audit Log) + login tamamı gerçek backend endpoint'lerine bağlı ve uçtan uca çalışıyor; admin login (parola + TOTP + lockout + audit + AdminSeeder) tamamen implement — eski GAP-ANALYSIS'in ima ettiğinden çok daha ileride.

**Çalışıyor:** Admin login (e-posta+parola+TOTP, lockout, audit); Dashboard (8 stat kartı); KYC onay/red akışı (sub-merchant key zorunlu, sebep chip'leri); Yorum moderasyon kuyruğu; Kullanıcı yönetimi (arama+filtre+suspend); Salon yönetimi (5 durum filtresi + suspend/reactivate); Randevu müdahale (admin iptal); Audit log görüntüleme (filtre + payload JSON expand); tüm endpoint'ler `RequireAuthorization("Admin")` policy arkasında; TR locale para/tarih formatlama, TypeScript strict.

**Kritik gap'ler:** `/v1/payments/refund` IDOR (→P0-1), admin iptal PSP refund tetiklemiyor ama UI "%100 iade" vaat ediyor (→P0-4), KYC kör onay (→P0-11), token yönetimi — refresh token atılıyor, localStorage, 401'de redirect yok (→P1-9), finansal rapor/komisyon takibi/Nilvera izleme sıfır (→P0-13 ile aynı kök), shadcn migration yapılmadı + TanStack ölü bağımlılık + pagination eksik + bayat login uyarısı (→P2-7).

**Alan-özel ek notlar:**
- Rol bazlı admin yetkilendirme yok: tek `Admin=99` rolü — süper admin vs operatör ayrımı yapılamıyor (`UserRole.cs:8`, `AuthSetup.cs:51`).
- Promo/kampanya yönetimi backend'de domain dahi yok (grep 0 sonuç).

### 2.6 Infra + DevOps + Deploy Hazırlığı (%30)

CI pipeline (build + 3 platform typecheck + Testcontainers integration test) çalışır durumda; lokal dev compose ortamı (Postgres/PostGIS, Redis AOF, Meilisearch, Seq — healthcheck'li) düzgün kurulmuş. Production deploy hikayesi fiilen sıfır.

**Çalışıyor:** CI pipeline (backend build+test, mobile/web-admin typecheck); dev compose (Postgres+PostGIS, Redis AOF, Meilisearch, Seq, healthcheck'li); health endpoint'leri (`/health`, `/health/ready` — Postgres+Redis ping); Serilog→Console+Seq+Sentry; startup idempotent migration + seed; Hangfire dashboard admin-role filtreli; `eas.json` iskeleti; web-admin `next.config.mjs output: 'standalone'`.

**Kritik gap'ler:** production deploy pipeline + Dockerfile tamamen yok (→P0-7), `${VAR}` placeholder'lar prod boot'u kırar (→P0-7), commit'li admin şifresi (→P0-8), mobile production build localhost API'ye işaret eder + EAS bağlanmamış (→P0-12), backup stratejisi hiç yok (→P1-7), CI Application test suite'ini koşturmuyor + net8 retarget uncommitted (→P1-8), ForwardedHeaders middleware yok, Hangfire Host-header bypass (→P1-16), R2 yok — KYC dosyaları lokal diskte (→P1-6), monitoring/metrics/uptime sıfır (→P2-10), Meilisearch compose'da çalışıyor ama backend'de sıfır entegrasyon (→P2-2).

### 2.7 Spec / İzlenebilirlik (Master Spec ↔ Modül Spec ↔ Kod) (%40)

Kod, projenin amacıyla (TR kuaför B2C marketplace, %10 komisyon, walk-in) büyük ölçüde uyumlu; MVP MUST listesinin ~%70'i çalışır durumda. Ama 3 differentiator'dan ikisi yarım: mahalle verisi/filtresi hiç yok, Türkçe NLP arama (Meilisearch) backend'de sıfır referans. İzlenebilirlik disiplini çökmüş: 16 modülden yalnızca M1'in spec'i var, `docs/rfc` dizini hiç açılmamış, master spec v0.1.0'da donmuş/imzasız iken .NET10→8, RS256→HS256, Meilisearch→ILIKE gibi kilitli kararlardan RFC'siz sapılmış.

**Çalışıyor:** M1 Auth tam; Apple/Google OAuth backend gerçek (GAP-ANALYSIS'in eski 🔴 iddiası yanlıştı); Admin login TOTP; KVKK self-servis tam; M4 Barber (BarberShop+Stylist+ServiceCatalog+KYC+lokal upload); M5 Availability (slot+concurrency-safe lock); M6 Booking (state machine+multi-service+kademeli iade+reschedule); Walk-in differentiator TAM; M8 Review; M9 Search kısmi; M10 kısmi (deep-link); M11 kısmi; M12 Admin (8 sayfa); Test altyapısı (201 test); Sentry KVKK-safe wiring.

**Kritik gap'ler:** gelir zinciri tamamen mock — PSP stub + webhook yok + e-Arşiv sıfır (→P0-5, P0-13), RFC disiplini sıfır + 15/16 modül spec'siz + eski GAP-ANALYSIS bayat (→P1-13), mahalle differentiator yarım (→P1-14), Meilisearch differentiator yarım (→P2-2), R2 yok (→P1-6), bildirim tercih merkezi yok (→P1 kapsamında dolaylı, KVKK marketing riski), komisyon hardcoded + Erzurum pilot RFC'siz (→P2-1), audit log 5 yıl retention politikasız (→P2-5).

**Alan-özel ek notlar:**
- MVP MUST #1'in yarısı yok: e-posta ile kayıt/doğrulama akışı hiç implement edilmemiş (telefon-first karar RFC'siz).
- Harita üzerinde keşif (liste↔harita toggle) MVP MUST listesinde ama implement edilmemiş.

### 2.8 Test Envanteri + Kalite Kapıları (%40)

Backend'de **201 test** var (113 domain + 51 application + 37 integration) ve domain testleri lokalde 113/113 yeşil. Kalite kapıları ciddi delik: Application testleri CI'da hiç çalışmıyor, coverage hiçbir yerde ölçülmüyor ve gerçek domain coverage **%54.5** (hedef %70+), mobile'da sıfır test dosyası, web-admin ve API contract testi tamamen yok. En riskli boşluklar para ve çakışma tarafında (bkz. §7 detay).

**Kritik gap'ler:** production payment webhook yok + refund akışı hiç test edilmemiş (→P0-5 kapsamında), concurrent double-booking: lock granülaritesi hatalı + DB constraint yok + test yok (→P1-1), Application testleri CI dışı + coverage gate yok (→P1-8), mobile sıfır test (→P1-8 ile ilişkili, ayrıca bkz. §7), HardDeleteUsersJob test edilmemiş (→P1-16), Outbox pattern hiç implement edilmemiş (→P1-2), web-admin test altyapısı + API contract testi yok (→P2-10).

---

## 3. P0 — Launch Blocker Gap'ler (13 madde)

**P0-1. Refund endpoint'inde tam IDOR: herhangi bir login kullanıcı herhangi bir payment'ı keyfi tutarla iade ettirebilir**
- Kanıt: `backend/src/Kuaforum.Api/Endpoints/PaymentEndpoints.cs:19-21` (sadece `RequireAuthorization`) + `:71` (`RefundRequest` `AmountMinor`+`Reason` client'tan); `backend/src/Kuaforum.Application/Modules/Payment/RefundPaymentCommand.cs:32-58` — handler payment'ı Id ile çekip doğrudan `provider.RefundAsync` çağırıyor, `ActorUserId` yalnız audit'e yazılıyor, sahiplik/rol kontrolü sıfır.
- Öneri: Endpoint'e "Admin" policy koy; handler'da actor==Admin veya (payment.CustomerId==actor + iptal edilmiş booking bağlamı) kontrolü; `AmountMinor`'ı client'tan alma, `booking.RefundPercentage`'dan türet. Integration test: müşteri-A → müşteri-B payment refund → 403.
- Efor: 2-4 saat

**P0-2. `AllowAnonymous` mock/complete endpoint'i production'da ödemesiz booking confirm'e izin veriyor**
- Kanıt: `PaymentEndpoints.cs:24-26` `GET /v1/payments/mock/complete` `AllowAnonymous`, dev-login'in aksine env-gate YOK (`Program.cs`'te koşulsuz `MapPaymentEndpoints`); token formatı tahmin edilebilir `mock-{paymentId:N}` (`CompletePaymentCommand.cs:78-83`) ve handler `payment.Provider==Mock` doğrulaması yapmıyor.
- Öneri: Endpoint'i `env.IsDevelopment()` bloğuna al (dev-login kalıbı) VE handler'a `payment.Provider == PaymentProviderName.Mock` guard'ı ekle (defense-in-depth).
- Efor: 2-3 saat

**P0-3. Cancel `Reason` client-controlled — müşteri `Reason=BarberRequested` göndererek %100 iade yüzdesi yazdırabilir**
- Kanıt: `BookingEndpoints.cs:123` `CancelBookingRequest(BookingCancellationReason Reason)` body'den; `BookingActions.cs:38-39` `booking.Cancel(request.Reason)` doğrudan, validator sadece `IsInEnum`; `Booking.cs:194-197` BarberRequested/PaymentFailed/PendingTimeout/AdminOverride → koşulsuz %100.
- Öneri: `Reason`'ı server-side türet: aktör customer→CustomerRequested, owner→BarberRequested; PaymentFailed/PendingTimeout/AdminOverride yalnız sistem/admin path'lerinden. Domain'e reason-aktör invariant'ı + test.
- Efor: 2-3 saat

**P0-4. İptal → gerçek para iadesi otomasyonu yok; müşteriye "iade işleme alındı" bildirimi gidiyor ama iade yapılmıyor (admin iptal dahil)**
- Kanıt: `BookingCancelledEvent`'in tek consumer'ı bildirim handler'ı (`BookingNotificationHandlers.cs:118-171`, satır 135-137 "%X iade işleme alındı" metni); `CancelBookingCommandHandler` yalnız `RefundPercentage` set ediyor, Payment aggregate'e hiç dokunmuyor; `AdminCancelBookingCommand.cs:36-46` de refund tetiklemiyor — web admin UI "%100 iade" vaat ediyor (`bookings/page.tsx:240-243`).
- Öneri: Cancel handler'larında (veya event consumer'da) `booking.PaymentId` doluysa `RefundPercentage`'dan `AddRefund` + PSP `RefundAsync`; başarısızlıkta Hangfire retry kuyruğu + "iade beklemede" durumu. `CancellationFlowTests`'e refund kaydı assertion'ı. Prod'da müşteri parasının platformda kalması 6502 sayılı Tüketicinin Korunması Hakkında Kanun riski.
- Efor: 1-2 gün

**P0-5. Gerçek ödeme zinciri yok: PayTR/Iyzico stub + webhook/3DS callback endpoint'i hiç yazılmamış + mobile ödeme UI bağlanmamış**
- Kanıt: `PayTrPaymentProvider.cs:12` "This is a SCAFFOLDED stub" (placeholder URL, `VerifyAsync` daima false); `IyzicoPaymentProvider.cs` aynı; Endpoints altında hiçbir `/webhooks` route'u yok; `mobile/app/booking/confirm.tsx:131-135` "Ödeme adımı geliştirme aşamasında" hardcoded — mobile'da tek payment API çağrısı yok. Not: eski GAP-ANALYSIS'in "3DS callback mevcut" iddiası **yanlıştı**.
- Öneri: Hesap beklemeden yapılabilir kısım: `POST /v1/webhooks/paytr` iskeleti (HMAC doğrulama + idempotent completion + duplicate/out-of-order tolerans + integration testleri) ve mobile WebView checkout ekranı mock provider ile uçtan uca. PayTR gerçek entegrasyonu founder merchant hesabı sonrası; Iyzico fallback V1.5.
- Efor: webhook iskeleti + mobile UI: 4-6 gün; PayTR gerçek: 1-2 hafta (🟣 founder hesap bloklu)
- **Güncelleme 2026-07-30 (RFC-006 implementasyonu):** Hesap beklemeden yapılabilir kısmın tamamı kapandı — mock provider ile uçtan uca müşteri ödeme akışı (mobile seçim kartı `confirm.tsx` + checkout ekranı `payment.tsx` + detay/owner rozeti), `POST /v1/webhooks/paytr` iskeleti (HMAC doğrulanmış şema + fail-closed config guard + idempotent completion + tutar-mismatch koruması) ve kapora modeli (`PaymentKind` + `Payment:DepositRate` config, ceil yuvarlama + kapora ≥ komisyon startup invariantı) canlı. As-built kayıt: [`docs/04-modules/07-payment.md`](04-modules/07-payment.md) §14. **Kalan yalnız founder-bloklu (🟣) gerçek PayTR credentials + şu canlıya alma checklist'i:**
  1. PayTR key/salt env değerleri (`PayTr:MerchantKey`/`MerchantSalt` + merchant id) ve sandbox'ta bir gerçek callback smoke testi.
  2. `total_amount` vs `payment_amount` taksit/vade-farkı kuralı teyidi — webhook tutar-mismatch koruması şu an `total_amount == GrossAmountMinor` eşitliği bekliyor; taksitli ödemede `total_amount` büyük gelebilir (`ProcessPaytrWebhookCommand.cs:53-72` içindeki not).
  3. MDR settlement hesabı: capture'da `pspMdrMinor` şu an 0 geçiliyor — gerçek PSP kesintisinin `NetToBarberMinor` hesabına bağlanması.
  4. WebView PSP host whitelist'i: `CheckoutWebView` originWhitelist'ine gerçek PayTR iFrame host'larının eklenmesi.
  5. Stale PSP session / provider-refresh davranışı: 30dk `ExpiresAt` geçmiş checkout URL'inin reddi + yeni initiate'e zorlama akışının gerçek PSP ile doğrulanması.
  6. `PayTrOptions`'a `ValidateOnStart` — prod'da eksik key/salt boot'ta yakalansın (webhook'taki fail-closed 503 guard'ı ikinci savunma katmanına insin).
  7. **Out-of-order "failed→success" sıralama deliği (final review I-1):** ReinitiateCheckout sonrası aynı `merchant_oid` ile eski session'ın GEÇ gelen *failed* callback'i taze payment'ı `Failed` yapar; ardından yeni session'ın *success* callback'i Capture guard'ına takılır ve "OK" döndüğü için PayTR retry etmez → para PSP'de tahsil edilmiş ama platformda kayıtsız kalabilir. Çözüm adayları: failed dalında da tutar/session-mismatch guard'ı VEYA session-bazlı merchant_oid (per-session suffix) — `Payment.cs:140` provider-refresh TODO'suyla birlikte ele alınmalı.

**P0-6. NetGSM stub + config key mismatch: prod'da OTP asla SMS'le gitmez, düz metin loga düşer → hiçbir kullanıcı kayıt olamaz**
- Kanıt: `backend/src/Kuaforum.Infrastructure/Auth/NetGsmSmsService.cs:24,49-51` — options `UserCode`/`Originator` bekliyor, `UserCode` boşsa "MOCK SMS ... code is {Code}" Warning loglar, doluysa bile HTTP çağrısı yok (TODO); `appsettings.Production.json` ise `NetGsm:Username/Password/Header` veriyor → `UserCode` asla bind olmaz, prod HER ZAMAN mock fallback. OTP-only auth'ta bu tam launch blocker + OTP'nin loga düşmesi KVKK/hesap ele geçirme riski.
- Öneri: Gerçek NetGSM HTTP client'ını yaz, config key'lerini eşitle, fallback'te kodu ASLA loglama (hata dön); SMS pumping için IP-bazlı rate limit ekle. Founder tarafı: NetGSM hesabı + originator onayı paralel başlamalı.
- Efor: 2-3 gün kod (🟣 hesap onayı hariç)

**P0-7. Production deploy edilemez: Dockerfile yok, deploy workflow yok, `${VAR}` placeholder'lar prod boot'u kırar**
- Kanıt: Repo genelinde `find -iname 'Dockerfile*'` → 0 sonuç; `.github/workflows`'ta sadece `ci.yml`; `appsettings.Production.json` satır 3-35 tamamen `${POSTGRES_CONNECTION_STRING}` tarzı placeholder — .NET config bunları expand ETMEZ: Serilog Seq sink startup'ta crash eder, `AllowedHosts` literal kalınca tüm istekler 400. `Program.cs:16`'daki `Contains("${SENTRY_DSN")` guard'ı sorunun bilindiğini kanıtlıyor.
- Öneri: Placeholder pattern'ini tamamen bırak → .NET native env override (`ConnectionStrings__Postgres` vb.); backend multi-stage Dockerfile (non-root + HEALTHCHECK) + web-admin standalone Dockerfile + `infra/docker-compose.prod.yml` (Caddy TLS) + GitHub Actions: build → GHCR → Hetzner SSH deploy. ForwardedHeaders middleware'i de bu pakete dahil et. Staging'de `ASPNETCORE_ENVIRONMENT=Production` ile bir kez boot smoke testi.
- Efor: 3-5 gün

**P0-8. Repo'ya commit'li admin şifresi production'da ilk boot'ta seed edilir**
- Kanıt: `backend/src/Kuaforum.Api/appsettings.json:25-31` (base — tüm ortamlara uygulanır): `Seed:Admin` `Email=admin@kuaforum.local`, `Password="ChangeMe!Admin2026"`; `appsettings.Production.json` bu bloğu override etmiyor; `AdminSeeder` her boot'ta koşuyor. Ayrıca Development'ta `FixedTotpSecret=JBSWY3DPEHPK3PXP` commit'li.
- Öneri: `Seed:Admin` bloğunu base'den `appsettings.Development.json`'a taşı; prod admin bootstrap yalnız env variable (`Seed__Admin__Password`) ile + ilk login'de şifre değişimi zorunlu; commit'li şifreyi rotate et (git history'de kaldı sayılır).
- Efor: 2-3 saat

**P0-9. Owner onboarding mobile'da uçtan uca kopuk: salon oluşturma disabled + KYC upload placeholder + KYC reject sonsuz kilit**
- Kanıt: (1) `mobile/app/(barber)/profile.tsx:113-114` "Salon oluştur" disabled ("şu an web admin'den") — backend `POST /v1/owner/barber-shop` hazırken; (2) `kyc.tsx:145-171` manuel storage-key text input ("Test için herhangi bir string girebilirsin"), `uploadKycDocument` (`barber.ts:137-148`) hiçbir ekranda çağrılmıyor, picker paketi yüklü değil; (3) `ReviewKyc` reject dalı shop status'a dokunmuyor (`SubmitKycCommand.cs:105-111`) + `BarberShop.cs:77-84` yalnız Draft→PendingKyc'ye izin veriyor → reddedilen kuaför backend'de **sonsuza dek** başvuramaz — mobile'daki re-submit formu her zaman 400 alır (critic çelişki çözümü). Gerçek bir kuaför tek başına Active olamıyor.
- Öneri: (a) Reject dalına `RevertToDraft()` + "reject→re-submit" integration testi (1-2 saat, önce bu); (b) `expo-document-picker` + `expo-image-picker` ile gerçek upload → mevcut `/v1/uploads/kyc`'a bağla; (c) salon oluşturma formu (ad/cinsiyet/il-ilçe/adres) mobile'a bağla + owner registration akışı kararı (Customer→BarberOwner rol geçişi).
- Efor: 3-4 gün toplam
- **Güncelleme 2026-07-30 (RFC-007): ✅ KAPANDI.** Dört bacak da canlı: (1) salon oluşturma mobile 3 adımlı sihirbaz (`app/onboarding/shop|address|documents.tsx`) `POST /v1/owner/barber-shop`'a bağlı; (2) gerçek KYC upload — `expo-document-picker`/`expo-image-picker` → `POST /v1/uploads/kyc` → submit; (3) reject-kilidi çözüldü — reject dalı shop'u Draft'a döndürüyor, re-submit çalışıyor; (4) Customer→BarberOwner atomik rol geçişi create-shop içinde (unique `ux_barber_shops_owner` index + concurrency 409 + DB-seviyesi index kanıt testi). Owner mobile'dan uçtan uca Active olabiliyor — curl E2E kanıtı: create→refresh(BarberOwner)→address→upload→submit→approve→Active. Kapanışta bulunan son blocker da giderildi: `neighborhoods` tablosu boştu ve NeighborhoodId zorunluydu → adres adımı gerçek hiçbir owner için geçilemiyordu; mahalle artık OPSİYONEL (nullable zincir: `SetAddressRequest`→command→`Address` VO; DB kolonu zaten nullable, migration gerekmedi) + Erzurum 3 merkez ilçesine pilot mahalle seed'i (`Tr81CitySeed.ErzurumNeighborhoods`); verilirse varlık + ilçe-tutarlılık kontrolü (400). Kalan ilgili borçlar: R2 (P1-6), KYC bildirimi (P1-10), TR mahalle verisi genişletme (P2-12).

**P0-10. KVKK aydınlatma metni + ToS uygulamada hiçbir yerde görüntülenemiyor; kayıtta parçalı açık rıza yok**
- Kanıt: `mobile/app/(auth)/login.tsx:123-126` legalLink Text'lerinin onPress'i yok (salt görsel); `(tabs)/profile.tsx:169` "Kullanım Koşulları" disabled+"Yakında"; kayıt akışında parçalı rıza ekranı yok — consents ekranı yalnız sonradan yönetim. Master spec "KVKK aydınlatma + parçalı açık rıza her kayıt akışında" der; mevcut "devam ederek kabul" bundled consent'in geçersiz rıza sayılma riski `[unverified 2026-07-23 — hukukçu teyidi gerek]`.
- Öneri: Statik aydınlatma + ToS ekranları (markdown/WebView) + ilk kayıtta ToS zorunlu / Marketing-Location opsiyonel checkbox akışı; metinlerin kendisi founder/hukukçu deliverable'ı — teknik iskeleti metin beklemeden kur.
- Efor: 1-2 gün (metinler hariç)
- **Güncelleme 2026-08-03 (RFC-008): ✅ KAPANDI.** (Not: RFC-008 metninde bu gap "P0-11" olarak anılıyordu — doğru numara P0-10; P0-11 KYC kör onaydır ve RFC-008 kapsamı dışıdır.) Üç bacak da canlı: (1) **Aydınlatma + ToS ekranları** — `mobile/app/legal/[doc].tsx`, versiyonlu legal servisten besleniyor (`GET /v1/legal/{doc}`, AllowAnonymous; login/profil linkleri artık tıklanabilir, bundled consent cümleleri kaldırıldı); (2) **OTP-sonrası VE mevcut kullanıcının ilk açılışında parçalı rıza gate'i** — `app/consent.tsx` + AuthGate `requiresTosConsent` (server-side türetim, tek doğruluk kaynağı `GET /v1/users/me`); ToS zorunlu, Marketing opsiyonel; `POST /v1/users/me/consents/initial` idempotent + eski versiyon satırını supersede eder + audit; (3) **versiyonlu tek-kaynak** — `LegalDocuments.CurrentTosVersion` sabiti; hukuki metin güncellenince versiyon bump'ı tüm kullanıcılara rıza turunu otomatik yeniden açar. Demo personalar güncel ToS rızasıyla seed'li; rıza akışının elle test kapısı consent'siz `DevLoginRole.CustomerFresh` ("Müşteri (rıza ekranı)" quick-login). Kalan ilgili borçlar: P2-13 (renderer sınırları, Marketing re-ask, `Supersede()` intent metodu), İYS (P1-15), hukukçu metin teyidi (§6 `[unverified]`).
- **LAUNCH-CHECKLIST kalemi:** placeholder legal metinlerle canlıya **ÇIKILMAZ** — (a) hukukçu metinleri founder deliverable'ı olarak gelmeli, (b) legal render'ın markdown kapsamı gerçek metne göre gözden geçirilmeli (renderer bilinçli minimal: `#`/`##` başlık + `> ` uyarı + paragraf + `**bold**` strip; `###`/liste/gerçek bold YOK — bkz. P2-13). Placeholder `> PLACEHOLDER` uyarı kutuları bilinçli olarak kullanıcıya görünür bırakıldı — canlıda görünürlerse launch hatasıdır.

**P0-11. KYC onayı kör: admin yüklenen belgeleri hiç göremeden onaylıyor**
- Kanıt: Kuyruk DTO'su sadece `DocumentCount` taşıyor (`AdminQueries.cs:63-71`); belge listeleme/görüntüleme endpoint'i yok (`UploadsEndpoints`'te sadece POST, `AdminEndpoints`'te KYC belge GET'i yok); web-admin `kyc/page.tsx:79` sadece "{documentCount} belge" yazıyor. PayTR pazaryeri sub-merchant onboarding'inde belge doğrulaması yapılmadığı ortaya çıkarsa platform sorumluluğu.
- Öneri: `GET /v1/admin/kyc/{submissionId}/documents` (Admin policy + audit) + kısa ömürlü imzalı URL veya auth'lu proxy ile belge servis; KYC modalına önizleme. KYC belgeleri hassas — `UseStaticFiles` ile public açma.
- Efor: 1 gün

**P0-12. Mobile production build store'a gönderilemez: EAS projectId yok, API URL localhost, icon/splash asset'leri hiç yok, FCM config yok**
- Kanıt: `mobile/src/api/client.ts:10-16` `EXPO_PUBLIC_API_BASE_URL` fallback'i localhost/10.0.2.2; `eas.json` profillerinde env bloğu yok; `app.json`'da `eas.projectId` yok, icon/splash/adaptiveIcon key'leri yok, `mobile/assets` dizini mevcut değil; `expo-notifications` plugin + `googleServicesFile` yok → standalone build'de push çalışmaz. Ayrıca `login.tsx:66-93` "Hızlı Giriş (Demo)" bölümü `__DEV__` guard'sız — store review'da reject riski.
- Öneri: `eas init` + production profile env (`EXPO_PUBLIC_API_BASE_URL=https://api.kuaforum.com`, `SENTRY_DSN`); icon/splash asset'leri `app.json`'a; `expo-notifications` plugin + `googleServicesFile`; demo login'i `{__DEV__ && ...}` ile sar (15 dk). Founder tarafı: Apple/Google/Expo hesapları + ikon tasarımı.
- Efor: 1-2 gün (🟣 hesaplar + tasarım hariç)

**P0-13. e-Arşiv/fatura (Nilvera) katmanı sıfır kod — gerçek tahsilat başladığı an vergi denetimi riski**
- Kanıt: `grep 'Nilvera|Invoice|Fatura|EArsiv' backend/src` → 0 sonuç; CLAUDE.md stack tablosu çift fatura akışını (kuaför→müşteri %20 KDV + platform→kuaför komisyon %20 KDV) Day-1 kararı olarak tanımlıyor; `PaymentSettledEvent` var ama fatura tetikleyen consumer yok. Faturasız komisyon tahsilatı denetimde doğrudan ceza kalemi.
- Öneri: PayTR ile aynı milestone'a bağla (mock ödemeyle canlıya çıkılmadığı sürece launch'ı bloklamaz ama gerçek para = fatura şart): Invoice modülü + `PaymentSettledEvent`→Nilvera draft + Hangfire retry; ilk adım olarak settle edilen ödemeleri faturalanabilir kayıt tablosunda biriktir. Founder: Nilvera hesabı + mali müşavir teyidi.
- Efor: ~1 hafta (🟣 hesap sonrası); kayıt tablosu şimdi 0.5 gün

---

## 4. P1 — Yüksek Risk Gap'ler (16 madde)

**P1-1. Concurrent double-booking koruması eksik: lock granülaritesi hatalı, DB exclusion constraint yok, test yok**
- Kanıt: `InitiateBookingCommand.cs:74` lock key `slot-lock:{StylistId}:{ScheduledAt:yyyyMMddHHmm}` sadece başlangıç dakikasına bağlı — 10:00-11:00 ve 10:30-11:30 farklı lock alıp busy-range check'i aynı anda geçebilir; `BookingConfiguration.cs:44`'te yalnız normal index, EXCLUDE constraint yok; testlerde paralel istek senaryosu sıfır.
- Öneri: (1) `btree_gist` ile `EXCLUDE USING gist (stylist_id WITH =, tstzrange WITH &&)` aktif statüler için — migration; (2) lock'u stilist bazına indirge veya constraint violation'ı 409'a map'le; (3) `Task.WhenAll`'lu integration test: 2 overlapping initiate → tam 1 başarılı.
- Efor: 2-3 gün

**P1-2. Outbox pattern yok — commit ile publish arası crash'te event (bildirim/refund side-effect) kaybolur**
- Kanıt: `ApplicationDbContext.cs:63-87` `SaveChangesAsync`: event'ler commit SONRASI in-process publish, handler exception'ı isteği patlatır; `grep 'OutboxMessage'` src'de yalnız yorum satırı. CLAUDE.md §6 kuralı uygulanmamış.
- Öneri: `OutboxMessages` tablosu + aynı transaction'da serialize + Hangfire dispatcher + idempotent consumer. Webhook işiyle birleşik getir; MVP'de bilinçli erteleniyorsa RFC'ye yaz.
- Efor: 2-3 gün (webhook ile birleşik)

**P1-3. PendingTimeout job yok — ödenmeyen Pending booking'ler slotu süresiz kilitliyor (bedava DoS)**
- Kanıt: `BookingCancellationReason.PendingTimeout` tanımlı (`BookingStatus.cs:18`) ama `BackgroundJobs` altında sadece 4 job var, Pending süpüren yok; `BookingBusyRangeQuery.cs:14-19` Pending'i busy sayıyor → login müşteri ödeme başlatmadan art arda Pending açıp stilistin tüm takvimini bloklayabilir.
- Öneri: `ExpirePendingBookingsJob` (15dk'da bir, 30dk+ Pending → `Cancel(PendingTimeout)`) + kullanıcı başına eşzamanlı Pending limiti (ör. 3).
- Efor: 3-5 saat

**P1-4. Rol policy'leri tanımlı ama hiçbir endpoint'te kullanılmıyor — Customer rolü owner/stylist endpoint'lerine erişebiliyor**
- Kanıt: `AuthSetup.cs:46-52`'de Customer/BarberOwner/Stylist policy'leri tanımlı; grep sonucu yalnız "Admin" 2 yerde kullanılıyor; `/v1/owner/barber-shop` (`BarberEndpoints.cs:61`), `/v1/stylists` (`:155`) düz `RequireAuthorization`; `CreateBarberShopCommand`'da rol kontrolü yok — Customer dükkan açabilir.
- Öneri: Grup bazında policy uygula (`ownerGroup.RequireAuthorization("BarberOwner")`); Customer→BarberOwner rol geçiş akışını owner onboarding işiyle birlikte bilinçli tasarla.
- Efor: 0.5 gün
- **Güncelleme 2026-07-30 (RFC-007): ✅ KAPANDI.** Owner grubu `BarberOwner`, stylist grubu `Stylist` policy'sinde; tek bilinçli istisna create-shop (rol geçiş tetiği, Customer'a açık). Integration pinleri: Customer → owner/stylist endpoint'leri 403; create-shop 201; promote sonrası bayat token 403 → refresh sonrası 200 (`OwnerOnboardingTests.cs`).

**P1-5. No-show server-side zaman guard'ı yok — owner sabah 09:00'da akşam randevusunu iadesiz no-show işaretleyebilir**
- Kanıt: `MarkNoShowCommandHandler` (`BookingActions.cs:171-191`) yalnız sahiplik + status kontrolü; `Booking.MarkNoShow` (`Booking.cs:178`) randevu saatine hiç bakmıyor; koruma sadece client-side (`booking/[id].tsx:209`). No-show iadesiz → müşteri parasını kaybeder, itirazda platform aleyhine.
- Öneri: Handler'a guard: `ScheduledAt` + grace (15dk) geçmeden no-show reddet (start/complete'teki mevcut time-window pattern'ini uygula); `today.tsx` butonunu aynı koşula bağla.
- Efor: 2-4 saat

**P1-6. KYC belgeleri lokal diskte + upload'lar hiç servis edilmiyor — R2 yok, restart'ta compliance evrakı kaybolur**
- Kanıt: `DependencyInjection.cs:155` `IObjectStorage` koşulsuz `LocalFileObjectStorage`; `GetReadableUrl` localhost URL üretiyor ama `Program.cs`'te `UseStaticFiles` yok → URL'ler lokalde bile 404; R2/S3 sınıfı repo'da yok. Container re-deploy'da vergi levhası/ruhsat kaybolur.
- Öneri: `AWSSDK.S3` endpoint-override ile R2 adapter (MinIO ile hesapsız test edilebilir) + config flag'li Local/R2 seçimi; belgeler public URL yerine auth'lu endpoint'ten (P0-11 KYC görüntüleme işiyle birleşik). Geçici: uploads'ı persistent volume'a mount.
- Efor: 1-2 gün
- **Güncelleme 2026-08-05: ✅ kod hazır, 🟣 founder aksiyonu bekliyor.** `S3ObjectStorage` (`backend/src/Kuaforum.Infrastructure/Storage/S3ObjectStorage.cs`, AWSSDK.S3 4.0.101.6 — nuget'te güncel major artık 3.7.x değil 4.x, doğrulanıp öyle pinlendi) `IObjectStorage`'ın S3/R2 implementasyonu; validation + storageKey üretimi `ObjectStorageRules`'a çıkarılıp `LocalFileObjectStorage` de ona yönlendirildi — iki backend arasında content-type/boyut/anahtar şekli davranış paritesi garantili (aynı `HashSet`, aynı hata mesajları). `GetReadableUrl` presigned GET (15dk TTL, `AWSSDK.S3`'ün `GetPreSignedUrlRequest`'i) — KYC belgesi için public URL asla üretilmiyor. `ObjectStorage:Provider=Local|S3` (`DependencyInjection.cs`), default her ortamda (Development + Production) `Local` — prod boot founder R2 bucket'ı açmadan kırılmaz. `Provider=S3` seçilip `ObjectStorage:S3:{ServiceUrl,Bucket,AccessKeyId,SecretAccessKey}` eksikse `AddOptions<ObjectStorageOptions>().ValidateOnStart()` (PaymentOptions'daki kalıbın aynısı) `Host.StartAsync`'te `OptionsValidationException` ile fail-fast — gerçek ASP.NET Core host pipeline'ında `ObjectStorageFailFastTests.cs` ile kanıtlı (MinIO'suz: hata S3 client hiç kurulmadan boot'ta patlıyor). Bucket lifecycle kararı: adapter bucket'ı runtime'da OLUŞTURMAZ (least-privilege R2 token'ları genelde CreateBucket yapamaz) — founder R2 dashboard'dan elle açar, test fixture'ı (`ObjectStorageS3Tests.cs`, Testcontainers.Minio 4.13.0) aynı adımı MinIO'da elle taklit eder. Review'da bulunup düzeltilen gerçek bug: `GetPreSignedUrlRequest.Protocol` SDK'da default HTTPS — plain-HTTP MinIO endpoint'inde TLS handshake'i patlatıyordu; ServiceUrl şemasından türetilen `Protocol` ile düzeltildi (R2 prod'da hep https, MinIO testte hep http — otomatik doğru). R2'ye özgü bilinen SDK uyumsuzluğu (`DisablePayloadSigning`, streaming SigV4 R2'de desteklenmiyor) config'e bağlandı, MinIO'da false kalıyor. Testcontainers.Minio 4.13.0 eklerken Testcontainers.PostgreSql/Redis'i de aynı sürüme çekmek gerekti (4.0.0 ile karışık kalınca `MissingMethodException` — sibling paketler aynı core `Testcontainers` sürümünde kalmalı). Kanıt: 4 yeni MinIO adapter testi (round-trip, presigned URL gerçek HTTP indirme, eksik key → `FileNotFoundException`, content-type reddi) + 1 fail-fast host testi, hepsi yeşil; mevcut `KycFlowTests` (Local provider, default) değişmeden yeşil kaldı. **🟣 Kalan:** founder Cloudflare R2 bucket + "Object Read & Write" API token açmalı, `.env.prod`'a `ObjectStorage__Provider=S3` + `ObjectStorage__S3__*` (bkz `infra/.env.prod.example` + `infra/README.md` §3.2/§4.1) girmeli — sonrası tek env değişikliği, kod tarafı bekliyor. İlk R2 geçişinde 1 smoke upload şart — `DisablePayloadSigning=true` yolu MinIO'da test edilemiyor (review 2026-08-05, test-kanıtsız tek dal).

**P1-7. Backup stratejisi hiç yok — booking/payment/KYC verisi tek kopyada**
- Kanıt: Repo genelinde pg_dump/backup/snapshot içeren script veya compose servisi yok; postgres-data tek named volume, offsite kopya ve restore runbook yok. Vergi/e-Arşiv kayıtları için saklama denetim gereksinimi.
- Öneri: Günlük pg_dump → R2 upload cron container (veya Hetzner snapshot + wal-g), 7+ gün retention; restore'u staging'de bir kez test edip `infra/README`'ye yaz. Deploy paketiyle birlikte.
- Efor: 1-2 gün

**P1-8. CI kalite kapıları delik: Application testleri (51) hiç koşmuyor, mobile jest/lint CI dışı, net8 retarget commit'lenmemiş, coverage gate yok**
- Kanıt: `ci.yml:47-51` yalnız Domain + Integration; `tests/Kuaforum.Tests.Application` adımı YOK; mobile job sadece typecheck; git status: 7 csproj net8.0 retarget'i uncommitted, `ci.yml` dotnet-version "10.0.x"; ölçülen domain coverage %54.5 (hedef %70), hiçbir `--collect` yok.
- Öneri: `ci.yml`'e Application test adımı + mobile "pnpm lint && pnpm test" (jest-expo preset ekleyerek); net8 retarget'i commit'le + dotnet-version 8.0.x + global.json; coverage'ı %55 threshold'la kilitle, sprint başına +5 hedefle.
- Efor: 1 gün

**P1-9. Web admin oturum/token yönetimi: refresh token çöpe atılıyor (15dk'da oturum ölüyor), localStorage, route guard yok**
- Kanıt: `login/page.tsx:21` sadece `accessToken` saklanıyor; `src/lib/api.ts:4-19` localStorage (XSS'e açık); `apiFetch` 401'de sadece hata fırlatıyor, login redirect yok; `middleware.ts` mevcut değil — dashboard shell'i token'sız da render olur.
- Öneri: Kısa vade: refreshToken sakla + 401'de tek seferlik refresh-retry + başarısızsa login redirect + layout'ta token check (0.5 gün). Orta vade: httpOnly cookie + Next middleware guard.
- Efor: 0.5 gün kısa vade

**P1-10. KYC onay/red bildirimi backend'de hiç gönderilmiyor — UI "bildirim gelir" vaat ediyor, onboarding funnel'ı sessizce ölüyor**
- Kanıt: `KycApprovedEvent`/`KycRejectedEvent` raise ediliyor (`KycSubmission.cs:52,68`) ama Application genelinde bu event'lere abone tek handler yok; `kyc.tsx:121` "Onaylandığında bildirim gelir" diyor.
- Öneri: Mevcut booking notification pattern'iyle (NotificationLog + push + feed) iki handler ekle; red bildirimine rejection reason'ı taşı.
- Efor: 3-5 saat

**P1-11. Owner kazanç/komisyon görünürlüğü sıfır + "Ciro" yanıltıcı (gerçekleşmemiş randevuları sayıyor)**
- Kanıt: %10 komisyon (`InitiateBookingCommand.cs:44`) hiçbir owner ekranında gösterilmiyor; net kazanç/settlement/rapor yok; `today.tsx:63-65` "Ciro" `isBookingRevenueCounted` ile Confirmed+Started'ı da sayıyor (`format.ts:177-183`).
- Öneri: (1) Ciro'yu Completed-only yap veya "beklenen ciro" etiketle (1 saat); (2) brüt/komisyon/net kırılımlı basit aylık kazanç ekranı (mevcut tablolardan SELECT, yeni tablo gerekmez). PayTR settlement raporu PSP sonrası.
- Efor: 2-3 gün

**P1-12. Walk-in müşteri akışı mobilde erişilemez — MVP farklılaştırıcısı rozeti aksiyon vaat edip vermiyor**
- Kanıt: `(tabs)/index.tsx:218` + `search.tsx:176` "Şu an müsait — sıraya gir" rozeti tıklanamıyor; `barber/[id].tsx`'te walk-in CTA yok; `confirm.tsx:40` `isWalkIn` her zaman false. Backend hazır (WalkInFlowTests + ExpireWalkInJob).
- Öneri: Kuaför detayında `hasWalkInAvailable` ise "Sıraya gir" → `isWalkIn:true` initiate onay ekranı. Pending randevuların Bugün ekranından onaylanamaması da aynı pakette çözülsün.
- Efor: 1-2 gün

**P1-13. SPEC BORCU: RFC disiplini sıfır + 15/16 modül spec'siz + GAP-ANALYSIS iki yönde bayat**
- Kanıt: `docs/rfc` mevcut değil, `docs/04-modules`'te sadece 00+01; master spec v0.1.0 donmuş/imzasız; kilitli kararlardan RFC'siz sapılmış: .NET10→8 (csproj net8.0), RS256→HS256 (`JwtTokenService.cs:34`), Meilisearch→SQL ILIKE, Erzurum fiili pilot.
- Öneri: Retrospektif RFC paketi (RFC-001 .NET8, RFC-002 SQL-search MVP, RFC-003 HS256, RFC-004 Erzurum pilot, RFC-005 e-posta kayıt + OAuth mobile V1.5 ertelemesi) + kritik 3 modülün (Payment/Booking/Availability) mevcut davranışını belgeleyen spec + master spec 0.2.0 founder onayı.
- Efor: 3-4 gün toplam

**P1-14. Konum kullanılmadan declare edilen izinler store reject riski + mahalle differentiator'ı yarım**
- Kanıt: `app.json`'da iOS `NSLocationWhenInUse` + Android `ACCESS_FINE/COARSE_LOCATION` declare'lı ama `expo-location`/`react-native-maps` yüklü değil; Neighborhood entity var ama seed'de 0 mahalle insert'i, `SearchBarbersQuery`'de neighborhoodId/mesafe/open-now parametresi yok. Discovery store persist de yok (her açılışta Erzurum'a döner).
- Öneri: Launch için minimum: kullanılmayan izin declare'larını kaldır (30 dk) + discovery store'a AsyncStorage persist; mahalle bootstrap (NVI açık veri) + PostGIS mesafe sıralamasını ayrı milestone yap veya vaadi RFC ile daralt.
- Efor: izin+persist 2 saat; mahalle 2-5 gün

**P1-15. İYS (6563 ETK) ticari elektronik ileti uyumu hiç ele alınmamış**
- Kanıt: `grep 'iys|ileti yönetim' backend+docs` → 0 sonuç; Marketing consent domain'de var ama İYS kaydı ve rızaların İYS bildirimi hiçbir spec/kodda geçmiyor `[unverified 2026-07-23 — hukukçu teyidi gerek]`. Transactional bildirimler muaf olsa da sınır tanımsız.
- Öneri: Marketing kanalı (kampanya push/SMS) açılmadan ÖNCE İYS gereksinim araştırması + in-app consent↔İYS senkron stratejisi; transactional/marketing sınırını spec'e yaz. Launch'ta marketing gönderimi yoksa bloklamaz ama ilk kampanyadan önce şart.
- Efor: araştırma 1 gün; entegrasyon 3-5 gün (kampanya öncesi)

**P1-16. Küçük güvenlik yaması paketi: Hangfire Host-header bypass + HardDeleteUsersJob testi + StylistService kararı**
- Kanıt: (1) `Program.cs:180-181` Hangfire dashboard "Host: localhost" ile auth atlanır (Host header client kontrolünde); (2) `HardDeleteUsersJob` 4 job'dan tek test edilmeyeni — KVKK kalıcı silme kanıtsız; (3) `StylistService` entity'si var ama hiçbir handler sorgulamıyor — stilist vermediği hizmete rezerve edilebilir (`InitiateBookingCommand.cs:58-65` yalnız shop-service kontrolü).
- Öneri: (1) Bypass'ı `RemoteIpAddress` loopback veya `env.IsDevelopment()`'a çevir (1 saat); (2) hard-delete integration testi (1 gün); (3) StylistService için karar: MVP'de "her stilist tüm hizmetleri verir" kararını RFC'ye yaz ve entity'yi askıya al (1 saat) — enforce etmek istiyorsan 1 gün.
- Efor: toplam 1.5-2 gün

---

## 5. P2 — Backlog / Kalite Borcu (13 madde)

**P2-1. Komisyon oranı hard-coded 0.10m — `CommissionTier` alanı ölü**
- Kanıt: `InitiateBookingCommand.cs:44` `private const DefaultCommissionRate=0.10m`; `BarberShop.CommissionTier` "standard" set ediliyor ama hiçbir yerde okunmuyor.
- Öneri: `ICommissionPolicy` servisi (config-driven) + shop'tan tier okuma; şimdilik tek tier olsa bile magic number'ı configuration'a taşı.
- Efor: 2-3 saat

**P2-2. Arama ölçek borcu: rating her sorguda korelasyonlu Average subquery + Meilisearch kararı askıda**
- Kanıt: `SearchBarbersQuery.cs:85,108` her shop için inline Reviews.Average; denormalize `AverageRating`/`ReviewCount` alanı yok; Meilisearch compose'da çalışıyor ama backend'de 0 referans.
- Öneri: `ReviewApproved`/`Rejected` consumer'ı ile shop üzerinde `AverageRating`+`ReviewCount` denormalize et; Meilisearch'ü RFC-002 ile V1.5'a yazıp container'ı compose'dan kaldır.
- Efor: 1 gün

**P2-3. i18n kararı askıda: i18next yüklü, 0 kullanım — CLAUDE.md kuralı fiilen ihlal**
- Kanıt: mobile `package.json`'da i18next 23 + react-i18next 15; `grep useTranslation/initReactI18next` → 0; ~7.200 satır ekran kodu hardcoded Türkçe.
- Öneri: Dürüst yol: dependency'leri kaldır + CLAUDE.md kuralını "V2'de i18n" olarak revize et (RFC'ye bağla). Retrofit maliyeti ekran sayısıyla lineer büyüyor — karar şimdi verilmeli.
- Efor: kaldırma 30 dk; retrofit 3-4 gün

**P2-4. Backend OAuth ölü kod: mobile'da Apple/Google giriş butonu hiç yok**
- Kanıt: Backend `/v1/auth/oauth/{provider}` JWKS doğrulamalı implement (`AuthEndpoints.cs:41`) ama mobile'da OAuth metodu/butonu sıfır — master spec auth kararının müşteri yüzeyi yapılmamış. Apple guideline 4.8: üçüncü taraf login sunulursa Sign in with Apple zorunlu `[unverified 2026-07-23]`.
- Öneri: OTP-only launch RFC'siyle V1.5'a ertele (backend kodu zararsız, kalsın) veya `expo-apple-authentication` + `google-signin` ile bağla.
- Efor: RFC 1 saat; impl 2-3 gün

**P2-5. Veri saklama/purge job'ları yok: RefreshToken/OtpCode/NotificationLog sınırsız büyür + AuditLog 5 yıl retention politikasız**
- Kanıt: `BackgroundJobs`'ta yalnız 4 job; `grep 'ExpiredToken|PurgeToken'` → 0; revoke edilmiş token'lar, tüketilmiş OTP'ler, eski bildirimler hiç silinmiyor; AuditLog için retention/arşiv kodu 0. `HealthData` consent tipi de karşılıksız — işlenmeyen özel nitelikli veri için rıza toplamak KVKK m.6 soru işareti.
- Öneri: Haftalık purge job (90g+ token, tüketilmiş OTP, 6ay+ okunmuş bildirim) + KVKK saklama-imha politikası dokümanı; HealthData consent'i kaldır veya kullanım planını yaz.
- Efor: 1 gün

**P2-6. Mobile UX/erişilebilirlik borçları: reschedule elle metin girişi, barber detay bölüm error state'leri, accessibility kapsamı, review onay metni çelişkisi**
- Kanıt: `booking/[id].tsx:225-247` reschedule regex'le serbest metin (slot picker yeniden kullanılmamış); `barber/[id].tsx:126-145` yalnız shopQuery guard'lı — stylists/services hata verirse sessizce boş; accessibilityLabel ~20 kullanım; `review/submit.tsx:27-28` "onaylanınca görünür" diyor ama backend default-Approved.
- Öneri: Slot grid'i paylaşımlı komponente çıkar + reschedule'da kullan; bölümlere inline "tekrar dene" fallback; Card/Chip'e accessibility prop'ları; review metnini API status'una göre koşullu yap.
- Efor: toplam 2-3 gün (parça parça alınabilir)

**P2-7. Web admin kalite borçları: shadcn migration yapılmadı + TanStack ölü bağımlılık + KYC/Users pagination yok + bayat login uyarısı**
- Kanıt: tailwind.config yok, tüm stiller inline; `@tanstack/react-query+table` dependency ama 0 kullanım; `kyc/page.tsx:39` + `users/page.tsx` page parametresi geçmiyor (21. kayıt sessizce görünmez); `login/page.tsx:83-87` "backend wired değil" notu bayat; Next 15.0.4 eski.
- Öneri: Pagination'ı iki sayfaya kopyala (2-3 saat) + bayat notu kaldır (10 dk) hemen; shadcn migration'ı tek seferde (1-2 gün); Next'i 15.x son patch'e yükselt.
- Efor: hızlı kısım 0.5 gün; migration 1-2 gün

**P2-8. Favoriler + owner kasada walk-in kayıt + salon profil düzenleme mobile + shop galeri + geçmiş takvim — bilinçli scope kararı bekleyen ürün boşlukları**
- Kanıt: `grep favorite` → 0 (backend+mobile); owner panelinde kapıdan gelen müşteri kaydı yok (komisyon bypass + eksik ciro); PATCH shop + PUT address backend'de var, mobile'da salt-okunur; ShopPhoto entity yok; `calendar.tsx:15-18` yalnız +14 gün. Hiçbiri spec'te "ertelendi" olarak işaretli değil.
- Öneri: Hepsini V1.5 backlog'una RFC/GAP-ANALYSIS ile kayıt altına al; ucuz kazanımlar: salon profil düzenleme 0.5-1 gün, geçmiş takvim 0.5 gün, favoriler 1-2 gün.
- Efor: kayıt 1 saat; opsiyonel işler 2-4 gün

**P2-9. Küçük backend tutarsızlıkları: Resend config key mismatch, kök route 404, rate limiter atomik değil, Suspend reason kayboluyor, domain'de `DateTimeOffset.UtcNow` gömülü**
- Kanıt: `appsettings.Production.json` "Resend:FromAddress" vs `ResendOptions.From`; `Program.cs:129` "/" → `/openapi/v1.json` ama OpenAPI paketi yok → 404; `RedisRateLimiter.cs:19-23` INCR ile EXPIRE arası crash'te key TTL'siz kalır; `BarberShop.Suspend(reason)` parametreyi hiçbir alana yazmıyor; `Booking.cs:45,86,200` `UtcNow` direkt.
- Öneri: Config key'leri eşitle, kök route'u `/health`'e çevir, INCR'ı Lua/transaction ile atomikleştir, `SuspendedReason`/`At` alanı ekle, `Cancel`'da `reason==NoShow`'u reddet, zaman kararlarını parametreye çıkar.
- Efor: toplam 1 gün

**P2-10. Monitoring/ölçüm borcu: uptime izleme yok, metrics yok, P95<300ms budget hiç ölçülmüyor, contract/E2E testleri yok**
- Kanıt: Prometheus/OpenTelemetry grep → 0; Better Stack config yok; repo'da hiçbir load-test script'i (k6/NBomber) yok; Bruno/Schemathesis → 0; Detox yok; web-admin test altyapısı sıfır.
- Öneri: Deploy sonrası ilk hafta: Better Stack ile `/health/ready` izleme (1 saat) + basit k6 smoke; OpenTelemetry + Schemathesis nightly + Playwright admin smoke'u ölçek sinyali gelince. Şimdi yatırım yapma (premature).
- Efor: uptime 1 saat; kalanı deploy sonrası 2-3 gün

**P2-11 (2026-07-30). RFC-006 implementasyon kalıntıları — küçük kalite borçları (review zincirinden)**
- `BookingPaymentProjection.Resolve` imza sıkılaştırma (nullable tuple yerine daha dar tip) + payment yükleme sorgusunun `LoadPaidAsync` benzeri tek helper'da ortaklaştırılması (`GetBookingQueries.cs` + `GetShopBookingsQuery.cs` aynı kalıbı tekrarlıyor).
- `DepositPaymentTests` doc-header güncelleme + dosya bölme: ortak booking/ödeme akış yardımcılarının `BookingTestApi` extraction'ı ile ayrılması.
- `mobile/app/booking/[id].tsx` 638+ satır — mutation/Alert bloklarının `useBookingActions` hook'una refactor'u.
- `GetStylistsByShopQuery`'ye deterministik `OrderBy` sigortası (sırasız SELECT — UI'da stilist sırası rastgele değişebilir).
- %15 kapora metinlerinin tek kaynağa inmesi: `confirm.tsx` fallback metnindeki "Hizmet bedelinin %15'i" hardcoded oran, `DEPOSIT_RATE`'ten türetilmeli (detay Alert'i 2026-07-30'da tutara çevrildi, fallback metin kaldı).
- Integration suite'te 1/4 oranında gözlemlenen isimsiz flake (reproduce edilemedi, 2026-07-30 tam koşuda temiz) — CI'da izlenmeli, tekrar ederse test adı + seed loglanmalı.
- Webhook failed-retry senaryosunda `payment.webhook_processed` audit satırı birikimi (her retry yeni satır) — retention/purge P2-5 kapsamına dahil edilmeli.
- Efor: toplam 1-1.5 gün (parça parça alınabilir)

**P2-12 (2026-07-30). RFC-007 owner onboarding kalıntıları — küçük kalite borçları (review zincirinden)**
- TR mahalle verisinin resmi kaynaktan genişletilmesi: mahalle seed'i şu an Erzurum-pilot (Yakutiye/Palandöken/Aziziye, yaygın bilinen merkez mahalleler). Tam TR kapsamı için TÜİK/PTT verisi import'u `[unverified — kaynak seçimi araştırılacak]`; mahalle backend'de opsiyonel olduğundan launch'ı bloklamaz.
- `GeoPickerModal` (onboarding) ↔ keşfet `CityPickerModal` birleşmesi — iki ayrı şehir seçici komponenti aynı işi yapıyor, tek paylaşımlı komponente inmeli.
- `BarberApi.getMyShop` hata-yutma ayrımı: her hatada `null` dönüyor — "shop yok (404/403)" ile "network hatası" ayrılamıyor; onboarding devam mantığı network hatasında yanlışlıkla adım 1'e düşebilir.
- Orphan KYC upload'ları: upload edilip submit'e bağlanmayan dosyalar diskte kalıyor — delete endpoint'i veya TTL temizliği (R2 geçişiyle birlikte, P1-6 kapsamına bağla).
- `expo-image-picker` `microphonePermission` prebuild teyidi: config plugin'in iOS Info.plist çıktısı gerçek prebuild ile doğrulanmadı.
- Gerçek geocoding: adres koordinatı şu an `tr-city-centroids` il merkezi yaklaşımı — salon haritada il merkezinde görünür; V1.5'te Google Maps geocoding (master spec kararı) ile değiştir.
- Onboarding concurrency-catch mesaj dürüstlüğü (O2 review M-2): create-shop yarış 409'unda kullanıcıya "zaten işletmen var" yerine jenerik hata metni gösterilebiliyor — mesajı duruma özel hâle getir.
- Promote sonrası refresh-fail + app restart edge'i (final review M-3): bayat access token 15 dk 403 döner, interceptor yalnız 401'de refresh yapar → `getMyShop` null → adım 1 → 409 çıkmazı; getMyShop hata-ayrımıyla birlikte ele al.
- `documents.tsx` yüklenen belge listesi local state (final review M-4): adım 3'ten çıkıp dönen kullanıcı belgeleri yeniden yükler — orphan upload borcuyla birlikte çöz.
- Efor: toplam 1-2 gün (parça parça alınabilir; mahalle import'u ayrı, kaynak kararı sonrası)

**P2-13 (2026-08-03). RFC-008 KVKK rıza kalıntıları — küçük kalite borçları (review zincirinden)**
- **Legal renderer sınırları:** satır-bazlı minimal parser yalnız `#`/`##` başlık + `> ` uyarı kutusu + paragraf tanır; `###` ve daha derin başlıklar paragraf gibi render edilir, liste işaretleri (`- `, `1. `) ayrıştırılmaz, `**bold**` yalnız strip edilir (gerçek bold/italik render yok). Hukukçu metni geldiğinde markdown kapsamı metne göre gözden geçirilmeli (launch-checklist kalemi, bkz. P0-10 güncellemesi); gerekirse V1.5'te kütüphane kararı.
- **Marketing re-ask akışı yok:** `requiresTosConsent` yalnız ToS versiyonuna bakar; `CurrentAydinlatmaVersion` bump'ında eski versiyonla granted Marketing rızası kullanıcıya otomatik yeniden sorulmaz (yalnız kullanıcı bir şekilde consent ekranına dönerse tazelenir). Aydınlatma versiyon değişiminde Marketing'i yeniden sorma akışı tasarlanmalı — İYS işiyle (P1-15) birlikte ele alınabilir.
- **Consent supersede domain intent metodu:** eski-versiyon rızasını kapatıp yenisini yazma iş kuralı şu an handler içinde `existingTos?.Revoke()` + `Consent.Grant(...)` ikilisiyle örtük; domain'e `Supersede(newVersion)` intent metodu eklenip event/audit semantiği tek yerde toplanmalı.
- Efor: toplam 0.5-1 gün (parça parça alınabilir; Marketing re-ask İYS kararına bağlı)

**P2-14 (2026-08-06). RFC-012/013 iş modeli kalıntıları — küçük kalite borçları (review zincirinden)**
- **Kompozit index yok (C3 review M2):** `InitiateBooking` sıcak yolu artık global 3-Pending sayımına ek olarak per-shop policy sayımları yapıyor (2-3 benzer tarama). Mevcut index'ler `ix_bookings_customer` / `ix_bookings_customer_scheduled`; `(customer_id, barber_shop_id, status)` kompoziti yok. MVP hacminde kabul edilebilir; trafik geldiğinde global+per-shop sayımı tek `GROUP BY`'a indirilebilir.
- **`BookingSlotSafetyTests.Exclude_constraint_alone_rejects_overlap_when_the_app_level_lock_is_bypassed` flaky:** `Task.WhenAll` ile eşzamanlı DB yazması yarıştıran, tasarım gereği zamanlamaya duyarlı test; sistem yükü altında 4 koşumdan 1'inde kırıldı, izole 3/3 geçti (2026-08-06 gözlemi). CI'da izlenmeli; kırılırsa deterministik bir bariyer (ör. advisory lock ile senkron başlatma) düşünülmeli.
- **Terk edilmiş `Initiated` CommissionPayment re-initiation'ı bloklar (C1+C2 review I2):** `CommissionPaymentCommands.cs`'te expiry/reinitiate yolu yok — kuaför ödeme başlatıp yarıda bırakırsa yeni deneme sonsuza dek engellenir. Bugün Mock-only olduğu için etkisiz; **gerçek PayTR aktivasyonundan ÖNCE şart** (RFC-006'daki `ReinitiateCheckout` kalıbı örnek alınabilir).
- **`CheckoutRequest.BookingId` slotunda statement id taşınması (C1+C2 review M1):** `CommissionPayment` PSP çağrısında booking alanını statement id için kullanıyor — Mock'a pinli, doc-comment'te "kalıcı kontrat değil" yazılı; gerçek PSP entegrasyonunda ayrıştırılmalı.
- **Mobile sabitleri backend ile elle senkron (C5b review M10):** `mobile/src/lib/booking-policy.ts`'teki 1-3 / 1-365 / 1-100 tavanları `BookingPolicy.cs` ile eşleşiyor ama hiçbir test bunu pinlemiyor; backend aralığı değişirse mobile sessizce eski aralığı gösterir.
- **Kaydedilmemiş değişiklik uyarısı yok (C5b review M9):** kuaför randevu ayarları ekranında `dirty` iken geri/tab geçişinde değişiklikler sessizce kaybolur.
- **`booking-settings` 404 ayrımı (C5b review M7):** `BookingPolicyCommands.cs` PlatformSettings bulunamadığında da 404 dönüyor → mobile "salon yok" boş-durumunu gösterir (yanıltıcı).
- **Mobile i18n borcu:** kuaför paneli ekranlarının hiçbiri i18next kullanmıyor (CLAUDE.md §7 "sabit string yasak" kuralıyla çelişen mevcut durum); bu cycle borcu büyütmedi ama ayrı bir i18n-migration işi gerekiyor (bkz. P2-11 i18n kararı).
- Efor: toplam 1-1.5 gün (parça parça; PayTR-öncesi zorunlu olan tek madde CommissionPayment reinitiation)

---

## 6. Compliance & Legal

Master spec KVKK aydınlatma + parçalı açık rıza + audit log + veri minimizasyonu şartlarını Day-1 kararı sayıyor. Kod tarafında altyapı (Consent entity, audit log, KVKK self-servis) var ama üç alan hukukçu teyidine muhtaç ve şu an **doğrulanamamış**:

| Konu | Durum | Etiket |
|---|---|---|
| Bundled consent geçerliliği ("devam ederek kabul" tek metin, parçalı rıza yok) | P0-10 | `[unverified 2026-07-23 — hukukçu teyidi gerek]` |
| İYS (6563 sayılı Elektronik Ticaretin Düzenlenmesi Hakkında Kanun) ticari elektronik ileti uyumu — marketing consent var ama İYS kaydı/bildirimi hiç ele alınmamış | P1-15 | `[unverified 2026-07-23 — hukukçu teyidi gerek]` |
| Apple App Store Guideline 4.8 (üçüncü taraf login sunulursa Sign in with Apple zorunluluğu) — OAuth backend'de var, mobile'da yok, karar bekliyor | P2-4 | `[unverified 2026-07-23]` |

Diğer compliance kalemleri (KVKK aydınlatma metni, kullanıcı/kuaför sözleşmesi, VERBİS kaydı, çerez politikası) metin/hukuki onay tarafında hâlâ 🟣 founder+avukat işi; teknik iskelet (ekranlar, consent flag'leri) P0-10 kapsamında metinsiz kurulabilir. e-Arşiv çift fatura akışının KDV oranları (%20/%20) CLAUDE.md'de sabit kabul edilmiş ama mali müşavir teyidi P0-13'ün önkoşulu.

---

## 7. Test Envanteri Gerçeği

**Gerçek sayı: 201 backend test** (113 domain + 51 application + 37 integration). CLAUDE.md'nin en son güncellemesindeki **193** rakamı bayat — Domain suite'e sonradan eklenen Fact/Theory'ler sayıyı 201'e çıkardı.

Kritik nüans: **Application test suite (51 test) CI'da hiç koşmuyor.** `.github/workflows/ci.yml:45-51` yalnızca `Kuaforum.Tests.Domain` ve `Kuaforum.Tests.Integration` adımlarını çalıştırıyor; `tests/Kuaforum.Tests.Application` pipeline'da adım olarak yok. Bu, "CI'da test gerekli, kırık test merge yasak" kuralının (CLAUDE.md §9) Application katmanı için fiilen delik olduğu anlamına gelir — 51 validator testi sessizce kırılabilir ve kimse fark etmez.

| Tür | Sayı | CI'da koşuyor mu | Not |
|---|---|---|---|
| Backend Domain unit | 113 | ✅ | Lokalde 113/113 yeşil; ölçülen line coverage %54.5, branch %48.3 (hedef %70+ — tutmuyor) |
| Backend Application (validator) | 51 | 🔴 hayır | `ci.yml`'de adım yok — P1-8 |
| Backend Integration (Testcontainers) | 37 | ✅ | booking lifecycle, cancellation refund matrisi, availability enforcement (13 test), payment initiation, review moderation, account deletion, shop suspension, walk-in, audit log |
| **Toplam backend** | **201** | kısmen | — |
| Concurrent double-booking testi | 0 | — | P1-1 |
| Refund endpoint integration testi | 0 | — | P0-1/P0-5 kapsamında test edilmemiş |
| HardDeleteUsersJob testi | 0 | — | 4 job'dan tek test edilmeyeni, P1-16 |
| Mobile Jest/RNTL | 0 | — | jest kurulu, config/test dosyası yok, P1-8 |
| Mobile Detox E2E | 0 | — | — |
| Web-admin test | 0 | — | test script'i/framework'ü yok |
| API contract (Bruno/Schemathesis) | 0 | — | P2-10 |
| k6/load test | 0 | — | P95<300ms budget hiç ölçülmüyor, P2-10 |

---

## 8. 14C Kapanış Durumu — Önceki Cycle'dan Bu Yana Ne Değişti

Eski GAP-ANALYSIS'in §14C listesindeki 12 maddenin güncel durumu (kod taramasıyla doğrulandı):

| # | Eski madde | Yeni durum | Kanıt |
|---|---|---|---|
| 1 | Mobile customer profile düzenleme eksik | ✅ **KAPANDI** | `mobile/app/account/edit-profile.tsx` mevcut ve işlevsel — `AuthApi.updateMyProfile` ile `PATCH /v1/users/me`, displayName+avatarUrl formu, query invalidation |
| 2 | Phone change OTP flow mobile UI yok | ✅ **KAPANDI** | `mobile/app/account/change-phone.tsx` iki adımlı akış (`initiateMut`→`confirmMut`) `/v1/users/me/phone-change/initiate`+`/confirm`'e bağlı |
| 3 | Owner için yorum cevap mobile UI yok | ✅ **KAPANDI** | `mobile/app/(barber)/reviews.tsx` — liste + reply mutation (3-500 char) + işletme cevabı render |
| 4 | Mobile i18n | 🔴 **AÇIK** (doğrulandı) | i18next kurulu, 0 kullanım — P2-3 |
| 5 | Customer "Hesap bilgilerini indir" eksik | ✅ **KAPANDI** | `GET /v1/users/me/export` (`UserEndpoints.cs:22`) + mobile `(tabs)/profile.tsx:93-112` `Share.share` ile bağlı |
| 6 | Stylist/Payment/Notification domain unit tests eksik | ✅ **KAPANDI** | `Kuaforum.Tests.Domain` altında `Barber/StylistTests.cs` + `Payment/PaymentTests.cs` + `Notification/NotificationLogTests.cs` — domain projesinde 113 test |
| 7 | Modül spec'leri (M2-M12) | 🔴 **AÇIK** | `docs/04-modules`'te hâlâ sadece 00+01 — P1-13 |
| 8 | Hetzner deploy pipeline | 🔴 **AÇIK** | Workflows'ta sadece `ci.yml`, deploy yok — P0-7 |
| 9 | PayTR/Iyzico gerçek HTTP | 🔴 **AÇIK** | Stub olarak kaldı — P0-5 |
| 10 | R2 production storage | 🟡 **KOD HAZIR, HESAP BEKLİYOR** | `S3ObjectStorage` + MinIO testleri + fail-fast eklendi (2026-08-05); default hâlâ `LocalFileObjectStorage` — P1-6 |
| 11 | Sentry DSN | 🟡 **KISMEN BAYAT** | Kod tarafı tamamen hazır (Sentry.AspNetCore 5.0.1 + Serilog bridge, env-guarded, `SendDefaultPii=false`); tek eksik founder'ın DSN'i — "entegrasyon eksik" gibi okunmamalı, salt hesap-bloklu |
| 12 | NetGSM gerçek SMS | 🔴 **AÇIK, daha kötü** | Stub + prod config key mismatch ile OTP hiç gitmiyor — P0-6 |

**Çözülen çelişki (tarama-arası):** Kuaför paneli taraması `kyc.tsx` için "red sonrası yeniden gönderim çalışıyor" diyordu; Domain taraması "KYC reddi sonrası işletme kilitleniyor" diyordu. Kod ikinciyi doğruluyor: `ReviewKyc` reject dalı shop status'a dokunmuyor (`SubmitKycCommand.cs:105-111`), shop `PendingKyc`'de kalıyor, yeni submit `shop.MarkPendingKyc()` çağırıyor ve `BarberShop.cs:77-80` yalnız Draft'tan geçişe izin veriyor → **reddedilen kuaför backend'de sonsuza dek kilitli**; mobile UI yeniden gönderim FORMU sunuyor ama istek her zaman 400 döner (bkz. P0-9).

---

## 9. Sonraki Cycle Planı (Cycle 9)

| # | Adım | Gerekçe | Efor |
|---|---|---|---|
| 1 | Güvenlik yaması sprint'i: refund authz (Admin policy + tutarı `RefundPercentage`'dan türet) + mock/complete env-gate + provider==Mock guard + cancel Reason server-side türetme + no-show zaman guard'ı + demo login `__DEV__` + Hangfire bypass fix — her birine integration test | Para akışının istismar yüzeyini kapatan 6 iş; hepsi küçük, bağımsız ve mevcut test altyapısıyla doğrulanabilir. PayTR gelmeden önce bu yüzey temiz olmalı — sonra dokunmak riskli. | 1.5 gün |
| 2 | İptal → refund otomasyonu: müşteri + admin cancel'da `PaymentId` doluysa `RefundPercentage`'dan `AddRefund` + provider `RefundAsync`, başarısızlıkta Hangfire retry; `CancellationFlowTests`'e refund assertion | P0-4'ü kapatır; "iade işleme alındı" bildirimi ile gerçeklik arasındaki farkı — en büyük güven/hukuk riskini — ortadan kaldırır. Adım 1'deki refund guard'larının üzerine oturur. | 1.5 gün |
| 3 | KYC akış onarımı: reject→`RevertToDraft` fix + "reject→re-submit" testi + `GET /v1/admin/kyc/{id}/documents` + web admin belge önizleme + KYC onay/red notification handler'ları | Onboarding funnel'ının üç kırığını tek pakette kapatır: reddedilen kuaför tekrar başvurabilir, admin kör onaylamaz, sonuç kuaföre bildirilir. Kuaför arzı olmadan marketplace yok. | 1.5 gün |
| 4 | CI onarımı: Application test adımı + net8 retarget commit + global.json + dotnet-version 8.0.x + mobile jest-expo preset + ilk 5-6 pure-logic test + coverage baseline %55 | Yarım günlük iş, sonraki tüm adımların güvenlik ağı. 51 Application testinin CI dışında olması "kırık test merge yasak" kuralını fiilen deliyor; retarget uncommitted kaldıkça her makinede farklı gerçek var. | 0.5 gün |
| 5 | Deploy temeli: `${VAR}` placeholder'ları .NET env override'a çevir + `Seed:Admin`'i Development'a taşı + şifre rotate + backend/web-admin Dockerfile + `docker-compose.prod.yml` (Caddy TLS) + ForwardedHeaders + GitHub Actions build→GHCR→Hetzner deploy + staging'de Production-env boot smoke | Deploy edilebilirlik P0 kümesinin tamamı (config crash, commit'li şifre, Dockerfile, pipeline) tek bağımlı zincir — bölmek anlamsız. Hetzner hesabı founder'dan geldiği an canlıya alınabilir hale getirir. | 4 gün |
| 6 | Mobile ödeme akışı: `booking/payment.tsx` WebView checkout + initiate/status polling + mock provider ile uçtan uca müşteri akışı; webhook endpoint iskeleti (`POST /v1/webhooks/paytr` — HMAC doğrulama + idempotent completion + testler) | PayTR merchant hesabı gelmeden yapılabilecek en değerli iş: provider geldiğinde sadece config değişir, tüm akış hazır olur. Outbox'ı da webhook consumer'ıyla birlikte getirmek için doğru an. | 4 gün |
| 7 | Owner onboarding mobile: salon oluşturma formu (`BarberApi.createShop`/`setAddress`) + `expo-document-picker`'lı gerçek KYC upload → `/v1/uploads/kyc` + rol policy'lerinin endpoint gruplarına uygulanması | Gerçek bir kuaförün mobile'dan tek başına Active olabilmesi launch'ın önkoşulu; rol policy işi (Customer→BarberOwner geçişi) doğal olarak aynı tasarım kararına bağlı, birlikte alınmalı. | 2.5 gün |
| 8 | KVKK ekranları: aydınlatma + ToS statik ekranları + kayıtta parçalı rıza akışı (ToS zorunlu, Marketing/Location opsiyonel) + kullanılmayan konum izinlerinin `app.json`'dan temizliği | Yasal P0; teknik iskelet hukukçu metnini beklemeden kurulur, metin placeholder ile girer. İzin temizliği store review riskini sıfırlar (30 dk). | 1.5 gün |
| 9 | Spec borcu kapanışı: RFC-001..005 (retrospektif: .NET8, SQL-search, HS256, Erzurum pilot, OTP-only/OAuth erteleme) + GAP-ANALYSIS'i bu sentezden yeniden üret (bu doküman) + master spec 0.2.0 + founder yazılı onayı | Kod borcu değil ama önceliklendirme hatalarının kaynağı: bayat GAP-ANALYSIS yapılmış işi tekrar planlatıyor, RFC'siz sapmalar "master spec bağlayıcı" iddiasını fiktif kılıyor. 1 günlük yatırım sonraki her cycle'ın planlama kalitesini düzeltir. | 1 gün |
| 10 | (Kapasite kalırsa) `ExpirePendingBookingsJob` + double-booking EXCLUDE constraint + concurrent test + PendingTimeout kullanıcı limiti | Slot DoS ve double-booking, trafik gelmeden görünmez ama launch günü itibar riski; DB constraint ucuz sigorta. Cycle taşarsa bir sonrakinin ilk maddesi. | 2.5 gün |

---

## 10. Founder Aksiyonları

Kod tarafı bu cycle'da ilerlerken paralel başlaması gereken 🟣 işler — lead-time'ı en uzun olanlar en üstte:

| # | Aksiyon | Neden kritik / lead-time notu |
|---|---|---|
| 1 | **PayTR Pazaryeri merchant başvurusu** — sözleşme + sub-merchant (split payment) modeli onayı | **EN UZUN LEAD TIME, bugün başlat.** Sandbox credentials gelmeden gerçek PSP entegrasyonu yazılamaz (P0-5). |
| 2 | **NetGSM hesabı** aç + originator (SMS başlığı) tahsis/onayı + API credentials | OTP-only auth'ta SMS olmadan hiçbir kullanıcı kayıt olamaz (P0-6); başlık onayı günler sürebilir. |
| 3 | **Nilvera e-Arşiv hesabı** + mali müşavir ile çift fatura akışının (kuaför→müşteri %20 KDV + platform→kuaför komisyon faturası) teyidi | Gerçek tahsilat öncesi yasal önkoşul (P0-13). |
| 4 | **Hetzner Cloud hesabı** + Helsinki sunucu + SSH key; **Cloudflare hesabı** (kuaforum.com DNS + R2 bucket + API token) | Deploy pipeline (Cycle 9 adım 5) bunlar gelince canlıya bağlanır. |
| 5 | **Firebase projesi** oluştur: `google-services.json` (Android) + FCM service account JSON | Standalone build'de push bunsuz çalışmaz (P0-12). |
| 6 | **Sentry organizasyonu** + backend ve mobile DSN'leri | Kod tarafı tamamen hazır, sadece DSN bekliyor (14C-11). |
| 7 | **Apple Developer** (99$/yıl) + **Google Play Console** (25$ tek seferlik) + Expo/EAS hesabı | Sonra `eas init` ile projectId bağlanacak (P0-12). |
| 8 | **App icon** (1024x1024) + splash tasarımı | `mobile/assets` hiç yok, bunsuz store'a binary gönderilemez; tasarımcıya bugün brief verilebilir (P0-12). |
| 9 | **Prod secret'larını üret ve güvenli sakla**: JWT signing key, OTP pepper, encryption key, Postgres şifresi; commit'lenmiş admin şifresini (`ChangeMe!Admin2026` — git history'de) rotate et | P0-8'in önkoşulu. |
| 10 | **Hukukçu görüşmesi**: KVKK aydınlatma metni + ToS metinleri + bundled consent geçerliliği + İYS/6563 ETK yükümlülük teyidi | Şu an tüm bu iddialar `[unverified]` (P0-10, P1-15). |
| 11 | **Resend hesabı** + kuaforum.com domain doğrulama | API key kod tarafında hazır bekliyor. |
| 12 | **Google Maps API key** (geocoding — adres girişinde kullanılacak) | M3/M10 geocoding şu an tamamen boş. |
| 13 | **Better Stack veya UptimeRobot hesabı** | Deploy sonrası `/health/ready` izleme — 15 dakikalık iş, ücretsiz tier yeter (P2-10). |
| 14 | **Erken ücretsiz İYS kaydı** | İYS dışı alınan Marketing onayları 3 iş günü içinde İYS'ye işlenmezse geçersiz sayılıyor — ilk gerçek kullanıcı onayı üretime düşmeden temel İYS kaydını aç (ücretsiz; detay: `docs/01-research/iys-etk-arastirma.md` §5) `[hukukçu teyidi gerek]`. |

---

## 11. Bağlantılar

- Master spec: `docs/PHASE-0-MASTER-SPEC.md`
- Module list: `docs/04-modules/00-module-list.md`
- M1 Auth spec: `docs/04-modules/01-auth.md`
- CLAUDE.md: proje kökü

**Son güncelleme:** 2026-07-23 (tam tarama); 2026-07-30 nokta güncellemesi — P0-5 RFC-006 kapanış notu + canlıya alma checklist'i + P2-11

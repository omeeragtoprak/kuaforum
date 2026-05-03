# Teknoloji Yığını — Karar ve Gerekçeler

> Her satır: **Karar · Gerekçe · Alternatif (neden değil) · Risk · Phase**.

---

## 1. Mobil Uygulama

### 1.1 Expo SDK 52+ (Managed Workflow)
- **Karar:** Expo + EAS Build/Submit + EAS Update (OTA).
- **Gerekçe:** Solo founder için iOS/Android build pipeline ergonomik. OTA update kritik patch'leri Apple/Google review beklemeden push eder. Native modül gerekirse `expo-dev-client` ile config plugin yazarak çözülür (Expo SDK 50+ ile çoğu native modül desteği var).
- **Alternatif:** React Native CLI (bare). Daha fazla kontrol ama solo'ya ekstra yük; build/CI/upgrade matriksi 2-3 hafta ekstra.
- **Risk:** Iyzico/PayTR SDK native gerektirirse config plugin yaz. Apple Pay tokenization Expo'da config plugin var (`expo-payments-stripe` benzeri yok ama PSP'lerin web view fallback'i var).
- **Phase:** P1.

### 1.2 React Native 0.76+ (New Architecture: Fabric + TurboModules)
- **Karar:** Expo'nun bağladığı RN sürümü.
- **Gerekçe:** Performans + bridgeless mimari. 2026 itibarıyla stable.

### 1.3 React Navigation 7
- **Karar:** Expo Router (file-based routing) tercih.
- **Gerekçe:** Expo SDK 52+ ile native, deep link desteği, type-safe.

### 1.4 State Management — Zustand
- **Karar:** Server state için TanStack Query, cross-screen client state için Zustand.
- **Gerekçe:** Redux Toolkit'ten daha hafif, boilerplate az. Solo için ergonomik.
- **Alternatif:** Redux Toolkit + RTK Query. Olgun ama heavier.

### 1.5 Forms — React Hook Form + Zod
- **Karar:** RHF + Zod resolver.
- **Gerekçe:** Validation backend'le shared (Zod schema TS'de export edilir, backend FluentValidation paralel).

### 1.6 UI Library — Karar Phase 1 başında
- **Adaylar:** Tamagui (compile-time optimization, multi-platform), React Native Paper (Material 3), NativeBase (deprecated, eleme).
- **Gerekçe (Tamagui):** Web admin ile aynı tema. Animasyon ergonomik. Modern.
- **Gerekçe (RN Paper):** Material Design olgun, hızlı başlangıç.
- **Risk:** Tamagui learning curve > RN Paper.
- **Phase:** P1 başında karar (1 hafta evaluation).

### 1.7 i18n — i18next + react-i18next
- **Karar:** Tüm metin string i18next üzerinden, MVP'de TR-only ama altyapı kurulu.

### 1.8 Maps & Geocoding
- **Karar:** Google Maps (geocoding API) + native Apple/Google Maps deep link (yol tarifi).
- **Gerekçe:** TR'de en doğru POI verisi. Ücret: $200/ay free tier, sonrası $5-20/1k geocoding.
- **Alternatif:** HERE Maps (TR coverage daha az), Mapbox (geocoding TR'de zayıf).

### 1.9 Push Notifications — Firebase Cloud Messaging (FCM)
- **Karar:** FCM, iOS APNs FCM üzerinden köprü.
- **Gerekçe:** Standart, ücretsiz, Expo notifications module hazır.

---

## 2. Backend

### 2.1 .NET 10 / ASP.NET Core
- **Karar:** Minimal API + Controller hibrit. **LTS, Kasım 2028'e kadar destekli.**
- **Düzeltme notu (2026-05-03):** Founder orijinal mesajda ".NET Core 8" yazdı; "Core" eki 2020'de kalktı. .NET 8 LTS Kasım 2026'da bitiyor — launch'tan **önce**. Bu yüzden **.NET 10 LTS** seçildi. Tek satır csproj değişikliği ile gerekirse .NET 8'e geri dönülebilir.
- **Gerekçe:** Performans, type safety, EF Core 10 olgun, native AOT olgunluğu V2 için iyi durumda.
- **Risk:** TR developer pool nispeten dar (Java/Node baskın), takım büyürse hire zorlaşabilir. Solo için risk yok.

### 2.2 EF Core 10
- **Karar:** Primary ORM, code-first migrations.
- **Gerekçe:** .NET native, change tracking, migration ergonomik.
- **Performans notu:** Read-heavy sorgularda `AsNoTracking()` + `Select` projection. PostGIS sorguları raw SQL veya `EF.Functions`.

### 2.3 MediatR
- **Karar:** Command + Query handler routing + pipeline behaviors.
- **Gerekçe:** CQRS uygulaması net. Validation/Logging/Transaction pipeline'a ekle.
- **Lisans uyarısı:** MediatR 12+ ücretli ticari lisans önerebilir (Jimmy Bogard'ın 2024 sonu duyurusu — 2026 itibarıyla durumu doğrula). [unverified 2026-05-03 — needs license check]
- **Alternatif:** Wolverine (tamamen ücretsiz) — yedek plan.

### 2.4 FluentValidation
- **Karar:** Request validation. MediatR pipeline behavior'da otomatik çalışır.

### 2.5 Mapster
- **Karar:** Entity ↔ DTO mapping.
- **Gerekçe:** AutoMapper'dan ~10x hızlı, source-gen olgun. Daha az reflection.

### 2.6 Hangfire
- **Karar:** Background jobs (outbox processor, e-Arşiv kuyruğu, refund retry).
- **Gerekçe:** Postgres backed (ek dependency yok), dashboard hazır.
- **Alternatif:** Quartz.NET (Hangfire'dan daha karmaşık ama daha güçlü cron). MVP için Hangfire yeterli.

### 2.7 SignalR (V1.5)
- **Karar:** Walk-in real-time queue için.
- **Phase:** V1.5. MVP'de yok.

### 2.8 Auth — ASP.NET Identity + JWT + Refresh
- **Karar:** ASP.NET Identity (kullanıcı + password hash). JWT 15dk access + 30g refresh (server-side rotated, device-bound).
- **OAuth:** Apple (iOS zorunlu), Google. Phase 1.
- **OTP:** NetGSM SMS. Phase 1.

### 2.9 Logging — Serilog → Seq
- **Karar:** Structured logging, Seq (.NET native log server, Hetzner self-host).
- **Gerekçe:** Solo için Seq UI ergonomik, ücretsiz developer edition var.
- **Alternatif:** Loki + Grafana, ELK. Daha kurumsal ama solo için over-kill.

### 2.10 Observability
- **Karar:** Sentry (errors + performance), OpenTelemetry → Seq (traces), Prometheus + Grafana (metrics — V1.5).
- **Gerekçe:** Sentry mobile + backend hibrit hazır.

---

## 3. Veri Katmanı

### 3.1 PostgreSQL 16
- **Karar:** Primary DB. Hetzner Cloud managed Postgres veya self-hosted CCX13 üzerinde.
- **Gerekçe:** JSON, full-text, PostGIS extension. EF Core uyum.
- **Backup:** Hetzner snapshot + WAL-G ile S3 (R2) sürekli arşiv.

### 3.2 PostGIS
- **Karar:** Spatial queries (yakındaki kuaför, mahalle polygon).
- **Gerekçe:** Mil/km mesafe, polygon contains, K-NN sorgu hızlı.

### 3.3 Redis 7
- **Karar:** Cache (TTL'li read-through), distributed lock (StackExchange.Redis), rate limiting (sliding window), session store.
- **Self-hosted veya managed:** Hetzner CCX13 self-host MVP'de yeter; ileride managed.

### 3.4 Meilisearch
- **Karar:** Full-text + faceted search. Türkçe tokenizer.
- **Gerekçe:** Elasticsearch'den hafif, kurulumu kolay, RAM ergonomik. Solo için ideal.
- **Türkçe:** Meilisearch'in Turkish stopword + stemming desteği iyi (2026 itibarıyla).
- **Alternatif:** PostgreSQL FTS (basit ama Türkçe stem zayıf), Typesense (benzer), Elasticsearch (over-kill MVP).

---

## 4. Hosting & DevOps

### 4.1 Hetzner Cloud (Helsinki/Frankfurt)
- **Karar:** CCX13 (4 vCPU, 16GB) MVP, CCX31 (8 vCPU, 32GB) soft-launch.
- **Gerekçe:** AB içi (KVKK Std Sözleşme + 5 gün bildirim), TR'ye 30-40ms, Hetzner Cloud Storage (S3 uyumlu) yan ürün, fiyat ~%70 daha az AWS'den.
- **Risk:** Hetzner enterprise SLA AWS'den zayıf. MVP'de OK, scale'de yedek plan (AWS Frankfurt).

### 4.2 Cloudflare (CDN + WAF + DNS + Tunnel)
- **Karar:** Free tier MVP, Pro $20/ay soft-launch.
- **Gerekçe:** DDoS, bot protection, WAF rules, image optimization.

### 4.3 Cloudflare R2 (S3 uyumlu obje storage)
- **Karar:** Kullanıcı/kuaför fotoğrafı, doküman yedek.
- **Gerekçe:** AWS S3'den ~%80 daha ucuz, egress ücretsiz, S3 API uyumlu.

### 4.4 Docker + Docker Compose (MVP) → Kubernetes (V2)
- **Karar:** MVP'de docker compose ile tek host. V2'de Hetzner managed K8s veya k3s.
- **Gerekçe:** Solo founder için K8s erken bir karar; modüler monolithse single host yeter.

### 4.5 GitHub Actions (CI/CD)
- **Karar:** Backend test + build, mobile EAS workflow, web admin Vercel veya Cloudflare Pages deploy.
- **Phase:** P0/P1 sınırı.

### 4.6 Better Stack (uptime monitoring) veya Cloudflare Health Checks
- **Karar:** P1 sonu, free tier MVP.

---

## 5. Üçüncü Parti Servisler

### 5.1 PayTR Pazaryeri
- **Karar:** Primary PSP. T+1 settlement, MDR %0.99-4 dinamik.
- **Phase:** P2.
- **Detay:** [`docs/09-payment/01-payment-architecture.md`](../09-payment/01-payment-architecture.md).

### 5.2 Iyzico Pazaryeri
- **Karar:** Fallback. T+3w holdback dezavantajı.
- **Phase:** P2 (abstraction sayesinde ileride aktif).

### 5.3 Nilvera (e-Arşiv)
- **Karar:** REST API ile çift fatura.
- **Gerekçe:** Modern API, VKN sorgu yan ürün, fiyat 150-800 TL/ay.
- **Alternatifler:** Logo, Mikrogis, Foriba, Uyumsoft, Edm. Fiyat/API ergonomisi karşılaştırması Phase 1 sonu.

### 5.4 NetGSM (SMS)
- **Karar:** Pay-as-you-go, OTP + kritik akış.
- **Gerekçe:** TR pazarında en iyi fiyat (~0.05-0.10 TL/SMS), olgun API.
- **Alternatif:** Twilio (TR uluslararası ücret), Iletimerkezi, Vatansms.

### 5.5 Postmark veya Resend (Mail)
- **Karar:** Transactional mail (fatura, makbuz, OTP fallback).
- **Gerekçe:** Postmark TR teslimat iyi, deliverability yüksek.

### 5.6 Sentry
- **Karar:** Error tracking + performance + release tracking.
- **Gerekçe:** Mobile + backend hibrit, free tier 5K err/ay.

### 5.7 Google Maps Platform
- **Karar:** Geocoding + Places. Yol tarifi native handoff (ücretsiz).

---

## 6. Tooling

| Araç | Karar | Gerekçe |
|---|---|---|
| Visual Studio Code / Rider | Geliştirme | Cross-platform |
| Bruno (open-source Postman) | API test koleksiyonu | Repo'da versiyonlu |
| Mermaid / Excalidraw / draw.io | Diyagramlar | Markdown'a embed |
| Migration tool | EF Core CLI | dotnet ef |
| Container registry | Hetzner Cloud Container Registry veya GHCR | private |
| Secret manager | Doppler veya HashiCorp Vault (V2) | MVP'de docker secret + .env |

---

## 7. Lisans & Bağımlılık Disiplini

- Bağımlılık ekleme: bir kez **ihtiyaç + alternatif + lisans + bakım sıklığı** check.
- 6 ayda commit yok, 50'den az star, lisans GPL/AGPL — kırmızı bayrak.
- Major version bump quarterly review.

---

## 8. Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/03-architecture/01-system-architecture.md`](01-system-architecture.md)
- [`docs/01-research/payment-infrastructure.md`](../01-research/payment-infrastructure.md)

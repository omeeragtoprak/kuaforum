# Kuaforum — Phase 0 Master Specification

**Doküman tarihi:** 2026-05-02
**Doküman sahibi:** Kuaforum core team (solo founder + Claude Code partnership)
**Versiyon:** 0.1.0 (taslak — kullanıcı onayı bekliyor)
**Sonraki adım:** Onay sonrası Phase 1 / Modül 1 (Auth) brainstorming + planning

---

## 0. Bu Doküman Nedir, Ne Değildir

Bu doküman **Phase 0 Foundation** çıktısıdır — Kuaforum projesinin tüm sonraki kararlarının dayanacağı temel kararlar, kapsamlar ve riskler burada kilitlenir. Kod yazımı değil, kural koyumu yapar. Phase 1+ modüllerinin her biri (Auth, Booking, Payment vs.) **bu doküman değişmeden** kendi spec/plan/implementation döngüsünden geçer.

**Bu doküman değişebilir mi?** Yalnızca regülasyon, rakip hareketi veya kullanıcı keşfinden gelen yeni bir bulgu ile. Değişiklik bir RFC ile yapılır (Bkz. § 13).

---

## 1. Yönetici Özeti (Executive Summary)

**Kuaforum**, Türkiye'de henüz "kategori sahibi" oyuncusu olmayan kuaför/berber **B2C marketplace** segmentine giren mobil uygulamadır. Üç yüzeyden oluşur:
1. **Müşteri uygulaması** (mobil — Expo/React Native): keşif, randevu, ödeme, navigasyon, değerlendirme.
2. **Kuaför paneli** (mobil-first responsive — aynı kod tabanı üzerinden web): işletme + stilist + hizmet katalog yönetimi, müsaitlik, randevu, raporlama.
3. **Admin paneli** (web): kullanıcı, kuaför doğrulama, finansal raporlama, şikâyet/ihtilaf yönetimi.

Türkiye'de Booksy ve Fresha'nın **gerçek lansmanı yok** ve mevcut yerli oyuncular (Kolay Randevu/SalonAppy, Salon Randevu, Kuaförüm Yanımda) **B2B SaaS odaklıdır**; pazaryeri keşif tarafı zayıftır. Bu, **18-24 ay'lık fırsat penceresinde** "yerli, KVKK uyumlu, e-Arşiv 2027 hazır, walk-in mod desteği olan, gender-aware keşif" sunan yeni girene **kategori sahipliği** şansı verir.

**Hedef:** 9-15 ay içinde **production-ready MVP** + 2-3 büyük şehirde **soft-launch**, ardından 12-18. ayda **organic + paid GTM** ile ulusal yayılma.

**Solo founder + 9-15 ay timeline + bootstrap → growth bütçe artışı.** Compliance day-1 production-grade (mock değil).

---

## 2. Kilitlenen Kararlar Tablosu

| # | Karar Alanı | Karar | Gerekçe | Referans Doküman |
|---|---|---|---|---|
| 1 | Proje ismi | **Kuaforum** | Türkçe doğal, "kuaför + forum / kuaförüm" çift okuma. Trademark + domain durumu Phase 0'ın kapanış check'idir. | `docs/01-research/competitor-analysis.md` |
| 2 | Mobil platform | **Expo (managed workflow)** + EAS Build/Submit | Solo founder için OTA + hot reload + tek kod. Native gereksinim çıkarsa expo-dev-client ile çözülür. | § 5 |
| 3 | Backend | **.NET 10 / ASP.NET Core** (LTS Kasım 2028'e kadar) | Founder orijinal mesajda ".NET Core 8" yazdı; "Core" eki 2020'de kalktı. .NET 8 LTS Kasım 2026'da bitiyor → launch'tan önce. **.NET 10 LTS** doğru karar. EF Core 10, MediatR 12.4 (MIT, license check pending), FluentValidation 11 (MIT), Hangfire. | § 5 |
| 4 | Veritabanı | **PostgreSQL 16** (primary), **Redis 7** (cache + session + light queue) | Kullanıcı tercihi. PostGIS extension lokasyon için. | § 5 |
| 5 | Hosting | **Hetzner Cloud (Helsinki/Frankfurt)** | AB içi, KVKK Standart Sözleşme + 5 iş günü Kuruma bildirim ile uyumlu. Maliyet/performans en iyi. | § 7, `docs/01-research/turkey-compliance.md` |
| 6 | Mimari | **Modular monolith** + Clean Architecture + CQRS (MediatR) + Outbox | Solo founder için microservices premature. Module boundaries net olduğu için ileride extract edilebilir. | `docs/03-architecture/01-system-architecture.md` |
| 7 | PSP (primary) | **PayTR Pazaryeri** (T+1 settlement) | Kuaför cash-flow dostu. Iyzico T+3 hafta holdback rakip dezavantajı. | `docs/09-payment/01-payment-architecture.md`, `docs/01-research/payment-infrastructure.md` |
| 8 | PSP (abstraction) | `IPaymentProvider` arayüzü ile PSP swap | Iyzico fallback olarak hazır. Pazarlık avantajı. | `docs/09-payment/01-payment-architecture.md` |
| 9 | Lisans | **TCMB lisansı GEREKLİ DEĞİL** (6493 sk. Madde 12/2-(b) "ticari temsilci" istisnası — fonlar bizim hesabımıza hiç girmediği için) | Iyzico/PayTR Pazaryeri ürünleri PSP'de fonu tutar. 50M TL/12 ay GMV eşiğinde TCMB'ye yıllık bildirim. | `docs/01-research/payment-infrastructure.md` |
| 10 | E-Arşiv | **Nilvera entegratör** + çift fatura modeli (kuaför→müşteri %20 KDV + platform→kuaför komisyon faturası %20 KDV) | E-ticaret aracı eşiği 500K TL — hızlı tetikleneceğiz. | `docs/01-research/turkey-compliance.md` |
| 11 | KDV | **Genel oran %20** (kuaför hizmetleri 2007/13033 BKK eki I/II'de yok) | İndirimli oran istisnası yok. | `docs/01-research/turkey-compliance.md` |
| 12 | İptal politikası | **Kademeli**: 24h+ önce %100 iade · 12-24h %50 · 0-12h %0 | %100 cezai şart muhtemelen haksız, kademeli yasal güvenli. | `docs/01-research/turkey-compliance.md` |
| 13 | KVKK uyum modeli | Parçalı açık rıza · Standart Sözleşme · 5 iş günü Kuruma bildirim · 30 gün cevap · 72 saat ihlal | Türkiye yeterlilik kararı vermemiş; AB hosting Std. Sözleşme + bildirim ile uyumlu. | `docs/01-research/turkey-compliance.md` |
| 14 | Kuaför doğrulama | Belge upload + manuel admin onayı (ESBİS public API yok) | Phase 0/1 manuel; Phase 2'de Nilvera VKN sorgu API entegrasyonu. | `docs/01-research/turkey-compliance.md` |
| 15 | Coğrafi kapsam | **Yazılım: 81 il/ilçe açık** (kullanıcı serbest seçim/konum). **Onboarding fokus: 2-3 pilot şehir** (kararı Phase 1 sonu) | Marketplace cold-start'a dayanıklı arz odağı. | `docs/02-requirements/01-mvp-scope.md` |
| 16 | MVP differentiators | **3'ü de MVP'de**: (a) cinsiyet+mahalle ayrımı, (b) **walk-in simplified** (toggle + filtre, full real-time queue V1.5'a), (c) KVKK self-servis + Türkçe NLP arama | Walk-in full-real-time karmaşıklığı solo founder için risk. | § 4 |
| 17 | Customer discovery | **Phase 0 sonunda 2 hafta sprint** — 30 kuaför + 30 müşteri görüşmesi. Concierge MVP test. | "Fikir yeni, doğrulama yok" cevabına karşı koruma. | `docs/01-research/customer-discovery-plan.md` (Phase 0 kapanışında yazılacak) |
| 18 | Şirket yapısı | **Limited Şirket** (Phase 0 → Phase 1 arasında kuruluş) | Iyzico/PayTR Pazaryeri kabul, e-Arşiv kabul, KVKK VERBİS gelecek genişleme. | `docs/13-project-management/04-company-formation-roadmap.md` |
| 19 | Bütçe modeli | **3 katmanlı tier** — Bootstrap ($80-150/ay) · Soft-launch ($300-600/ay) · National launch ($1.500+/ay) | Bütçe büyümeyi dönüm noktalarına bağlar. Compliance kurulumu ek 110-280K TL bir defalık. | `docs/13-project-management/03-budget-tiers.md` |
| 20 | Repo stratejisi | **Monorepo** — `/backend` (.NET 8 solution) · `/mobile` (Expo) · `/web-admin` (Next.js veya Vite React) · `/docs` · `/infra` | Solo founder için tek versiyon kontrolü, paylaşımlı kütüphane (TS types, contract). | `docs/03-architecture/01-system-architecture.md` |

---

## 3. Vizyon & Stratejik Konumlandırma

> "Türkiye'nin her ilçesinde, müşterinin **5 dakikada** (keşif → randevu → ödeme), kuaförün **5 saniyede** (panel → randevuyu kabul/reddet) işini halledebileceği bir pazaryeri."

**Temel iddialarımız:**
1. **Kategori sahibi yok** — Booksy/Fresha TR'de değil, yerliler B2B SaaS'a sıkışmış. Bu boşluk doldurulmaya elverişli.
2. **Compliance şehri** — 2027 e-Arşiv zorunluluğu + KVKK olgunlaşan denetim → yeni girenin "compliance day-1" iddiası gerçek bir farklılaştırıcı.
3. **TR-özgü UX** — cinsiyet ayrımlı (kuaför/berber) keşif, mahalle filtresi, walk-in toggle, Türkçe NLP arama. Global oyuncuların yerelleştirme borcu.

**Başarı metrikleri (12 ay sonu):**
- 3 büyük şehirde **300+ aktif kuaför** ve **30.000+ aylık aktif müşteri**.
- **Aylık tamamlanmış randevu sayısı: 25.000+**.
- **No-show oranı: < %8** (kademeli iade + ön kapora etkisi).
- **Net Promoter Score: > 40** (her iki taraf — müşteri ve kuaför).

---

## 4. MVP Tanımı (Phase 1 + Phase 2 Kapsamı)

**Tam liste:** `docs/02-requirements/01-mvp-scope.md`. Burada özet:

### MUST-have (MVP launch için zorunlu)
- M1 Auth (telefon + e-posta, OTP, Apple/Google OAuth, JWT + refresh)
- M2 User & Role (Customer, BarberOwner, Stylist, Admin)
- M3 Geographic data (TR il/ilçe/mahalle hierarchy + PostGIS)
- M4 Barber profile + verification (belge upload + manuel admin onay)
- M5 **Availability engine** (en zor modül — slot yönetimi, rezervasyon, izin/tatil)
- M6 Booking lifecycle (state machine: pending/confirmed/started/completed/cancelled/no-show)
- M7 Payment (PayTR Pazaryeri primary, Iyzico fallback hazır, kademeli iade)
- M8 Rating & Review (moderasyon + kuaför reply)
- M9 Search & Discovery (filtre: cinsiyet/mahalle/fiyat/açık/walk-in/rating)
- M10 Maps & Navigation handoff (Google Maps / Apple Maps deep link)
- M11 Notifications (FCM push + NetGSM SMS + e-posta — kritik akışlar)
- E-Arşiv entegrasyonu (Nilvera) — çift fatura
- Walk-in **simplified mode** (toggle + filtre)
- KVKK self-servis veri silme + Türkçe NLP arama (Meilisearch)

### SHOULD-have (MVP sonrası ilk 3 ay — V1.5)
- M12 Admin paneli (gelişmiş ihtilaf, finansal rapor, KYC kuyruğu)
- M13 Kuaför analitik dashboard
- M14 Promo kodlar + referans
- Walk-in **full real-time queue** (SignalR + presence)

### COULD-have (MVP sonrası 6+ ay — V2)
- M15 Sadakat/puan sistemi
- M16 Çoklu dil (EN, AR, RU)
- Yapay zeka stil önerileri (foto upload + öneri)
- B2B SaaS modülü (kuaförler için bağımsız işletme yönetim aracı)

### EXPLICITLY OUT-OF-SCOPE (asla MVP değil)
- POS/yazarkasa entegrasyonu
- WhatsApp Business API hatırlatma (SMS yeter)
- Stok yönetimi (boya, ekipman)
- Personel maaş/prim hesaplama

---

## 5. Mimari & Teknoloji Yığını Özeti

**Ayrıntılı:** `docs/03-architecture/01-system-architecture.md` ve `docs/03-architecture/02-tech-stack.md`.

```
┌──────────────────────────────────────────────────────────────────┐
│                      Kuaforum Modular Monolith                    │
│                                                                   │
│  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────────┐ │
│  │  Identity       │  │  Catalog        │  │  Booking         │ │
│  │  (Auth, RBAC)   │  │  (Barber, Svc)  │  │  (Appointments)  │ │
│  └─────────────────┘  └─────────────────┘  └──────────────────┘ │
│  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────────┐ │
│  │  Availability   │  │  Payment        │  │  Reviews         │ │
│  │  (Calendar)     │  │  (PSP abstrac.) │  │  (Moderation)    │ │
│  └─────────────────┘  └─────────────────┘  └──────────────────┘ │
│  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────────┐ │
│  │  Search         │  │  Notification   │  │  Geography       │ │
│  │  (Meilisearch)  │  │  (FCM/SMS/Mail) │  │  (PostGIS)       │ │
│  └─────────────────┘  └─────────────────┘  └──────────────────┘ │
│  ┌─────────────────┐  ┌─────────────────┐                       │
│  │  Audit/Outbox   │  │  Admin          │                       │
│  └─────────────────┘  └─────────────────┘                       │
└──────────────────────────────────────────────────────────────────┘
        │                       │                   │
   ┌────▼────┐            ┌────▼─────┐         ┌───▼───────┐
   │Postgres │            │  Redis   │         │Meilisearch│
   │+PostGIS │            │  (cache) │         │           │
   └─────────┘            └──────────┘         └───────────┘

   Dış servisler: PayTR Pazaryeri · Iyzico (fallback) · Nilvera (e-Arşiv) · 
                  FCM (push) · NetGSM (SMS) · Google Maps (geocoding) · 
                  Sentry (error) · Seq (logging) · Cloudflare R2 (file)
```

**Yığın Özeti:**
- **Backend:** ASP.NET Core 8 · EF Core 10 · MediatR · FluentValidation · Mapster · Hangfire (background jobs) · SignalR (V1.5 walk-in)
- **DB:** PostgreSQL 16 + PostGIS · Redis 7 (cache + lock) · Meilisearch (full-text + Turkish tokenizer)
- **Mobile:** Expo SDK 52+ · React Native 0.76+ · React Navigation · Zustand · React Hook Form + Zod · Tamagui veya React Native Paper · i18next
- **Web admin:** Next.js 15 · shadcn/ui · TanStack Table/Query
- **Auth:** ASP.NET Identity + JWT + Refresh Token · OAuth (Apple, Google) · OTP (NetGSM)
- **Hosting:** Hetzner Cloud (Helsinki) · Docker Compose (MVP) → K8s (V2 ölçek) · Cloudflare CDN + WAF · Cloudflare R2 (S3 uyumlu)
- **CI/CD:** GitHub Actions · Docker registry (Hetzner private) · EAS (mobile)
- **Observability:** Sentry · Seq · Prometheus + Grafana · Uptime monitoring (Better Stack veya Cloudflare Health Checks)

---

## 6. Compliance Duruşu (Phase 0 Çıkış)

| Konu | Karar | Phase | Maliyet |
|---|---|---|---|
| TCMB lisansı | Gerekli **DEĞİL** (Iyzico/PayTR Pazaryeri ile) | — | 0 |
| Şirket | Limited Şti (1 ortaklı) | P0→P1 arası | ~25-40K TL kuruluş |
| KVKK aydınlatma + açık rıza | Avukat onaylı şablon, parçalı toggle | P1 | 15-30K TL avukat |
| KVKK VERBİS | Phase 0/1'de muafsın (50 çalışan/100M TL altı) | P2 (gerekirse) | 0-3K TL |
| KVKK Standart Sözleşme | Hetzner için imzala + Kuruma 5 iş günü bildirim | P1 launch öncesi | 0 (manuel) |
| E-Arşiv entegrasyonu | Nilvera (REST API) | P1 | 150-800 TL/ay + 5K TL kurulum |
| Tüketici cayma | Kademeli iade (24/12/0) sözleşmede | P1 | 0 (avukatla) |
| Kuaför doğrulama | Belge upload + manuel onay (P0/P1), Nilvera VKN sorgu (P2) | P1+P2 | 0-200 TL/ay/sorgu |
| Sigorta (siber + meslek) | Soft-launch öncesi | P2 | 5-15K TL/yıl |
| **TOPLAM kurulum** | — | — | **~110-280K TL bir defalık + 200-1.000 TL/ay** |

---

## 7. Bütçe & Timeline (3 katmanlı)

| Katman | Faz | Aylık operasyon | Anahtar gider | Tetikleyici |
|---|---|---|---|---|
| **Bootstrap** | Phase 0 + Phase 1 (geliştirme) | $80-150 | 1 Hetzner CCX13 ($25), Cloudflare free, Sentry free, NetGSM pay-as-you-go | Trafik yok |
| **Soft-launch** | Phase 2 sonu + pilot şehir | $300-600 | Managed Postgres, Redis, R2, FCM, Sentry team, e-Arşiv abonmanı, alan adı + SSL | İlk gerçek kullanıcı |
| **National** | Ölçek + 3+ şehir | $1.500+ | Daha büyük Hetzner CCX31, replicas, log retention, support tools | 3.000+ aylık aktif kullanıcı |

**Detay:** `docs/13-project-management/03-budget-tiers.md`

**Timeline:**
- **Phase 0** (foundation + customer discovery + spec): **2-4 hafta** — şu an bu fazdayız.
- **Phase 1** (M1-M5 backbone + repo + CI/CD): **8-12 hafta**.
- **Phase 2** (M6-M11 marketplace flow + e-Arşiv + KYC manuel): **12-16 hafta**.
- **Phase 2 sonu = MVP launch ready**.
- Soft-launch (1 pilot şehir, davetiye ile): ay 9-11.
- Açık launch (3 büyük şehir + ulusal): ay 12-15.

---

## 8. Risk Kaydı

**Tam liste:** `docs/13-project-management/02-risk-register.md`. Top 10:

| # | Risk | Etki | Olasılık | Mitigasyon |
|---|---|---|---|---|
| 1 | Booksy/Fresha 18-24 ay'da TR'ye agresif giriş | YÜKSEK | ORTA | Hız + KVKK/e-Arşiv compliance moat + lokal kuaför ilişkileri |
| 2 | Marketplace cold-start (likidite yetersiz) | YÜKSEK | YÜKSEK | Pilot şehir + 30 kuaför ön niyet + concierge MVP |
| 3 | Solo founder burnout | YÜKSEK | ORTA | Kapsam disiplini · Phase gate · agent otomasyon · "v1.5 erteleme" hakkı |
| 4 | KVKK denetim cezası (eksik aydınlatma vs.) | YÜKSEK | DÜŞÜK | Avukat onaylı şablon · ihlal SOP yazılı · denetim sertifikalı yıllık review |
| 5 | PayTR onboarding gecikmesi (KYC) | ORTA | ORTA | Iyzico paralel onboarding · 2 PSP entegrasyon hazır |
| 6 | Iyzico T+3w problem kuaförler için (PayTR fallback'a düşersek) | ORTA | DÜŞÜK | Abstraction zaten hazır |
| 7 | E-Arşiv eşik tetikleyicisi beklediğimizden hızlı geçilir | ORTA | YÜKSEK | Nilvera ile day-1 entegrasyon |
| 8 | Walk-in simplified yetersiz, kullanıcı talep eder | DÜŞÜK | ORTA | V1.5'a yol açık, SignalR scaffold hazır |
| 9 | Mahalle/ilçe veri kalitesi (resmi kaynak vs. coğrafi gerçek) | ORTA | YÜKSEK | NVI (Nüfus İdaresi) açık veri + manuel düzeltme paneli |
| 10 | Kuaför sahte profil (fake KYC) | YÜKSEK | DÜŞÜK | Belge OCR + ESBİS web doğrulama (manuel) + ilk randevu retroaktif kontrol |

---

## 9. Phase 0 Çıktıları (Deliverables)

Bu doküman onaylandıktan sonra üretilecek (✅ = halihazırda yazılmış):

- [x] `docs/01-research/competitor-analysis.md` ✅
- [x] `docs/01-research/payment-infrastructure.md` ✅
- [x] `docs/01-research/turkey-compliance.md` ✅
- [x] `docs/PHASE-0-MASTER-SPEC.md` ✅ (bu doküman)
- [ ] `docs/00-overview/01-vision.md`
- [ ] `docs/00-overview/02-glossary.md`
- [ ] `docs/00-overview/03-stakeholders.md`
- [ ] `docs/02-requirements/01-mvp-scope.md`
- [ ] `docs/02-requirements/02-personas.md`
- [ ] `docs/02-requirements/03-non-functional.md`
- [ ] `docs/03-architecture/01-system-architecture.md`
- [ ] `docs/03-architecture/02-tech-stack.md`
- [ ] `docs/03-architecture/03-deployment-topology.md`
- [ ] `docs/03-architecture/04-design-patterns.md`
- [ ] `docs/04-modules/00-module-list.md`
- [ ] `docs/05-data/01-er-overview.md`
- [ ] `docs/06-flows/01-user-journeys.md`
- [ ] `docs/07-api/01-conventions.md`
- [ ] `docs/08-security/01-auth-overview.md`
- [ ] `docs/08-security/02-kvkk-overview.md`
- [ ] `docs/09-payment/01-payment-architecture.md`
- [ ] `docs/09-payment/02-commission-model.md`
- [ ] `docs/10-ui-ux/01-design-principles.md`
- [ ] `docs/11-testing/01-strategy.md`
- [ ] `docs/12-devops/01-ci-cd.md`
- [ ] `docs/12-devops/02-environments.md`
- [ ] `docs/13-project-management/01-roadmap.md`
- [ ] `docs/13-project-management/02-risk-register.md`
- [ ] `docs/13-project-management/03-budget-tiers.md`
- [ ] `docs/13-project-management/04-company-formation-roadmap.md`
- [ ] `docs/13-project-management/05-customer-discovery-plan.md`
- [ ] `docs/14-go-to-market/01-launch-strategy.md`
- [ ] `docs/15-claude-setup/01-overview.md`
- [ ] `docs/15-claude-setup/02-agents.md`
- [ ] `docs/15-claude-setup/03-mcp-context7.md`
- [ ] `docs/15-claude-setup/04-permanent-rules.md`
- [ ] `CLAUDE.md` (proje kökü)
- [ ] `README.md` (proje kökü)
- [ ] `.claude/agents/*` (8-9 agent definition)
- [ ] `.claude/settings.local.json` (project settings)
- [ ] `.gitignore`, `.editorconfig`, `LICENSE` (Phase 0 kapanışta git init)

---

## 10. Phase 1+ Yol Haritası

**Phase 1 modülleri (sıra):**
1. **M1 Auth & Authorization** — JWT, refresh, OAuth, RBAC, OTP
2. **M2 User & Role Management** — Customer/BarberOwner/Stylist/Admin profilleri
3. **M3 Geographic Data** — il/ilçe/mahalle bootstrap (NVI), PostGIS yapısı
4. **M4 Barber Profile & Verification** — kayıt akışı, belge upload, admin onay
5. **M5 Availability Engine** — en zor modül; slot yönetimi, rezervasyon, izin

Her modül kendi brainstorming → spec → implementation plan → kod döngüsünden geçer. Phase 0 onayından sonra ilk olarak **M1 brainstorming** başlatılır.

---

## 11. Açık Sorular (Phase 0 kapanış öncesi karara muhtaç)

1. **Trademark + domain check** — `kuaforum.com`, `kuaforum.com.tr`, `kuaforum.app` durumu? TPMK ön sorgu? Kullanıcı bu turun içinde mi sonra mı?
2. **MVP coğrafi pilot şehri** — kararı Phase 1 sonu (kuaför keşif sprint'i sonucu); şu anda erken.
3. **Çoklu dil MVP'de mi?** — kullanıcı brifte demedi; varsayılan: TR-only MVP, EN/AR/RU V2'ye.
4. **Müşteri ödeme başarısızlığı durumunda re-try akışı** — Payment modülü brainstorming'inde detaylanır.
5. **Kuaför KYC'de "MYK Seviye 4 Kuaförlük belgesi" zorunlu mu opsiyonel mi?** — 2026/1 Tebliğ ile zorunlu meslek listesinde. Yasal danışmanlık.
6. **Sigorta** — siber sorumluluk + meslek sorumluluk — Phase 2 öncesi aktive edilir.
7. **MVP launch öncesi penetrasyon testi (pen-test)** — 3. parti firma ile zorunlu; bütçe tier'ına eklenecek.

---

## 12. Karar Süreci & RFC

Bu dokümandaki bir karar değişecekse **RFC (Request for Comments)** açılır:
- `docs/rfc/RFC-NNN-<konu>.md` — gerekçe, mevcut durum, önerilen değişiklik, etki, ret/kabul kriteri.
- Kabul edilirse PHASE-0-MASTER-SPEC.md güncellenir, **versiyon artırılır** (semver), `Değişiklik Günlüğü` (§ 14) güncellenir.

---

## 13. Onay Kriterleri (Definition of Done — Phase 0)

Phase 0 **tamamlandı** sayılır şu koşullarda:
- [x] Rakip analizi tamam (`docs/01-research/competitor-analysis.md`)
- [x] Ödeme + regülasyon araştırması tamam (`docs/01-research/payment-infrastructure.md`, `turkey-compliance.md`)
- [ ] Bu master spec onaylanmış
- [ ] Tüm § 9 deliverable'ları yazılmış
- [ ] Customer discovery sprint planı yazılmış (görüşme rehberi + 60 kontak hedefi)
- [ ] Domain + trademark check yapılmış
- [ ] CLAUDE.md + .claude/agents kurulmuş
- [ ] Git init + ilk commit
- [ ] Kullanıcı (founder) yazılı onayı

---

## 14. Değişiklik Günlüğü

| Tarih | Versiyon | Değişiklik | Yazar |
|---|---|---|---|
| 2026-05-02 | 0.1.0 | İlk taslak | Founder + Claude |

---

## 15. Imza Hattı

> Bu dokümanı okuyup onayladığımı, Kuaforum projesinin Phase 0 çıkışı olarak yukarıdaki kararları kabul ettiğimi, sonraki fazların bu temel üzerine inşa edileceğini taahhüt ediyorum.

**Founder:** ____________________  Tarih: __________

**Claude Code:** kayıt için bu dokümanın hash'i kayıt altındadır.

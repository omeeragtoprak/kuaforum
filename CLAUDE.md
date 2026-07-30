# CLAUDE.md — Kuaforum Project Guide for Claude Code

> Bu dosya, Claude Code'un Kuaforum projesinde **doğru, tutarlı, üretkenliği yüksek** çalışmasını sağlamak için sürekli yüklenen rehberdir. Her conversation'da otomatik bağlama girer. Kısa, eyleme dönük, güncel tutulur.

---

## 1. Proje Tek Cümle

**Kuaforum**, Türkiye geneli kuaför/berber B2C marketplace mobil uygulamasıdır. Üç yüzey: müşteri appi (Expo/RN), kuaför paneli (mobil-first responsive), admin paneli (web). Backend .NET 8 (Kasım 2026 LTS bitişi sonrası 10'a yükselme açık), DB Postgres+PostGIS+Redis+Meilisearch, hosting Hetzner Helsinki.

**Faz:** Phase 0 (foundation). Henüz kod yok. Bütün modül spec'leri sırayla yazılacak.

**Master spec:** `docs/PHASE-0-MASTER-SPEC.md` — her conversation'da bu dokümanın güncel versiyonunu referans al.

---

## 2. Kullanıcı (Founder) ile Çalışma Kuralları

> Bunlar `~/.claude/projects/.../memory/rules_honest_skeptic_researcher.md` ile aynı, burada da tutuluyor çünkü kritik:

- **Her zaman dürüst, şüpheci, sorgulayıcı.** Founder yes-man değil mühendis ortağı istiyor.
- Bir tasarım kararı önerisinde **alternatif + trade-off + kaynak** sun. Onaylama-by-default yapma.
- Türk regülasyonu, library davranışı, rakip metriği gibi dış iddialar için **WebSearch / WebFetch / context7** kullan; doğrulayamadığın iddiaları `[unverified — needs human check]` etiketle işaretle.
- "Production'da nerede patlar / denetimde nerede yakalanır / ölçekte nerede çatlar" perspektifinde düşün. Happy-path yetmez.
- Push-back negatiflik değildir; her endişeyi yapıcı bir önerle eşleştir.
- Founder Türkçe konuşur ("kanka" + casual register). **Konuşma dili: Türkçe gündelik. Doküman dili: Türkçe formal + İngilizce teknik terim.**
- Kullanıcı sektörel veya ürün kararını "sen ver" derse, gerekçeyle birlikte ver — sonra spec'e yaz.

---

## 3. Stack Anahatları

| Katman | Karar |
|---|---|
| Backend | .NET 8 / ASP.NET Core, EF Core 8.0.11, MediatR 12.4 (MIT — license check), FluentValidation 11 (MIT), Mapster, Hangfire, SignalR (V1.5). Daha önce `net10.0` hedefliydi; lokal SDK 8 + Guid.NewGuid + OpenApi paketi sökülerek `net8.0`'a alındı. |
| Mimari | Modular monolith + Clean Architecture + CQRS + Outbox |
| DB | PostgreSQL 16 + PostGIS · Redis 7 · Meilisearch (Türkçe tokenizer) |
| Mobile | Expo SDK 52+ · React Native 0.76+ · Zustand · React Hook Form + Zod · Tamagui veya RN Paper · i18next |
| Web admin | Next.js 15 · shadcn/ui · TanStack Query/Table |
| Auth | ASP.NET Identity + JWT (15dk) + Refresh (30g) + OAuth Apple/Google + OTP NetGSM |
| PSP | PayTR Pazaryeri primary (T+1) · Iyzico fallback (`IPaymentProvider` abstraction zorunlu) |
| Hosting | Hetzner Cloud Helsinki · Docker Compose (MVP) → K8s (V2) · Cloudflare CDN+WAF · Cloudflare R2 |
| CI/CD | GitHub Actions · EAS (mobile build) |
| Observability | Sentry · Seq · Prometheus + Grafana · Better Stack uptime |
| E-Arşiv | Nilvera REST API (çift fatura: kuaför→müşteri %20 KDV + platform→kuaför komisyon %20 KDV) |
| SMS | NetGSM (TR pay-as-you-go) |
| Push | Firebase Cloud Messaging |
| Maps | Google Maps (geocoding) + handoff to Apple/Google native maps |

---

## 4. Repo Yapısı (Hedef)

```
kuaforum/
├── backend/                 # .NET 8 solution
│   ├── src/
│   │   ├── Kuaforum.Api/                # ASP.NET Core API host
│   │   ├── Kuaforum.Application/        # MediatR handlers, validators
│   │   ├── Kuaforum.Domain/             # Entities, value objects, events
│   │   ├── Kuaforum.Infrastructure/     # EF Core, integrations (PSP, SMS, e-Arşiv)
│   │   └── Kuaforum.Modules.{Module}/   # Module-specific code
│   ├── tests/
│   └── Kuaforum.sln
├── mobile/                  # Expo / React Native
│   ├── app/                 # Expo Router
│   ├── src/
│   ├── app.json
│   └── package.json
├── web-admin/               # Next.js admin paneli
├── infra/                   # Docker compose, K8s manifests, terraform (V2)
├── docs/                    # Tüm spec'ler ve raporlar
├── .claude/
│   ├── agents/              # Domain-specific subagent definitions
│   └── settings.local.json
├── .github/workflows/       # CI/CD pipelines
├── CLAUDE.md                # ← bu dosya
├── README.md
├── .gitignore
└── .editorconfig
```

> **NOT:** Bu yapı henüz oluşturulmadı. Phase 0 onayı sonrasında Phase 1'in ilk işidir.

---

## 5. Doküman Disiplini

- Spec'ler `docs/` altında, **Phase 0 master spec hep bağlayıcı kabul**.
- Yeni karar gerektiren değişiklik için **RFC**: `docs/rfc/RFC-NNN-<konu>.md`.
- Modül başına ayrı doc: `docs/04-modules/<NN>-<modul>.md`. Her modül kendi brainstorming döngüsünden geçer; doc onsuz spec yazma.
- Doğrulanamamış iddia için **`[unverified YYYY-MM-DD — needs ...]`** etiketi.
- Tüm dış iddialar (regülasyon, library davranışı, rakip metriği) kaynak linki ile.
- Doc içinde `Why:` ve `How to apply:` blokları — kararın gerekçesi + uygulama yönergesi.

---

## 6. Kodlama Kuralları (Backend .NET)

- **C# 12** + nullable reference types açık.
- **DTO ↔ Entity ayrımı zorunlu**; Mapster ile.
- **Exception handler middleware** (`ProblemDetails` standardı).
- **Endpoint per use case** (Minimal API tercihi); büyüyen modüllerde Controller'a geç.
- **Request validation FluentValidation ile MediatR pipeline behavior'da**.
- **Domain Event** kullan, **Integration Event** Outbox pattern'iyle.
- **No magic strings** — `Constants` veya `enum` üret.
- **Async/await uçtan uca**, `.Result` ve `.Wait()` yasak.
- **EF Core**: `AsNoTracking()` read-only sorgularda; `IQueryable` projeksiyonla `Select`'le; `Include` sadece gerekli yerde.
- **Migration** her modülde ayrı; `dotnet ef migrations add ModuleName_FeatureName`.
- **Logging:** Serilog → Seq. Structured log; PII (telefon, e-posta tam değer) maskele.

---

## 7. Kodlama Kuralları (Mobile / Frontend)

- **TypeScript strict** açık.
- **Component dosyası max ~300 satır**; aşılırsa parçala.
- **State:** local için `useState`; cross-screen için Zustand store; server için TanStack Query.
- **Form:** React Hook Form + Zod schema; `zodResolver` ile.
- **i18n:** her metin string i18next üzerinden; sabit string yasak (test/log dışında).
- **Network:** axios + interceptor (token refresh, retry, request id).
- **Error UX:** her async aksiyonda loading + empty + error state mevcut. "Beyaz ekran" yasak.
- **Accessibility:** `accessible`, `accessibilityLabel`, font scale + min tap target 44pt.

---

## 8. Güvenlik Standartları (Day-1)

- Şifreler **Argon2id** veya ASP.NET Identity default (PBKDF2 with high iterations).
- Token: JWT (RS256), refresh server-side rotated, **device-bound** (refresh token değiştiğinde fingerprint kontrol).
- **Rate limiting** Redis sliding window.
- **OWASP Top 10** kontrol listesi her PR review'da.
- **Secrets** asla repo'da; `.env` + Hetzner secret manager / docker secret.
- **PII** loglarda maskele; `Telefon` → `+90 5•• ••• •• 23` gibi.
- **KVKK aydınlatma + parçalı açık rıza** her kayıt akışında.
- **Audit log** kritik aksiyonlarda (admin onay, ödeme iptal, veri silme).

---

## 9. Test Standartları

- Backend: **xUnit + NSubstitute + Testcontainers (Postgres + Redis)**. Hedef coverage **%70+** core domain, **%50+** infrastructure.
- Mobile: **Jest + React Native Testing Library**. Critical flow **Detox** E2E (booking + payment).
- API contract: **Schemathesis** veya **Bruno** collection.
- **CI'da test gerekli**, kırık test merge yasak.
- **Performance budget**: P95 API latency < 300ms, mobile cold start < 3s.

---

## 10. Sık Komutlar

> Her komutu çalıştırmadan **bir kere oku ve düşün**. Yıkıcı komutlardan (rm, drop, force-push) kullanıcı onayı al.

```bash
# Backend
cd backend && dotnet build
cd backend && dotnet test
cd backend && dotnet ef migrations add Mxx_Description --project src/Kuaforum.Infrastructure --startup-project src/Kuaforum.Api
cd backend && dotnet ef database update --project src/Kuaforum.Infrastructure --startup-project src/Kuaforum.Api
cd backend/src/Kuaforum.Api && dotnet watch run

# Mobile
cd mobile && pnpm install
cd mobile && pnpm start
cd mobile && pnpm eas build --profile development --platform ios

# Web admin
cd web-admin && pnpm dev

# Compose (local dev environment)
docker compose -f infra/docker-compose.dev.yml up -d
docker compose -f infra/docker-compose.dev.yml down
```

---

## 11. Agent Kullanımı

Bu projede şu sub-agent'ların `.claude/agents/` altında tanımı var (Phase 0 onayında oluşturulacak):

- **backend-developer** — .NET 8 + EF Core + MediatR uzmanı
- **mobile-developer** — Expo + React Native + Tamagui uzmanı
- **database-architect** — PostgreSQL + PostGIS + EF Core migration
- **security-auditor** — KVKK + PCI + OWASP review
- **payment-integration** — PayTR + Iyzico + e-Arşiv (Nilvera) uzmanı
- **test-engineer** — xUnit + Jest + Detox
- **devops-engineer** — Docker + Hetzner + GitHub Actions
- **product-strategist** — gereksinim, market, GTM
- **documentation-writer** — markdown disiplinli, kaynak-bazlı

`Agent` çağrısında `subagent_type` parametresi ile seç. Çoklu bağımsız iş paralel agent ile.

---

## 12. MCP & Skills

- **context7** — `mcp__context7__query-docs` ile **her library kullanımı öncesi** dokümantasyon sorgula. Kütüphane API'leri sürekli değişiyor; eğitim verim eskiyebilir.
- Kalıcı kurallar `~/.claude/projects/.../memory/` altında ve bu CLAUDE.md'de paralel tutulur.

---

## 13. Push-Back & Calibration Discipline

Founder agresif scope tercih ediyor. Şu durumlarda mutlaka geri itme yap:
1. **MVP scope tehlikeli büyürse** → "şu feature'ı V1.5'a alalım" öner.
2. **Compliance atlanırsa** → "bu yasal/finansal risk, şöyle ele alalım" diye yaz.
3. **Solo founder kapasitesi tehlikedeyse** → "bu 1 hafta+ ekstra demek" diye uyar.
4. **Premature optimization veya gold-plating** → "buna şimdi ihtiyaç yok" söyle.

---

## 14. Phase Kapanışı Kontrolleri

Her faz/modül kapanışında:
- [ ] Spec doc yazılmış ve self-review (placeholder/çelişki/belirsizlik check)
- [ ] Founder yazılı onayı
- [ ] CLAUDE.md ve master spec güncel
- [ ] Memory (`~/.claude/projects/.../memory/`) ilgili anahtar bulgularla güncel
- [ ] Git commit + tag (`phase-0`, `m1-auth`, ...)

---

## 15. Hızlı Yardım

- **"Şu kısımda devam et"** dediğinde önce `docs/PHASE-0-MASTER-SPEC.md` § 9 deliverable'larını oku, yazılmamış ilkini bul.
- **"Modül X'e geç"** dendiğinde brainstorming skill'i çağır.
- **"Hata aldım"** dendiğinde önce log + repro adımları iste.
- **"Üretime alalım"** dendiğinde § 13 Definition of Done check'lerini sor.

---

**Son güncelleme:** 2026-07-23 — **Cycle 9** (221 backend test yeşil: 114 domain + 50 application + 57 integration; mobile 35 jest testi; tarihçe için `docs/GAP-ANALYSIS.md` + git log).
Cycle 9 kapsamı: (1) 10-agent fullstack gap analizi → GAP-ANALYSIS.md 2026-07-23 ile yeniden üretildi (13 P0 / 16 P1 / 10 P2, dosya:satır kanıtlı); (2) Güvenlik sprint'i — refund IDOR (Admin-only + tutar RefundPercentage'dan), mock/complete Development-only + provider==Mock guard, cancel Reason server-side türetme, Hangfire dashboard loopback-IP, demo login __DEV__; Refund'da ikinci kez EF Core Include+Add→Modified gotcha'sı yakalandı (memory: efcore-include-add-modified-gotcha); (3) İptal→oto-refund: BookingCancelledEvent → BookingRefundService → ProcessBookingRefundJob (AutomaticRetry 5, tutar-bazlı idempotency); (4) KYC onarımı — reject→RevertToDraft (sonsuz kilit bitti), admin belge listesi+auth'lu content proxy (audit'li), KYC onay/red bildirimleri, kuyruk PagedResult; (5) CI onarımı — Application testleri CI'ya eklendi, dotnet 8.0.x + global.json, coverage baseline, mobile jest-expo bootstrap; (6) Deploy temeli — backend+web-admin Dockerfile, compose.prod + Caddy TLS + pg_dump sidecar, ${VAR} placeholder'lar .NET env override'a çevrildi, base'deki dev secret'lar (JWT/Otp/Encryption/Seed:Admin) Development'a taşındı + Production'da eksik secret → fail-fast (negatif smoke kanıtlı), NetGSM/Resend config key eşitleme + prod'da OTP loglanmaz, ForwardedHeaders, GHCR→Hetzner deploy.yml (founder hesapları bekliyor); (7) Slot güvenliği — ExpirePendingBookingsJob (15dk/30dk eşik), müşteri başına 3 Pending limiti, PostgreSQL btree_gist EXCLUDE constraint (M15) + concurrent yarış testleri; (8) Web admin — 401→refresh→retry single-flight + kalıcı deviceFingerprint (Connection.Id tuzağı çözüldü), KYC/Users pagination, enum string-serialization bug fix (suspend/onay butonları görünmüyordu); (9) Spec borcu — docs/rfc RFC-001..005 (hepsi Accepted 2026-07-23; RFC-004 'şimdilik Erzurum, 81 il açık, yurtdışı V2+', RFC-005 OTP-only launch) + as-built spec'ler 05-availability/06-booking/07-payment.
Deploy öncesi checklist: prod DB'de örtüşen aktif booking taraması (M15 constraint), Hangfire kuyruk davranışı staging doğrulaması, GitHub production environment Required reviewers.
**Cycle 10 (2026-07-29→30) — RFC-006 kapora checkout tamamlandı:** 262 backend test (123 domain + 63 application + 76 integration) + 53 mobile jest, tamamı yeşil. Subagent-driven development süreciyle (görev başına implementer + spec review + adversarial kalite review): PaymentKind FullOnline|Deposit + M16 migration; Payment:DepositRate/CommissionRate config (ValidateOnStart, komisyon hard-code'u kalktı); kapora=ceil(total×0.15), komisyon tek kaynak Domain PaymentAmounts (round); ReinitiateCheckout resume/switch (tür değiştirme + failed-retry, tek payment satırı); POST /v1/webhooks/paytr (şema resmi doc'tan doğrulandı, HMAC constant-time + golden-value test, fail-closed boş-key guard'ı — forge açığı review'da yakalandı, tutar-mismatch stale-session koruması, idempotent PaymentCompletionService); mobile: confirm.tsx seçim kartı (kapora varsayılan) + booking/payment.tsx checkout ekranı (mock simülasyon + WebView iskeleti + originWhitelist/toRelativePath token-sızıntı fix'leri + verify-poll/countdown) + detay "Salonda ödenecek" + owner "Salonda: X TL" rozeti + "Ödemeyi tamamla" CTA + cache invalidation; T10: ValidationBehavior Result<T> InvalidCastException fix'i (her ICommand<T> validation'ı 500 dönüyordu — mutation-verified). MockPaymentProvider relative-URL fix. Review zincirinin yakaladığı gerçek açıklar: refund-CTA ölü yolu, webhook boş-key forge, WebView intercept ölü kodu, Bearer exfiltration primitifi, stale CTA. Canlıya alma checklist'i (7 madde, out-of-order failed→success deliği dahil): GAP-ANALYSIS P0-5 güncellemesi.
**Bir sonraki güncelleme:** owner onboarding mobile (Cycle 9 adım 7) + KVKK rıza ekranları (adım 8) + R2 storage + founder hesapları bağlanınca gerçek PSP/SMS entegrasyonları

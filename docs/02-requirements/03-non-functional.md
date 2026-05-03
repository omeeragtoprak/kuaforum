# Non-Functional Requirements (NFR)

> Sistemin "ne yaptığı" değil "**nasıl yaptığı**" konusundaki gereksinimler. Bu hedefler MVP launch'tan **önce** karşılanmalı.

---

## 1. Performans

| Metrik | Hedef (MVP) | Hedef (Scale) |
|---|---|---|
| API CRUD P95 latency | < 300ms | < 200ms |
| API search P95 | < 500ms | < 300ms |
| API checkout init P95 | < 800ms (PSP roundtrip) | < 600ms |
| Mobile cold start | < 3s | < 2s |
| Mobile warm start | < 1s | < 800ms |
| Mobile screen-to-screen | < 200ms | < 150ms |
| Push delivery (FCM) | < 5s | < 3s |
| Webhook ack | < 200ms | < 150ms |
| DB query slow log | > 1s alarm | > 500ms alarm |

## 2. Ölçeklenebilirlik

- **Concurrent users (MVP):** 1.000.
- **Concurrent users (Year 1):** 10.000.
- **Database size hedefi:** 50GB Year 1, 500GB Year 2.
- **Single-host monolith** MVP'de yeter; horizontal scaling Phase 3 (V2).
- **Read replica** Phase 3 — read-heavy raporlar için.
- **Search index size:** Meilisearch 1GB → 10GB.

## 3. Erişilebilirlik (Uptime)

| Component | SLA |
|---|---|
| API | %99.5 (MVP) → %99.9 (Year 1) |
| Mobile app (cloud-side) | %99.5 |
| Web admin | %99.0 (kabul edilebilir, gece bakım OK) |
| Webhook ingest | %99.9 (kayıp tolere edilemez) |

**Bakım penceresi:** Pazar 03:00-05:00 TR — gerekirse + duyuru.

## 4. Güvenlik (Day-1 Production)

- **TLS:** TLS 1.3 zorunlu, TLS 1.2 sadece eski client. HSTS preload.
- **Auth:** JWT RS256 + refresh device-bound + OTP rate limit.
- **Rate limiting:** Endpoint başına Redis sliding window.
- **OWASP Top 10:** Her PR review checklist.
- **Pen-test:** Soft-launch öncesi 3rd party.
- **Audit log:** Append-only, kritik aksiyonlar (admin onay, refund, KYC değişikliği, KVKK silme).
- **Secret:** Repo'da YASAK; Doppler veya docker secret.
- **PII at-rest encryption:** Postgres pgcrypto veya app layer (telefon, e-posta, TCKN).
- **Backup encryption:** WAL-G + GPG encrypt.

## 5. Veri Bütünlüğü

- **Payment + Booking** atomicity (outbox pattern).
- **Idempotency keys** zorunlu (PSP webhook, mutation).
- **No data loss** — webhook delivery 99.9%+, retry budget.
- **Backup retention:** Postgres günlük 30 gün, haftalık 90 gün, aylık 1 yıl.
- **DR test:** Aylık restore drill.

## 6. KVKK / Veri Koruma

- **Açık rıza:** Parçalı toggle (her amaç ayrı).
- **Veri silme SLA:** 30 gün cevap, 30 gün içinde silinmiş olmalı.
- **İhlal bildirim:** 72 saat KVK Kurumu, 72 saat ilgili kişiler.
- **PII mask in logs.** Telefonun sadece son 4 hanesi.
- **Hassas veri (m.6):** Alerji notu / sağlık → ayrı consent + ayrı erişim.
- **Sınır ötesi veri (Hetzner Helsinki):** Standart Sözleşme + 5 iş günü Kuruma bildirim.
- **VERBİS:** Eşik aşılınca derhal başvuru.

## 7. Erişilebilirlik (Accessibility)

- **WCAG 2.1 AA** mobil + web admin.
- **Screen reader** desteği (iOS VoiceOver, Android TalkBack).
- **Font scale** kullanıcı tercihine uy (200%'a kadar).
- **Tap target** min 44pt (iOS) / 48dp (Android).
- **Color contrast** 4.5:1 (normal text).
- **Renk-bağımsız** information (sadece renkle anlam YASAK).

## 8. Kullanılabilirlik (Usability)

- **Time-to-first-booking** (TTFB metric): yeni müşteri sign-up → ilk randevu < **5 dakika**.
- **Kuaför onboarding** sign-up → KYC complete < **15 dakika** (form süresi).
- **Mobile error recovery:** her async aksiyon retry button.
- **Empty state** her listede (placeholder + suggested action).

## 9. Lokalizasyon

- **TR-only MVP.**
- i18next altyapı kuruldu, sıfır hard-coded string.
- **Tarih formatı:** TR (`DD.MM.YYYY`).
- **Para:** ₺ (TRY) only.
- **Saat dilimi:** Europe/Istanbul.
- **Number format:** `1.234,56` TR.

## 10. Bakım & Geliştirme

- **Dependency update review:** Aylık (Renovate veya Dependabot).
- **Major version bump:** Quarterly review, RFC.
- **Migration safety:** Always backwards-compatible (yeni kolon nullable, sonra ilgilensin).
- **Feature flag:** ConfigCat veya self-hosted Unleash (V1.5).

## 11. Observability

- **Error tracking:** Sentry — release tagged.
- **Log:** Serilog → Seq, structured.
- **Metrics:** Prometheus + Grafana (V1.5).
- **Trace:** OpenTelemetry → Seq (V1.5).
- **Uptime:** Better Stack (V1.5).
- **Alerting:** Sentry → Slack/email; Better Stack → PagerDuty (V1.5).

## 12. Compliance Posture

- **PCI-DSS:** Scope **PSP'de**, biz sadece reference. Kart bilgisi backend'e gelmez.
- **KVKK:** Tam uyum (Section 6).
- **VUK 509:** E-Arşiv 7-gün SLA.
- **Tüketici Kanunu:** Kademeli iade politikası uyumlu.

---

## NFR Doğrulama

Soft-launch öncesi her NFR için **test/doğrulama planı** Phase 2 sonu:
- Performans: k6 load test
- Güvenlik: pen-test
- KVKK: avukat audit
- Accessibility: aXe + manuel kontrol
- Uptime: 4 hafta soak test

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/02-requirements/01-mvp-scope.md`](01-mvp-scope.md)
- [`docs/11-testing/01-strategy.md`](../11-testing/01-strategy.md)

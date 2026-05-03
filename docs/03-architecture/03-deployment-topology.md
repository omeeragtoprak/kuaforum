# Deployment Topolojisi

> Hetzner Helsinki + Cloudflare + Docker Compose (MVP) → Kubernetes (V2).

---

## 1. MVP Topolojisi (Tier 1 + Tier 2)

```
                                ┌──────────────────────┐
                                │    Internet (TR)     │
                                └──────────┬───────────┘
                                           │
                                           ▼
                              ┌────────────────────────┐
                              │   Cloudflare CDN/WAF   │
                              │  (DNS, DDoS, rate, R2) │
                              └──────────┬─────────────┘
                                         │ HTTPS
                                         ▼
                       ┌─────────────────────────────────┐
                       │    Hetzner Cloud — Helsinki     │
                       │  ┌───────────────────────────┐  │
                       │  │  CCX13 (4 vCPU, 16GB)     │  │
                       │  │  Docker Compose stack:    │  │
                       │  │  ┌─────────────────────┐  │  │
                       │  │  │  api (.NET 8)       │  │  │
                       │  │  │  web-admin (Next)   │  │  │
                       │  │  │  postgres-16        │  │  │
                       │  │  │  redis-7            │  │  │
                       │  │  │  meilisearch        │  │  │
                       │  │  │  seq                │  │  │
                       │  │  │  hangfire-dashboard │  │  │
                       │  │  └─────────────────────┘  │  │
                       │  └───────────────────────────┘  │
                       │                                 │
                       │  Volumes: /var/lib/postgres     │
                       │           /var/lib/redis        │
                       │           /var/lib/meili        │
                       │           /var/log/seq          │
                       └────────────────┬────────────────┘
                                        │ S3 API
                                        ▼
                              ┌──────────────────┐
                              │ Cloudflare R2    │
                              │ (file storage)   │
                              └──────────────────┘
```

**Single-host justification:** Solo founder, MVP scope, modular monolith. Multi-host yönetimi gereksiz overhead.

---

## 2. Soft-launch Topolojisi (Tier 2 — pilot şehir)

CCX13 → CCX23 (8 vCPU, 32GB) upgrade. Postgres ve Redis **managed**'a geçer (Hetzner Cloud DB):

```
Hetzner Cloud Helsinki:
  - 1× CCX23 (api + web-admin + meilisearch + hangfire + seq)
  - 1× Managed Postgres (orta plan)
  - 1× Managed Redis veya self-host CCX11 dedicated

External:
  - Cloudflare CDN+WAF Pro
  - Cloudflare R2 (file)
  - Sentry, Better Stack, Postmark, Nilvera, NetGSM, FCM
```

---

## 3. National Launch Topolojisi (Tier 3)

Daha agresif — read replica + horizontal API scaling:

```
Hetzner Cloud Helsinki + Frankfurt:
  - 2× CCX33 API (load balanced via Cloudflare)
  - 1× Managed Postgres primary + 1× read replica
  - 1× Managed Redis cluster
  - 2× Meilisearch (replica + master)
  - 1× CCX23 Seq + Hangfire dashboard

External:
  - Cloudflare Business
  - Cloudflare R2 multi-region
  - Sentry Business
  - PagerDuty / Better Stack
```

---

## 4. Environment Listesi

| Environment | Amaç | URL pattern | Erişim | Veri |
|---|---|---|---|---|
| **Local dev** | Geliştirme | `localhost` | Founder | Mock + sandbox |
| **Staging** | QA + pen-test | `staging.kuaforum.com` | IP whitelist + auth | Sentetik veri |
| **Production** | Gerçek kullanıcı | `kuaforum.com` + `app.kuaforum.com` + `admin.kuaforum.com` | Public + auth | Gerçek |

---

## 5. Network & Security

- **Cloudflare** önünde DNS + WAF + bot protection + rate limit.
- **Hetzner firewall:** sadece 80/443 public, 22 SSH IP whitelist.
- **Internal network:** Docker bridge, container'lar arası direkt.
- **Postgres + Redis:** sadece internal network'ten erişilebilir, public expose YASAK.
- **TLS:** Cloudflare Full (Strict) — Cloudflare ↔ Hetzner uçtan TLS, Let's Encrypt origin sertifika.
- **Bağımlı service health:** API başlangıçta Postgres + Redis + Meilisearch ping kontrolü.

---

## 6. Backup & Recovery

| Component | Stratejisi | Retention | Test |
|---|---|---|---|
| Postgres | WAL-G continuous → R2 (encrypted) + nightly base | 30g günlük + 90g haftalık + 1y aylık | Aylık restore drill |
| Redis | RDB snapshot saatlik | 7 gün | Quartal drill |
| File (R2) | R2 versioning + multi-region (V1.5) | 90 gün soft delete | Quartal drill |
| Code | GitHub remote | Sınırsız | — |
| Secrets | Doppler audit log | 90 gün | — |

**RPO (Recovery Point Objective):** 1 saat (continuous WAL).
**RTO (Recovery Time Objective):** 4 saat (cold restore).

---

## 7. Deployment Pipeline

```
GitHub PR open
    │
    ▼
GitHub Actions: lint + test
    │ (green)
    ▼
Merge to develop
    │
    ▼
Build container image (ghcr.io)
    │
    ▼
Auto-deploy to staging (SSH + docker compose pull/up)
    │
    ▼
[Manuel test on staging]
    │
    ▼
Merge develop → main
    │
    ▼
GitHub environment "production" — manuel approval
    │
    ▼
Build + tag image (semver + commit SHA)
    │
    ▼
Deploy production (rolling — old container drains 30s, new spins up)
```

**Rollback:** `docker compose pull <previous-tag> && docker compose up -d` — 1-2 dakika.

---

## 8. Mobile Deployment

- **Dev:** EAS Build dev profile + expo-dev-client.
- **Preview:** TestFlight (iOS) + Internal Testing (Android), EAS preview profile.
- **Production:** App Store + Google Play, EAS production profile.
- **OTA:** EAS Update — kritik patch için Apple/Google review beklemeden push.

---

## 9. Web Admin Deployment

- **Cloudflare Pages** veya **Vercel** Phase 1.
- Production: `admin.kuaforum.com`.

---

## 10. Cost Snapshot (per tier)

| Tier | Hetzner | Cloudflare | Toplam (3rd party hariç) |
|---|---|---|---|
| Bootstrap | $25 | $0 | $25/ay |
| Soft-launch | $115 | $20 | $135/ay |
| National | $400 | $200 | $600/ay |

Detay: [`docs/13-project-management/03-budget-tiers.md`](../13-project-management/03-budget-tiers.md).

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/03-architecture/01-system-architecture.md`](01-system-architecture.md)
- [`docs/12-devops/01-ci-cd.md`](../12-devops/01-ci-cd.md)
- [`docs/12-devops/02-environments.md`](../12-devops/02-environments.md)

# Environment'lar

> Local dev, staging, production. Her birinin kendine özgü erişim, veri, secret kapsamı.

---

## 1. Environment Tablosu

| Environment | Amaç | URL | Erişim | Veri | Secret |
|---|---|---|---|---|---|
| **Local dev** | Geliştirme | `http://localhost:5000` (api), `http://localhost:3000` (admin), Expo dev | Founder | Mock + sandbox | `.env.local` |
| **Staging** | QA + pen-test + customer discovery | `https://staging-api.kuaforum.com`, `https://staging.kuaforum.com`, EAS preview | IP whitelist + auth | Sentetik anonymized | GitHub secrets |
| **Production** | Gerçek kullanıcı | `https://api.kuaforum.com`, `https://app.kuaforum.com`, `https://admin.kuaforum.com` | Public + auth | Gerçek | Doppler / docker secret |

---

## 2. Local Dev Setup

### Gerekli araçlar
- .NET 8 SDK
- Node.js 20 + pnpm 9
- Docker Desktop
- Expo CLI (`npx expo`)
- EAS CLI (`pnpm dlx eas-cli`)
- Visual Studio Code veya Rider

### İlk kurulum
```bash
git clone https://github.com/kuaforum/kuaforum.git
cd kuaforum

# Backend
cd backend
dotnet restore
docker compose -f ../infra/docker-compose.dev.yml up -d  # Postgres + Redis + Meili
dotnet ef database update --project src/Kuaforum.Infrastructure --startup-project src/Kuaforum.Api
dotnet run --project src/Kuaforum.Api

# Mobile (yeni terminal)
cd mobile
pnpm install
cp .env.example .env.local
pnpm start

# Web admin
cd web-admin
pnpm install
pnpm dev
```

### Local data
- Postgres: `kuaforum_dev` DB.
- Seed script: `backend/scripts/seed-dev.sh` (Bogus ile sahte veri).
- Reset: `docker compose down -v && docker compose up -d` + tekrar seed.

---

## 3. Staging

- **Hosting:** Hetzner Helsinki CCX13 (single host).
- **Branch:** `develop` auto-deploy.
- **Veri:** Sentetik. Customer discovery sprint için 30+ test kuaför + 30+ test müşteri.
- **PSP:** PayTR sandbox + Iyzico sandbox.
- **Nilvera:** Sandbox.
- **NetGSM:** Test SMS (gerçek mesaj göndermez).
- **FCM:** Ayrı Firebase project (`kuaforum-staging`).
- **Secret:** GitHub Actions secrets, Hetzner host `.env` (gitignored).

### Staging access
- Web admin: Cloudflare Access (founder + 1-2 customer discovery moderator).
- API: IP whitelist (founder + customer discovery participants).
- Mobile EAS preview: TestFlight (iOS internal) + Google Play Internal Track.

---

## 4. Production

- **Hosting:** Hetzner Helsinki, başlangıç CCX23 → CCX33 scale.
- **Branch:** `main` manuel approval ile deploy.
- **PSP:** PayTR + Iyzico **prod**.
- **Nilvera:** Prod.
- **NetGSM:** Prod (gerçek SMS).
- **FCM:** `kuaforum-prod` Firebase project.
- **Secret:** Doppler (V1.5'tan itibaren), MVP'de docker secret + Hetzner volume encrypted.

### Production access
- Web admin: Founder + (V1.5) ek admin'ler. Cloudflare Access + IP whitelist.
- API: Public.
- Mobile: App Store + Google Play public listing.
- SSH: Founder only, key-based auth, fail2ban + Cloudflare Tunnel (V1.5).

### Production deploy gates
- `main` branch'a merge **PR review** ile (Phase 1+).
- GitHub environment "production" **manuel approval** (founder).
- Migration **staging'de test** edildi tag'lı.
- Rollback playbook hazır.

---

## 5. Mobile Environment Mapping

```ts
// mobile/.env.* dosyaları

// .env.local (gitignored)
API_BASE_URL=http://localhost:5000
SENTRY_DSN=<dev-dsn>
PSP_PROVIDER=paytr  // veya 'mock'

// .env.staging (Expo profile için)
API_BASE_URL=https://staging-api.kuaforum.com
SENTRY_DSN=<staging-dsn>
PSP_PROVIDER=paytr-sandbox

// .env.production
API_BASE_URL=https://api.kuaforum.com
SENTRY_DSN=<prod-dsn>
PSP_PROVIDER=paytr
```

EAS profile mapping (`eas.json`):
```json
{
  "build": {
    "development": { "channel": "development", "developmentClient": true },
    "preview":     { "channel": "preview",     "distribution": "internal" },
    "production":  { "channel": "production",  "autoIncrement": true }
  }
}
```

---

## 6. Database Migration Cross-Environment

| Adım | Local dev | Staging | Production |
|---|---|---|---|
| Migration ekle | Founder yazar | — | — |
| Test | `dotnet ef database update` local | CI auto-apply | — |
| Apply | — | Auto via deploy | **Manuel** veya CI flag-protected |
| Rollback | `dotnet ef database update <PreviousMigration>` | Aynı | Önce backup, sonra script |

---

## 7. Cost per Environment

| Env | Aylık |
|---|---|
| Local dev | $0 |
| Staging | ~$50 |
| Production (Tier 1) | ~$25 |
| Production (Tier 2) | ~$250 |
| Production (Tier 3) | ~$1.500+ |

---

## 8. Environment Promotion

- Feature → develop → staging deploy → QA → main → production deploy.
- Hot-fix: hotfix branch → main direkt → production. Sonra develop'a back-merge.

---

## 9. Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/12-devops/01-ci-cd.md`](01-ci-cd.md)
- [`docs/03-architecture/03-deployment-topology.md`](../03-architecture/03-deployment-topology.md)

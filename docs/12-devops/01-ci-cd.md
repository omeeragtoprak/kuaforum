# CI/CD Pipeline

> GitHub Actions + EAS + Cloudflare Pages. Hetzner SSH deploy. Manuel approval prod öncesi.

---

## 1. Repo Strategy

- **Monorepo:** `backend/` + `mobile/` + `web-admin/` + `infra/` + `docs/`.
- **Branch:** `main` (prod), `develop` (staging), feature branch'ler PR ile develop'a.
- **Branch protection (V1.5):** main + develop force-push yasak, PR review zorunlu.

---

## 2. Backend Pipeline

### `.github/workflows/backend-ci.yml`

```yaml
name: Backend CI
on:
  push: { paths: ["backend/**"], branches: [main, develop] }
  pull_request: { paths: ["backend/**"] }

jobs:
  test:
    runs-on: ubuntu-latest
    services:
      postgres: { image: postgres:16, env: { POSTGRES_PASSWORD: test }, ports: [5432:5432] }
      redis: { image: redis:7, ports: [6379:6379] }
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0' }
      - run: dotnet restore backend/Kuaforum.sln
      - run: dotnet build backend/Kuaforum.sln --no-restore
      - run: dotnet test backend/Kuaforum.sln --no-build --collect:"XPlat Code Coverage"
      - uses: codecov/codecov-action@v4

  build:
    needs: test
    if: github.ref == 'refs/heads/main' || github.ref == 'refs/heads/develop'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: docker/login-action@v3
        with: { registry: ghcr.io, username: ${{ github.actor }}, password: ${{ secrets.GITHUB_TOKEN }} }
      - uses: docker/build-push-action@v5
        with:
          context: backend
          push: true
          tags: ghcr.io/kuaforum/api:${{ github.sha }}

  deploy-staging:
    needs: build
    if: github.ref == 'refs/heads/develop'
    runs-on: ubuntu-latest
    steps:
      - uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.STAGING_HOST }}
          username: ${{ secrets.STAGING_USER }}
          key: ${{ secrets.STAGING_SSH_KEY }}
          script: |
            cd /opt/kuaforum
            export IMAGE_TAG=${{ github.sha }}
            docker compose pull api
            docker compose up -d api
            docker exec kuaforum-api dotnet ef database update

  deploy-prod:
    needs: build
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    environment: production  # GitHub manuel approval
    steps:
      - uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.PROD_HOST }}
          username: ${{ secrets.PROD_USER }}
          key: ${{ secrets.PROD_SSH_KEY }}
          script: |
            cd /opt/kuaforum
            export IMAGE_TAG=${{ github.sha }}
            docker compose pull api
            docker compose up -d api --no-deps
            # Migration burada koşmaz; ayrı manuel adım (ileride otomasyona alınır)
```

---

## 3. Mobile Pipeline (EAS)

### `.github/workflows/mobile-ci.yml`

```yaml
name: Mobile CI
on:
  push: { paths: ["mobile/**"], branches: [main, develop] }
  pull_request: { paths: ["mobile/**"] }

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: pnpm/action-setup@v4
      - uses: actions/setup-node@v4
        with: { node-version: 20 }
      - run: cd mobile && pnpm install
      - run: cd mobile && pnpm typecheck
      - run: cd mobile && pnpm lint
      - run: cd mobile && pnpm test --coverage

  build-preview:
    needs: test
    if: github.ref == 'refs/heads/develop'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: expo/expo-github-action@v8
        with:
          eas-version: latest
          token: ${{ secrets.EXPO_TOKEN }}
      - run: cd mobile && eas build --profile preview --platform all --non-interactive

  ota-update:
    needs: test
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: expo/expo-github-action@v8
        with: { eas-version: latest, token: ${{ secrets.EXPO_TOKEN }} }
      - run: cd mobile && eas update --branch production --message "${{ github.event.head_commit.message }}"
```

---

## 4. Web Admin Pipeline

- **Cloudflare Pages auto-deploy** main + develop branch'lerden.
- Build: `pnpm build` (Next.js standalone).
- Preview: PR'lar için preview URL.

---

## 5. Migration Strategy

- **Backwards-compatible** zorunlu (yeni kolon nullable, sonra ilgilensin).
- **Staging'de doğrula**, **prod manuel apply**.
- **Rollback planı** her migration için yazılı.
- **Long-running migration** background job + zero-downtime deploy.

---

## 6. Secret Management

- **Doppler** (V1.5).
- **MVP:** GitHub Actions secrets + Hetzner host'ta `.env` (gitignored).
- Env var listesi: `infra/.env.example` (placeholder ile).

---

## 7. Pull Request Disiplini

- PR template: değişiklik açıklaması + test plan + migration var mı + breaking change.
- CI yeşil zorunlu.
- (V1.5) Code review otomatik atama.
- **Squash merge** tercih (linear history).

---

## 8. Release Strategy

- **SemVer** backend image tag: `v{major}.{minor}.{patch}` + `commit-sha`.
- **Mobile:** Expo Update branch (development/preview/production).
- **Tag:** `phase-0`, `m1-auth`, `mvp-launch` gibi anlamlı tag'lar.
- **Changelog:** `CHANGELOG.md` her release'te.

---

## 9. Hot-fix Pipeline

- `hotfix/*` branch main'den.
- Direkt main'e merge sonrası develop'a back-merge.
- Mobile için OTA update kullan (Apple/Google review beklemeden).

---

## 10. Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/03-architecture/03-deployment-topology.md`](../03-architecture/03-deployment-topology.md)
- [`docs/12-devops/02-environments.md`](02-environments.md)

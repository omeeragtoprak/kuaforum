# Oracle Cloud Always Free — Production Deployment Runbook

> **Hedef:** Tüm Kuaforum stack'ini **$0/ay sonsuz** Oracle Cloud Always Free Tier üzerinde çalıştır.

## 1. Always Free Tier Nedir?

Oracle Cloud, kayıt olduğun saniyeden itibaren **kalıcı ücretsiz** olan bir set kaynak sunar:

| Kaynak | Free Tier Limit |
|---|---|
| **VM** | 2× E2.1.Micro (1 OCPU + 1GB RAM, AMD x86) **veya** 1× Ampere A1 (4 OCPU + 24GB RAM, ARM) |
| **Block Storage** | 200GB total |
| **Boot Volume** | 50GB her VM |
| **Network** | 10TB egress/ay |
| **Load Balancer** | 1× 10 Mbps |
| **Managed Postgres** | (free tier dışı — self-host kullan) |
| **Object Storage** | 10GB + 50K Class A request/ay |

**Kuaforum için en iyi seçim: Ampere A1 ARM** — 4 OCPU + 24GB RAM çoğu için fazla.

> ⚠️ **Trade-off:** ARM mimarisi → .NET 10 multi-arch image build edilmeli (`linux/arm64`).

---

## 2. Hesap Açma (~15 dk)

1. https://cloud.oracle.com/sign-up
2. **Region seçimi kritik:** "Frankfurt" veya "Amsterdam" (Türkiye'ye en yakın AB DC). KVKK Standart Sözleşme + 5 iş günü Kuruma bildirim ile uyumlu (bkz. `docs/01-research/turkey-compliance.md`).
3. Kişisel bilgiler + kredi kartı doğrulama (charge yok, sadece doğrulama)
4. SMS doğrulama
5. Hesap aktive olduktan sonra: Console aç → "Always Free" rozeti üzerinde "Compute" gör

---

## 3. VM Oluştur (Ampere A1)

### Console üzerinden

1. **Compute → Instances → "Create instance"**
2. **Image:** Ubuntu 24.04 LTS (Always Free uyumlu)
3. **Shape:** `VM.Standard.A1.Flex`
   - OCPU: **4**
   - Memory: **24 GB**
   - "Always Free Eligible" yazısı görünmeli
4. **Networking:**
   - Default VCN seç
   - "Assign a public IPv4 address" ☑
5. **SSH keys:**
   - Mac'inde SSH key yoksa: `ssh-keygen -t ed25519 -C kuaforum-oracle`
   - Console'a `~/.ssh/id_ed25519.pub` içeriğini yapıştır
6. **Boot volume:** 50 GB (varsayılan)
7. **Create**
8. ~2 dk sonra "Running" — public IP'yi not al (örn. `158.180.x.x`)

### Bağlan

```bash
ssh ubuntu@<public-ip>

# İlk seferde:
sudo apt update && sudo apt upgrade -y
sudo timedatectl set-timezone Europe/Istanbul
```

---

## 4. Firewall + Network (Oracle özel)

Oracle iki katman firewall var: **iptables** (VM içi) + **Security List** (VCN seviyesi).

### Security List (Oracle Console)

1. **Networking → Virtual Cloud Networks → default-vcn → default subnet → Default Security List**
2. **Ingress Rules → Add:**
   - Source: `0.0.0.0/0` · Protocol: TCP · Port: **80** (HTTP)
   - Source: `0.0.0.0/0` · Protocol: TCP · Port: **443** (HTTPS)
3. SSH (port 22) zaten açık olmalı

### Ubuntu iptables

Oracle Ubuntu image'ı **default DROP** politikalı geliyor. Bunu Cloudflare Tunnel kullanmadıysak override et:

```bash
sudo iptables -I INPUT 1 -p tcp --dport 80 -j ACCEPT
sudo iptables -I INPUT 1 -p tcp --dport 443 -j ACCEPT
sudo netfilter-persistent save
```

---

## 5. Docker + Compose

```bash
# Docker resmi yükleyici
curl -fsSL https://get.docker.com | sudo sh

# Compose v2 plugin (built-in çoğunlukla)
sudo apt install -y docker-compose-plugin

# Ubuntu user'ı docker grubuna ekle (sudo'suz docker)
sudo usermod -aG docker ubuntu
exit  # SSH'tan çık ve tekrar gir → grup üyeliği aktive olur

# Doğrula
docker version
docker compose version
```

---

## 6. Repo'yu Çek + Build

```bash
# Git
sudo apt install -y git
cd ~
git clone <repo-url> kuaforum
cd kuaforum
```

### .NET 10 image multi-arch build (ARM uyumlu)

**Mac'inde** (kendi geliştirme makinende):

```bash
# Buildx kur (eğer yoksa)
docker buildx create --use --name multiarch

# Build + push (GitHub Container Registry)
cd backend
docker buildx build \
  --platform linux/arm64,linux/amd64 \
  -f Dockerfile \
  -t ghcr.io/<owner>/kuaforum-api:latest \
  --push .
```

> Backend `Dockerfile`'ı henüz yok. Aşağıdaki içerikle `backend/Dockerfile` oluştur (örnek):

```dockerfile
# backend/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY *.sln Kuaforum.slnx ./
COPY src/Kuaforum.Api/Kuaforum.Api.csproj src/Kuaforum.Api/
COPY src/Kuaforum.Application/Kuaforum.Application.csproj src/Kuaforum.Application/
COPY src/Kuaforum.Domain/Kuaforum.Domain.csproj src/Kuaforum.Domain/
COPY src/Kuaforum.Infrastructure/Kuaforum.Infrastructure.csproj src/Kuaforum.Infrastructure/
RUN dotnet restore src/Kuaforum.Api/Kuaforum.Api.csproj
COPY src/ src/
RUN dotnet publish src/Kuaforum.Api/Kuaforum.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN groupadd -r app && useradd -r -g app -u 1001 app
COPY --from=build --chown=app:app /app/publish .
USER app
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "Kuaforum.Api.dll"]
```

---

## 7. Production docker-compose

VM'de `~/kuaforum/infra/docker-compose.prod.yml` oluştur:

```yaml
services:
  postgres:
    image: postgis/postgis:16-3.4-alpine
    restart: unless-stopped
    environment:
      POSTGRES_USER: ${POSTGRES_USER:-kuaforum}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: ${POSTGRES_DB:-kuaforum}
    volumes:
      - postgres-data:/var/lib/postgresql/data
      - ./postgres-init:/docker-entrypoint-initdb.d:ro
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER}"]
      interval: 10s

  redis:
    image: redis:7-alpine
    restart: unless-stopped
    command: ["redis-server", "--appendonly", "yes"]
    volumes:
      - redis-data:/data

  meilisearch:
    image: getmeili/meilisearch:v1.11
    restart: unless-stopped
    environment:
      MEILI_ENV: production
      MEILI_MASTER_KEY: ${MEILI_MASTER_KEY}
    volumes:
      - meilisearch-data:/meili_data

  api:
    image: ghcr.io/<owner>/kuaforum-api:latest
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__Postgres: "Host=postgres;Database=${POSTGRES_DB:-kuaforum};Username=${POSTGRES_USER:-kuaforum};Password=${POSTGRES_PASSWORD}"
      ConnectionStrings__Redis: "redis:6379"
      Jwt__SigningKey: ${JWT_KEY}
      Jwt__Issuer: kuaforum
      Jwt__Audience: kuaforum-clients
      Encryption__Key: ${ENCRYPTION_KEY}
      Otp__Pepper: ${OTP_PEPPER}
      Sms__Provider: ${SMS_PROVIDER:-mock}
      Email__Provider: ${EMAIL_PROVIDER:-mock}
      Resend__ApiKey: ${RESEND_API_KEY:-}
      Resend__From: ${RESEND_FROM:-}
      Payment__DefaultProvider: ${PAYMENT_PROVIDER:-mock}
      Cors__AllowedOrigins__0: ${WEB_ADMIN_URL:-https://admin.kuaforum.com}
      Cors__AllowedOrigins__1: ${MOBILE_ORIGIN:-https://app.kuaforum.com}
    depends_on:
      postgres: { condition: service_healthy }
      redis: { condition: service_started }
    expose: ["5000"]

  caddy:
    image: caddy:2-alpine
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy-data:/data
      - caddy-config:/config
    depends_on: [api]

volumes:
  postgres-data:
  redis-data:
  meilisearch-data:
  caddy-data:
  caddy-config:
```

**Caddyfile** (otomatik HTTPS — Let's Encrypt ücretsiz):

```caddy
api.kuaforum.com {
    reverse_proxy api:5000
    encode gzip zstd
    header {
        Strict-Transport-Security "max-age=31536000; includeSubDomains; preload"
        X-Content-Type-Options "nosniff"
        Referrer-Policy "strict-origin-when-cross-origin"
        -Server
    }
}
```

**`.env` dosyası** (KESİNLİKLE GIT'E COMMIT ETMEYIN):

```bash
POSTGRES_USER=kuaforum
POSTGRES_PASSWORD=<openssl rand -base64 32>
POSTGRES_DB=kuaforum
MEILI_MASTER_KEY=<openssl rand -base64 32>
JWT_KEY=<openssl rand -base64 64>
ENCRYPTION_KEY=<openssl rand -base64 32>
OTP_PEPPER=<openssl rand -base64 32>
SMS_PROVIDER=mock          # netgsm gerçek hesap geldikten sonra
EMAIL_PROVIDER=resend
RESEND_API_KEY=re_...      # https://resend.com/api-keys
RESEND_FROM=Kuaforum <noreply@kuaforum.com>
PAYMENT_PROVIDER=mock      # paytr gerçek hesap geldikten sonra
WEB_ADMIN_URL=https://admin.kuaforum.com
MOBILE_ORIGIN=https://app.kuaforum.com
```

---

## 8. Cloudflare DNS + Free SSL

1. Cloudflare → Free plan ile domain ekle (`kuaforum.com.tr` veya `kuaforum.com`)
2. **DNS:** A record:
   - `api` → `<oracle-public-ip>` (Proxied off — Caddy SSL alacak; Cloudflare proxy konflikt yapar)
   - `admin` → Cloudflare Pages URL'i
3. Caddy otomatik Let's Encrypt çekecek (port 80 + 443 açık olmalı)

---

## 9. İlk deploy

```bash
cd ~/kuaforum/infra
nano .env  # yukarıdaki değerleri yapıştır + secret üret
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d

# Migration uygula (ilk kez)
docker compose -f docker-compose.prod.yml exec api dotnet Kuaforum.Api.dll
# Migration auto-apply — Program.cs'de wired (ileride RFC ile)
# Şimdilik manuel ssh:
docker compose -f docker-compose.prod.yml exec api /bin/bash -c "
  cd /app && dotnet ef database update
"  # NOT: dotnet ef tool runtime image'da yok, alternatif: migration EF CLI'siz uygula

# Logs izle
docker compose -f docker-compose.prod.yml logs -f api
```

> ⚠️ **Migration not:** `dotnet ef database update` runtime image'ında yok. 3 seçenek:
> 1. Build image'ında SDK varsa migration için ayrı container çalıştır
> 2. Program.cs'e `await db.Database.MigrateAsync()` ekle (auto-apply on start)
> 3. SSH ile bare-metal `dotnet ef` (tool global'a kur)
>
> Önerilen (V1.5): #2 + Hangfire health check.

---

## 10. Web Admin Deploy (Cloudflare Pages — ücretsiz)

```bash
# Mac'inde
cd web-admin
pnpm build

# Cloudflare Pages CLI
npx wrangler pages publish .next --project-name=kuaforum-admin
# İlk seferde: cloudflare hesap auth, project oluştur
# Sonraki sefer: aynı komut, otomatik deploy
```

URL: `kuaforum-admin.pages.dev` (ücretsiz subdomain) veya `admin.kuaforum.com.tr` (custom domain).

---

## 11. Mobile EAS Build

EAS Build free tier: **30 build/ay** (hem iOS hem Android için).

```bash
cd mobile
pnpm dlx eas-cli login   # Expo hesabı (ücretsiz)
pnpm dlx eas-cli build:configure
pnpm dlx eas-cli build --platform android --profile preview
# ~15 dk sonra .apk linki gelir → telefondan indirip yükle
# (Apple Dev hesabı YOKSA bile Android için preview build ücretsiz)
```

---

## 12. Bütçe Doğrulama

| Kalem | Aylık |
|---|---|
| Oracle Cloud A1 4-OCPU + 24GB | **$0** |
| Cloudflare Free + Pages | **$0** |
| Cloudflare R2 (10GB ücretsiz) | **$0** |
| Resend free (3K e-posta/ay) | **$0** |
| Sentry free (5K event/ay) | **$0** |
| EAS Build free (30 build/ay) | **$0** |
| FCM | **$0** |
| GitHub Actions (public repo) | **$0** |
| Domain (.com.tr) | **~₺3-5/ay amortisman** |
| **TOPLAM aylık sabit** | **~₺3-5** |

Transaction-bazlı (sadece kullanım var ise):
- PSP komisyon (gelir gelirse)
- NetGSM SMS (~₺0.05/SMS, ya da Mock'la kal)

---

## 13. Bağlantılar

- Oracle Cloud Always Free: https://www.oracle.com/cloud/free/
- Caddy auto-HTTPS: https://caddyserver.com/docs/automatic-https
- Cloudflare Pages: https://pages.cloudflare.com/
- EAS Build: https://docs.expo.dev/build/introduction/
- [`docs/FREE-SETUP.md`](../FREE-SETUP.md) — ücretsiz alternatifler genel rehber
- [`mobile/RUNNING-ANDROID-STUDIO.md`](../../mobile/RUNNING-ANDROID-STUDIO.md) — emulator ile dev test

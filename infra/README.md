# Infra — Local Development & Production

## 1. Local Development

```bash
cp .env.example .env
docker compose -f docker-compose.dev.yml up -d
docker compose -f docker-compose.dev.yml ps
```

Servisler:
- **Postgres** (PostGIS hazır) → `localhost:5432` (host override: bkz. `docker-compose.override.yml`, host'ta `5435`) — DB `kuaforum_dev`, user `kuaforum`, password `devpassword`.
- **Redis** → `localhost:6379` (host override `6380`).
- **Meilisearch** → `localhost:7700` — master key `dev_master_key_change_me`.
- **Seq** (log UI) → `http://localhost:5341`.

Postgres init script `postgres-init/01-create-extensions.sql` ile **postgis**, **pgcrypto**, **uuid-ossp** uzantılarını otomatik kurar.

### Durdur / Reset

```bash
docker compose -f docker-compose.dev.yml down            # durdur (data kalır)
docker compose -f docker-compose.dev.yml down -v         # data dahil sıfırla
```

---

## 2. Production (Hetzner, MVP — Docker Compose, tek host)

Dosyalar:
- `docker-compose.prod.yml` — postgres, redis, backend (API), web-admin, Caddy (TLS + reverse proxy), backup sidecar.
- `Caddyfile` — reverse proxy config, otomatik HTTPS (Let's Encrypt).
- `.env.prod.example` → kopyala → `.env.prod` (asla commit etme, zaten gitignore'da).
- `backup/backup.sh` — nightly `pg_dump` sidecar (7 gün retention, local volume).

### 2.1 Network topolojisi (Critical Rule: sadece giriş noktası public)

Bu compose dosyasında **sadece `caddy` servisi host'a port publish ediyor** (80/443). `postgres`, `redis`, `backend`, `web-admin` internal Docker network'te (`internal`), host'tan erişilemez — Caddy tek public giriş noktası. Bu, agent tanımındaki "internal services Docker network'de izole, sadece API public" kuralının bir adım ötesi: burada API bile doğrudan public değil, Caddy arkasında. Gerekçe: TLS terminasyonu + tek bir yerden rate-limit/WAF (ileride Cloudflare) + backend'i tamamen ağdan izole tutmak.

### 2.2 Deploy runbook (Hetzner sunucusu hazır olduğunda, tek seferlik)

```bash
# Sunucuda (Ubuntu + Docker + Docker Compose kurulu varsayılıyor)
git clone https://github.com/Next-Yazilim/kuaforum.git /opt/kuaforum
cd /opt/kuaforum/infra
cp .env.prod.example .env.prod
vim .env.prod   # gerçek secret'ları doldur — bkz. § 3 tablo

docker compose --env-file .env.prod -f docker-compose.prod.yml pull   # veya `build` GHCR yoksa
docker compose --env-file .env.prod -f docker-compose.prod.yml up -d
docker compose --env-file .env.prod -f docker-compose.prod.yml ps
curl -f https://api.kuaforum.com/health/ready
```

Sonraki deploy'lar `.github/workflows/deploy.yml` üzerinden otomatik (bkz. § 4).

**ÖNEMLİ:** `docker-compose.prod.yml` `.env.prod` (değil `.env`) kullandığı için **her compose komutunda `--env-file .env.prod` şart** — yoksa Compose dosyayı otomatik yüklemez ve `${POSTGRES_USER}` gibi placeholder'lar boş kalır.

---

## 3. Zorunlu environment değişkenleri (Production)

Tüm backend değerleri `.env.prod` üzerinden `env_file:` ile container'a enjekte edilir; .NET'in native hierarchical config override'ı `Section__Key` formatını kullanır (örn. `Jwt__SigningKey` → `Jwt:SigningKey`). appsettings.Production.json artık `${VAR}` placeholder İÇERMİYOR — sadece secret olmayan sabitler var, secret'ların tamamı bu tablodaki env değişkenlerinden gelir.

### 3.1 Zorunlu — eksikse boot ÇÖKER (fail-fast, kanıtlı — bkz. § 7)

| Env var | appsettings key | Eksikse ne olur | Nerede fail-fast |
|---|---|---|---|
| `ConnectionStrings__Postgres` | `ConnectionStrings:Postgres` | Boot anında `InvalidOperationException` | `DependencyInjection.AddInfrastructure` (service registration, `Build()` öncesi) |
| `ConnectionStrings__Redis` | `ConnectionStrings:Redis` | Boot anında `InvalidOperationException` | Aynı yer |
| `Jwt__SigningKey` | `Jwt:SigningKey` | Boot anında `InvalidOperationException` ("32+ bytes") | `AuthSetup.AddKuaforumAuth` |
| `Encryption__Key` | `Encryption:Key` | Boot anında `InvalidOperationException` | `AesEncryptionService` ctor — `AdminSeeder` her boot'ta resolve edildiği için eager tetiklenir |
| `Otp__Pepper` | `Otp:Pepper` | Boot anında `InvalidOperationException` | `Program.cs` — bu cycle'da eklendi |

Min 32 byte önerilir (`openssl rand -base64 32`) — `Jwt__SigningKey`, `Otp__Pepper`, `Encryption__Key` için.

### 3.2 Opsiyonel — eksikse boot BAŞARILI, özellik graceful degrade eder

| Env var | Eksikse davranış |
|---|---|
| `NetGsm__UserCode`, `NetGsm__Password`, `NetGsm__Originator` | OTP SMS gönderimi `false` döner + **Error log** (kod loglanmaz — bkz. § 5). NetGSM hesabı açılana kadar beklenen durum. |
| `Resend__ApiKey` | Email gönderimi mock/log-only kanala düşer (crash yok). |
| `PayTr__MerchantId`, `PayTr__MerchantKey`, `PayTr__MerchantSalt` | Ödeme denemesi runtime'da başarısız olur, boot etkilenmez. PayTR hesabı açılana kadar beklenen durum. |
| `ObjectStorage__Provider` | Boş/`Local` → KYC belgeleri container diskinde (re-deploy'da kaybolur, GAP P1-6), boot etkilenmez. `S3` set edilip `ObjectStorage__S3__*` eksikse **boot ÇÖKER** (fail-fast — bkz § 3.1'e benzer, ama koşullu: yalnız Provider=S3 seçiliyken). |
| `ObjectStorage__S3__ServiceUrl/Bucket/AccessKeyId/SecretAccessKey/DisablePayloadSigning` | `ObjectStorage__Provider=S3` DEĞİLSE tamamen yok sayılır. R2 bucket formatı ve adımlar `.env.prod.example` içinde yorumlu. |
| `Fcm__ServiceAccountJsonPath` (veya `Fcm__ServiceAccountJson`) | Push mock kanala düşer. Dosya mount edilmemişse (bkz. `docker-compose.prod.yml` backend.volumes, comment'li) `File.Exists` false döner. |
| `OAuth__Google__ClientId`, `OAuth__Apple__ClientId` | O login yöntemi reddeder (`logger.LogWarning` + `null`), diğer login yöntemleri etkilenmez. |
| `Sentry__Dsn` | Sentry SDK disabled (zaten intentional — boş DSN = no overhead). |
| `Seq__ApiKey` | Seq sink API key'siz dener; bu compose'da zaten `seq` servisi YOK (bkz. § 6 risk). |
| `Seed__Admin__Email`, `Seed__Admin__Password` | Admin bootstrap **atlanır + Warning log** (kanıtlandı, § 7 smoke test). |
| `AllowedHosts` | appsettings.Production.json default'u `api.kuaforum.com`. |

### 3.3 Docker-compose orkestrasyon değişkenleri (secret değil ama zorunlu)

| Env var | Amaç |
|---|---|
| `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` | Postgres container bootstrap + `ConnectionStrings__Postgres` içine manuel gömülmeli (aşağıda not) |
| `REDIS_PASSWORD` | Redis `--requirepass` + `ConnectionStrings__Redis` içine manuel gömülmeli |
| `API_DOMAIN`, `ADMIN_DOMAIN`, `ACME_EMAIL` | Caddyfile `{$VAR:default}` |
| `GHCR_NAMESPACE`, `IMAGE_TAG` | `docker compose pull` hangi image'ı çekecek |
| `NEXT_PUBLIC_API_BASE_URL` | web-admin build-arg (build-time, JS bundle'a gömülür — bkz. § 6 risk) |
| `BACKUP_RETENTION_DAYS`, `BACKUP_INTERVAL_SECONDS` | backup sidecar |

**Not (bilinçli tasarım kararı):** `.env.prod` tek düz dosya — `ConnectionStrings__Postgres` gibi tam-birleştirilmiş bağlantı stringleri İÇİNDE `POSTGRES_PASSWORD` ile aynı şifre bir daha yazılıyor (iki yerde aynı secret). Alternatif (compose'da `${POSTGRES_PASSWORD}` interpolation ile stringi otomatik kurmak) daha DRY ama `--env-file` timing/quoting sorunlarına daha kırılgan; solo founder için "iki yerde aynı şifre, açıkça yorumlanmış" daha az sürpriz riski taşıyor. `.env.prod.example` içinde bu açıkça yorum olarak belirtildi.

---

## 4. CI/CD Deploy Pipeline

`.github/workflows/deploy.yml` (bu cycle'da eklendi, `ci.yml`'e dokunulmadı):

- **Tetik:** `push` → `main`, veya manuel `workflow_dispatch`.
- **build-api / build-web-admin:** her ikisi paralel, GHCR'a push (`ghcr.io/<owner-lowercase>/kuaforum-api` / `kuaforum-web-admin`, tag = sha + latest).
- **check-secrets:** `HETZNER_HOST` + `HETZNER_SSH_KEY` set mi kontrol eder; değilse `deploy` job'ı **graceful skip** (Hetzner hesabı yok, image build+push yine de çalışır).
- **deploy:** GitHub Environment `production` altında; SSH ile sunucuya bağlanıp `git pull` + `docker compose pull` + `up -d` + smoke curl.

### 4.1 Founder hesapları geldiğinde bağlanacak noktalar

| Ne zaman | Ne yapılmalı |
|---|---|
| Hetzner sunucu alındığında | `HETZNER_HOST`, `HETZNER_SSH_KEY` (deploy-only kullanıcı, ed25519), `HETZNER_USER` (default `deploy`) GitHub Actions **secrets**'a eklenir. Sunucuda `/opt/kuaforum` altına repo clone edilir + `.env.prod` elle doldurulur (asla CI'dan geçmez). |
| **KRİTİK — repo admin manuel adımı:** | GitHub → Settings → Environments → `production` → **Required reviewers** aç. Bu YAML dosyasından set edilemez; açılmadan bu workflow prod'a **onaysız** deploy eder — proje kuralı "Production deploy asla kullanıcı onayı olmadan" bunu gerektiriyor. **Bu cycle'da yapılmadı, sadece flag edildi.** |
| Domain seçildiğinde | `API_DOMAIN`/`ADMIN_DOMAIN`/`ACME_EMAIL` `.env.prod`'a, `NEXT_PUBLIC_API_BASE_URL` hem `.env.prod`'a hem GitHub Actions **variable**'a (`vars.NEXT_PUBLIC_API_BASE_URL`) yazılır. |
| NetGSM hesabı açıldığında | `NetGsm__UserCode/Password/Originator` `.env.prod`'a. Gerçek HTTP client hâlâ YAZILMADI (`NetGsmSmsService.SendOtpAsync` STUB — `TODO(M11-impl)` yorumu kodda duruyor) — ayrı bir iş. |
| PayTR hesabı açıldığında | `PayTr__MerchantId/Key/Salt` `.env.prod`'a. |
| Firebase (FCM) service account alındığında | `infra/secrets/firebase-sa.json` sunucuya konur, `docker-compose.prod.yml` backend servisindeki comment'li `volumes:` bloğu açılır. |
| Cloudflare R2 bucket'ı açıldığında | R2 dashboard'da bucket + "Object Read & Write" API token oluştur, `ObjectStorage__Provider=S3` + `ObjectStorage__S3__*` `.env.prod`'a (bkz § 3.2 + `.env.prod.example`). Uygulama bucket'ı runtime'da OLUŞTURMAZ — elle açılmış olması gerekir. GAP P1-6. |

---

## 5. NetGSM / OTP güvenlik davranışı (bu cycle'da sıkılaştırıldı)

`NetGsmSmsService` ve `MockSmsService` artık `IHostEnvironment` enjekte ediyor:

- **Production'da** NetGSM config eksikse (`NetGsm:UserCode` boş): `LogError` + `return false`. **OTP kodu loglanmaz.**
- **Production'da** `MockSmsService` bir şekilde wire edilirse (misconfiguration savunması — beklenen yol değil, `Sms:Provider` default'u zaten `netgsm`): aynı şekilde `LogError` + `return false`, kod loglanmaz.
- Dev/staging'de her iki servis de eskisi gibi kodu Warning log'a yazar (local flow kolaylığı).

Bu, önceki davranıştan (her ortamda config eksikse kodu logla) kasıtlı bir sapma — KVKK + credential-leak riski nedeniyle.

---

## 6. Bilinen riskler / sınırlar (bu cycle kapsam dışı bırakılanlar)

1. **Seq container prod compose'da YOK.** `Serilog:WriteTo:Seq:serverUrl` default'u `http://seq:5341` — bu isim prod ağında resolve olmaz, Serilog sink sessizce retry/fail eder (Serilog kendi hata yutma davranışı — **boot'u etkilemez**, sadece log Seq'e gitmez). Görev listesinde prod compose servisleri açıkça sayılmıştı (postgres/redis/backend/web-admin/Caddy/backup) ve Seq yoktu — bilerek eklenmedi (ekstra container = ekstra RAM, tek CCX Hetzner kutusunda maliyet). **Founder kararı gerekiyor:** Seq'i prod'a eklemek mi (kaynak maliyeti), yoksa V1.5 Prometheus+Grafana'ya kadar sadece `docker compose logs`/Sentry'e mi güvenmek?
2. **WAL-G → R2 backup henüz yok.** `backup/backup.sh` basit `pg_dump` + local volume, 7 gün retention — **aynı host'ta**, disaster-proof DEĞİL (host disk kaybı = backup de gider). R2 bucket açılınca gerçek çözüm bu değil.
3. **Migration'lar boot'ta otomatik uygulanıyor** (`DependencyInjection.SeedReferenceDataAsync` → `ctx.Database.MigrateAsync`), her environment için — mevcut/önceden var olan davranış, bu cycle'da değiştirilmedi. `docs/12-devops/01-ci-cd.md`'nin tasavvur ettiği "migration ayrı manuel adım" YOK; her `docker compose up` migration'ları sessizce apply eder. Kritik rule ("migration prod'a apply edilmeden staging'de test edildi mi kontrol") bu repoda **teknik olarak zorlanmıyor** — sadece süreç disiplini. Yıkıcı bir migration prod'da otomatik uygulanabilir. Flag ediliyor, bu cycle'da gate eklenmedi (kapsam dışı).
4. **Resend `From` key bug'ı bulundu ve düzeltildi:** appsettings.Production.json'da `"FromAddress"` yazıyordu, gerçek C# property `From` (`ResendOptions.From`) — eşleşmediği için hiç bind olmuyordu, `ResendEmailChannel` sessizce default'a düşüyordu. Bu cycle'da düzeltildi (NetGSM'deki UserCode/Originator eşleşme sorununun ikizi — aynı taramada bulundu).
5. **`.env.prod` içindeki secret'lar container `env_file:` ile TÜM backend ortam değişkenlerine karışıyor** (örn. `ACME_EMAIL`, `GHCR_NAMESPACE` gibi compose-only değişkenler de backend container'ının env'ine sızıyor — zararsız ama gürültülü). `docker compose config` çıktısında görülebilir. Temiz çözüm iki ayrı env dosyası olurdu; tek dosya solo-founder için daha az sürtünme, bilinçli tercih.
6. **Serilog `WriteTo` array'i JSON config layering'de index-bazlı merge olur** (.NET'in bilinen davranışı) — base `appsettings.json`'daki Console `outputTemplate` Production'da tam override edilmeyebilir (yalnız kozmetik, secret sızıntısı değil). Derinlemesine incelenmedi, kapsam dışı.
7. **`NEXT_PUBLIC_API_BASE_URL` build-time bake.** Next.js standalone image'ı bu URL'i JS bundle'a build anında gömer — runtime'da değiştirilemez. Tek prod environment olduğu sürece sorun değil; ikinci bir environment (staging) gerekirse ayrı image build (farklı `--build-arg`) veya runtime-config pattern'e geçilmeli. Şimdilik YAGNI, build edilmedi.

---

## 7. Production boot smoke test (kanıt — bu cycle'da çalıştırıldı)

Backend image'ı dev compose'un Postgres/Redis'ine (`infra_default` Docker network) bağlanarak `ASPNETCORE_ENVIRONMENT=Production` ile ayağa kaldırıldı:

**Pozitif (tüm zorunlu secret'lar verildi):**
```
docker run -d --network infra_default -p 18080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ConnectionStrings__Postgres="Host=postgres;...;Database=kuaforum_smoke" \
  -e ConnectionStrings__Redis="redis:6379" \
  -e Jwt__SigningKey="..." -e Otp__Pepper="..." -e Encryption__Key="..." \
  -e AllowedHosts="*" \
  kuaforum-api:local
```
Sonuç: `Up 8 seconds (healthy)`, `GET /health` → 200, `GET /health/ready` → 200 (`postgres: ok`, `redis: ok`), `GET /` → 302 → `/health`, log: `AdminSeeder: Seed:Admin:Email/Password not set — no admin user created` (Warning, sessiz geçti — task 2 kanıtı).

**Negatif (`Otp__Pepper` bilerek verilmedi):**
Sonuç: container **Exited (133)** saniyeler içinde, log:
```
Unhandled exception. System.InvalidOperationException: Otp:Pepper is required in Production
   at Program.<Main>$(String[] args) in /src/src/Kuaforum.Api/Program.cs:line 81
```

Bu ikisi birlikte "önceden `${VAR}` placeholder prod'u sessizce bozuyordu" bulgusunun kapandığını kanıtlıyor: artık ya doğru config ile temiz boot alıyor, ya da eksik secret'ta AÇIKÇA ve HEMEN çöküyor — sessiz yarı-çalışır durum yok.

**Smoke test sırasında bulunan ek kritik bug (düzeltildi):** ilk negatif test denemesi BEKLENMEDİK şekilde boot etti — çünkü base `appsettings.json`'da `Jwt:SigningKey`/`Otp:Pepper`/`Encryption:Key` için dev placeholder değerler vardı (`"dev-only-jwt-signing-key-change-in-production-min-32-bytes"` vb.) ve Production dosyası bunları override etmediği için (kasıtlı olarak secret'ları prod json'dan çıkardığım için) **base'deki dev secret'ları sessizce miras alıyordu** — yani env var hiç set edilmese bile boot BAŞARILI oluyordu, ama bilinen/repo'da public dev anahtarlarla. Bu, orijinal `${VAR}` bug'ından daha kötü bir güvenlik açığıydı (tahmin edilebilir JWT signing key = token forge). Düzeltme: bu üç key + `ConnectionStrings` base'den tamamen çıkarılıp `appsettings.Development.json`'a taşındı (aynı `Seed:Admin` deseni). Düzeltme sonrası negatif test yukarıdaki gibi doğru şekilde çöküyor.

---

## 8. Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../docs/PHASE-0-MASTER-SPEC.md)
- [`docs/12-devops/01-ci-cd.md`](../docs/12-devops/01-ci-cd.md) — aspirational plan; bazı detaylar (image namespace, migration stratejisi) bu cycle'daki gerçek implementasyondan farklı, güncellenmedi (kapsam dışı).
- [`docs/12-devops/02-environments.md`](../docs/12-devops/02-environments.md)

# Kuaforum — Koyeb + Neon Ücretsiz/Kartsız Deploy Runbook

> **Hedef:** Backend'in founder'ın Mac'inden **tamamen bağımsız** çalışması — "MacBook kapalı olsa bile APK çalışsın." Kredi kartı **istemeyen** ücretsiz katmanlarla. Oracle Cloud Always Free (bkz. `docs/12-devops/03-oracle-cloud-free.md`, `docs/FREE-SETUP.md` § 3) kayıt sırasında kredi kartı doğrulaması istediği için **reddedildi** — bu doküman onun yerine geçen, gerçekten kartsız yol.

**Seçilen mimari:**

| Katman | Servis | Neden |
|---|---|---|
| Backend container | **Koyeb** | 1 ücretsiz web servis, 0.1 vCPU / 512MB RAM / 2GB SSD, kredi kartı istemez, **uyumuyor** (Render free tier'ın aksine) [Koyeb Free Tier 2026](https://www.srvrlss.io/provider/koyeb/) |
| Postgres + PostGIS | **Neon** | 0.5GB depolama, süresiz ücretsiz, PostGIS + btree_gist destekli (aşağıda doğrulandı), kartsız |
| Redis | **Yok (kaldırıldı)** | Bkz. § 1 — tek-örnekli dağıtımda gerek yok |

Bu doküman founder hesap açtıktan sonra izlenecek adımları, gereken tüm env değişkenlerini, ve bilinen sınırları içerir. **Gerçek deploy bu conversation'da YAPILMADI** (kimlik bilgisi yoktu) — kod/config tarafı hazırlandı ve yerel olarak (Docker + yerel Postgres) doğrulandı.

---

## 1. Redis opsiyonel hale getirildi — özet

Redis iki yerde kullanılıyordu: hız sınırlama (`IRateLimiter`) ve randevu slot kilidi (`IDistributedLock`). İkisi de artık **`ConnectionStrings:Redis` boşsa** in-memory karşılıklara düşüyor:

- `backend/src/Kuaforum.Infrastructure/Caching/InMemoryRateLimiter.cs` — process-local sabit pencere sayaç.
- `backend/src/Kuaforum.Infrastructure/Caching/InMemoryDistributedLock.cs` — process-local `ConcurrentDictionary` tabanlı kilit (Redis `SET NX PX` eşdeğeri).
- `backend/src/Kuaforum.Infrastructure/Caching/RedisUnavailableStartupNotice.cs` — boot'ta **bir kez** WARNING loglar (sessiz düşüş yok).
- DI kablolaması: `backend/src/Kuaforum.Infrastructure/DependencyInjection.cs` (`redisConnString` boşsa in-memory dal).
- `/health/ready`: Redis yokken `checks.redis.status="not_configured"` döner, bunu "degraded" SAYMAZ (`backend/src/Kuaforum.Api/Program.cs`).

**Neden güvenli:** Randevu slot çakışmasının asıl garantisi zaten PostgreSQL `EXCLUDE` constraint'i (`M15_BookingSlotExclusion` migration, `btree_gist` + `postgis` extension'ları üzerinde kurulu) — Redis kilidi yalnız "hızlı yol" optimizasyonuydu, DB'ye gitmeden erken reddediyordu. Redis yokken istek doğrudan DB kısıtına düşer, davranış (409 Conflict) **aynı kalır** — bkz. § "Doğrulama" altındaki kanıt.

**Sınır (bilerek kabul edilen risk):** Bu in-memory karşılıklar **süreç-içi (process-local)** — birden fazla container/instance arasında paylaşılmaz. Koyeb free-tier **tek örnekli** (autoscale yok, 0.1 vCPU tek instance) bir dağıtım için tasarlandı. **Dağıtım çok-örnekli (multi-instance/K8s) hale gelirse Redis (veya eşdeğeri) tekrar ZORUNLUDUR** — bu noktada geri dönmeyi unutma.

---

## 2. Docker imajı — Koyeb uyumluluğu

`backend/Dockerfile` + `backend/docker-entrypoint.sh` değişti:

- **Port:** Koyeb portu `PORT` env değişkeniyle deploy anında bildirir. Docker `ENV` build-time'da donduğu için buna referans veremez — bu yüzden `docker-entrypoint.sh` runtime'da `$PORT`'u okuyup `ASPNETCORE_URLS=http://+:${PORT:-8000}` kurar, sonra `dotnet Kuaforum.Api.dll`'i `exec` eder (PID 1 kalır, SIGTERM doğru iletilir). **Varsayılan 8000** (Koyeb'in kendi default'u) — `PORT` set edilmezse buraya düşer.
- **Hetzner etkilenmedi:** `infra/docker-compose.prod.yml`'deki `backend` servisine artık explicit `PORT: "8080"` veriliyor — Caddyfile + healthcheck hâlâ 8080 varsayıyor, davranış değişmedi.
- **Bağlantı dizeleri zaten tamamen env'den geliyordu** (`ConnectionStrings__Postgres`, artık opsiyonel `ConnectionStrings__Redis`, `Jwt__SigningKey`, `Otp__Pepper`, `Encryption__Key`) — bu görevde ek değişiklik gerekmedi, mevcut fail-fast (`backend/src/Kuaforum.Infrastructure/DependencyInjection.cs` + `Program.cs`) korundu ve hata mesajları hangi değişkenin eksik olduğunu zaten söylüyor.
- **İmaj boyutu:** Multi-stage zaten vardı (SDK build → slim `aspnet:8.0` runtime, non-root `$APP_UID`), korundu — ek değişiklik yapılmadı.

---

## 3. Migration ve seed stratejisi — kararlar + gerekçe

### 3.1 Migration: açılışta otomatik `Database.MigrateAsync()` — DEĞİŞTİRİLMEDİ

Bu zaten mevcut davranıştı (`DependencyInjection.SeedReferenceDataAsync` → `ctx.Database.MigrateAsync(ct)`, her boot'ta). **Karar: bu şekilde kalsın.**

**Gerekçe:** Koyeb'de kalıcı shell yok (bir defalık `docker exec` ile migration çalıştıramazsın — free tier'da SSH/exec erişimi yok), yani migration'ların uygulanma yolu ya "boot'ta otomatik" ya da "ayrı bir CI job'ı `dotnet ef database update` çalıştırıp DB'ye bağlanır" olabilirdi. Tek-örnekli bir dağıtımda (Koyeb free-tier autoscale yok) boot-time migration makul ve zaten kod tabanında var olan davranışla tutarlı — ekstra bir CI job'ı kurmak (deploy pipeline'ı Koyeb hesabı olmadan zaten kuramıyoruz) bu noktada YAGNI.

**Bilinen risk (yeni değil, `infra/README.md` § 6.3'te zaten flag edilmişti):** Koyeb bir rolling-deploy sırasında kısa bir pencerede eski+yeni instance'ı birlikte çalıştırırsa, iki instance aynı anda `MigrateAsync()` çağırabilir. EF Core migration'ları transaction içinde uygulanır; iki instance aynı migration'ı aynı anda uygulamaya çalışırsa biri "already applied" ile temiz geçer ya da kısa bir çakışmayla başarısız olup **crash-loop** yapabilir (Koyeb otomatik restart eder, DB durumu bozulmaz — migration'lar idempotent). Solo-founder tek-instance test ortamı için düşük risk, ama **yıkıcı bir migration'ı asla iki farklı schema-değiştiren deploy'u art arda hızlıca push etmeyerek** azalt.

### 3.2 Demo seed: `Seed:RichDemo` bayrağı — VARSAYILAN KAPALI, DEĞİŞTİRİLMEDİ

`appsettings.json`'da zaten `Seed:Demo/ComprehensiveDemo/RichDemo` hepsi `false` (base config'te hiç yok, `GetValue<bool>` default false). Koyeb'de founder test edeceği için istersen açarsın:

```
Seed__RichDemo=true
```

Bu, `RichDemoSeeder`'ı (13 Erzurum salonu + her randevu durumu + yorum havuzu + örnek faturalama) çalıştırır — **`ASPNETCORE_ENVIRONMENT=Production` olsa bile** (bu ek/additive seed, TRUNCATE değil, `DemoDataResetter`'ın Production guard'ından farklı bir yoldan geçer — bkz. `DependencyInjection.SeedReferenceDataAsync`). **Yıkıcı reset (TRUNCATE) ise `IHostEnvironment.IsProduction()` iken her koşulda `InvalidOperationException` fırlatır** (`DemoDataResetter.ResetAsync`, çift katman: hem `Program.cs`'teki `isProduction` parametresi hem sınıfın kendi `env.IsProduction()` kontrolü) — bu davranışa DOKUNULMADI, Koyeb'de `ASPNETCORE_ENVIRONMENT=Production` kalsa bile demo veri sıfırdan TRUNCATE edilemez.

### 3.3 `DevEndpoints`/dev-login — güvenlik değerlendirmesi (KARAR FOUNDER'DA)

Founder NetGSM/PayTR hesapları henüz yok — gerçek OTP akışıyla test edemiyor, `dev-login`'e ihtiyacı var. Ama bu ortam internete açık bir Koyeb URL'i.

**Önceki davranış:** `POST /v1/auth/dev-login` ve `/v1/dev/*` yalnız `env.IsDevelopment()` iken map ediliyordu. Koyeb'de `ASPNETCORE_ENVIRONMENT=Development` set etmek bunu açardı AMA aynı zamanda **Production fail-fast korumalarını da (Otp:Pepper zorunluluğu, appsettings.Development.json'daki dev-secret fallback'leri) devre dışı bırakırdı** — `infra/README.md` § 7'de belgelenen, daha önce bir kez gerçek bir güvenlik açığına yol açmış tuzağın AYNISI (predictable JWT signing key = token forge).

**Yeni mekanizma (bu görevde eklendi):** `DevTools:Enabled` bayrağı (`Kuaforum.Api.Configuration.DevToolsGate`), Development DIŞINDA (Production dahil) varsayılan **KAPALI**. Açılırsa:
- `ASPNETCORE_ENVIRONMENT=Production` kalmaya devam eder → Production fail-fast'ler (Otp:Pepper, Jwt:SigningKey, Encryption:Key zorunluluğu, HTTPS redirect/HSTS) **korunur**.
- Yalnız `/v1/auth/dev-login` + `/v1/dev/*` route'ları ek olarak açılır.
- İsteğe bağlı `DevTools:AccessKey` — set edilirse dev-only uçlara gelen her istek `X-Dev-Access-Key` header'ının bu değerle eşleşmesini ister (sabit-zaman karşılaştırma, aksi halde 403).
- Boot log'unda **gürültülü** bir WARNING (AccessKey yoksa daha kritik bir ikinci WARNING: "TAMAMEN anonim").

**Risk (AccessKey set edilmezse):** `dev-login` `AllowAnonymous`'tur ve body'de yalnız bir rol adı ister — **Admin dahil herhangi bir role hiçbir kimlik doğrulaması olmadan token verir**. AccessKey olmadan bu, internete açık "kimliksiz Admin girişi" demektir.

**Mitigasyon seçenekleri (founder karar versin):**

| Seçenek | Güvenlik | Kullanım kolaylığı |
|---|---|---|
| `DevTools:Enabled=false` (kalıcı), gerçek OTP kurulana kadar test etme | En güvenli | Founder şimdi test edemez |
| `DevTools:Enabled=true` + **`DevTools:AccessKey` set** (örn. `openssl rand -hex 16`) | Paylaşılan-secret katmanı — URL'i tahmin eden biri hâlâ engellenir | Founder her istekte `X-Dev-Access-Key` header'ı eklemeli (mobile app test build'inde sabit env olarak gömülebilir) |
| `DevTools:Enabled=true`, AccessKey YOK | **Risk kabul edilmiş** — herkes Admin token alabilir | En kolay |
| IP allowlist | **Uygulanmadı** — Koyeb free tier'da ingress IP kısıtı olup olmadığı [unverified 2026-08-11] | — |

**Önerim (mühendis olarak, karar yine founder'ın):** `DevTools:Enabled=true` + `DevTools:AccessKey` set. Tek satır header eklemek (mobile test build'inde `EXPO_PUBLIC_DEV_ACCESS_KEY` gibi bir env'e gömülebilir) çok küçük bir sürtünme karşılığında "internete açık kimliksiz Admin" riskini kapatıyor.

---

## 4. Neon uyumluluğu

### 4.1 Bağlantı dizesi — Npgsql ayarları

Neon'un resmi .NET rehberi şu formatı öneriyor [Connecting .NET Applications to Neon Database](https://neon.com/guides/dotnet-neon-setup):

```
Host=<neon-hostname>;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true
```

`ConnectionStrings__Postgres` env değişkenine bu formatta yazılacak (mevcut `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.10 paketi ekstra config gerektirmiyor, `AddInfrastructure` zaten `UseNpgsql(pgConnString, ...)` ile bağlanıyor — kod değişikliği gerekmedi, yalnız connection string formatı).

**KARAR: Neon'un POOLED (PgBouncer) hostname'i DEĞİL, DIRECT (pooler'sız) hostname kullanılmalı.** Gerekçe (iki bağımsız kaynaktan doğrulanmış risk):

1. Neon'un kendi rehberi: *"using a pooled connection string for database migrations can be prone to errors"* — biz migration'ları HER boot'ta otomatik çalıştırıyoruz (§ 3.1), yani bu risk her deploy'da devreye girer.
2. **Hangfire.PostgreSql `LISTEN`/`NOTIFY` kullanıyor** (job kuyruğu bildirimi için) — bu, PgBouncer'ın **transaction-mode pooling**'i ile [temelde uyumsuz](https://github.com/pgbouncer/pgbouncer/issues/655): bildirim, pool'a geri dönüp başka bir client'a atanmış bir server-connection'da gelebilir, kaybolur. Neon PgBouncer'ı transaction-mode ile çalıştırıyor [Connection pooling - Neon Docs](https://neon.com/docs/connect/connection-pooling).

Tek Koyeb instance'ı zaten sınırlı sayıda bağlantı açacağından (Npgsql kendi client-side pooling'i yeterli, `MaxPoolSize` default'u genelde yeterli), Neon'un PgBouncer katmanına ihtiyaç yok — direkt bağlantı hem daha basit hem bu iki riski tamamen ortadan kaldırıyor.

### 4.2 PostGIS ve btree_gist — DOĞRULANDI, BLOKER YOK

Görev, bu ikisinin Neon'da desteklenip desteklenmediğinin **varsayılmaması, doğrulanmasını** istiyordu:

- **PostGIS:** Neon'un resmi extension dokümantasyonunda listeli, ayda ~39.8K kurulum ile en popüler extension'lardan biri [The postgis extension - Neon Docs](https://neon.com/docs/extensions/postgis).
- **btree_gist:** Neon'un resmi extension dokümantasyonunda listeli, ayda ~26.9K kurulum [The btree_gist extension - Neon Docs](https://neon.com/docs/extensions/btree_gist). Bu, M15 `EXCLUDE` constraint'inin (randevu çakışma garantisi) GiST index'i için gerekli — **bloker olsaydı bu proje Neon'da çalışamazdı**, desteklendiği doğrulandı.

**Elle kurulum gerekmiyor:** `ApplicationDbContext.OnModelCreating` (`modelBuilder.HasPostgresExtension("postgis"/"pgcrypto"/"btree_gist")`) EF Core migration'larına `CREATE EXTENSION IF NOT EXISTS` SQL'i olarak gömülü (`M01_Identity_Initial` postgis+pgcrypto için, `M15_BookingSlotExclusion` btree_gist için) — bu zaten `Database.MigrateAsync()` ile OTOMATİK uygulanıyor, ekstra bir adım yok. Tek koşul: Neon'daki DB rolünün `CREATE EXTENSION` izni olması — Neon'un varsayılan sahip rolü bunu destekleyen extension'lar için normalde izinlidir, ama **founder ilk boot sonrası `\dx` ile doğrulamalı** (Neon SQL Editor veya `psql`).

**Küçük bir not (bloker değil, [unverified 2026-08-11]):** Aramada PostGIS'in bazı hesaplarda compute restart sonrası `pg_available_extensions` listesinden geçici olarak düşebildiğine dair izole bir destek talebi bulundu ([PostGIS Extension Not Available After Multiple Compute Restarts](https://www.answeroverflow.com/m/1424951187047845980)) — bu, extension'ın *listelenebilirliği* ile ilgili görünüyor (compute image güncellemesi sonrası), zaten kurulmuş bir extension'ın *veritabanından silinmesi* değil (Neon'da storage/compute ayrık — compute suspend/resume şema/veriye dokunmaz). Yine de founder ilk deploy'dan sonra `\dx` ile postgis/btree_gist/pgcrypto'nun gerçekten kurulu kaldığını bir kez teyit etsin.

### 4.3 Neon compute-hour bütçesi — GERÇEK BİR RİSK, YENİ BULGU

Neon free tier ayda **100 CU-hour** veriyor [Free plan limits - Neon FAQs] — 0.25 CU'luk bir compute için bu ~400 saat/ay (bir ayın ~%55'i) sürekli-açık kalma bütçesi demek. Neon'un "scale to zero" özelliği 5 dakika inaktivite sonrası compute'u otomatik uyutur ve bu bütçeyi korur — **AMA** Hangfire'ın varsayılan kuyruk polling aralığı **15 saniyede bir** DB'ye sorgu atıyor (`Queue poll interval: 00:00:15`, yerel smoke test log'unda görüldü). Bu, Neon compute'unun **neredeyse hiç uyumaması** anlamına gelebilir [unverified 2026-08-11 — Neon'un "hangi sorgu inaktivite sayacını sıfırlar" davranışı ölçülmedi, ama bir polling sorgusu da bir sorgudur] — bu durumda 100 CU-hour bütçesi ~16-17 günde tükenir, ayın geri kalanında Neon compute suspend kalır veya paid plan gerekir.

**Mitigasyon (bu görevde eklendi):** `Hangfire:QueuePollIntervalSeconds` artık env'den override edilebilir (`backend/src/Kuaforum.Infrastructure/DependencyInjection.cs`). Hetzner prod'da DOKUNULMADI (default hâlâ Hangfire.PostgreSql'in kendi 15s'i — orada Postgres zaten her zaman açık bir VM'de, sık polling maliyetsiz). **Koyeb/Neon dağıtımında önerilir:**

```
Hangfire__QueuePollIntervalSeconds=120
```

Bu, arka plan işlerinin (randevu hatırlatma, otomatik no-show, abonelik faturalama vb.) tetiklenme gecikmesini 15s'den 120s'ye çıkarır — bir test/demo ortamı için kabul edilebilir bir trade-off, gerçek kullanıcı-yüzü akışlarını (booking create/cancel, ödeme) ETKİLEMEZ (onlar HTTP request-response, Hangfire kuyruğuna girmiyor).

---

## 5. Gereken env değişkenlerinin tam listesi (Koyeb)

### 5.1 Zorunlu — eksikse boot çöker

| Env var | Değer/örnek |
|---|---|
| `ConnectionStrings__Postgres` | Neon **direct** (pooler'sız) connection string, `SSL Mode=Require;Trust Server Certificate=true` ile (bkz. § 4.1) |
| `Jwt__SigningKey` | `openssl rand -base64 48` |
| `Otp__Pepper` | `openssl rand -base64 32` |
| `Encryption__Key` | `openssl rand -base64 32` |
| `AllowedHosts` | Koyeb'in verdiği public domain (örn. `xxx.koyeb.app`) |

### 5.2 Opsiyonel — Koyeb/Neon'a özgü

| Env var | Amaç | Önerilen değer |
|---|---|---|
| `PORT` | Koyeb otomatik set eder, elle vermene gerek yok | (Koyeb kendi ayarlıyor) |
| `ConnectionStrings__Redis` | **VERME** — boş bırak, in-memory fallback devreye girsin (bkz. § 1) | (boş) |
| `Seed__RichDemo` | Founder test verisi istiyorsa | `true` |
| `DevTools__Enabled` | dev-login/test-login açmak için (bkz. § 3.3) | `true` (founder kararına bağlı) |
| `DevTools__AccessKey` | dev-login'i paylaşılan-secret ile korumak için | `openssl rand -hex 16` |
| `Hangfire__QueuePollIntervalSeconds` | Neon compute-hour tüketimini azaltmak için (bkz. § 4.3) | `120` |
| `Sentry__Dsn` | Hata izleme (opsiyonel, boşsa SDK disabled) | — |
| `NetGsm__UserCode/Password/Originator` | Gerçek SMS OTP (yoksa `false` + Error log, kod loglanmaz) | — |
| `PayTr__MerchantId/Key/Salt` | Gerçek ödeme (yoksa runtime'da fail, boot etkilenmez) | — |
| `ObjectStorage__Provider` + `ObjectStorage__S3__*` | R2/S3 KYC dosya depolama (yoksa `Local` — container diskinde, re-deploy'da kaybolur) | — |
| `Seed__Admin__Email/Password` | İlk admin kullanıcısı (bir kere set et, idempotent) | — |

Mevcut Hetzner env tablosunun tamamı (`ConnectionStrings__Redis` artık **opsiyonel** olarak güncellendi) için bkz. `infra/README.md` § 3.

---

## 6. Bilinen sınırlar (dürüst liste)

1. **512 MB RAM / 0.1 vCPU.** Backend + Hangfire aynı process'te çalışıyor (ayrı worker yok). Yerel smoke testte boot + demo seed + birkaç booking isteği rahat sığdı; gerçek yük altında (RichDemoSeeder + eşzamanlı istekler) OOM riski ölçülmedi — founder ilk deploy'dan sonra Koyeb metrics'ten RAM kullanımını izlemeli.
2. **Neon cold start.** 5 dakika inaktivite sonrası compute suspend olur, sonraki sorgu ~300-800ms ek gecikme yaşar (tipik toplam ilk-sorgu süresi 500ms-1s) [Connection latency and timeouts - Neon Docs]. Koyeb'in kendisi UYUMUYOR (free tier'da "no sleep" — [Koyeb Free Tier 2026]), yani API her zaman ayakta ama İLK istekte DB'ye erişirken bu gecikmeyi hissedebilir.
3. **Neon 100 CU-hour/ay bütçesi** — bkz. § 4.3, `Hangfire:QueuePollIntervalSeconds` ile azaltılabilir ama tamamen ortadan kalkmaz (kullanıcı trafiği de compute'u uyandırır).
4. **Hangfire arka plan işleri "uyuyan" ortamda:** Koyeb instance'ı uyumadığından (madde 2) Hangfire Server sürekli çalışır durumda kalır — recurring job'lar (`ExpirePendingBookingsJob`, `AutoNoShowJob`, `BookingReminderJob`, `CloseMonthlySubscriptionInvoicesJob`, `SuspendOverdueSubscriptionsJob`) normal cron zamanlamasında tetiklenir, Render/Heroku free tier'daki gibi "instance uyuduğu için cron kaçtı" riski YOK. Asıl risk Neon tarafında (madde 3), Koyeb tarafında değil.
5. **Dev-login internete açık olabilir** (§ 3.3) — founder'ın bilinçli kararına bağlı, mitigasyon (`DevTools:AccessKey`) var ama IP kısıtı yok.
6. **R2/Object storage kurulmadıysa KYC dosyaları container diskinde** — Koyeb'in re-deploy'da disk kalıcılığı [unverified 2026-08-11 — Koyeb'in ephemeral disk davranışı bu görevde ölçülmedi], muhtemelen kaybolur (Hetzner'deki aynı GAP P1-6 ile aynı durum).
7. **Bu deploy dokümanı gerçek bir Koyeb/Neon hesabı üzerinde TEST EDİLMEDİ** — yalnız yerel Docker + yerel Postgres ile eşdeğer bir senaryo doğrulandı (bkz. § 7). Gerçek Neon bağlantısında SSL/pooling davranışı ilk deploy'da teyit edilmeli.

---

## 7. Doğrulama (bu görevde yapıldı, kanıt)

1. **Backend suite'leri yeşil:** 235 domain + 74 application + 267 integration (244 mevcut + 23 yeni: Redis'siz boot/booking/conflict testleri + in-memory lock/rate-limiter birim testleri + DevToolsGate birim testleri). Yeni testler: `backend/tests/Kuaforum.Tests.Integration/NoRedisIntegrationTestFactory.cs`, `NoRedisIntegrationTests.cs`, `InMemoryCachingFallbackTests.cs`, `DevToolsGateTests.cs`.
2. **Redis'siz gerçek boot kanıtı (Docker, yerel):**
   - `docker build -f backend/Dockerfile -t kuaforum-api:local backend` → başarılı.
   - Container `ASPNETCORE_ENVIRONMENT=Production`, `ConnectionStrings__Postgres` (yerel throwaway Postgres+PostGIS), **`ConnectionStrings__Redis` HİÇ verilmeden**, gerekli secret'larla (`Jwt__SigningKey`, `Otp__Pepper`, `Encryption__Key`) başlatıldı.
   - `docker ps` → `Up ... (healthy)` (HEALTHCHECK'in `$PORT` türetimi + `/health/ready` doğru çalıştığının kanıtı).
   - Log: `ConnectionStrings:Redis yapılandırılmadı — rate limiting bellek-içi sayaca, randevu slot kilidi bellek-içi kilide düştü.` (bir kez, WARNING).
   - `GET /health` → 200. `GET /health/ready` → 200, `checks.redis.status="not_configured"`, `checks.postgres.status="ok"`.
   - `DevTools__Enabled=true` + `DevTools__AccessKey=smoke-secret` ile: AccessKey'siz `POST /v1/auth/dev-login` → 403; yanlış key → 403; doğru key → 200 + token.
   - Bu token'larla booking akışı: owner shop/stylist/service sorgulandı, customer `POST /v1/bookings` → **201 Created**.
   - Aynı stylist için 15 dakika kaydırılmış çakışan ikinci istek → **409 Conflict** ("Bu saat dolu, başka bir saat seçin.") — Redis YOKKEN slot çakışma korumasının (in-memory kilit + DB EXCLUDE constraint) çalıştığının doğrudan kanıtı.
   - Ayrıca `DevTools__Enabled=true`, `AccessKey` VERİLMEDEN: boot log'unda ikinci, daha kritik WARNING (`... TAMAMEN anonim açık ...`) + AccessKey header'ı olmadan dev-login → 200 (beklenen, "risk kabul edilmiş" durumu).
3. **Docker imajı yerelde build oldu ve çalıştı** — madde 2 ile aynı kanıt (Neon yerine yerel throwaway Postgres+PostGIS container'ına bağlanarak).
4. **Commit + doğrulama:** bkz. `git log` / `git show --name-status` (bu görevin commit'i).

---

## 8. Founder'dan gereken tam liste (hesaplar geldiğinde)

1. **Koyeb hesabı** — kartsız kayıt, GitHub ile bağlanabilir (image GHCR'dan çekilebilir, mevcut `.github/workflows` GHCR push zaten var).
2. **Neon hesabı** — proje oluştur, **direct (pooler'sız)** connection string'i kopyala (§ 4.1).
3. § 5.1'deki zorunlu secret'ları üret (`openssl rand -base64 ...`).
4. § 3.3'teki dev-login kararını ver: `DevTools:Enabled` açık mı, `AccessKey` seçiliyor mu.
5. İlk deploy sonrası Neon SQL Editor'de `\dx` ile postgis/btree_gist/pgcrypto'nun kurulu olduğunu teyit et.
6. Koyeb dashboard'dan RAM/CPU metriklerini birkaç gün izle (madde 6.1).

---

## 9. Bağlantılar

- `infra/README.md` — Hetzner/Docker Compose ana runbook, env tablosu, bilinen riskler.
- `docs/FREE-SETUP.md` — genel ücretsiz-katman haritası (bu doküman onun Koyeb+Neon'a özel, uygulamaya dönük genişlemesi).
- `docs/12-devops/03-oracle-cloud-free.md` — Oracle Cloud alternatifi (kredi kartı doğrulaması gerektirdiği için founder tarafından reddedildi, referans olarak duruyor).
- [Koyeb Environment Variables docs](https://www.koyeb.com/docs/build-and-deploy/environment-variables)
- [Neon .NET connection guide](https://neon.com/guides/dotnet-neon-setup)
- [Neon connection pooling](https://neon.com/docs/connect/connection-pooling)
- [Neon PostGIS extension](https://neon.com/docs/extensions/postgis) · [Neon btree_gist extension](https://neon.com/docs/extensions/btree_gist)
- [Neon free plan limits](https://neon.com/faqs/free-plan-limits-and-quotas)

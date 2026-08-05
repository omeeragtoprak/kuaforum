# Ücretsiz Tier Hosting Araştırması — Pilot Ölçeği (Erzurum)

**Araştırma tarihi:** 2026-08-05
**Kapsam:** Founder kararı — "mevcut stack + ücretsiz tier" (Firebase/BaaS reddedildi, .NET 8 + Postgres+PostGIS + Redis mimarisi korunuyor). Hedef: pilot ölçeğinde (Erzurum, düşük trafik) ~0 TL/ay, production-benzeri, staging olarak da işlev gören bir dağıtım. Hetzner (Helsinki) daha sonra devreye girecek; bu doküman yalnızca **köprü dönemi** için.
**Verdict (TL;DR):** **Kombinasyon A — Oracle Cloud Always Free ARM VM üzerinde `infra/docker-compose.prod.yml`'in (neredeyse) değişmeden çalıştırılması.** Sebep: Hangfire'ın recurring job mimarisi sürekli-açık bir süreç ve sürekli-açık bir DB bağlantısı gerektiriyor; bu, "serverless Postgres + scale-to-zero container" kombinasyonlarının (Neon, Koyeb, Cloud Run) **hepsini** pilot senaryosunda eler — cimrilik değil, mimari uyumsuzluk. Detay ve risk kabulü aşağıda.

---

## 1. Bağlam: Neyi Elemek Zorundayız

Stack'in üç kısıtı, "ücretsiz tier" seçimini standart "hangi PaaS ucuz" sorusundan farklılaştırıyor:

1. **PostGIS + `btree_gist` EXCLUDE constraint (M15):** Standart bir "Postgres uyumlu" servis yetmez; extension listesi ve exclusion constraint desteği doğrulanmalı.
2. **Hangfire recurring job'lar** (`ExpirePendingBookingsJob`, `ProcessBookingRefundJob` — RFC-006/booking slot güvenliği bunlara bağlı): Hangfire'ın kendi dokümantasyonu açık — *"Your Hangfire Server instance should be always on"* [Hangfire Docs — Recurrent Tasks](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html). Recurring job scheduler dakikada bir DB'yi kontrol eder, storage polling (`QueuePollInterval`, SQL/Postgres storage'da varsayılan saniyeler mertebesinde) sürekli açık bir bağlantı ister. **Bu, "scale-to-zero" felsefesiyle taban tabana zıt** — aşağıda §4'te sayısallaştırılıyor.
3. **Redis** rate-limit (sliding window) + distributed lock için gerçek zamanlı, düşük gecikmeli erişim ister; bağlantı sayısı değil ama TLS + komut hacmi önemli.

**Why:** Bu üç kısıt birlikte değerlendirilmezse "her biri ayrı ayrı ücretsiz" bir kombinasyon seçilip production'da (veya pilotta) Hangfire job'larının sessizce çalışmadığı keşfedilir — ki bu tam olarak booking slot güvenliği ve otomatik refund'un dayandığı mekanizma (CLAUDE.md Cycle 9: "Slot güvenliği — ExpirePendingBookingsJob").
**How to apply:** Her aday için önce "always-on mu, scale-to-zero mu" sorusunu sor; scale-to-zero olan hiçbir compute adayı Hangfire'ı barındıramaz.

---

## 2. Postgres + PostGIS Ücretsiz Seçenekler

| Sağlayıcı | Free tier | PostGIS | `btree_gist` | Scale-to-zero / pause davranışı | Not |
|---|---|---|---|---|---|
| **Neon** | 0.5 GB depolama, **100 compute-saat/ay**, 1 proje (max 2 CU) | ✅ tam destek [Neon Docs — postgis](https://neon.com/docs/extensions/postgis) | ✅ destekleniyor, exclusion constraint örneği dokümante [Neon Docs — btree_gist](https://neon.com/docs/extensions/btree_gist) | **Compute-metered scale-to-zero**, ~5 dk idle sonrası suspend; cold start ~500ms–1s [srvrlss.io Neon Review](https://www.srvrlss.io/provider/neon/) | Compute-saat aşılınca compute suspend olur, sonraki faturalama dönemine kadar bağlanılamaz [Neon FAQ — free plan limits](https://neon.com/faqs/free-plan-limits-and-quotas) |
| **Supabase** | 500 MB DB, 1 GB dosya, 5 GB egress, 2 proje | ✅ dashboard'dan tek tıkla, matür destek | ✅ pre-installed, dashboard'dan aktif edilir [Supabase Docs — Extensions](https://supabase.com/docs/guides/database/extensions) | **Sabit küçük instance** (compute-metered değil) — yalnız **7 gün tam işlemsizlikte** proje "pause" olur [Supabase Docs — Free Project Pausing](https://supabase.com/docs/guides/platform/free-project-pausing) | Otomatik yedek yok, e-posta desteği yok (free tier) |
| **Aiven for PostgreSQL** | 1 GB depolama (2025'te 5→1 GB düşürüldü), 1 GB RAM, 1 CPU, süre sınırı yok | [unverified 2026-08-05 — extension listesi free tier'da doğrulanamadı, Aiven genelde PostGIS'i destekliyor ama free plan'da explicit teyit gerekiyor] | [unverified] | Süresiz, ama kaynak çok kısıtlı | [Aiven Changelog — free plan storage](https://aiven.io/changelog/6ad6c429-6c1c-4418-abfb-2ca82f927414) |
| **Xata** | 15 GB depolama, cold-start yok, pause yok | Extension listesine PostGIS eklenmiş (popülerliğe göre kademeli açılıyor) [Xata Blog](https://xata.io/blog/changes-free-tier) | [unverified — free tier'da spesifik teyit yok] | Yok (her zaman aktif) | 2025'te search/files API'leri free tier'dan çıkarıldı — sade Postgres kullanımı etkilenmiyor |
| **Prisma Postgres** | 100k operasyon, 500 MB, 50 DB | [unverified — dokümantasyonda PostGIS'e özel referans bulunamadı, yalnızca pgvector doğrulandı] | [unverified] | Muhtemelen serverless/idle-suspend (mimari benzer) | Kredi kartı gerekmiyor [Prisma Postgres](https://www.prisma.io/postgres) |
| **Render Postgres** | 1 GB, **30 gün sonra expire** | — | — | Expire sonrası 14 gün grace, sonra silinir | Kalıcı kullanım için uygun değil, eleniyor [Render Changelog](https://render.com/changelog/free-postgresql-instances-now-expire-after-30-days-previously-90) |

### Kritik bulgu: Hangfire + Neon uyumsuzluğu (sayısal)

Neon'un free tier'ı **100 compute-saat/ay** veriyor. Bir ay ortalama 720 saat. Hangfire sunucusu (recurring job scheduler + queue polling) DB bağlantısını sürekli canlı tuttuğu için Neon'un 5 dakikalık idle-suspend eşiğine hiç girmez — yani compute **hep açık** kalır. 100 saat / 720 saat ≈ ay içinde **~4-5 gün** içinde compute-saat kotası tükenir, sonrasında `Payment gerekli / bağlanılamıyor` hatası [Neon FAQ — free plan limits](https://neon.com/faqs/free-plan-limits-and-quotas). Bu, Neon'un hedef kullanım profilinin (spiky/idle serverless workload) bizim workload'umuzun (sürekli bağlı monolitik API + Hangfire) **tam tersi** olmasından kaynaklanıyor — "en popüler serverless Postgres" olması bizim için bir artı değil, bir elemedir.

**Buna karşılık Supabase compute-metered değil** — sabit küçük bir instance, yalnızca *tam işlemsizlikte* 7 gün sonra pause oluyor. Hangfire'ın sürekli trafiği bu pause'u zaten tetiklemez; yani paradoksal biçimde **Supabase, Neon'dan daha uyumlu** — ama depolama (500 MB) ve "otomatik yedek yok" kısıtları pilot ölçeği için yeterli olsa da RFC-009 sonrası büyüme senaryosunda erken aşılır.

**Sonuç:** Eğer Postgres ayrı bir yönetilen serviste barındırılacaksa (Kombinasyon B), **Supabase, Neon'a tercih edilmeli** — ama bu senaryonun tamamı §5'te ayrı bir nedenle zaten eleniyor.

---

## 3. Redis Ücretsiz Seçenekler

| Sağlayıcı | Free tier | TLS | Not |
|---|---|---|---|
| **Upstash** | 256 MB, **500.000 komut/ay**, 10 GB bant genişliği, tek bölge | ✅ varsayılan açık, free tier dahil [Upstash Docs — Pricing](https://upstash.com/docs/redis/overall/pricing) | StackExchange.Redis ile REST/TCP uyumlu; TLS sorunsuz |

**Kabaca hesap:** 500k komut/ay ≈ günde ~16.600 komut ≈ dakikada ~11.5 komut. Rate-limit sliding-window (her istekte 2-3 Redis komutu: INCR/EXPIRE benzeri) + distributed lock (job başına birkaç komut) ile pilot ölçeğinde (Erzurum, düşük trafik, birkaç yüz kullanıcı/gün) **muhtemelen yeterli**, ama API trafiği arttıkça (her authenticated istekte rate-limit check varsa) ilk aşılan kaynak bu olabilir — izlenmeli. Alternatif arandığında Redis Cloud free (30 MB, çok daha küçük) veya self-hosted `redis:7-alpine` (Oracle VM'de zaten dahil, Kombinasyon A) tercih edilir.

**Why:** Upstash'in komut bazlı faturalama modeli "sürekli bağlı" bir workload'da (StackExchange.Redis connection multiplexer sürekli PING atar) hızlı tüketilebilir; bu da yine Kombinasyon A'daki self-hosted Redis'i (sınırsız komut, aynı Docker network) cazip kılıyor.
**How to apply:** Kombinasyon B seçilirse Upstash komut sayacı ilk haftada izlenmeli; StackExchange.Redis'in keep-alive/heartbeat ayarları gereksiz komut üretmeyecek şekilde ayarlanmalı (kod değişikliği — bu doküman kapsamı dışında, ayrı bir implementasyon notu).

---

## 4. Container / Compute Hosting Ücretsiz Seçenekler

| Sağlayıcı | Free tier durumu (2026-08) | Always-on mu? | Hangfire uyumlu mu? |
|---|---|---|---|
| **Fly.io** | **Kaldırıldı (Ekim 2024).** Yeni hesaplar yalnız $5 deneme kredisi / 7 gün alıyor, sonra kredi kartı zorunlu pay-as-you-go [ExpressTech — Fly.io free tier died](https://expresstech.io/7-fly-io-alternatives-in-2026-real-pricing-after-the-free-tier-died/) | Paid'de evet (~$1.94/ay minimal Machine) | Evet ama **ücretsiz değil** |
| **Render** | Free web service, **15 dk trafik yoksa spin-down**, sonraki istekte ~1 dk cold start [GitHub Discussion #197645](https://github.com/orgs/community/discussions/197645) | ❌ | **Hayır — öldürücü.** Recurring job scheduler'ın kendisi "trafik" sayılmaz, servis uyur, job hiç tetiklenmez |
| **Railway** | Kalıcı free tier yok; $5 tek seferlik deneme kredisi (30 gün), sonra min. $5/ay Hobby [Kuberns — Railway 2026](https://kuberns.com/blogs/railway-free-tier/) | Trial'da evet ama süreli | Süreli — pilot için yetersiz |
| **Koyeb** | 1 free web service (512 MB / 0.1 vCPU), **1 saat trafiksizlikte scale-to-zero, kapatılamaz**; 2026'da Mistral AI satın alımı sonrası free tier yeni kullanıcılara kapatıldı [Koyeb Community](https://community.koyeb.com/t/web-service-keeps-being-unreachable-on-free-tier/3545) | ❌ | **Hayır** |
| **Google Cloud Run** | 180.000 vCPU-sn + 360.000 GiB-sn + 2M istek/ay ücretsiz, **varsayılan scale-to-zero**; `min-instances=1` ile always-on yapılırsa idle instance CPU+RAM için **ücretli** (~$10-65/ay tahmini, konfigürasyona göre) [CloudGeeks — min-instances trap](https://insights.cloudgeeks.com.au/blog/cloud-run-min-instances-silent-budget-trap/) | Yalnız ücretliyken | **min-instances=0 ile hayır; =1 ile evet ama artık "ücretsiz" değil** |
| **Google Cloud Always Free — e2-micro VM** | 1 e2-micro (1 vCPU shared, 1 GB RAM), yalnız `us-west1/us-central1/us-east1`, 30 GB standart disk [aatayyab.wordpress.com](https://aatayyab.wordpress.com/2026/06/26/google-cloud-free-tier-services-and-limits/) | ✅ gerçek VM, uyumaz | Evet — ama 1 GB RAM tüm stack'e (Postgres+Redis+.NET API+web-admin) **yetmez**; yalnız backend+Redis için düşünülebilir, DB harici olmalı; ABD bölgesi → Türkiye'ye gecikme |
| **Oracle Cloud Always Free — Ampere A1 ARM** | **2026-06-15'ten itibaren 2 OCPU/12 GB'a düşürüldü** (önceden 4 OCPU/24 GB) [InfoQ — Oracle halves free tier](https://www.infoq.com/news/2026/07/oracle-cloud-free-tier-limits/), + 200 GB block storage, gerçek her-zaman-açık VM | ✅ | **Evet — tam docker compose stack'i taşır** |

**Why (Cloud Run özel not):** Cloud Run mimari olarak "istek geldiğinde çalış" modeline dayanıyor; Hangfire'ın arka planda kendi kendine tetiklenen recurring job'ları hiçbir zaman bir "istek" üretmediği için `min-instances=0` ile job'lar hiç çalışmaz. `min-instances=1` bunu çözer ama bu noktada "ücretsiz tier" vasfı kaybolur (aylık $10-65 aralığı, memory/CPU konfigürasyonuna göre) — yani Cloud Run, Render/Koyeb ile aynı kategoride elenir.

---

## 5. Öneri Matrisi

### Kombinasyon A — Oracle Cloud Always Free ARM VM, tek host, compose.prod'a en yakın hâliyle (NET ÖNERİ)

**Kurulum:** Oracle Cloud'da bir Ampere A1 (2 OCPU/12 GB, Ubuntu) VM aç → Docker + Docker Compose kur → `infra/docker-compose.prod.yml` neredeyse değişmeden çalıştır (postgis/postgis:16-3.4-alpine, redis:7-alpine, backend, web-admin, Caddy — hepsi zaten bu dosyada var) → Cloudflare DNS ile bir alt domain'i VM public IP'sine yönlendir → Caddy otomatik TLS alır.

- **Artı:** Tüm stack tek yerde, sıfır mimari değişiklik, Hangfire sorunsuz çalışır (gerçek her-zaman-açık VM), PostGIS+btree_gist zaten `postgis/postgis` image'ında var (extension sorgusu bile gerekmez — self-hosted), Redis sınırsız komut, `backup` servisi (pg_dump sidecar) zaten compose'da mevcut. **Hetzner'e geçiş = aynı compose dosyası + DNS değişikliği** — pratik olarak sıfır ek mühendislik.
- **Eksi / risk:**
  - **Kapasite riski:** "Out of host capacity" hatası popüler bölgelerde (özellikle Frankfurt) yaygın şikayet konusu [Oracle Community/HN raporları](https://community.amperecomputing.com/t/how-to-get-around-the-out-of-capacity-error-on-the-always-free-tier-of-oci/3432); ilk denemede VM açılamayabilir, birkaç gün/bölge denemesi gerekebilir.
  - **Kota riski:** 2026-06'da sessizce 4 OCPU/24GB → 2 OCPU/12GB düşürüldü, Oracle önceden duyurmadan [InfoQ](https://www.infoq.com/news/2026/07/oracle-cloud-free-tier-limits/) — gelecekte tekrar kısılabilir; **free tier garantisi yok, sözleşme değil**.
  - **Anekdotal hesap askıya alma/sonlandırma raporları** (Hacker News, Oracle forumları) — gerekçe belirtilmeden [Oracle Forums thread](https://forums.oracle.com/ords/apexds/post/always-free-tier-account-suspended-without-clear-reason-3380), [HN #36008957](https://news.ycombinator.com/item?id=36008957). `[unverified 2026-08-05 — anekdotal, sistemik olduğu doğrulanamadı ama tekil değil, ciddiye alınmalı]`. Resmi politika: 60 gün hesap işlemsizliği hesabı riske atar; Always Free kaynaklar "reclaim edilmez" deniyor ama pratikte istisnalar bildiriliyor.
  - Kredi kartı gerekiyor (kimlik doğrulama, ücretlendirme yok ama hold konabilir).
  - Tek nokta hata (SPOF) — ama zaten pilot/staging amaçlı, prod değil.
- **Kurulum eforu:** Düşük-orta (~1 gün; kapasite hatası çıkarsa +birkaç gün bölge/deneme).
- **Bütçe etkisi:** 0 TL/ay (domain hariç, bkz. §6).
- **Security delta:** compose.prod zaten non-root image + internal network izolasyonu + Caddy tek public giriş noktası sağlıyor — Hetzner planıyla birebir aynı güvenlik postürü.

### Kombinasyon B — Supabase (Postgres) + Upstash (Redis) + always-on compute ihtiyacı çözülemiyor

Bu kombinasyonun DB ve cache ayağı tek başına makul (§2-3), ama **compute ayağı çöküyor**: Koyeb/Render/Cloud Run'ın hiçbiri gerçek "her zaman açık, ücretsiz" bir container barındırma sunmuyor (§4). Backend'i barındıracak ücretsiz bir always-on container platformu **yok** — olsaydı bile Hangfire hâlâ orada çalışmak zorunda, ve o platform da kendi ücretsiz kotasıyla sınırlı olurdu. Tek çıkış yolu backend'i yine bir VM'e (Oracle/GCP e2-micro) koymak — ki bu noktada Postgres/Redis'i de aynı VM'de self-host etmek (Kombinasyon A) hem daha basit hem daha ucuz (ekstra ağ gecikmesi/egress yok, tek yerde yönetim).
- **Ne zaman anlamlı olur:** Hangfire job'ları geçici olarak devre dışı bırakılabiliyorsa (ör. yalnız API entegrasyon testi/demo ortamı, booking slot job'ları kritik değilse) — ama bu pilotun "production-benzeri" hedefiyle çelişir.
- **Sonuç:** **Elendi** — mimari kısıt (§1 madde 2) B'yi B yapan tam da "compute'u ayrı, ucuz bir yerde barındırma" fikrini imkansız kılıyor.

### Kombinasyon C — Lokal geliştirme devam, Hetzner'i bekle

- **Artı:** Sıfır ek risk, sıfır ek eforu, mevcut `docker-compose.dev.yml` zaten çalışıyor.
- **Eksi:** Founder'ın uzaktan/mobilden test edebileceği, gerçek bir public URL'i olan staging ortamı **yok** — demo, KYC belge upload testi, mobile-gerçek-cihaz testi gibi senaryolar lokal ağ dışına çıkamıyor. RFC-009 sonrası "sahada göster" ihtiyacını karşılamıyor.
- **Ne zaman tercih edilir:** Oracle kapasite denemesi 1 haftadan uzun sürerse veya founder'ın kısa vadede uzaktan demo ihtiyacı yoksa **geçici fallback** olarak.

### Karar tablosu

| Kriter | A (Oracle VM) | B (Supabase+Upstash+?) | C (Lokal + bekle) |
|---|---|---|---|
| Hangfire çalışır mı | ✅ | ❌ (compute ayağı yok) | ✅ (lokalde) |
| PostGIS+btree_gist | ✅ (self-host, garantili) | ✅ (Supabase) | ✅ (lokal) |
| Public URL / uzaktan erişim | ✅ | ✅ (DB/cache), ❌ (compute) | ❌ |
| Kurulum eforu | Düşük-orta | Orta (ve sonra yine A'ya döner) | Yok |
| Hetzner'e geçiş eforu | Neredeyse sıfır (aynı compose) | Orta (yeniden mimarı) | Sıfır (zaten yok) |
| Ana risk | Kapasite/kota/askıya alma (Oracle) | Mimari boşluk (SPOF çözümsüz) | Demo/test yapılamaz |

---

## 6. Founder Aksiyonları

1. **Oracle Cloud hesabı aç** (oracle.com/cloud/free) — kredi/banka kartı gerekiyor (kimlik doğrulama, otomatik ücretlendirme yok, always-free kaynaklarda). Bölge: Frankfurt (eu-frankfurt-1) önerilir — Hetzner'in planlı V2 ikincil bölgesiyle de örtüşüyor, Erzurum'a Helsinki'den de Frankfurt'tan da benzer gecikme.
2. **Kapasite denemesi:** İlk denemede "out of host capacity" hatası alınırsa panik yok — birkaç gün içinde farklı availability domain/saatte tekrar denenmeli. 1 haftayı aşarsa Kombinasyon C'ye geçici dönüş değerlendirilmeli.
3. **Domain:** Cloudflare zaten CDN/WAF için planda; **domain satın alımı ücretsiz değil** (~$10-15/yıl, .com veya .com.tr) — bu, "~0 TL/ay" hedefinin tek gerçek istisnası. Cloudflare Registrar (at-cost) veya mevcut bir registrar (Namecheap/Isimtescil) kullanılabilir; DNS ücretsiz Cloudflare'de kalır.
4. **Web-admin için Vercel free (Hobby) KULLANILMASIN:** Vercel Hobby planı sözleşmesel olarak **ticari/gelir getiren projeler için değil** [deploywise.dev — Vercel Hobby limits](https://deploywise.dev/blog/vercel-free-tier-limits-2026); Kuaforum bir B2C marketplace olduğu için bu tanıma girer `[unverified 2026-08-05 — ToS'un tam metni ve uygulanma sıkılığı hukuki teyit ister, ama risk almaya değmez]`. Zaten `infra/docker-compose.prod.yml` içinde web-admin'in kendi Dockerfile'ı var — aynı Oracle VM'de Caddy arkasında servis edilmesi hem bu riski ortadan kaldırıyor hem ekstra hesap/entegrasyon eforu istemiyor.
5. **Hesap listesi:** Oracle Cloud (VM), Cloudflare (zaten var — DNS/WAF/R2), GHCR (zaten var — image registry, `docs/12-devops` deploy akışında mevcut). Ek olarak Neon/Supabase/Upstash hesabı **açmaya gerek yok** (Kombinasyon A self-host).
6. **Sonraki adım (bu doküman kapsamı dışı, ayrı görev):** Oracle VM üzerinde `.env.prod` secret'larının doldurulması, GitHub Actions'ta `hetzner-staging` yerine/yanında `oracle-staging` SSH hedefinin eklenmesi — infra/CI dosyalarına dokunmadan önce founder onayı gerekir.

---

## 7. Bilinçli Dışarıda Bırakılanlar

Kubernetes (V2, MVP'de gereksiz) · Terraform/IaC otomasyonu (V2, tek VM için manuel kurulum yeterli) · Multi-region free tier kombinasyonları (pilot tek bölge) · Hangfire'ın kendisini serverless-uyumlu bir job runner ile değiştirmek (kod değişikliği, bu doküman yalnızca hosting araştırması — ayrı bir RFC konusu olabilir ama şu an YAGNI: Oracle VM zaten sorunu çözüyor) · Fly.io paid tier'ı ($1.94/ay minimal) — "ücretsiz" hedefiyle çelişiyor, değerlendirmeye alınmadı.

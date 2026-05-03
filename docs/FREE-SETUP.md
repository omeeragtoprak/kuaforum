# Kuaforum — Tamamen Ücretsiz Kurulum Rehberi

> **Hedef:** Geliştirme aşamasında **$0/ay**, lansman aşamasında **sadece transaction-bazlı maliyetler** (komisyon + SMS başına). Sabit aylık abonelik ücretleri **mümkün olduğunca sıfır**.

**Sembol:**
- 🆓 tamamen ücretsiz (kalıcı)
- 🟢 ücretsiz katman (limit dahilinde — pratikte yeterli)
- 💰 transaction-bazlı (sadece kullanım anında, abonelik yok)
- 🟡 düşük maliyet alternatifi var
- 🔴 ücretsiz alternatif yok / sadece "free tier" yetersiz

---

## 1. Geliştirme (Dev) — $0/ay Hedefi

| Kategori | Seçim | Tip | Notlar |
|---|---|---|---|
| **Backend runtime** | .NET 10 SDK | 🆓 | Microsoft ücretsiz |
| **Veritabanı** | PostgreSQL 16 + PostGIS (Docker) | 🆓 | Lokal Docker |
| **Cache/Queue** | Redis 7 (Docker) | 🆓 | Lokal Docker |
| **Search** | Meilisearch (Docker) | 🆓 | Lokal Docker |
| **Log** | Seq community (Docker) | 🆓 | Lokal Docker |
| **Mobile dev** | Expo + Expo Go | 🆓 | Telefonda ücretsiz |
| **Web admin** | Next.js dev server | 🆓 | Lokal |
| **Editor** | VS Code | 🆓 | |
| **Git** | GitHub free | 🟢 | Public veya private repo |
| **Container** | Docker Desktop | 🆓 | Bireysel kullanım ücretsiz |
| **Tools** | dotnet ef, pnpm, EAS CLI | 🆓 | |
| **Tüm 3rd party** | Mock implementasyonlar | 🆓 | PSP/SMS/email/maps mock |

**Sonuç:** Dev makinesinde tek bir abonelik gerekmez. **Aylık $0**.

---

## 2. Üretim (Production) — Minimum Maliyet Hedefi

### Mandatory paid (kaçınılamaz)

| Kalem | Maliyet | Alternatif |
|---|---|---|
| **Domain (.com)** | ~$10/yıl | Namecheap, Porkbun |
| **Apple Developer** (sadece iOS App Store için) | $99/yıl | **Expo Go ile dev'de bypass — App Store yayını isterseniz gerekir** |
| **Google Play Console** | $25 bir defalık | Tek seferlik, yıllık değil |

### Komisyon-bazlı (abonelik yok, gelir varsa öder)

| Servis | Model | Notlar |
|---|---|---|
| **PayTR Pazaryeri** | %0.99 - %4.00 dinamik MDR | 💰 İşlem başına. Aylık abonelik **YOK**. |
| **Iyzico Pazaryeri** | %4.29 + 0.25 TL standard (negotiable %2.49) | 💰 İşlem başına. Abonelik yok. |

> **Önemli:** PSP'ler "abonelik" değil "komisyon" alıyor. Yani **ilk müşteri gelene kadar $0**. Bu, ücretsiz kalmak isteyen yeni girişimci için ideal.

### Aylık ücretsiz veya çok düşük tier

| Servis | Free Tier | Sonra |
|---|---|---|
| **Hosting: Oracle Cloud Always Free** | 🟢 **Sınırsız** — 4 OCPU ARM + 24GB RAM, 200GB storage, 10TB egress/ay | "Always Free" — yıllık olarak ücretsiz kalmaya devam |
| **Hosting alternatif: Fly.io** | 🟢 ~3 küçük VM (256MB RAM each) | Üzerinde minimal aylık ücret |
| **Hosting alternatif: Hetzner Cloud** | $5-25/ay | En ucuz "managed" hosting |
| **CDN: Cloudflare** | 🆓 Free plan | DNS + CDN + WAF + DDoS — sınırsız trafik |
| **File storage: Cloudflare R2** | 🟢 10GB ücretsiz, $0.015/GB sonra | S3 uyumlu, **egress ücretsiz** |
| **DB: Postgres (Neon/Supabase free tier)** | 🟢 0.5GB-3GB ücretsiz | Production için ek katmanı |
| **DB alternatif: Self-host on Oracle Cloud Free** | 🆓 Sınırsız | Kendi VM'inde Postgres+PostGIS |
| **Cache: Redis (Upstash free tier)** | 🟢 10K commands/gün | Veya self-host |
| **Email: Resend free tier** | 🟢 3000 e-posta/ay (100/gün) | İletim için yeterli MVP'de |
| **Email alternatif: MailerSend free** | 🟢 3000 e-posta/ay | Resend yedek |
| **Push: Firebase Cloud Messaging** | 🆓 **Tamamen ücretsiz** (sınırsız) | Google ücretsiz tutuyor |
| **OAuth: Google Sign-In** | 🆓 Ücretsiz | Cloud Console projesi açık |
| **OAuth: Apple Sign-In** | $99/yıl Apple Dev gerektirir | iOS App Store yayını için |
| **Maps: OpenStreetMap** | 🆓 Tamamen ücretsiz, API key yok | Tile usage policy'sine uy |
| **Maps: Native deep link (Apple/Google Maps)** | 🆓 Sadece URL açar, API çağrısı yok | Yol tarifi için yeterli |
| **Geocoding: Nominatim (OSM)** | 🆓 1 req/sec rate limit | Düşük hacim için yeterli |
| **Geocoding: MapTiler free** | 🟢 100K req/ay | Yedek |
| **SMS OTP: WhatsApp Cloud API** | 🟢 Bazı ülkelerde 1000 free/ay | TR'de henüz tam destek değil |
| **SMS OTP: SMS yerine email OTP** | 🆓 Tamamen ücretsiz | UX biraz daha yavaş ama $0 |
| **SMS: NetGSM pay-as-you-go** | 💰 ~₺0.05/SMS | Alternatif yok TR-mobile için |
| **Error tracking: Sentry free** | 🟢 5K event/ay | Solo founder için yeterli |
| **Error tracking: GlitchTip self-host** | 🆓 Sentry-uyumlu, açık kaynak | Hetzner/Oracle Cloud üzerinde |
| **Uptime: Better Stack free** | 🟢 10 monitor/ay | |
| **Uptime: UptimeRobot free** | 🟢 50 monitor/5dk | |
| **Logs: Seq community** | 🆓 Self-host, sınırsız | Solo developer ücretsiz |
| **Logs: Better Stack free tier** | 🟢 1GB/ay | Yedek |
| **CI/CD: GitHub Actions** | 🟢 2000 dakika/ay private repo | Public repo'da sınırsız |
| **CI/CD: Cloudflare Pages** | 🆓 Sınırsız build | Web admin deploy için |
| **CI/CD: Vercel free** | 🟢 100GB bandwidth/ay | Next.js deploy alternatifi |
| **e-Arşiv (yasal)** | 💰 150-800 TL/ay (Nilvera, Logo, vs.) | E-arşiv eşiği aşılınca zorunlu (500K TL ciro) |
| **e-Arşiv kaçınma:** | — | Eşik altında kaldığın sürece şart değil |
| **VPS Backup: Restic + Cloudflare R2** | 🟢 10GB ücretsiz | Otomatize edilebilir |

### Lisans uyarısı (free yazılım kütüphaneleri)

| Library | Sürüm | Lisans |
|---|---|---|
| **MediatR** | 12.4.1 (pinned) | 🆓 MIT (12.5+ commercial) |
| **FluentValidation** | 11.11.0 (pinned) | 🆓 MIT (12+ commercial) |
| **FluentAssertions** | (kullanılmadı) | 🔴 8+ commercial — biz Shouldly veya xunit Assert kullanıyoruz |
| **Mapster** | 7.x | 🆓 MIT |
| **Hangfire** | 1.8.x | 🆓 LGPL (ticari kullanıma uygun) |
| **Hangfire.Pro** | (kullanılmadı) | 🔴 commercial |
| **Otp.NET** | 1.4.x | 🆓 MIT |
| **NetTopologySuite** | 2.6+ | 🆓 BSD |
| **EF Core** | 10.x | 🆓 Apache 2.0 |
| **Serilog** | 9.x | 🆓 Apache 2.0 |
| **xUnit** | 2.x | 🆓 Apache 2.0 |
| **NSubstitute** | 5.x | 🆓 BSD |
| **Testcontainers** | 4.x | 🆓 MIT |

> **Sonuç:** Tüm runtime kütüphanelerimiz ücretsiz / açık kaynak. **MediatR ve FluentValidation pinned versiyonlarını** sabit tutuyoruz — bir sonraki major sürümlerine atlayınca lisans değişebilir.

---

## 3. Maksimum-Free Production Topolojisi (Önerilen)

```
┌──────────────────────────────────────────────────────────────────┐
│              Oracle Cloud Always Free Tier                        │
│   (4 OCPU Ampere ARM + 24GB RAM + 200GB disk — $0/ay sonsuz)     │
│                                                                   │
│   Docker Compose:                                                 │
│   ┌─────────────────────────────────────┐                         │
│   │  api (.NET 10)         Postgres+PostGIS                       │
│   │  web-admin (Next.js)   Redis                                  │
│   │  meilisearch           Seq                                    │
│   └─────────────────────────────────────┘                         │
└──────────────────────────────────────────────────────────────────┘
                          │
                          ▼
                ┌─────────────────────┐
                │  Cloudflare Free     │
                │  DNS + CDN + WAF     │
                │  + Cloudflare R2     │
                │  (file storage 10GB) │
                └─────────────────────┘

Mobile build:
   Expo EAS Free tier (preview build) → Expo Go QR scan
   App Store yayını için: Apple Dev ($99/yıl, isteğe bağlı)
   Google Play yayını için: Play Console ($25 one-time)

3rd party (transaction-only):
   PayTR (komisyon-bazlı, abonelik yok)
   Iyzico (komisyon-bazlı, abonelik yok)
   FCM (sınırsız ücretsiz)
   Resend (3K e-posta/ay ücretsiz)
   NetGSM SMS (pay-as-you-go, ~₺0.05/SMS) — alternatif: email OTP
```

**Bu topoloji ile aylık sabit gider: $10-15** (sadece domain + Apple Dev opsiyonel; eğer Apple Dev'siz devam ederseniz Expo Go ile demo limitli ama $0 mümkün).

---

## 4. Bizim Mevcut Kurulumumuzu Free Tier'a Map'leme

| Bizdeki | Mevcut | Ücretsiz alternatif |
|---|---|---|
| Hosting | Hetzner $25/ay (Tier 1) | **Oracle Cloud Always Free** ($0) |
| File storage | Cloudflare R2 ($5/ay tahmini) | Cloudflare R2 free tier (10GB ücretsiz) |
| Email | Postmark $15/ay | **Resend free tier** (3K/ay) |
| Maps | Google Maps API $30/ay | **Native deep link** (free) + **Nominatim** geocoding |
| Logs | Seq self-host (zaten ücretsiz) | Aynen kalır |
| Errors | Sentry $26/ay | Sentry free tier 5K/ay (solo için yeter) veya **GlitchTip self-host** |
| Uptime | Better Stack $24/ay | **UptimeRobot free** veya self-host Healthchecks.io |
| CI/CD | GitHub Actions standard | Public repo: sınırsız |
| OAuth | Google + Apple | Google free; Apple opsiyonel ($99 sadece App Store yayını için) |

**Aylık tasarruf: ~$100-150** (Tier 2'den Tier 0'a düşürülürse).

---

## 5. Trade-off'lar (Dürüst)

Ücretsiz seçenekler ile değiş-tokuş ettiğin şeyler:

| Servis | Free | Trade-off |
|---|---|---|
| Oracle Cloud Always Free | $0 sonsuz | Kayıt sırasında kredi kartı doğrulama (charge yok) + ARM mimarisi (ARM64 build gerekli) |
| OpenStreetMap | $0 | Google'a kıyasla daha az POI verisi, Türkiye coverage'ı yer yer eksik |
| Nominatim geocoding | $0 | 1 req/sec rate limit (yüksek hacim için yetmez) |
| Sentry free | 5K event/ay ücretsiz | Yoğun trafik gelince paid plan'a geçiş gerekli |
| Resend free | 3K e-posta/ay | 100/gün limit |
| Email OTP yerine SMS | $0 | UX biraz yavaş (e-posta kullanıcının inbox'ında bulması daha uzun) |
| Apple Dev'siz | $0 | iOS App Store'a yayın yapılamaz, sadece Expo Go ile demo |
| Self-host Sentry/GlitchTip | $0 | Yönetim yükü artar (uptime + backup) |

**Ekonomik olarak en mantıklı:** İlk 6 ay 100% free; ilk gerçek müşteri gelene kadar paid'e geçme. PSP komisyonu üzerinden gelir akışı başlayınca kademeli olarak paid tier'lara geç.

---

## 6. Yapılacaklar (Free Tier'a Geçiş)

| Madde | Mevcut | Free Plan | Efor |
|---|---|---|---|
| `EmailChannel` Resend impl | 🟡 Mock | Resend SDK (free 3K/ay) | S |
| OpenStreetMap geocoding wrapper | 🔴 yok | Nominatim adapter | S |
| OpenStreetMap mobile harita component | 🔴 yok | react-native-leaflet-view veya WebView + Leaflet | M |
| Native maps deep link button | 🔴 yok | `Linking.openURL()` ile Apple/Google Maps | S |
| Email OTP fallback (SMS yerine) | 🔴 yok | OtpService'i email kanalı destekleyecek şekilde extend | M |
| Oracle Cloud deployment runbook | 🔴 yok | Step-by-step `docs/12-devops/03-oracle-cloud-free.md` | M |
| Cloudflare R2 setup runbook | 🔴 yok | Bucket + API token oluşturma + .env config | S |
| GlitchTip self-host stub | 🔴 yok | docker-compose.prod.yml'a ekleme | M |
| Apple Dev'siz Expo Go demo flow | 🟡 mevcut ama doc yok | RUNNING.md'ye eklenmeli | S |

---

## 7. Bağlantılar

- [`PHASE-0-MASTER-SPEC.md`](PHASE-0-MASTER-SPEC.md)
- [`13-project-management/03-budget-tiers.md`](13-project-management/03-budget-tiers.md) — orijinal tier'lı bütçe modeli
- [`mobile/RUNNING.md`](../mobile/RUNNING.md) — dev'de çalıştırma
- Oracle Cloud Always Free: https://www.oracle.com/cloud/free/
- Resend: https://resend.com/pricing
- OpenStreetMap usage policy: https://operations.osmfoundation.org/policies/tiles/
- Nominatim usage: https://operations.osmfoundation.org/policies/nominatim/

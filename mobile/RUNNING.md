# Kuaforum Mobile — Çalıştırma Rehberi

> Bu rehber, mobil uygulamayı yerel makinede ve telefondan test edebilmek için tüm adımları içerir.

## 1. Önkoşullar (sen yapacaksın)

```bash
# Node.js 20+ (zaten yüklü görünüyor: v25.9.0)
node --version

# pnpm yükle
npm i -g pnpm

# Docker Desktop indir + yükle (backend için):
# https://www.docker.com/products/docker-desktop

# Telefonuna "Expo Go" uygulamasını yükle:
# iOS: App Store → Expo Go
# Android: Google Play → Expo Go
```

## 2. Backend'i ayağa kaldır

```bash
# 1) Postgres + Redis + Meilisearch + Seq başlat
#    Eğer hostta 5432 başka bir postgres tarafından doluysa
#    `infra/docker-compose.override.yml` ile başka bir host portuna map et
#    (örn. 5435:5432). Bizim mevcut kurulum 5435 portunu kullanıyor.
cd kuaforum/infra
docker compose -f docker-compose.dev.yml -f docker-compose.override.yml up -d
docker compose -f docker-compose.dev.yml -f docker-compose.override.yml ps   # 4 servis "healthy"

# 2) Migration'ları uygula
#    Design-time DbContext factory KUAFORUM_DESIGN_TIME_CONNECTION env var bekler;
#    default port 5432, override'lı çalışıyorsak (örn. 5435) burada ver.
cd ../backend
export PATH="$PATH:$HOME/.dotnet/tools"
KUAFORUM_DESIGN_TIME_CONNECTION="Host=localhost;Port=5435;Database=kuaforum_dev;Username=kuaforum;Password=devpassword" \
  dotnet ef database update \
  --project src/Kuaforum.Infrastructure \
  --startup-project src/Kuaforum.Api

# 3) API'yi başlat (port 5100, launchSettings.json'da tanımlı)
cd src/Kuaforum.Api
dotnet run

# Yeni terminal — sağlık testi:
curl http://localhost:5100/health
# {"status":"ok","timestamp":"..."}
```

## 3. Mobile'i ayağa kaldır

```bash
cd kuaforum/mobile
pnpm install            # ~2 dk
pnpm start              # Expo dev server + QR kod
```

Çıktıda QR kod ve "Metro waiting on exp://..." göreceksin.

### Telefon ile aynı WiFi'da

- iOS: Kameranızı QR'a tutun → "Open in Expo Go" → uygulama yüklenir.
- Android: Expo Go açın → "Scan QR code" → tarat.

### Tunnel modu (farklı network)

```bash
pnpm start --tunnel
```

(ngrok kuruyor, ilk seferinde ~30s gecikme.)

## 4. Akış — ne çalışıyor?

✅ **Çalışan akış:**
1. **Login ekranı:** Telefon (+905XXXXXXXXX) gir → "Kod gönder"
2. Backend OTP üretir, **mock SMS** olduğu için kod **backend console'una yazılır**:
   ```
   warn: Kuaforum.Infrastructure.Auth.MockSmsService
       MOCK SMS to +90 555 ••• •• 67: OTP code is 123456
   ```
3. **OTP ekranı:** Console'dan kodu kopyala, gir → "Doğrula"
4. JWT alınır, **Tab'lar açılır:** Keşfet / Randevularım / Profil
5. **Keşfet:** Şu an boş (NVI il/ilçe veri seti import script'i henüz çalışmadı — bir sonraki turn).
6. **Profil:** Çıkış yap çalışır.

🚧 **Henüz çalışmayan:**
- Kuaför listesi (M9 Search modülü gerekli, henüz yazılmadı)
- Slot picker → backend hazır ama BarberShop verisi yok
- Ödeme akışı (M7 Payment henüz yazılmadı)
- Apple/Google OAuth (kayıt için credentials ve Apple Dev hesabı gerekli)

## 5. Backend logu izle

```bash
# Yeni terminal:
tail -f /tmp/kuaforum-api.log    # eğer file'a yönlendirildiyse
# veya doğrudan dotnet run output'u izle
```

Seq UI: http://localhost:5341 (structured log gözlemi).

## 6. Sorun çıkarsa

| Hata | Çözüm |
|---|---|
| `connection refused localhost:5100` | Backend ayakta değil → `dotnet run` çalıştığını doğrula |
| `connection refused localhost:5435` | Postgres yok → `docker compose -f docker-compose.dev.yml -f docker-compose.override.yml up -d`. Override yoksa varsayılan 5432; başka bir Postgres ile çakışıyorsa override yazıp port değiştir. |
| Telefon "Network response timed out" | Aynı WiFi'da olduğunu kontrol et veya `--tunnel` |
| OTP gelmiyor | Mock SMS — backend console'dan kodu al |
| 401 Unauthorized | Token süresi geçmiş → çıkış-giriş yap |
| `Migration failed` | DB sıfırla: `docker compose down -v` + tekrar up |

## 7. Demo limitleri (gerçek production'a kalan)

- **Gerçek SMS:** NetGSM hesabı 🟣
- **Apple OAuth:** Apple Developer ($99/yıl) 🟣
- **Google OAuth:** Google Cloud Console + OAuth client 🟣
- **PayTR Pazaryeri:** Hesap + KYC + sub-merchant onboarding 🟣
- **Nilvera e-Arşiv:** Abonelik 🟣
- **Google Maps:** API key + billing 🟣
- **Apple App Store / Google Play:** EAS Build + listing 🟣

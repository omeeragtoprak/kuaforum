# Kuaforum Mobile — Android Studio Emulator ile Test

> Telefon yerine Mac'inde Android emulator ile test etmek istiyorsan.
> Telefon ile test daha kolay — bkz. [`RUNNING.md`](RUNNING.md).

## 1. Gerekli kurulumlar (ilk kez)

### A. Genel araçlar (zaten yüklü olmalı)

```bash
# pnpm (yoksa)
npm i -g pnpm

# Docker Desktop indir ve kur:
# https://www.docker.com/products/docker-desktop
# Mac: drag-drop Applications klasörüne, açtıktan sonra Docker daemon ayağa kalkmalı
docker --version    # 24+ gerekli
```

### B. Android Studio + Emulator

1. **Android Studio indir:** https://developer.android.com/studio (~1 GB indir + ~10 GB disk)
2. Mac'te `.dmg` aç → Applications'a sürükle → açtığında "Setup Wizard"a izin ver
3. İlk açılışta sürücü/emulator paketleri indirir (~5-10 dk)
4. **AVD oluştur:**
   - Sağ üst → "Device Manager" (telefon ikonu)
   - "Create virtual device"
   - Pixel 7 veya Pixel 8 seç → Next
   - System image: **Android 14 (API 34)** "Google Play" işaretli olan → varsa "Download" → Next
   - "Finish"
5. AVD'yi başlat (▶️ play butonu) → **Android emulator açılır** (mac'te yeni pencere)

### C. Expo Go (emulator içine)

Emulator açıldıktan sonra:
1. Emulator içinde **Play Store** aç → Google hesabı ile giriş yap (gmail yeter)
2. Play Store'da **"Expo Go"** ara → "Install"
3. Yüklendiğinde Expo Go simgesini ekrana bırak (henüz açma)

---

## 2. Backend'i ayağa kaldır (her test öncesi)

```bash
# 1. Postgres + Redis + Meilisearch + Seq başlat
cd /Users/omer/Desktop/Projelerim/sirket_projeleri/kuaforum/infra
docker compose -f docker-compose.dev.yml up -d
docker compose -f docker-compose.dev.yml ps   # 4 servis "Up" olmalı

# 2. Migration uygula (ilk kez)
cd ../backend
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet ef database update \
  --project src/Kuaforum.Infrastructure \
  --startup-project src/Kuaforum.Api

# 3. API'yi başlat
cd src/Kuaforum.Api
dotnet run
```

**Backend log'da şunlar görünmeli:**
```
[xx:xx:xx INF] Now listening on: http://localhost:5000
[xx:xx:xx INF] GeographySeeder: seeding 81 cities...
[xx:xx:xx INF] DemoBarberSeeder: creating 6 demo barber shops...
================================================================
ADMIN USER CREATED: admin@kuaforum.local
TOTP SECRET (Base32): JBSWY3DPEHPK3PXP   ← Authenticator'a yaz
================================================================
```

**Test:** Yeni terminalde `curl http://localhost:5000/health` → `{"status":"ok",...}`

---

## 3. Mobile dev server başlat

```bash
# Yeni terminal
cd /Users/omer/Desktop/Projelerim/sirket_projeleri/kuaforum/mobile

# İlk kez:
pnpm install
# (~2-3 dk, ~1.5 GB node_modules)

# Dev server başlat
pnpm start
```

Çıktı:
```
› Metro waiting on exp://192.168.x.x:8081
› Press a │ open Android
› Press i │ open iOS simulator
› Press w │ open web

› Press ? │ show all commands

[QR kod görünür]
```

---

## 4. Emulator'a yükle

**En kolay yol: Metro'da `a` tuşuna bas**

Metro çalışırken terminalde **`a`** tuşuna bas → Metro otomatik:
1. Emulator'ı bulur (önceden açık olmalı!)
2. Expo Go'yu açar
3. App'i yükler ve çalıştırır

**Çalışmazsa manuel yol:**
1. Emulator'da Expo Go aç
2. "Enter URL manually" → `exp://192.168.x.x:8081` (Metro'nun gösterdiği IP)
3. Connect → app açılır

---

## 5. Demo akışı

```
Login ekranı açılır
↓
Telefon yaz: +905551234567
"Kod gönder" → backend OTP üretir
↓
Backend terminal'i izle:
   "MOCK SMS to +90 555 ••• •• 67: OTP code is 123456"
↓
Bu 6 haneli kodu emulator'a yaz → "Doğrula"
↓
Tab'lar açılır: Keşfet / Randevularım / Profil
↓
Keşfet → İstanbul tıkla → 2 demo kuaför
   "Modern Stüdyo Kuaför (Kadıköy, Unisex)"
   "Klasik Berber Beşiktaş"
↓
Birini aç → hizmet seç → stilist seç → slot seç
"Devam et" → onay ekranı → "Randevuyu oluştur"
↓
"Randevu oluşturuldu" — Pending durumda
↓
Postman/Bruno'dan: POST /v1/payments/initiate
  Authorization: Bearer <JWT> (verifyOtp response'dan)
  Body: {"bookingId": "..."}
→ checkoutUrl döner: http://localhost:5000/v1/payments/mock/complete?token=mock-...
↓
Tarayıcıda checkoutUrl'i aç → "Mock payment captured"
↓
Mobile'da pull-to-refresh → Confirmed durumda
```

---

## 6. Yaygın Sorunlar

| Hata | Çözüm |
|---|---|
| `Network response timed out` | Emulator localhost'a `10.0.2.2` üzerinden erişir. `mobile/src/api/client.ts`'de `EXPO_PUBLIC_API_BASE_URL=http://10.0.2.2:5000` env'i ayarla |
| Emulator çok yavaş | Android Studio → AVD Manager → emulator config → "Hardware - GLES 2.0" + "Cold Boot Now" |
| `pnpm install` hata veriyor | `rm -rf node_modules pnpm-lock.yaml && pnpm install` |
| Backend `connection refused localhost:5432` | Docker compose `up` çalıştırmadın → `docker compose -f infra/docker-compose.dev.yml up -d` |
| Backend "Migration failed" | Yeni DB. `docker compose down -v && docker compose up -d` ardından migration tekrar |
| OTP gelmiyor | Mock SMS — backend console'a yazılır. Yukarıdaki adımları izle |
| Expo Go "Something went wrong" | Metro bundler timeout. `pnpm start --clear` ile cache temizle |
| Web admin login çalışmıyor | `dotnet run` çıktısındaki TOTP secret'ı Authenticator'a manuel ekle |

---

## 7. Emulator için özel: localhost ↔ 10.0.2.2

Android emulator'da `localhost` **emulator'ın kendi içi**. Mac'teki backend'e ulaşmak için:

```typescript
// mobile/.env (oluştur)
EXPO_PUBLIC_API_BASE_URL=http://10.0.2.2:5000
```

Sonra `pnpm start` ile dev server'ı yeniden başlat.

**Gerçek Android telefon için:** Mac IP'si gerekli. `ifconfig | grep inet` → `192.168.x.x` → `EXPO_PUBLIC_API_BASE_URL=http://192.168.x.x:5000`. Mac firewall port 5000'e izin vermeli.

---

## 8. Çıkış (her test sonrası)

```bash
# Backend kapat: Ctrl+C
# Mobile dev server kapat: Ctrl+C
# Docker servislerini kapat (data kalır):
cd infra
docker compose -f docker-compose.dev.yml down

# Data dahil sıfırla (yeniden seed için):
docker compose -f docker-compose.dev.yml down -v
```

---

## 9. Bağlantılar

- [`RUNNING.md`](RUNNING.md) — telefon ile test (daha kolay)
- [`docs/PHASE-0-MASTER-SPEC.md`](../docs/PHASE-0-MASTER-SPEC.md)
- [`docs/FREE-SETUP.md`](../docs/FREE-SETUP.md) — ücretsiz alternatifler

# Kuaforum — Manuel Android APK Build'leri

Bu dizin, founder'ın kendi Android telefonundan test etmesi için elle üretilmiş
**release-tipi ama debug-imzalı** APK'ları içerir. `builds/*.apk` `.gitignore`'da
— büyük binary dosyalar repo'ya girmez, yalnız bu README versiyonlanır.

---

## Son build

| Alan | Değer |
|---|---|
| Dosya | `kuaforum-20260806-1415.apk` |
| Boyut | ~74 MB |
| Commit (backend + mobile) | `44a6714` — `fix(mobile): kapora akışı metinleri — ödeme onay yerine geçmez, kuaför onayı + tam iade güvencesi (RFC-013 C5c)` |
| Build zamanı | 2026-08-06 ~13:53–14:15 (yerel) |
| Gömülü API adresi | `http://192.168.2.15:5100` (Mac'in LAN IP'si, `EXPO_PUBLIC_API_BASE_URL` build-time env ile — kaynak kod değiştirilmedi) |
| İmza | **debug keystore** (Expo prebuild şablonunun varsayılanı — bkz. "İmza kararı") |
| Package | `com.kuaforum.app` |

> **Not:** Build sırasında mobile ağacında art arda commit'ler landed oldu (paralel
> ajanlar aktif). APK, build başladığı andaki (~13:53) çalışma ağacını yansıtır —
> bu commit veya sonrasında mobile'a giden değişiklikler bu APK'da **olmayabilir**.
> Kesinlik için yeni bir build gerekir (aşağıdaki "Yeniden build" bölümü).

---

## Kurulum (founder'ın telefonunda)

1. APK dosyasını telefona aktar (AirDrop yoksa: USB kablo, Google Drive, WhatsApp
   kendine gönderme, ya da `python3 -m http.server` ile Mac'ten LAN üzerinden indir).
2. Telefonda **Ayarlar → Güvenlik → Bilinmeyen kaynaklardan yükleme** (Android
   sürümüne göre "Bu kaynaktan yükleye izin ver" — dosya yöneticisi/Chrome bazlı
   sorulur) iznini APK'yı açacağın uygulama için aç.
3. Dosyaya dokun → Yükle. Var olan bir "Kuaforum" kurulumu varsa ve imza
   farklıysa Android **önce eski sürümü kaldırmanı ister** (debug-imza her
   build'de aynı sabit anahtar olduğu için normalde bu build'ler üst üste
   kurulabilir, ama Expo Go veya farklı bir kaynaktan kurulmuş sürüm varsa çakışır).
4. Aç. **Telefon ve Mac aynı Wi-Fi ağında olmalı** (uygulama `192.168.2.15:5100`'e
   konuşuyor — farklı ağdaysa hiçbir istek dönmez, sonsuz "yükleniyor" görürsün).

### Bu APK debug-imzalı — mağazaya gitmez

Bu bir **geliştirici/QA build'idir**, Google Play'e veya iç dağıtıma (Play Console
internal testing, TestFlight benzeri) **yüklenemez**. Expo prebuild şablonu
`release` build type'ını da `debug.keystore` ile imzalıyor (bkz. aşağıda "İmza
kararı" — bunu ben değiştirmedim, şablonun kendi varsayılanı). Gerçek dağıtım için
EAS Build (`eas build --profile production`) ile ayrı bir upload-keystore
gerekiyor — bu iş kapsamında değildi.

---

## Backend'i LAN'da çalıştırma (founder kendi başlatacak)

Görev bitince backend'i kapattım (`pkill -f Kuaforum.Api`) — founder test etmek
istediğinde kendisi başlatmalı:

```bash
# 1) Dev servisleri ayakta olmalı (Postgres 5435, Redis 6380, Seq 5341, Meilisearch 7700)
docker compose -f infra/docker-compose.dev.yml up -d
docker ps   # kuaforum-postgres-dev / kuaforum-redis-dev sağlıklı mı kontrol et

# 2) Backend'i tüm arayüzlere bağlı başlat (yalnız localhost DEĞİL — 0.0.0.0 şart,
#    yoksa telefon bağlanamaz)
cd backend/src/Kuaforum.Api
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5100 dotnet run
```

Mac'in LAN IP'si değişirse (başka ağa bağlanırsan, DHCP yenilerse) `ipconfig getifaddr en0`
ile yeni IP'yi al ve APK'yı o IP ile **yeniden build et** (adres JS bundle'ına
build-time'da gömülüyor, runtime'da değiştirilemiyor).

**Doğrulama (bu build için yapıldı, kanıt aşağıda):**
```bash
curl -s http://192.168.2.15:5100/health
curl -s http://192.168.2.15:5100/v1/geography/cities | head -c 200
```

---

## KRİTİK: Release build'de hızlı-giriş butonları YOK — OTP'yi nasıl gireceksin

Uygulama şu an **misafir gezinme** destekliyor: login olmadan keşfet / arama /
salon profili görüntüleme çalışır. Ama **randevu almak** için kayıt/giriş zorunlu
ve bu telefon + OTP akışı.

`mobile/app/(auth)/login.tsx` içindeki hızlı-giriş ("Müşteri (rıza ekranı)" vb.
demo persona) butonları **`__DEV__` guard'lı** — yani Expo Go / `expo start`
geliştirme modunda görünürler ama bu **release APK'da görünmeyecekler**. Founder
gerçek telefon numarasıyla normal OTP akışından geçecek.

**OTP SMS'i gerçekten gönderilMİYOR.** Backend `Development` ortamında
`MockSmsService` kullanıyor (`backend/src/Kuaforum.Infrastructure/Auth/MockSmsService.cs`)
— gerçek SMS yerine kodu **backend konsol/terminal logunda** `Warning` seviyesinde
basıyor. Founder telefon numarasını girip "kod gönder"e bastığında, backend'i
çalıştırdığın terminalde (ya da Seq'te, `http://localhost:5341`) şuna benzer bir
satır çıkacak:

```
[14:32:07 WRN] MOCK SMS to +90 555 •• •• 12: OTP code is 384726. (Replace with NetGSM in staging.)
```

Founder bu 6 haneli kodu (`384726` örnekte) telefonundaki OTP giriş ekranına
elle yazacak. Terminal Mac'te açık kalmalı, founder ekran görüntüsü isteyebilir
ya da sen (backend'i başlatan) sesli/mesajla kodu ona iletebilirsin.

**Alternatif öneri (bu görevin kapsamı dışında, ileri iş):** `GET /v1/auth/otp/debug`
gibi Development-only bir endpoint eklenip son üretilen kodu JSON'da döndürse
founder'ın terminale bakma zorunluluğu kalkar — ama bu sınırın backend/src'e
dokunmayı gerektirdiği için bu build görevinde yapılmadı (parallel agent'lar
backend üzerinde çalışıyor, çakışma riski). Küçük, izole bir takip görevi olarak
önerilir.

---

## Prebuild plugin doğrulaması

`npx expo prebuild --platform android --clean` sonrası üretilen
`mobile/android/app/src/main/AndroidManifest.xml` içinde `expo-image-picker`
plugin config'inin (`app.json` → kameraPermission/photosPermission/microphonePermission:false)
yansıdığı doğrulandı:

```
<uses-permission android:name="android.permission.READ_EXTERNAL_STORAGE"/>
<uses-permission android:name="android.permission.RECORD_AUDIO" tools:node="remove"/>
<uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE"/>
```

`RECORD_AUDIO`'nun `tools:node="remove"` ile açıkça kaldırılmış olması,
`app.json`'daki `microphonePermission: false` ayarının plugin tarafından işlendiğinin
kanıtı (aksi halde image-picker varsayılan olarak mikrofon izni de ekler).
`expo-document-picker` bağımlılığı `app.json` plugin listesinde yok — bu
kütüphane Android'de izin metni gerektiren bir config plugin'e sahip değil
(Storage Access Framework üzerinden çalışıyor, ekstra manifest girişi gerekmiyor),
o yüzden eksik değil.

## İmza kararı

Ekstra bir müdahale gerekmedi: Expo'nun bare-workflow şablonu zaten
`android/app/build.gradle`'da `release` build type'ını `signingConfigs.debug`'a
bağlıyor (satır ~112, "Caution! In production, you need to generate your own
keystore file." yorumuyla). Bu üretilen dosya `mobile/android/` altında ve
gitignored — repo'da hiçbir imza/secret izi yok. Gerçek prod dağıtımı için
ayrı bir upload keystore (`eas credentials` veya manuel `keytool`) gerekir.

## LAN erişim kanıtı (bu build'e ait)

```
$ curl -s http://192.168.2.15:5100/health
{"status":"ok","timestamp":"2026-08-06T10:52:33.579717+00:00"}

$ curl -s http://192.168.2.15:5100/v1/geography/cities | head -c 200
[{"id":"56d5b445-3cf1-4ab6-91f0-d2c11a5c0afa","plateCode":1,"name":"Adana", ...
```

---

## Yeniden build (bir dahaki sefere)

```bash
export JAVA_HOME="/Applications/Android Studio.app/Contents/jbr/Contents/Home"
export ANDROID_HOME="$HOME/Library/Android/sdk"
export ANDROID_SDK_ROOT="$HOME/Library/Android/sdk"
export PATH="$JAVA_HOME/bin:$ANDROID_HOME/platform-tools:$PATH"
export EXPO_PUBLIC_API_BASE_URL="http://<MAC-LAN-IP>:5100"   # ipconfig getifaddr en0

cd mobile
npx expo prebuild --platform android --clean
cd android
./gradlew assembleRelease --no-daemon
# çıktı: mobile/android/app/build/outputs/apk/release/app-release.apk
cp app/build/outputs/apk/release/app-release.apk \
   ../../builds/kuaforum-$(date +%Y%m%d-%H%M).apk
```

Gradle wrapper ilk seferde ~8.10.2 dağıtımını indirir (birkaç dakika); sonraki
build'ler daha hızlı olur (dependency cache sıcak).

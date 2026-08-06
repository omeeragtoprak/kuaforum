# Kuaforum — Manuel Android APK Build'leri

Bu dizin, founder'ın kendi Android telefonundan test etmesi için elle üretilmiş
**release-tipi ama debug-imzalı** APK'ları içerir. `builds/*.apk` `.gitignore`'da
— büyük binary dosyalar repo'ya girmez, yalnız bu README versiyonlanır.

---

## Kısayol: tek komutla backend + Docker

Repo kökünde iki script var, ikisi de `chmod +x` edilmiş:

```bash
./dev-up.sh     # Docker (Postgres/Redis/Meilisearch/Seq) + backend'i tek seferde ayağa kaldırır
./dev-down.sh   # backend'i durdurur + Docker'ı indirir
```

`dev-up.sh` şunu yapar:
1. `infra/docker-compose.dev.yml` (+ varsa `infra/docker-compose.override.yml`) ile
   Docker servislerini kaldırır, Postgres + Redis **sağlıklı** olana kadar bekler.
2. Mac'in LAN IP'sini (`ipconfig getifaddr en0`) bulur, ekrana büyük punto basar —
   **APK'yı build ederken bu IP'yi kullan.**
3. Backend'i `ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5100`
   ile başlatır (tüm arayüzlere bağlı — telefon bu yüzden ulaşabiliyor).
4. OTP kodlarının göründüğü log satırlarını (`OTP code is` / `MOCK SMS`) ayrı,
   yeşil/📱 işaretli bir akışta gösterir — tüm logun içinde aramana gerek kalmaz.
5. Ctrl+C ile backend + log akışı temiz kapanır. Docker servisleri **AYAKTA kalır**
   (kasıtlı — sık sık yeniden indirip beklemek gerekmesin). Tamamen kapatmak için
   `./dev-down.sh`.

Script'in kendisi `docker-compose.override.yml` yoksa uyarı basar (Postgres portu
appsettings.Development.json'ın beklediği 5435 yerine varsayılan 5432'de açılır).

---

## İki APK arasındaki fark

| | `kuaforum-20260806-1415.apk` (eski) | `kuaforum-test-<tarih>.apk` (yeni) |
|---|---|---|
| Hızlı giriş (Demo rol butonları) | **Görünmüyor** — `__DEV__` guard'lı, release build'de `__DEV__` false | **Görünüyor** — build'de `EXPO_PUBLIC_ENABLE_TEST_LOGIN=1` set edildi |
| Giriş yöntemi | Yalnız gerçek telefon numarası + OTP (mock SMS, terminal logundan okunur) | Hızlı giriş butonları (Customer / Müşteri rıza ekranı / Kuaför / Yeni kuaför Draft / Admin) **veya** OTP |
| Ekranda uyarı | — | Hızlı giriş bölümünün üstünde sarı "⚠️ TEST BUILD" şeridi |
| Amaç | "Gerçek kullanıcı" akışını test etmek | Founder'ın hızlıca farklı rollere (müşteri/kuaför/admin) tek dokunuşla girip UI gezmesi |

**Her ikisi de** debug-imzalı, `com.kuaforum.app` paketi, aynı backend'e (`EXPO_PUBLIC_API_BASE_URL`
build-time'da gömülü) konuşuyor. Store'a/iç dağıtıma **hiçbiri** yüklenemez (bkz. aşağıda
"Bu APK debug-imzalı").

---

## Son build (test APK — hızlı giriş açık)

| Alan | Değer |
|---|---|
| Dosya | `kuaforum-test-20260806-1501.apk` |
| Boyut | 77.753.996 bayt (~74,2 MB) |
| SHA-256 | `2dafc70f5b9b2795475dc72d19ce5752fe7197d280eeaa6c4ded1e9f60978b17` |
| Commit (mobile) | `feat(dev): test build'de hızlı giriş + tek komut dev ortamı (dev-up/dev-down)` — bu iş paketinin commit'i |
| Build zamanı | 2026-08-06 14:34–15:01 (yerel, ~27 dk — `assembleRelease` çıktısı: `BUILD SUCCESSFUL in 26m 59s`) |
| Gömülü API adresi | `http://192.168.2.15:5100` (`EXPO_PUBLIC_API_BASE_URL`) |
| Hızlı giriş | **AÇIK** (`EXPO_PUBLIC_ENABLE_TEST_LOGIN=1`) — login ekranında sarı "TEST BUILD" şeridi + 5 rol butonu |
| İmza | debug keystore (bkz. "İmza kararı") |
| Package | `com.kuaforum.app` |

> Önceki build (`kuaforum-20260806-1415.apk`, hızlı giriş KAPALI) hâlâ `builds/`
> altında duruyor — karşılaştırma / "gerçek kullanıcı" akışı testi için sakla.

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

### TEST BUILD'i yanlışlıkla gerçek kullanıcıya dağıtma

`kuaforum-test-*.apk` dosyaları hızlı-giriş butonlarıyla geliyor — herhangi bir rolle
(müşteri, kuaför, admin) OTP'siz giriş yapılabiliyor. Bu **sadece founder'ın kendi
telefonunda iç test için**. Gerçek kullanıcıya/App Store'a giden hiçbir dağıtım
`EXPO_PUBLIC_ENABLE_TEST_LOGIN=1` ile build edilmemeli. Login ekranındaki sarı
"TEST BUILD" şeridi de bunun hatırlatıcısı.

---

## Backend'i LAN'da çalıştırma (founder kendi başlatacak)

**Önerilen: `./dev-up.sh`** (repo kökünde) — yukarıdaki "Kısayol" bölümüne bak.

Elle yapmak istersen:

```bash
# 1) Dev servisleri ayakta olmalı (Postgres 5435, Redis 6380, Seq 5341, Meilisearch 7700)
docker compose -f infra/docker-compose.dev.yml -f infra/docker-compose.override.yml up -d
docker ps   # kuaforum-postgres-dev / kuaforum-redis-dev sağlıklı mı kontrol et

# 2) Backend'i tüm arayüzlere bağlı başlat (yalnız localhost DEĞİL — 0.0.0.0 şart,
#    yoksa telefon bağlanamaz)
cd backend/src/Kuaforum.Api
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5100 dotnet run
```

Mac'in LAN IP'si değişirse (başka ağa bağlanırsan, DHCP yenilerse) `ipconfig getifaddr en0`
ile yeni IP'yi al (`./dev-up.sh` bunu zaten otomatik ekrana basıyor) ve APK'yı o IP ile
**yeniden build et** (adres JS bundle'ına build-time'da gömülüyor, runtime'da değiştirilemiyor
— aşağıdaki "Yeniden build" bölümüne bak).

**Doğrulama (bu build için `./dev-up.sh` ile yapıldı, kanıt aşağıda):**
```bash
curl -s http://192.168.2.15:5100/health
```

---

## KRİTİK (güncellendi): Hızlı-giriş artık test APK'sında GÖRÜNÜYOR

Önceki build'de (`kuaforum-20260806-1415.apk`) hızlı-giriş butonları **`__DEV__`
guard'lıydı** — yani yalnızca Expo Go / `expo start` geliştirme modunda görünüyordu,
release APK'da görünmüyordu ve founder gerçek OTP akışından geçmek zorundaydı.

`mobile/app/(auth)/login.tsx` artık şu koşulu kullanıyor:

```ts
const showQuickLogin = __DEV__ || process.env.EXPO_PUBLIC_ENABLE_TEST_LOGIN === '1';
```

- **Normal/store build** (env set edilmeden build edilirse) → hızlı giriş **gizli**
  kalır, eski güvenlik davranışı korunur.
- **Test APK** (`EXPO_PUBLIC_ENABLE_TEST_LOGIN=1` ile build edilirse, aşağıdaki
  "Yeniden build" komutu) → hızlı giriş **görünür**, üstünde sarı "TEST BUILD" uyarısı.

Uygulama şu an **misafir gezinme** de destekliyor: login olmadan keşfet / arama /
salon profili görüntüleme çalışır. Randevu almak için kayıt/giriş zorunlu.

### Hızlı giriş rolleri (test APK'sında görünen 5 buton)

| Buton | Ne yapar |
|---|---|
| 👤 Müşteri olarak gir | Randevu al, kuaför keşfet — normal müşteri hesabı |
| 🔐 Müşteri (rıza ekranı) | Her girişte KVKK rıza akışı yeniden sorulur — onboarding testi için |
| ✂️ Kuaför olarak gir | Onaylı salon — kuaför panelini, randevu yönetimini test et |
| 🆕 Yeni kuaför (Draft) | KYC başvurusu tamamlanmamış — onboarding sihirbazı testi için |
| 🛡️ Admin olarak gir | KYC onay, kullanıcı yönetimi, yorum moderasyonu |

### OTP ile giriş (test APK'sında da hâlâ çalışır, gerçek telefonda ZORUNLU)

**OTP SMS'i gerçekten gönderilMİYOR.** Backend `Development` ortamında
`MockSmsService` kullanıyor (`backend/src/Kuaforum.Infrastructure/Auth/MockSmsService.cs`)
— gerçek SMS yerine kodu **backend konsol/terminal logunda** `Warning` seviyesinde
basıyor. `./dev-up.sh` kullanıyorsan bu satır ayrıca 📱 işaretiyle ayrı vurgulanıyor:

```
📱  [14:32:07 WRN] MOCK SMS to +90 555 •• •• 12: OTP code is 384726. (Replace with NetGSM in staging.)
```

Founder bu 6 haneli kodu (`384726` örnekte) telefonundaki OTP giriş ekranına
elle yazacak.

**Alternatif öneri (bu görevin kapsamı dışında, ileri iş):** `GET /v1/auth/otp/debug`
gibi Development-only bir endpoint eklenip son üretilen kodu JSON'da döndürse
founder'ın terminale bakma zorunluluğu kalkar — backend/src'e dokunmayı gerektirdiği
için bu görevde yapılmadı. Küçük, izole bir takip görevi olarak önerilir.

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
$ ./dev-up.sh
...
############################################################
#   Mac LAN IP :  192.168.2.15
#   Backend    :  http://192.168.2.15:5100
############################################################
...
==> Backend hazır: http://192.168.2.15:5100/health  (Ctrl+C ile kapat)

$ curl -s http://192.168.2.15:5100/health
{"status":"ok","timestamp":"2026-08-06T11:31:08.355382+00:00"}

$ ./dev-down.sh
==> Backend süreçleri durduruluyor...
==> Docker dev servisleri indiriliyor...
 Container kuaforum-postgres-dev  Removed
 Container kuaforum-redis-dev  Removed
Kapandı.
```

---

## Yeniden build (bir dahaki sefere)

```bash
export JAVA_HOME="/Applications/Android Studio.app/Contents/jbr/Contents/Home"
export ANDROID_HOME="$HOME/Library/Android/sdk"
export ANDROID_SDK_ROOT="$HOME/Library/Android/sdk"
export PATH="$JAVA_HOME/bin:$ANDROID_HOME/platform-tools:$PATH"
export EXPO_PUBLIC_API_BASE_URL="http://<MAC-LAN-IP>:5100"   # ./dev-up.sh ekrana basıyor
export EXPO_PUBLIC_ENABLE_TEST_LOGIN=1                        # hızlı giriş butonları AÇIK olsun istersen

cd mobile
npx expo prebuild --platform android --clean
cd android
./gradlew assembleRelease --no-daemon
# çıktı: mobile/android/app/build/outputs/apk/release/app-release.apk
cp app/build/outputs/apk/release/app-release.apk \
   ../../builds/kuaforum-test-$(date +%Y%m%d-%H%M).apk
```

`EXPO_PUBLIC_ENABLE_TEST_LOGIN` set edilmezse hızlı giriş **gizli** kalır — dosyayı
o zaman `kuaforum-$(date ...)` (test olmayan isim) olarak adlandır, karışmasın.

Gradle wrapper ilk seferde ~8.10.2 dağıtımını indirir (birkaç dakika); sonraki
build'ler daha hızlı olur (dependency cache sıcak).

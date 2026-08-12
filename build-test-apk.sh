#!/usr/bin/env bash
# Kuaforum — test APK derleme (founder'ın telefonunda elle test için)
#
# Kullanım:
#   ./build-test-apk.sh                 # LAN IP'yi otomatik bulur
#   ./build-test-apk.sh 192.168.1.11    # adresi elle ver
#
# NEDEN BU SCRIPT VAR — 2026-08-06'da üç kez aynı tuzağa düşüldü:
#
#  1) Env değişkeninin adı `EXPO_PUBLIC_API_BASE_URL`, `EXPO_PUBLIC_API_URL` DEĞİL.
#     Yanlış ad sessizce çalışır: derleme başarılı olur, APK `.env`'deki varsayılana
#     (10.0.2.2 — yalnız emülatörde geçerli) düşer, gerçek telefonda her ekran
#     "sunucuya ulaşılamadı" verir.
#
#  2) `EXPO_PUBLIC_*` env'i `expo prebuild`'e vermek YETMEZ. JS bundle Gradle'ın
#     içinde üretiliyor, dolayısıyla env `./gradlew`'a da geçmek zorunda.
#
#  3) `./gradlew` bu makinede sistem Java'sını bulamıyor; Android Studio'nun
#     paketlediği JDK'yı JAVA_HOME olarak vermek gerekiyor.
#
# Test build bayrağı (EXPO_PUBLIC_ENABLE_TEST_LOGIN=1) ÜÇ şeyi birden açar:
#   - login ekranındaki rol bazlı hızlı-giriş butonları
#   - Android cleartext HTTP izni (app.config.js → expo-build-properties)
#   - uygulama içinden sunucu adresi değiştirme ekranı
# Store build'lerinde bu bayrak VERİLMEZ; üçü de kapalı kalır.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

# --- Sunucu adresi -----------------------------------------------------------
# İki biçim kabul edilir:
#   ./build-test-apk.sh                                  → LAN IP otomatik bulunur (yalnız aynı ağda çalışır)
#   ./build-test-apk.sh 192.168.1.11                     → LAN IP elle
#   ./build-test-apk.sh https://xxx.trycloudflare.com    → TÜNEL (her ağdan çalışır, mobil veride de)
#
# Tünel neden gerekiyor: LAN IP yalnız telefon aynı Wi-Fi'daysa çalışır. Founder
# başka bir ağdayken veya mobil veriyle test etmek istediğinde uygulama ölür.
# `cloudflared tunnel --url http://localhost:5100` hesap gerektirmeden HTTPS bir
# adres verir; o adresi buraya geçirmek yeterli.
if [[ $# -ge 1 && "$1" == http* ]]; then
  API_BASE_URL="${1%/}"          # sondaki / temizlenir
  VERIFY_NEEDLE="$(printf '%s' "$API_BASE_URL" | sed -E 's#^https?://##')"
elif [[ $# -ge 1 ]]; then
  API_BASE_URL="http://$1:5100"
  VERIFY_NEEDLE="$1"
else
  LAN_IP="$(ipconfig getifaddr en0 2>/dev/null || true)"
  if [[ -z "$LAN_IP" ]]; then
    echo "HATA: LAN IP bulunamadı (en0 kapalı olabilir). Adresi elle ver:" >&2
    echo "  ./build-test-apk.sh 192.168.1.11" >&2
    echo "  ./build-test-apk.sh https://xxx.trycloudflare.com" >&2
    exit 1
  fi
  API_BASE_URL="http://${LAN_IP}:5100"
  VERIFY_NEEDLE="$LAN_IP"
fi

# --- Java --------------------------------------------------------------------
if [[ -z "${JAVA_HOME:-}" ]] || [[ ! -x "${JAVA_HOME}/bin/java" ]]; then
  for candidate in \
    "/Applications/Android Studio.app/Contents/jbr/Contents/Home" \
    "/opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home"; do
    if [[ -x "${candidate}/bin/java" ]]; then
      export JAVA_HOME="$candidate"
      break
    fi
  done
fi
if [[ -z "${JAVA_HOME:-}" ]] || [[ ! -x "${JAVA_HOME}/bin/java" ]]; then
  echo "HATA: JDK bulunamadı. Android Studio kurulu mu?" >&2
  exit 1
fi
export PATH="${JAVA_HOME}/bin:${PATH}"

# --- Android SDK -------------------------------------------------------------
# `expo prebuild --clean` android/ klasörünü baştan yaratıyor ve içindeki
# `local.properties` (sdk.dir) dosyasını da siliyor. Bu yüzden ANDROID_HOME
# ortamdan verilmezse Gradle "SDK location not found" ile düşer.
if [[ -z "${ANDROID_HOME:-}" ]]; then
  for candidate in "$HOME/Library/Android/sdk" "$HOME/Android/Sdk"; do
    [[ -d "$candidate" ]] && { export ANDROID_HOME="$candidate"; break; }
  done
fi
if [[ -z "${ANDROID_HOME:-}" ]] || [[ ! -d "$ANDROID_HOME" ]]; then
  echo "HATA: Android SDK bulunamadı (ANDROID_HOME)." >&2
  exit 1
fi
export ANDROID_SDK_ROOT="$ANDROID_HOME"

# --- Derleme -----------------------------------------------------------------
export EXPO_PUBLIC_ENABLE_TEST_LOGIN=1
export EXPO_PUBLIC_API_BASE_URL="$API_BASE_URL"

# Hızlı giriş (dev-login) paylaşılan secret'ı. Sunucu bu ucu `DevTools:AccessKey` ile
# koruyor; header gönderilmezse 403 "Geçersiz veya eksik X-Dev-Access-Key" döner ve
# APK'daki 5 rol hızlı-girişinin TAMAMI çalışmaz (emülatörde bu şekilde yakalandı —
# sunucu doğru kuruluydu, istemci header'ı hiç göndermiyordu).
#
# Değer burada SABİT YAZILMAZ. İki kaynaktan gelir:
#   1) Ortamda zaten EXPO_PUBLIC_DEV_ACCESS_KEY varsa o kullanılır.
#   2) Yoksa ve RENDER_API_KEY verilmişse, Render'ın env-vars API'sinden çekilir —
#      böylece anahtar repoda da, komut geçmişinde de durmaz.
# Hiçbiri yoksa derleme SÜRER ama uyarı verir: uzak sunucuya karşı hızlı giriş çalışmaz
# (yerel Development sunucusu AccessKey istemediği için orada sorun olmaz).
if [ -z "${EXPO_PUBLIC_DEV_ACCESS_KEY:-}" ] && [ -n "${RENDER_API_KEY:-}" ]; then
  EXPO_PUBLIC_DEV_ACCESS_KEY=$(
    curl -s -H "Authorization: Bearer $RENDER_API_KEY" \
      "https://api.render.com/v1/services/${RENDER_SERVICE_ID:-srv-d9tfcu6417fc73e9lbhg}/env-vars?limit=50" \
    | python3 -c "import json,sys; print(next((e.get('envVar',e).get('value','') for e in json.load(sys.stdin) if e.get('envVar',e).get('key')=='DevTools__AccessKey'), ''))"
  )
  export EXPO_PUBLIC_DEV_ACCESS_KEY
fi

if [ -n "${EXPO_PUBLIC_DEV_ACCESS_KEY:-}" ]; then
  export EXPO_PUBLIC_DEV_ACCESS_KEY
  echo "▸ Hızlı giriş   : anahtar var (${#EXPO_PUBLIC_DEV_ACCESS_KEY} karakter)"
else
  echo "▸ Hızlı giriş   : UYARI — anahtar YOK, uzak sunucuda hızlı giriş 403 verecek"
fi

echo "▸ Sunucu adresi : $API_BASE_URL"
echo "▸ JAVA_HOME     : $JAVA_HOME"
echo "▸ ANDROID_HOME  : $ANDROID_HOME"
echo "▸ prebuild..."
(cd mobile && npx expo prebuild --platform android --clean)

echo "▸ gradle assembleRelease... (birkaç dakika sürer)"
(cd mobile/android && ./gradlew assembleRelease)

# --- Çıktı -------------------------------------------------------------------
SRC_APK="mobile/android/app/build/outputs/apk/release/app-release.apk"
[[ -f "$SRC_APK" ]] || { echo "HATA: APK üretilmedi: $SRC_APK" >&2; exit 1; }

mkdir -p builds
STAMP="$(date +%Y%m%d-%H%M)"
OUT_APK="builds/kuaforum-test-${STAMP}.apk"
# Eski test APK'ları kafa karıştırıyor (founder yanlışlıkla eskisini kuruyor) — temizle.
rm -f builds/kuaforum-test-*.apk
cp "$SRC_APK" "$OUT_APK"

# --- Doğrulama: adres gerçekten APK'ya girdi mi? -----------------------------
# Gradle'ın ara çıktı yolları sürümden sürüme değişiyor; ilk yazımda tahmin edilen
# yol tutmadı ve doğrulama SESSİZCE atlandı — tam da önlemek istediğimiz durum.
# Bu yüzden bundle APK'nın KENDİSİNDEN okunuyor: APK varsa yol da vardır.
#
# Hermes, içinde Türkçe karakter/emoji geçen string'leri UTF-16 saklıyor; bu yüzden
# düz `strings` yetmez, iki kodlamayı da aramak gerekiyor.
python3 - "$OUT_APK" "$VERIFY_NEEDLE" <<'PY'
import sys, zipfile
apk, ip = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(apk) as z:
    names = [n for n in z.namelist() if n.endswith('index.android.bundle')]
    if not names:
        print("✘ HATA: APK içinde JS bundle bulunamadı.")
        sys.exit(1)
    data = z.read(names[0])
hits = data.count(ip.encode('utf-8')) + data.count(ip.encode('utf-16-le'))
if hits:
    print(f"✔ Doğrulandı: '{ip}' APK bundle'ında {hits} kez geçiyor.")
else:
    print(f"✘ HATA: '{ip}' APK bundle'ında YOK — APK yanlış adrese bakıyor.")
    print("  Muhtemel sebep: env değişkeninin adı yanlış (EXPO_PUBLIC_API_BASE_URL olmalı)")
    print("  ya da env gradlew'a geçmedi. APK'yı DAĞITMA.")
    sys.exit(1)
PY

echo
echo "✔ Hazır: $OUT_APK  ($(du -h "$OUT_APK" | cut -f1))"
echo "  Kurulum: adb install -r $OUT_APK"
echo "  Not: IP tekrar değişirse yeniden derlemeye gerek yok —"
echo "       giriş ekranındaki 'Sunucu: ... / Değiştir' ile uygulama içinden ayarlanır."

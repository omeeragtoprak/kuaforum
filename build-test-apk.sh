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
if [[ $# -ge 1 ]]; then
  LAN_IP="$1"
else
  LAN_IP="$(ipconfig getifaddr en0 2>/dev/null || true)"
  if [[ -z "$LAN_IP" ]]; then
    echo "HATA: LAN IP bulunamadı (en0 kapalı olabilir). Adresi elle ver:" >&2
    echo "  ./build-test-apk.sh 192.168.1.11" >&2
    exit 1
  fi
fi
API_BASE_URL="http://${LAN_IP}:5100"

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
python3 - "$OUT_APK" "$LAN_IP" <<'PY'
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

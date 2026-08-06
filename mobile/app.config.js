// Expo dinamik konfigürasyon — app.json'ı temel alır, YALNIZ test build'lerinde
// ek ayarlar uygular.
//
// NEDEN: Android 9+ (API 28) varsayılan olarak cleartext (http://) trafiğini
// ENGELLER. Test APK'sı LAN'daki dev backend'e `http://<lan-ip>:5100` üzerinden
// bağlandığı için bu izin olmadan uygulamanın TÜM ekranları "sunucuya ulaşılamadı"
// hatası verir (2026-08-06'da hem emülatörde hem founder'ın telefonunda görüldü).
//
// Production'da API HTTPS (Caddy TLS) üzerinden konuşacağı için bu izin store
// build'lerinde AÇILMAZ — yalnız EXPO_PUBLIC_ENABLE_TEST_LOGIN=1 ile derlenen
// test build'lerinde etkinleşir (aynı bayrak hızlı-giriş butonlarını da açar).
const appJson = require('./app.json');

const isTestBuild = process.env.EXPO_PUBLIC_ENABLE_TEST_LOGIN === '1';

module.exports = () => {
  const expo = { ...appJson.expo };

  if (isTestBuild) {
    // NOT: `expo.android.usesCleartextTraffic` alanı SDK 52'de AndroidManifest'e
    // yansımıyor (denendi, manifest'te çıkmadı) — dokümante edilen yol
    // expo-build-properties plugin'i.
    expo.plugins = [
      ...(expo.plugins ?? []),
      ['expo-build-properties', { android: { usesCleartextTraffic: true } }],
    ];
  }

  return { ...appJson, expo };
};

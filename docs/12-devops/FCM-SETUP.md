# FCM (Firebase Cloud Messaging) Setup

> Gerçek push notification delivery için adım adım rehber. Mock fallback dev'de çalışır; staging/production için bu adımlar gerekli.

---

## 1. Firebase Project Oluştur

1. [Firebase Console](https://console.firebase.google.com) → "Add project"
2. Proje adı: `kuaforum-prod` (veya `kuaforum-staging`)
3. Google Analytics: opt-out (MVP için gerek yok)
4. "Create project" → bekle

## 2. Android Uygulaması Ekle

1. Firebase Console → proje → "Add app" → Android icon
2. Package name: `com.kuaforum.app` (Expo `app.json` `android.package` ile aynı olmalı)
3. App nickname: `Kuaforum Android`
4. SHA-1: opt-out (sonra eklenir)
5. **Download `google-services.json`** → `mobile/google-services.json` olarak kaydet
6. Skip SDK install steps (Expo SDK halleder)

## 3. iOS Uygulaması Ekle

1. Firebase Console → proje → "Add app" → iOS icon
2. Bundle ID: `com.kuaforum.app` (Expo `app.json` `ios.bundleIdentifier` ile aynı)
3. **Download `GoogleService-Info.plist`** → `mobile/GoogleService-Info.plist`
4. Apple Push Notification Authentication Key (`.p8`) yükle:
   - Apple Developer → Keys → "+ Create a key" → APNs auth key
   - Download .p8 file (one-time)
   - Firebase Console → Project Settings → Cloud Messaging → "APNs Authentication Key" → Upload .p8 + Key ID + Team ID

## 4. Expo Config Ekle

`mobile/app.json` içine:

```json
{
  "expo": {
    "android": {
      "package": "com.kuaforum.app",
      "googleServicesFile": "./google-services.json",
      "useNextNotificationsApi": true
    },
    "ios": {
      "bundleIdentifier": "com.kuaforum.app",
      "googleServicesFile": "./GoogleService-Info.plist"
    },
    "plugins": [
      ["expo-notifications", { "icon": "./assets/notification-icon.png", "color": "#ffffff" }]
    ]
  }
}
```

`google-services.json` ve `GoogleService-Info.plist` **`.gitignore`'a** eklenmeli (secret).

## 5. Backend Service Account

1. Firebase Console → Project Settings → Service accounts
2. "Generate new private key" → JSON dosyası indirilir
3. Bu JSON Hetzner'da **secret olarak** saklanmalı:
   - Docker Compose: `docker compose --env-file .env.prod up`, `.env.prod` içinde:
     ```
     Fcm__ServiceAccountJson={"type":"service_account",...}
     ```
   - Veya dosya yolu:
     ```
     Fcm__ServiceAccountJsonPath=/secrets/firebase-service-account.json
     ```
   - Backend `appsettings.{Environment}.json` üzerinden de okunur:
     ```json
     {
       "Fcm": {
         "ServiceAccountJsonPath": "/secrets/firebase-sa.json"
       }
     }
     ```

Backend `DependencyInjection.cs` env veya appsettings okur; credentials varsa `FcmPushChannel` aktif, yoksa `MockPushChannel` (dev için).

## 6. EAS Build (Standalone)

Expo Go ile gerçek push delivery test edemezsin. Standalone build gerekli:

```bash
cd mobile
pnpm eas build --profile development --platform android  # APK
pnpm eas build --profile development --platform ios       # development IPA (TestFlight için preview profile)
```

Build sonrası emulator/device'a yükle, login ol → app `getDevicePushTokenAsync` ile FCM token alır → backend'e `POST /v1/users/me/device-tokens` ile register eder.

## 7. Doğrulama

### Backend tarafı:
```bash
# Token kayıtlı mı?
curl http://localhost:5100/v1/users/me/device-tokens \
  -H "Authorization: Bearer <token>"
```
Şu an query endpoint yok (yazılmamış); gerekirse direkt DB'den kontrol et:
```sql
SELECT user_id, fcm_token, platform, is_active, last_seen_at
FROM device_tokens WHERE user_id = '<id>';
```

### FCM dispatch testi:
1. App'te login ol (token register olur)
2. Bir kuaför hesabıyla başka aygıttan booking confirm et
3. Customer cihazında push bildirimi gelmeli
4. Backend log'larında: `FCM: dispatched 1/1` benzeri

### Stale token cleanup:
FCM `Unregistered` döndürdüğünde `FcmPushChannel.DeactivateInvalidTokensAsync` ilgili row'u `is_active=false` yapar. Sonraki dispatch'te o token'a denenmez.

## 8. Production Checklist

- [ ] Firebase project oluşturuldu (prod + staging ayrı)
- [ ] APNs .p8 key Firebase'e yüklendi
- [ ] `google-services.json` + `GoogleService-Info.plist` repo'ya **eklenmedi** (secrets dir)
- [ ] Service account JSON Hetzner'da `chmod 600` ile depolandı
- [ ] Backend `Fcm__ServiceAccountJsonPath` env var set edildi
- [ ] EAS Build başarılı (development + production profile)
- [ ] iOS Production: APNs production environment seçildi (sandbox değil)
- [ ] Test: gerçek device'da en az 1 booking confirmation push'u alındı

## 9. Bilinen Tuzaklar

- **iOS sandbox vs production**: TestFlight ve App Store farklı APNs ortamları kullanır. Backend tek key ile ikisini de servis eder ama p8 key Apple Developer'da "All apps" scope'unda olmalı.
- **Android v1 API**: Eski FCM legacy API (HTTP, server key) kapalı; v1 API zorunlu (service account + HTTP/2). FirebaseAdmin SDK zaten v1 kullanıyor.
- **Token rotation**: Mobile her app start'ta token'ı tekrar gönderir. Backend `device_tokens` tablosunda eski token'ı bulup refresh eder; user değiştiyse reassign eder.
- **Expo Go**: getDevicePushTokenAsync Expo Go'da çalışmaz. Geliştirme süresince mock fallback yeterli; gerçek delivery testleri standalone build'de.
- **Rate limit**: FCM her sender için ~600/min sınırı (multicast 500 token/batch). Çok büyük campaign'lerde batch logic gerek.

## 10. Bağlantılar

- [Expo Notifications](https://docs.expo.dev/push-notifications/overview/)
- [FirebaseAdmin .NET SDK](https://firebase.google.com/docs/admin/setup)
- [APNs key creation](https://developer.apple.com/help/account/configure-app-capabilities/create-a-pass-type-id/)

---

**Owner action required:** § 1, 2, 3, 5, 6 — founder Firebase + Apple Developer account aktif olduktan sonra.

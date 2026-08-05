# RFC-010 — Misafir Gezinme + Sosyal Giriş (Google/Apple) Genişlemesi

**Status:** ✅ Accepted — founder onayı 2026-08-05
**Tarih:** 2026-08-05
**Kapsam:** `mobile/app/_layout.tsx` (AuthGate), `mobile/app/(auth)/login.tsx`, `mobile/package.json`; backend mevcut kod aktifleştiriliyor — `Kuaforum.Api/Endpoints/AuthEndpoints.cs`, `Kuaforum.Application/Modules/Identity/Auth/OAuthLoginCommand.cs`, `Kuaforum.Infrastructure/Auth/AppleOAuthProvider.cs`, `Kuaforum.Infrastructure/Auth/GoogleOAuthProvider.cs`
**İlgili:** [RFC-005](RFC-005-otp-only-launch.md) (OTP-only launch — bu RFC **revize** ediyor, RFC-005'e superseded notu eklendi), [App Store Review Guideline 4.8](https://developer.apple.com/app-store/review/guidelines/) (RFC-005'te doğrulanmış bulgu, burada yeniden kullanılıyor)

---

## Kararlar (founder, 2026-08-05)

1. **Misafir gezinme:** keşfet / arama / kuaför profili ekranları login zorunlu olmadan görüntülenebilir.
2. **Randevu anında kayıt zorunlu:** randevu oluşturma adımına gelindiğinde login/kayıt şart.
3. **Kayıt yöntemleri genişliyor:** telefon-OTP + Google + Apple — üçü birlikte sunulur. iOS'ta Google sunulduğu için Apple, App Store Guideline 4.8 gereği **zorunlu** (RFC-005'in doğrulanmış bulgusu, aşağıda).

## RFC-005 ile ilişki

RFC-005'in "OTP-only launch" kararını **revize eder** — kayıt yöntemi tekelini kaldırır, OTP'yi üç yöntemden biri olarak **korur**. RFC-005 dosyasına şu not eklendi: *"Superseded by RFC-010 (kayıt yöntemleri genişledi); OTP korunuyor."*

RFC-005'in Guideline 4.8 bulgusu hâlâ geçerli ve bu RFC'nin doğrudan dayanağı: RFC-005 zaten öngörmüştü (§How to Apply madde 3) — *"V1.5'te OAuth eklenirken... Google Sign-In eklenecekse aynı sürümde Apple Sign-In de eklenmeli."* Bu RFC o öngörüyü uyguluyor; V1.5 beklemeden, launch'ta.

## Tasarım

### Backend — değişiklik yok, mevcut kod aktifleştiriliyor

- `POST /v1/auth/oauth/{provider}` zaten tam implement + `AllowAnonymous` (`AuthEndpoints.cs:41-43`), `provider ∈ {apple, google}` doğrulaması `OAuthLoginCommandValidator`'da mevcut. `OAuthLoginCommandHandler`, OTP akışıyla **aynı** response tipini (`VerifyOtpResponse`) döner — mobile'daki token/refresh/hydrate zinciri **değişmeden** çalışır.
- **Telefon numarası OAuth'ta zorunlu değil:** `User.CreateCustomerByOAuth` yalnızca `Email` / `DisplayName` / `Role` / `Status` set eder; `User.PhoneE164` nullable (`User.cs:14`) ve OAuth akışında hiç atanmaz. Sonuç: NetGSM/SMS bağımlılığı yalnızca OTP tercih eden kullanıcılar + SMS bildirimleri için kalır — OAuth ile kayıt, SMS altyapısına bağlı değil.
- **Implementasyon notu:** telefon-siz OAuth kullanıcısı SMS-bazlı bildirim alamaz. Booking bildirim kanalının (push/FCM mi, SMS mi birincil) bu senaryoda nasıl davrandığı implementasyon planında ayrıca doğrulanmalı — bu RFC bildirim kanalı seçimini değiştirmiyor, yalnızca telefon-siz kullanıcı senaryosunun artık var olacağını not düşüyor.

### Mobile

- **AuthGate revizyonu** (`app/_layout.tsx:18-71`): bugün **tek global redirect** — `(auth)` ve legal route'ları hariç her route login gerektiriyor (satır 45-48: `if (!isAuthenticated) { if (!inAuthGroup && !inLegalRoute) router.replace('/(auth)/login'); return; }`). Ekranların kendi auth kontrolü yok — `(tabs)/search.tsx`, `barber/[id].tsx`, `booking/confirm.tsx` hiçbiri `useAuthStore`/`isAuthenticated` okumuyor, tamamen global gate'e güveniyorlar. Revize: misafirin erişebileceği route'lar listesine `(tabs)` (keşfet/arama/profil-görüntüleme) ve `barber/[id]` (kuaför profili) eklenir; korumalı kalan: `booking/*` (oluşturma + detay), `(barber)/*` (owner paneli), profil-yazma aksiyonları. `Why:` mekanizma tek bir route-grubu allow-list'i olduğu için mimari değişmiyor, yalnızca hariç tutulan liste büyüyor — ekran bazlı yeni guard kodu yazmaya gerek yok.
- **Korumalı aksiyon tetikleyici** *(implementasyonda revize edildi, 2026-08-05)*: ayrı bir `AuthPrompt` bileşeni eklendi — korumalı sekmeler (bookings/notifications/profile) ve `booking/confirm` misafire açıklamalı "giriş yap" kartı gösterir; login'e `redirectTo` parametresi taşınır (open-redirect'e karşı `sanitizeRedirectTo` ile yalnız iç path kabul edilir). İlk taslaktaki "modal gerekmez, sadece allow-list daralır" yaklaşımı, seçim kaybı UX'i nedeniyle terk edildi. `[ara karar]` Founder'ın bahsettiği "korumalı aksiyonlar (randevu/favori/profil)" ifadesindeki **favori özelliği kodda mevcut değil** (`grep -rln "favorite\|Favorite" mobile/` → 0 sonuç) — bu RFC favori eklemiyor; ileride eklenirse aynı allow-list mantığına girer, ayrı bir karar gerekmez.
- **Login ekranı** (`app/(auth)/login.tsx`): telefon input + "Kod gönder" altına "Apple ile devam et" (yalnız iOS) / "Google ile devam et" (iOS + Android) butonları. Yeni bağımlılıklar — hiçbiri bugün `package.json`'da yok (`grep -iE "expo-apple|google-signin|expo-auth-session" mobile/package.json` → 0 sonuç): `expo-apple-authentication` (iOS native modül, Expo config plugin + Apple Developer "Sign in with Apple" capability gerektirir) + `@react-native-google-signin/google-signin`. Bu iki paket ismi RFC-005'in 2026-07-23 tarihli araştırmasından taşınıyor, bu RFC'de yeniden doğrulanmadı — implementasyon öncesi context7/güncel doküman kontrolü önerilir. Başarılı OAuth → `AuthApi.oauthLogin(provider, idToken, deviceFingerprint)` → mevcut `hydrate()` + `resolvePostAuthRoute()` zinciri (RFC-008'in consent-gate helper'ı) **aynen** çalışır — OAuth kullanıcısı `RequiresTosConsent` ise `/consent`'e düşer, ek kod gerekmez.
- Hata/iptal: kullanıcı OAuth popup'ını kapatırsa sessiz iptal (Alert gösterilmez) — native SDK'nın "user cancelled" hata koduyla `extractApiError` tetiklenmemeli (OTP akışındaki hata gösterim deseninden bilinçli sapma).

### Test

- Integration: OAuth handler testlerinin mevcut kapsamı doğrulanmalı (varsa genişletilir, yoksa eklenir) — yeni kullanıcı oluşturma (`Phone == null`), var olan e-posta ile eşleşme, geçersiz `id_token` reddi.
- Mobile: AuthGate allow-list genişlemesi jest testi (misafir `(tabs)` ve `barber/[id]`'e erişebilir; `booking/confirm`'e erişemez, login'e düşer); OAuth buton happy-path mock testi.

## Why

Misafir gezinme, kayıt-önce-göz-at sürtünmesini kaldırıyor — pazaryeri ürünlerinde standart, TR kuaför pazarında güven eşiği yüksek bir kategori için özellikle değerli (önce salonu/fiyatı gör, sonra kaydol). Sosyal giriş, telefon+OTP'yi tek kanal olmaktan çıkararak NetGSM'e bağımlılığı azaltıyor ve Apple/Google alışkanlığı olan kullanıcıyı kaybetmiyor; RFC-005'in "ölü kod değil, kullanılmayan yatırım" diye işaretlediği backend'i devreye sokuyor — sıfırdan yazım yok, yalnızca mobile bağlantısı.

## How to apply

Sıra: mobile AuthGate route allow-list güncelleme (kod-only, backend dokunulmuyor) → OAuth SDK paket kurulumu + config plugin (iOS "Sign in with Apple" capability, Android Google Services) → `login.tsx` buton + `AuthApi.oauthLogin` bağlama → hydrate/consent-gate regresyon testi → App Store/Play Store submission öncesi Guideline 4.8 checklist'i (Apple ve Google aynı sürümde birlikte).

## Founder Aksiyonları (bloklayıcı)

| Aksiyon | Neden gerekli |
|---|---|
| Google Cloud Console'da OAuth 2.0 Client ID (iOS + Android + Web) oluşturma | `@react-native-google-signin` client ID olmadan çalışmaz |
| Apple Developer hesabında "Sign in with Apple" capability + Service ID tanımlama | `expo-apple-authentication`, App Store submission'ında bu capability'yi ister |
| Firebase/Google Cloud proje kurulumunun Google Sign-In için zorunlu olup olmadığının netleştirilmesi `[unverified 2026-08-05 — needs: seçilen paketin (expo-auth-session vs. @react-native-google-signin) Firebase bağımlılığı implementasyon öncesi teyit edilmeli]` | SDK seçimi Firebase gerektirebilir |
| App Store / Play Store review sürecinde Guideline 4.8 checklist'inin submission sorumlusu tarafından imzalanması | RFC-005 bulgusu — Apple/Google birlikte sunulmalı, aksi 4.8 ihlali riski |

## Bilinçli dışarıda bırakılanlar

Favori özelliği (kodda yok, bu RFC eklemiyor — founder'ın bahsettiği kapsamda ama ayrı bir karar/iş) · ~~misafirken seçilen slot/taslak randevunun login sonrası korunması~~ *(implementasyonda kapsam İÇİNE alındı, 2026-08-05: seçimler `redirectTo` query'siyle taşınıyor — yeni persistence katmanı olmadan, mevcut confirm param şemasının yeniden serileştirilmesiyle)* · Facebook veya başka sosyal login sağlayıcıları (talep yok) · web landing'de misafir deneyimi ([RFC-011](RFC-011-web-landing-barber-profiles.md)'in kapsamı, ayrı doküman).

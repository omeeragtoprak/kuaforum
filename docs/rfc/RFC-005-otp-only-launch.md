# RFC-005 — MVP Launch'ta Yalnız Telefon+OTP Girişi (Apple/Google OAuth V1.5'e Erteleme)

**Status:** ✅ **Accepted — founder onayı 2026-07-23.** MVP launch girişi yalnız telefon+OTP; Apple/Google OAuth mobile bağlantısı V1.5'a ertelendi (backend OAuth kodu zararsız şekilde bekler).
**Tarih:** 2026-07-23
**Kapsam:** Mobile auth akışı (`mobile/app/(auth)/login.tsx`), backend OAuth endpoint'i (kod değişikliği önerilmiyor — mevcut haliyle kalması öneriliyor)

---

## Öneri (henüz "Karar" değil)

MVP launch'ta müşteri/kuaför mobile girişi **yalnızca telefon + OTP** (NetGSM) ile yapılsın; Apple/Google OAuth backend'de implement edilmiş haliyle **dursun ama mobile'da hiç UI'a bağlanmasın**, entegrasyon V1.5'e ertelensin.

## Bağlam

- **Master spec'in kilitli kararı, bu öneriyle çelişiyor:** § 2 karar #15 (M1 Auth kapsamı), satır 82 ve 157: "M1 Auth (telefon + e-posta, OTP, **Apple/Google OAuth**, JWT + refresh)"; "Auth: ASP.NET Identity + JWT + Refresh Token · **OAuth (Apple, Google)** · OTP (NetGSM)". OAuth, MVP kapsamının **içinde**, "sonra eklenecek" değil. Bu RFC, onaylanırsa master spec'i değiştiren bir karar olur — retrospektif değil, ileriye dönük bir kapsam değişikliği önerisi.
- **Fiili durum (kod kanıtı):**
  - Backend: `POST /v1/auth/oauth/{provider}` endpoint'i tam implement — `AuthEndpoints.cs:41-44,85-96`, `OAuthLoginCommand.cs`, `AppleOAuthProvider.cs` (Apple JWKS doğrulama), `GoogleOAuthProvider.cs` (Google JWKS doğrulama). Bu **ölü kod değil, çalışan kod** — yalnızca hiçbir client tarafından çağrılmıyor.
  - Mobile: `grep -rni "oauth\|apple-authentication\|google-signin\|expo-apple\|expo-auth-session" mobile/app mobile/src mobile/package.json` → **0 sonuç.** `mobile/app/(auth)/login.tsx` yalnızca telefon numarası input'u + "Kod gönder" butonu + (dev-only) rol bazlı hızlı giriş butonları içeriyor. Hiçbir Apple/Google giriş butonu yok, `expo-apple-authentication` veya bir Google Sign-In paketi `package.json`'da yok.
  - Sonuç: backend'deki OAuth kodu şu an **erişilemez, zararsız ölü kod** — güvenlik riski yaratmıyor (kimse çağırmıyor), ama MVP kapsamının bir parçası olarak "bitti" sayılamaz çünkü kullanıcıya hiç sunulmuyor.

## Apple App Store Guideline 4.8 — Doğrulanmış Bulgu

Founder brief'inde bu konu `[unverified — needs check]` olarak işaretlenmişti; doğrulama yapıldı, **güncel kaynaktan teyit edildi**:

Apple'ın resmi App Store Review Guidelines'ından (fetch tarihi: 2026-07-23) [Guideline 4.8 — Login Services](https://developer.apple.com/app-store/review/guidelines/):

> "Apps that use a third-party or social login service (such as Facebook Login, Google Sign-In, Log in with X, Sign In with LinkedIn, Login with Amazon, or WeChat Login) to set up or authenticate the user's primary account with the app must also offer as an equivalent option another login service..."
>
> **Another login service is not required if:**
> - **"Your app exclusively uses your company's own account setup and sign-in systems."**

**Sonuç:** Kuaforum'un telefon+OTP girişi (NetGSM SMS gönderimi dış servis olsa da, hesap oluşturma/doğrulama akışı Kuaforum'un kendi sistemi) bu istisna kapsamına giriyor — **guideline 4.8, OTP-only bir girişte tetiklenmiyor.** "Sign in with Apple zorunlu" riski yalnızca Kuaforum **başka bir 3. parti/sosyal login (ör. yalnızca Google Sign-In) sunar ama Apple'ı sunmazsa** ortaya çıkar. Şu anki fiili durumda (mobile'da hiçbir OAuth butonu yok) risk **sıfır**. Bu RFC onaylanıp OAuth kalıcı olarak V1.5'e ertelense bile bu bulgu geçerliliğini korur; yalnızca V1.5'te Google OAuth eklenirken Apple OAuth'un da **aynı anda** eklenmesi gerektiği anlamına gelir (aşağıda How to Apply'da not düşüldü).

Bu artık `[unverified]` değil — kaynak birincil (developer.apple.com), güncel. Tek kalan belirsizlik: Apple'ın "own account setup and sign-in systems" ifadesini SMS-OTP'ye nasıl uyguladığına dair kendi geçmiş review kararlarında istisnai yorumlar olabilir; bu RFC'nin bulgusu metne dayalı, App Review'un vaka bazlı (case-by-case) davranışını garanti etmez `[unverified 2026-07-23 — needs: gerçek submission'da App Review'un rejection geçmişi/emsal taraması]`.

## Alternatifler ve Trade-off

| Alternatif | Artı | Eksi |
|---|---|---|
| **A — OTP-only launch (bu RFC'nin önerisi)** | Mobile geliştirme yükü azalır (OAuth SDK entegrasyonu, buton UI, hata durumları yok); guideline 4.8 tetiklenmiyor (yukarıda doğrulandı); backend zaten hazır, V1.5'te yalnızca mobile tarafı eklenir. Türk kullanıcı segmentinde telefon numarası zaten birincil kimlik (bankacılık/e-devlet alışkanlığı) — OAuth'un kritik olmama ihtimali yüksek `[unverified 2026-07-23 — needs: customer discovery sprint bulgusu, henüz yapılmadı bkz. RFC-004]`. | Master spec'in kilitli MVP kapsamını (§2 karar #15) daraltıyor — founder onayı **şart**, bu RFC onaysız kod'a yansıtılmamalı. Bazı kullanıcılar (özellikle iOS, gençler) "Apple/Google ile devam et" alışkanlığında olabilir — dönüşüm oranı etkisi ölçülmedi. |
| **B — OAuth'u şimdi bağla (mobile SDK entegrasyonu)** | Master spec kararına sadık kalır; launch'tan itibaren tam kapsam. | Tahmini **2-3 gün** ek iş: `expo-apple-authentication` (iOS native modül, config plugin + Apple Developer hesabında Sign in with Apple capability) + bir Google Sign-In paketi (`@react-native-google-signin/google-signin` veya `expo-auth-session` + Google OAuth client) kurulumu, iki platformda test, hata/iptal durumları (kullanıcı OAuth popup'ı kapatırsa vb.), buton UI + i18n metinleri. Solo founder kapasitesinde launch öncesi başka önceliklerle (ör. RFC-001'in .NET yükseltme borcu) çakışabilir. |
| **C — Yalnız Apple OAuth ekle, Google'ı ertele** | iOS App Store'da guideline 4.8 riskini tamamen ortadan kaldırır (zaten kendi sistemin var, ekstra gerek yok) ama "Apple ile devam et" isteyen kullanıcıyı karşılar. | Yarım iş — Android/Google kullanıcıları için OAuth yok, tutarsız platform deneyimi. Gerekçesi zayıf: madem guideline 4.8 zaten tetiklenmiyor (own account system istisnası), yalnızca Apple eklemenin ek değeri sınırlı. |

## Why

Backend OAuth kodu zaten üretim kalitesinde yazılmış (JWKS doğrulama, id_token verification) — bu bir eksiklik değil, bir **kullanılmayan yatırım**. Mobile tarafında OAuth UI'ının hiç bağlanmamış olması, ya bilinçli bir "MVP'yi küçült" kararı ya da basitçe "sıra gelmedi" durumu olabilir — kod bunu ayırt ettirmiyor. CLAUDE.md § 13 push-back kuralı #1 ("MVP scope tehlikeli büyürse → 'şu feature'ı V1.5'a alalım' öner") bu RFC'nin gerekçesi: telefon+OTP, Türk pazarında birincil kimlik doğrulama kanalı olduğu için (bankacılık/e-devlet paraleli — bu iddia şu an `[unverified]`, customer discovery sprint'i bekliyor), OAuth'suz launch teknik risk taşımıyor gibi görünüyor; ama bu bir **ürün/GTM kararı**, mühendislik kararı değil — founder onayı olmadan mobile'da "kalıcı olarak yok" diye işaretlenmemeli.

## How to Apply

**Bu RFC onaylanmadan hiçbir kod değişikliği yapılmamalı** — CLAUDE.md § "Push-Back Discipline": "Şu kararı sen verdin, founder onaylamamış → onay alıncaya kadar yazma" kuralı burada doğrudan geçerli.

Onaylanırsa:
1. `docs/PHASE-0-MASTER-SPEC.md` § 2 karar #15 (satır 82, 157) güncellenmeli: "OTP-only launch (bkz. RFC-005); Apple/Google OAuth backend hazır, mobile entegrasyonu V1.5" notu eklenmeli.
2. `docs/04-modules/01-auth.md`'de M1 Auth spec'i güncellenmeli — OAuth'un "implement edildi ama launch'ta expose edilmiyor" statüsü net yazılmalı (test-engineer/security-auditor'ın "neden test edilmiyor" sorusuna önceden cevap).
3. **V1.5'te OAuth eklenirken:** guideline 4.8 gereği, eğer Google Sign-In eklenecekse **aynı sürümde Apple Sign-In de eklenmeli** (yalnız Google eklemek 4.8'i tetikler — "own account system" istisnası artık geçerli olmaz çünkü tek başına OTP değil, 3. parti login de sunuluyor olur). Bu, V1.5 planlamasında bir bağımlılık olarak not düşülmeli.
4. Onaylanmazsa (Alternatif B seçilirse): mobile-developer agent'a `expo-apple-authentication` + Google Sign-In entegrasyonu task'ı açılır, tahmini 2-3 gün.

## Geri Dönüş Yolu (Revert Path)

Bu RFC bir kod değişikliği önermiyor (backend zaten hazır, mobile zaten OAuth'suz) — "revert" kavramı burada geçerli değil. Onaylanırsa geri dönüş, basitçe V1.5'te mobile OAuth entegrasyonunu yapmaktan ibaret (Alternatif B'nin işi) — herhangi bir "sökme" gerekmiyor.

## Etkilenen Dosyalar

Mevcut (değişiklik önerilmiyor, yalnızca durum tespiti):
```
backend/src/Kuaforum.Api/Endpoints/AuthEndpoints.cs                    (satır 41-44, 85-96 — POST /v1/auth/oauth/{provider}, çalışır durumda)
backend/src/Kuaforum.Application/Modules/Identity/Auth/OAuthLoginCommand.cs
backend/src/Kuaforum.Infrastructure/Auth/AppleOAuthProvider.cs         (JWKS doğrulama, tam implement)
backend/src/Kuaforum.Infrastructure/Auth/GoogleOAuthProvider.cs        (JWKS doğrulama, tam implement)
mobile/app/(auth)/login.tsx                                             (yalnızca telefon+OTP UI, OAuth butonu yok)
mobile/package.json                                                     (expo-apple-authentication / google-signin paketleri yok)
```

Onaylanırsa güncellenmesi gereken (bu RFC'nin kapsamında değişmedi):
```
docs/PHASE-0-MASTER-SPEC.md          (§ 2 karar #15, satır 82, 157)
docs/04-modules/01-auth.md           (M1 Auth spec — OAuth statüsü)
```

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md) § 2 karar #15
- [`docs/04-modules/01-auth.md`](../04-modules/01-auth.md)
- [RFC-004 — Erzurum pilot](RFC-004-erzurum-pilot.md) — customer discovery sprint henüz çalıştırılmadı, "telefon Türk pazarında birincil kimlik" iddiası da aynı sprint çıktısını bekliyor
- [App Store Review Guidelines — Guideline 4.8 Login Services](https://developer.apple.com/app-store/review/guidelines/)

---

Son güncelleme: 2026-07-23

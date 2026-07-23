# RFC-003 — JWT İmzalama: RS256 Yerine HS256

**Status:** Accepted (retrospektif — founder imzası bekliyor)
**Tarih:** 2026-07-23 (RFC yazım tarihi; kod değişikliği tarihi tespit edilemiyor)
**Kapsam:** `backend/src/Kuaforum.Infrastructure/Auth/JwtTokenService.cs`, `JwtOptions.cs`

---

## Karar

Access JWT imzalama algoritması, birden fazla dokümanda (master spec dahil) kilitlenen **RS256** (asimetrik, RSA imzalı) yerine **HS256** (simetrik, HMAC-SHA256) olarak implement edildi.

Koddan doğrulama — `backend/src/Kuaforum.Infrastructure/Auth/JwtTokenService.cs:33-34`:
```csharp
var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
var creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
```
`SymmetricSecurityKey` + `HmacSha256` — bu literal olarak HS256, RS256 değil. `JwtOptions.cs`'te de yalnızca tek bir `SigningKey` (string) alanı var; RS256 için gerekli olan public/private key çifti (`RsaSecurityKey`) hiç modellenmemiş.

## Bağlam

- **Kilitli karar, birden fazla dokümanda RS256:**
  - `docs/04-modules/01-auth.md:125` — "JWT signing | RS256, key rotation 90 günde 1 (V1.5)"
  - `docs/04-modules/01-auth.md:353` — "JwtTokenService (RS256 ile dev anahtarı)"
  - `docs/07-api/01-conventions.md:141` — "JWT bearer (RS256, 15dk)"
  - `docs/02-requirements/03-non-functional.md:44` — "Auth: JWT RS256 + refresh device-bound + OTP rate limit"
  - `docs/08-security/01-auth-overview.md:44` — "Access JWT (RS256) | 15 dk | SecureStore | Otomatik refresh"
  - CLAUDE.md § 8 (Güvenlik Standartları): "Token: JWT (**RS256**), refresh server-side rotated, device-bound..."
- **Fiili implementasyon HS256:** yukarıdaki kod alıntısı. Bu beş ayrı doküman kararının hiçbiri güncellenmedi; kod ile spec arasında **beş yerde** çelişki var.
- **OAuth doğrulama tarafı RS256 kullanıyor (tutarsız iç mimari):** `AppleOAuthProvider.cs` ve `GoogleOAuthProvider.cs`, Apple/Google'ın JWKS endpoint'lerinden (`https://appleid.apple.com/auth/keys`, `https://www.googleapis.com/oauth2/v3/certs`) aldıkları **asimetrik** `JsonWebKey` ile id_token doğruluyor — yani sistem zaten RS256/JWKS doğrulama akışını biliyor ve kullanıyor, ama kendi ürettiği (issue ettiği) access token için simetrik anahtara düştü. Bu, "RS256'yı implement edemedik" değil, "kendi token'ımız için bilinçli/pragmatik bir kısayol seçildi" izlenimi veriyor — ama RFC'siz.
- **Secret yönetimi mevcut durumu:**
  - `appsettings.Development.json`: `"SigningKey": "dev-only-jwt-signing-key-change-in-production-min-32-bytes"` — 58 ASCII karakter = 464 bit. HS256 için RFC 7518 § 3.2 minimum gereksinimi ("A key of the same size as the hash output (for instance, 256 bits for 'HS256') or larger MUST be used" — [RFC 7518](https://www.rfc-editor.org/rfc/rfc7518.html)) **karşılanıyor** (464 > 256 bit).
  - `appsettings.Production.json`: `"SigningKey": "${JWT_SIGNING_KEY}"` — zaten env variable ile dışarıdan enjekte ediliyor, secret repo'da yok. Bu kısım CLAUDE.md § 8 ("Secrets asla repo'da") ile **uyumlu**, düzeltme gerektirmiyor.
  - Anahtar rotasyonu (`docs/04-modules/01-auth.md:125`'te "90 günde 1, V1.5" olarak planlanmış) şu an ne RS256 ne HS256 için implement edilmiş değil — bu RFC'nin kapsamı dışında, ayrı bir backlog maddesi.

## Alternatifler ve Trade-off

| Alternatif | Artı | Eksi |
|---|---|---|
| **A — RS256'ya dön (master spec kararı)** | Public key dağıtılabilir — token'ı doğrulayan servis, imzalayan servisle aynı secret'a sahip olmak zorunda değil. Gelecekte 3. parti doğrulayıcı (mikroservis, partner entegrasyonu) eklenirse anahtar paylaşımı riski yok. | Modular monolith + tek API host mimarisinde (master spec § 2 karar #6) şu an **hiçbir doğrulayan, imzalayanın dışında değil** — kazanç teorik. RSA anahtar çifti üretimi/saklama/rotasyon operasyonel ek iş. |
| **B — HS256'da kal (mevcut, fiili karar)** | Basit: tek secret, key management yükü yok. Tek servis/tek API host senaryosunda güvenlik açığı yaratmıyor — imzalayan ve doğrulayan aynı process/secret'ı paylaşıyor zaten. | V2'de mikroservis ayrıştırması veya 3. parti token doğrulama ihtiyacı doğarsa, HS256 secret'ının her doğrulayan tarafa dağıtılması gerekir — bu, secret sızıntı yüzeyini büyütür (RS256'da yalnızca public key dağıtılırdı). |
| **C — RS256 + JWKS endpoint (hibrit, V2 hazırlığı)** | Mobile/web-admin, backend'in `/.well-known/jwks.json` endpoint'inden public key çekip token doğrulayabilir — Apple/Google entegrasyonuyla tutarlı desen. | Şimdi hiçbir client kendi başına JWT doğrulamıyor (hepsi backend'e sorup RequireAuthorization middleware'ine güveniyor) — bu yüzden MVP'de gerekçesi zayıf. |

## Why

Modular monolith mimarisinde (tek API host, master spec § 2 karar #6) token'ı imzalayan ve doğrulayan aynı process içinde — bu senaryoda HS256, RS256'ya göre güvenlik açısından **eşdeğer** ve operasyonel olarak daha basit; bu meşru bir mühendislik tercihi. Ama beş ayrı dokümanda RS256 olarak kilitlenmiş bir kararı sessizce değiştirmek, güvenlik mimarisi kararlarının izlenebilirliğini kırıyor — özellikle CLAUDE.md § 8'in "Token: JWT (RS256)" maddesi güvenlik standardı olarak listelenmişken. Founder onayı olmadan güvenlik kararı değiştirilmemeli (CLAUDE.md § 13, push-back kuralı #2: "Compliance atlanırsa → bu yasal/finansal risk, şöyle ele alalım diye yaz").

## How to Apply

1. Founder HS256'yı MVP için onaylarsa: beş dokümandaki "RS256" referansları güncellenmeli — `docs/04-modules/01-auth.md:125,353`, `docs/07-api/01-conventions.md:141`, `docs/02-requirements/03-non-functional.md:44`, `docs/08-security/01-auth-overview.md:44` → "HS256 (bkz. RFC-003; tek-host modular monolith'te RS256'ya eşdeğer; mikroservis ayrıştırmasında RS256'ya geçiş planlı)". CLAUDE.md § 8 de aynı şekilde güncellenmeli (bu görev CLAUDE.md güncellemesini işaret eder, fiili değişikliği bu RFC yapmaz — CLAUDE.md ayrı bir iş kapsamında).
2. **RS256'ya geçiş tetikleyicileri** (V2/mikroservis hazırlığı, founder onayı gerekir, bağlayıcı değil):
   - Modular monolith'ten bir modülün ayrı bir servise/pod'a çıkarılması (extract) planlandığında **VEYA**
   - 3. parti bir entegrasyon (partner API, ayrı bir mobil/masaüstü client) kendi başına token doğrulama ihtiyacı duyduğunda.
3. **Secret yönetimi — mevcut durum yeterli, ek iş gerekmiyor:** dev key 464 bit (RFC 7518 minimumu 256 bit'in üzerinde), prod key env variable ile enjekte ediliyor. Tek eksik: **anahtar rotasyonu** implement edilmemiş — `docs/04-modules/01-auth.md:125`'teki "90 günde 1, V1.5" planı hâlâ geçerli, bu RFC'yi etkilemiyor, ayrı backlog maddesi olarak kalmalı.
4. RS256'ya geçilirse gerekenler (yalnızca alternatif A seçilirse): `JwtOptions` genişletilip `RsaPrivateKey`/`RsaPublicKey` (veya PEM dosya yolu) eklenir, `JwtTokenService.Issue()` içinde `SymmetricSecurityKey` → `RsaSecurityKey`, `HmacSha256` → `RsaSha256` değişir; `Program.cs`'teki `AddJwtBearer` doğrulama tarafı da simetrik→asimetrik anahtara göre güncellenir.

## Geri Dönüş Yolu (Revert Path)

`JwtTokenService.cs:33-34`'te `SymmetricSecurityKey`/`HmacSha256` → `RsaSecurityKey`/`RsaSha256Signature`; `JwtOptions.cs`'e RSA anahtar çifti alanları eklenir; `Program.cs`'teki JWT bearer authentication ayarı (`TokenValidationParameters.IssuerSigningKey`) simetrik key yerine public `RsaSecurityKey` bekleyecek şekilde güncellenir. Prod'da yeni bir RSA anahtar çifti üretilip `JWT_SIGNING_KEY` env değişkeni yerine `JWT_RSA_PRIVATE_KEY`/`JWT_RSA_PUBLIC_KEY` gibi ikiliye geçilmesi gerekir — **bu geçiş sırasında eski HS256 ile imzalanmış tüm aktif access token'lar (max 15 dk ömürlü) geçersiz olur**, ama refresh token'lar etkilenmez (ayrı bir mekanizma, `HashRefreshToken` SHA-256 hash'i JWT algoritmasından bağımsız).

## Etkilenen Dosyalar

```
backend/src/Kuaforum.Infrastructure/Auth/JwtTokenService.cs   (satır 33-34 — SymmetricSecurityKey + HmacSha256)
backend/src/Kuaforum.Infrastructure/Auth/JwtOptions.cs        (yalnızca tek SigningKey alanı, RSA çifti yok)
backend/src/Kuaforum.Api/appsettings.json                     (dev SigningKey, düz metin — dev-only, kabul edilebilir)
backend/src/Kuaforum.Api/appsettings.Production.json          (SigningKey env variable — zaten doğru pattern)
```

Karşılaştırma için (RS256/JWKS zaten kullanılan yerler, değişmiyor):
```
backend/src/Kuaforum.Infrastructure/Auth/AppleOAuthProvider.cs   (JWKS ile RS256 id_token doğrulama)
backend/src/Kuaforum.Infrastructure/Auth/GoogleOAuthProvider.cs  (JWKS ile RS256 id_token doğrulama)
```

## Bağlantılar

- [`docs/04-modules/01-auth.md`](../04-modules/01-auth.md) § 5 (satır 125), § "Infrastructure implementations" (satır 353)
- [`docs/07-api/01-conventions.md`](../07-api/01-conventions.md) (satır 141)
- [`docs/02-requirements/03-non-functional.md`](../02-requirements/03-non-functional.md) (satır 44)
- [`docs/08-security/01-auth-overview.md`](../08-security/01-auth-overview.md) (satır 44)
- [`docs/GAP-ANALYSIS.md`](../GAP-ANALYSIS.md) — `JwtTokenService.cs:34` referanslı tespit
- [RFC 7518 — JSON Web Algorithms, § 3.2 (HMAC minimum key size)](https://www.rfc-editor.org/rfc/rfc7518.html)
- [OWASP JSON Web Token Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html)

---

Son güncelleme: 2026-07-23

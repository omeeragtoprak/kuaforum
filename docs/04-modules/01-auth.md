# M1 — Auth & Authorization

> Phase 1 ilk modülü. Tüm sonraki modüller (User, Barber, Booking, Payment...) bu modüldeki kimliğe bağımlı.
> **Sürüm:** 0.1.0 (taslak — implementation öncesi review)
> **Bağımlılık:** Yok (bütün diğer modüllerin temeli).

---

## 1. Amaç

Kuaforum'un tüm kullanıcılarının (Customer, BarberOwner, Stylist, Admin) güvenli kayıt, kimlik doğrulama, oturum yönetimi, yetkilendirme akışlarını sağlar. KVKK aydınlatma + parçalı açık rıza day-1 dahil.

## 2. Stakeholder'lar

| Rol | İlgi |
|---|---|
| Customer | Kayıt, OTP/OAuth login, profil, hesap silme (KVKK) |
| BarberOwner | Kayıt + telefon doğrulama (sonraki M4'te işletme ekleme) |
| Stylist | BarberOwner tarafından davet → kayıt |
| Admin | E-posta + parola + TOTP login, RBAC ile tüm yetkilere erişim |

## 3. User Stories

### Customer
- US-1.1: Bir Customer telefon numarası ve OTP ile **kayıt olur** ve giriş yapar.
- US-1.2: Bir Customer Apple veya Google ile **OAuth login** yapar.
- US-1.3: Bir Customer **çıkış yapar**, refresh token sunucuda iptal olur.
- US-1.4: Bir Customer **çoklu cihaz** desteği ile birden fazla aktif oturum tutabilir.
- US-1.5: Bir Customer **hesabını siler** (KVKK m.7 — silme hakkı). 30 gün soft delete + hard delete.
- US-1.6: Bir Customer **parçalı açık rıza** verir (zorunlu sözleşme + opsiyonel marketing/lokasyon/sağlık).
- US-1.7: Bir Customer **telefon numarasını değiştirir** (yeniden OTP doğrulama).

### BarberOwner
- US-1.8: BarberOwner Customer ile aynı OTP akışıyla kayıt olur, sonra "kuaför hesabı" başlatır (M4'te detay).

### Admin
- US-1.9: Admin **e-posta + parola + TOTP** ile login. IP whitelist (Cloudflare Access) + 2FA.
- US-1.10: Admin **kullanıcıyı askıya alır / engeller**.

### Cross-cutting
- US-1.11: Tüm rollerde **JWT (15dk) + refresh (30g, device-bound, rotated)**.
- US-1.12: Tüm rollerde **rate limit** (OTP send 3/saat, login 5/dk).
- US-1.13: **Audit log**: kayıt, login, logout, hesap silme, rol değişikliği, parola sıfırlama.

## 4. Functional Requirements

### FR-1 — Telefon + OTP Kayıt
1. POST `/v1/auth/otp/send`
   - Body: `{ phoneE164: "+905551234567", purpose: "register" | "login" }`
   - Validation: TR mobil format (`+90 5XX...`), purpose enum.
   - Rate limit: 3 OTP/saat per telefon.
   - NetGSM SMS, 6 haneli kod, TTL 5 dakika.
   - Response 200: `{ otpId, expiresAt }`. Kod response'a girmez.
2. POST `/v1/auth/otp/verify`
   - Body: `{ otpId, code, deviceFingerprint, deviceName }`
   - Validation: kod 6 hane, deviceFingerprint UUID v7.
   - Yanlış kod → counter +1; 5 hatadan sonra OTP iptal.
   - Doğru kod + telefon kayıtlı değil → kayıt akışı (yeni User oluşur, role: Customer).
   - Doğru kod + telefon kayıtlı → login akışı.
   - Response 200: `{ accessToken, refreshToken, expiresIn, user: { id, phoneE164, displayName?, role } }`.

### FR-2 — OAuth (Apple, Google)
1. POST `/v1/auth/oauth/{provider}`
   - Body: `{ idToken, deviceFingerprint, deviceName }`
   - Provider verify (signature + issuer).
   - E-posta resolve → mevcut hesap → login; yok → yeni hesap.
   - Telefon doğrulanmamış kayıt için sonra "telefonunu doğrula" akışı.
   - Response 200: aynı format.

### FR-3 — Admin Login
1. POST `/v1/auth/admin/login`
   - Body: `{ email, password, totpCode }`
   - ASP.NET Identity verify password (PBKDF2 high iterations).
   - TOTP doğrulama (Authenticator).
   - IP whitelist Cloudflare Access (uygulama dışı).
   - Response 200: aynı format + admin scopes.

### FR-4 — Refresh Token
1. POST `/v1/auth/refresh`
   - Body: `{ refreshToken, deviceFingerprint }`
   - Token bul, expiry kontrol (server-side stored), device match.
   - Reuse detection: token zaten kullanılmışsa **tüm cihazları logout**.
   - Yeni access + yeni refresh (rotation).
2. POST `/v1/auth/logout`
   - Body: `{ refreshToken }`
   - Token revoke.
3. POST `/v1/auth/logout/all`
   - Header: Authorization: Bearer <accessToken>
   - User'ın tüm refresh token'ları revoke.

### FR-5 — Hesap Silme (KVKK)
1. DELETE `/v1/users/me`
   - Soft delete: `deletedAt = now`. 30 gün sonra hard delete.
   - Tüm refresh token revoke + tüm aktif oturumlar düşer.
   - PII anonimleştirme (telefon `+90 000 000 00 00`, e-posta hash).
   - **30 gün içinde geri yükleme talebi:** `POST /v1/users/me/restore` — sadece soft delete döneminde.
   - Hard delete background job: günlük çalışır, `deletedAt + 30g < now` olanları kesin siler (audit log kalır).

### FR-6 — Açık Rıza (KVKK)
- Kayıt sırasında **4 ayrı toggle**:
  1. Aydınlatma metni + sözleşme (zorunlu).
  2. Marketing iletişimi (opsiyonel).
  3. Konum (opsiyonel).
  4. Sağlık verisi işleme (opsiyonel).
- Her toggle **ayrı `Consent` kaydı**: `userId`, `purpose`, `granted: bool`, `version`, `timestamp`.
- Versiyon değişirse yeniden onay.

### FR-7 — Telefon Değiştirme
1. POST `/v1/users/me/phone-change/initiate` { newPhone }
2. NetGSM yeni telefona OTP.
3. POST `/v1/users/me/phone-change/confirm` { otpId, code }
4. Eski telefon → audit log + soft delete dönemi başlamaz (sadece değişim).

## 5. Non-Functional

| NFR | Hedef |
|---|---|
| OTP send P95 | < 800ms (NetGSM HTTP roundtrip) |
| OTP verify P95 | < 100ms |
| OAuth verify P95 | < 500ms (provider verify) |
| Refresh P95 | < 100ms |
| Concurrent login per user | 5 device max (FIFO sırasıyla en eski revoke) |
| OTP code entropy | 6 hane (10⁶ space) + brute force koruması (5 deneme limit) |
| Refresh token entropy | 256 bit secure random |
| JWT signing | RS256, key rotation 90 günde 1 (V1.5) |
| Password hash | ASP.NET Identity default (PBKDF2 iter 100k+) |

## 6. Veri Modeli

### Entity: User (Aggregate Root)
| Field | Type | Notes |
|---|---|---|
| Id | Guid (UUID v7) | PK |
| PhoneE164 | string?, max 16 | Unique nullable (OAuth-only kullanıcı yok telefon olabilir) |
| Email | string?, max 256 | Unique nullable, lowercased |
| EmailNormalized | string? | Index |
| DisplayName | string?, max 80 | UI'da görünen isim |
| AvatarUrl | string?, max 500 | R2 URL |
| Role | enum: Customer / BarberOwner / Stylist / Admin | Birincil rol |
| Status | enum: Active / Suspended / Deleted | |
| CreatedAt | timestamptz | |
| UpdatedAt | timestamptz | |
| DeletedAt | timestamptz? | KVKK silme |
| LastLoginAt | timestamptz? | |
| Locale | string, default `tr-TR` | |
| TimeZone | string, default `Europe/Istanbul` | |

### Entity: AdminCredential (1:1 User, Admin only)
| Field | Type | Notes |
|---|---|---|
| UserId | Guid | PK + FK User |
| PasswordHash | string | ASP.NET Identity |
| PasswordSalt | string | |
| TotpSecret | string | encrypted |
| LastPasswordChangeAt | timestamptz | |

### Entity: RefreshToken (Aggregate)
| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| UserId | Guid | FK User |
| TokenHash | string (SHA-256) | Plain token never stored |
| DeviceFingerprint | string | UUID v7 |
| DeviceName | string?, max 100 | "iPhone 15 Pro" gibi |
| ExpiresAt | timestamptz | |
| CreatedAt | timestamptz | |
| RevokedAt | timestamptz? | |
| RevokedReason | enum: UserLogout / TokenReuse / AdminRevoke / AccountDeleted / Expired | |
| ReplacedByTokenId | Guid? | Rotation chain |
| LastUsedAt | timestamptz? | |
| LastUsedIp | string?, max 45 | IPv4 + IPv6 destek |

### Entity: OtpCode
| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| PhoneE164 | string | İndex (rate limit için) |
| CodeHash | string (SHA-256) | Plain code never stored |
| Purpose | enum: Register / Login / PhoneChange / PasswordReset | |
| ExpiresAt | timestamptz | TTL 5 dk |
| AttemptsLeft | int | Default 5 |
| Status | enum: Pending / Used / Expired / Consumed | |
| CreatedAt | timestamptz | |
| UsedAt | timestamptz? | |

### Entity: Consent
| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| UserId | Guid | FK User |
| Purpose | enum: ToS / Marketing / Location / Health | |
| Granted | bool | |
| Version | string, max 20 | Aydınlatma metni versiyonu |
| GrantedAt | timestamptz | |
| RevokedAt | timestamptz? | |
| Source | enum: Registration / SettingsUpdate / RevocationRequest | |

### Entity: AuditLog
| Field | Type | Notes |
|---|---|---|
| Id | bigint identity | PK (high volume) |
| ActorUserId | Guid? | Null sistem aksiyonu |
| ActorType | enum: User / System / Admin | |
| ActionType | string, max 64 | "user.register", "user.login", "user.delete" |
| ResourceType | string, max 64 | "User", "RefreshToken", "Consent" |
| ResourceId | string, max 64 | |
| PayloadJson | jsonb | İlgili meta veri |
| Ip | string?, max 45 | |
| UserAgent | string?, max 500 | |
| RequestId | string?, max 36 | Trace correlation |
| CreatedAt | timestamptz | |

> **Audit log silinmez** (append-only, 5 yıl saklama, KVKK silme'de bile).

## 7. Domain Events

- `UserRegisteredEvent(userId, role, registeredVia: "otp" | "oauth-apple" | "oauth-google")`
- `UserLoggedInEvent(userId, deviceFingerprint, ip)`
- `UserLogoutEvent(userId, refreshTokenId)`
- `UserAccountDeletedEvent(userId, deletedAt)`
- `UserAccountRestoredEvent(userId, restoredAt)`
- `ConsentGrantedEvent(userId, purpose, version)`
- `ConsentRevokedEvent(userId, purpose)`
- `RefreshTokenReuseDetectedEvent(userId, tokenId, ip)` → tüm cihazları logout

## 8. State Machine

### OtpCode
```
   Pending ──verify ok──► Consumed
      │
      ├─5 wrong attempts──► Expired
      └─TTL 5dk──────────► Expired
```

### RefreshToken
```
   Active ──refresh──► Replaced (chain ileride yeni token)
      │
      ├─logout──► Revoked (UserLogout)
      ├─reuse──► Revoked (TokenReuse — tüm zincir invalid)
      ├─admin──► Revoked (AdminRevoke)
      ├─delete──► Revoked (AccountDeleted)
      └─TTL──► Expired
```

### User Status
```
   Active ──admin suspend──► Suspended ──admin unsuspend──► Active
      │
      └─delete──► Deleted (soft) ──30g──► Hard delete (anonimize)
            │
            └─restore (soft dönem)──► Active
```

## 9. API Endpoints

| Method | Path | Auth | Açıklama |
|---|---|---|---|
| POST | `/v1/auth/otp/send` | public | OTP gönder |
| POST | `/v1/auth/otp/verify` | public | OTP doğrula → tokenler |
| POST | `/v1/auth/oauth/apple` | public | Apple OAuth |
| POST | `/v1/auth/oauth/google` | public | Google OAuth |
| POST | `/v1/auth/admin/login` | public | Admin login (e-posta + parola + TOTP) |
| POST | `/v1/auth/refresh` | public (refresh token body) | Token yenile |
| POST | `/v1/auth/logout` | bearer | Mevcut cihaz logout |
| POST | `/v1/auth/logout/all` | bearer | Tüm cihazlar logout |
| GET  | `/v1/users/me` | bearer | Profil bilgileri |
| PATCH| `/v1/users/me` | bearer | Profil güncelle (displayName, avatar) |
| DELETE | `/v1/users/me` | bearer | Hesabı sil (soft) |
| POST | `/v1/users/me/restore` | bearer | Sil sürecindeyse iptal et |
| POST | `/v1/users/me/phone-change/initiate` | bearer | Yeni telefon OTP başlat |
| POST | `/v1/users/me/phone-change/confirm` | bearer | OTP doğrula, telefon değiş |
| GET  | `/v1/users/me/consents` | bearer | Mevcut rızalar |
| PATCH| `/v1/users/me/consents` | bearer | Rıza güncelle (parçalı) |
| GET  | `/v1/users/me/sessions` | bearer | Aktif cihazlar |
| DELETE | `/v1/users/me/sessions/{id}` | bearer | Belirli cihazı çık |

## 10. Edge Case'ler

| Senaryo | Beklenti |
|---|---|
| OTP brute force (10x yanlış kod) | OtpCode iptal + telefon 1 saat soft block |
| Aynı telefon farklı hesaplarla denenirse | Aynı phoneE164 → aynı user (telefon birincil identifier) |
| OAuth e-posta zaten telefon hesabıyla kayıtlı | Hesap birleştirme prompt'u (V1.5) — MVP'de "bu e-posta zaten var" hata |
| Refresh token reuse | Tüm zincir + tüm cihazlar revoke + Sentry alert |
| Aynı device'tan 2 farklı user login | Device fingerprint user'a bağlı değil — birden fazla user destekler (paylaşımlı device) |
| 6. cihaz login | En eski refresh token revoke |
| Hesap soft delete sonrası login dener | "Hesabınız silinme sürecinde" + restore link |
| Admin TOTP secret kayıp | Backup recovery code (V1.5); MVP'de SMS fallback |
| KVKK aydınlatma metin değişti | Login'de zorla yeni rıza |
| Telefon numarası başka birinin elinde (operator değişti) | OTP yine NetGSM ile gider; doğru kişi alır. Edge ihtimali olarak: kayıp bildirim kuralı yardım merkezi (V1.5). |

## 11. Test Stratejisi

### Unit
- `User.CreateCustomer` value object validations.
- `OtpCode.Verify` state machine.
- `RefreshToken.Rotate` happy + reuse detection.
- `Consent.Grant / Revoke` versiyon kontrolü.

### Integration (Testcontainers)
- `OtpVerifyHandler` happy path → User + RefreshToken + AuditLog yazıldı.
- `RefreshHandler` reuse → tüm zincir revoke.
- `DeleteAccountHandler` → User soft delete + tokens revoke + Consent revoke.
- `Phone change` → eski OTP geçersiz, yeni telefon güncellendi.

### E2E (Detox — Phase 2)
- Customer kayıt akışı (Mobile → API).
- Apple OAuth happy path.
- Logout from all devices.

### Load (k6)
- 100 concurrent OTP send (rate limit doğru çalışmalı).
- 200 concurrent token refresh.

## 12. Riskler

| Risk | Mitigasyon |
|---|---|
| NetGSM API down → OTP gönderemiyoruz | Iletimerkezi fallback (V1.5); MVP'de manuel admin retry |
| OTP SMS bot (premium telefon scam) | TR ülke kodu zorunlu + telefon başına rate limit |
| OAuth provider değişikliği | `IOAuthProvider` abstraction |
| Refresh token DB shared bir process'te yarış | DB row lock + tek atomic transaction |
| KVKK denetim "açık rıza versiyonlama eksik" | Consent.Version field zorunlu, her aydınlatma değişikliğinde yeniden onay |
| Admin TOTP secret leak | encrypted at-rest, audit on access |

## 13. Acceptance Criteria

Tüm aşağıdakiler **yeşil** olmadan modül "tamam" sayılmaz:

- [ ] Customer telefon + OTP kayıt + login uçtan uca çalışıyor.
- [ ] Apple + Google OAuth çalışıyor (sandbox).
- [ ] Admin e-posta + parola + TOTP çalışıyor.
- [ ] Refresh token rotation + reuse detection unit + integration test geçer.
- [ ] Hesap silme + 30 gün soft + hard delete background job çalışıyor.
- [ ] Parçalı consent (4 toggle) kayıt ekranında, versiyonlu, audit log'a yansıyor.
- [ ] Rate limit: OTP 3/saat per telefon, login 5/dk per IP.
- [ ] PII loglarda maskeli (telefon +90 5•• ••• •• 23).
- [ ] EF migration çalışıyor + rollback senaryosu test edildi.
- [ ] xUnit + Testcontainers test coverage **domain %75+, infra %50+**.
- [ ] Sentry release tag eklendi.
- [ ] Audit log tüm user.* event'lerini yazıyor.
- [ ] OpenAPI doc güncel (`/openapi/v1.json`).
- [ ] CLAUDE.md M1 referansı + endpoint listesi.

## 14. Implementation Sırası (Plan)

1. **Domain entities + value objects** — User, RefreshToken, OtpCode, Consent, AuditLog, AdminCredential. Domain events.
2. **EF Core configurations** + ApplicationDbContext'e DbSet ekle.
3. **EF migration** `M01_Identity_Initial`.
4. **Application interfaces** — IJwtTokenService, IOtpService, ISmsService, IOAuthProvider, IPasswordHasher.
5. **Infrastructure implementations** — JwtTokenService (RS256 ile dev anahtarı), OtpService, NetGSMSmsService (sandbox/mock), GoogleOAuthProvider, AppleOAuthProvider, ASP.NET Identity password hasher adapter.
6. **Commands + Validators + Handlers** — RegisterCustomer, SendOtp, VerifyOtp, OAuthLogin, AdminLogin, RefreshTokenCommand, LogoutCommand, DeleteAccountCommand, RestoreAccountCommand, UpdateConsentsCommand.
7. **Endpoints** — `/v1/auth/*`, `/v1/users/me*` Minimal API.
8. **Tests** — xUnit unit + Testcontainers integration.
9. **Background jobs** — Hangfire `HardDeleteUsersJob` (günlük).
10. **OpenAPI annotations** + Swagger doc kontrol.

## 15. Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/08-security/01-auth-overview.md`](../08-security/01-auth-overview.md)
- [`docs/08-security/02-kvkk-overview.md`](../08-security/02-kvkk-overview.md)
- [`docs/04-modules/00-module-list.md`](00-module-list.md)

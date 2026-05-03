# Kimlik Doğrulama & Yetkilendirme — Genel Bakış

> Detaylı endpoint + state machine M1 modül spec'inde. Bu doküman cross-cutting kararları özetler.

---

## 1. Kimlik Türleri

| Kullanıcı tipi | Kayıt yöntemi | Birincil identifier |
|---|---|---|
| **Customer** | Telefon + OTP veya OAuth (Apple/Google) | Telefon |
| **BarberOwner** | Telefon + e-posta + OTP | Telefon |
| **Stylist** | BarberOwner tarafından davet (e-posta + telefon + OTP) | Telefon |
| **Admin** | Manuel oluşturma (founder) | E-posta + 2FA |

---

## 2. Auth Akışları

### 2.1 Telefon + OTP
1. `POST /v1/auth/otp/send` (telefon).
2. NetGSM SMS, 6 haneli kod, TTL 5 dk.
3. `POST /v1/auth/otp/verify` (telefon + kod).
4. Yeni kullanıcı → kayıt akışı; mevcut → JWT + refresh.

### 2.2 OAuth (Apple, Google)
1. Mobile native SDK ile authorization code al.
2. `POST /v1/auth/oauth/{provider}` (code).
3. Provider verify + e-posta üzerinden user resolve veya yeni hesap.
4. JWT + refresh dön.

### 2.3 Admin login (e-posta + parola + TOTP)
1. `POST /v1/auth/admin/login`.
2. Parola Argon2id veya ASP.NET Identity default.
3. TOTP (Google Authenticator) zorunlu.
4. JWT + refresh + IP whitelist kontrolü.

---

## 3. Token Stratejisi

| Token | Süre | Storage (Mobile) | Yenileme |
|---|---|---|---|
| **Access JWT** (RS256) | 15 dk | SecureStore | Otomatik refresh |
| **Refresh** (opaque) | 30 gün | SecureStore | Rotated her kullanımda |
| **Device fingerprint** | hesap ömrü | SecureStore | Yeniden bind cihaz değişiminde |

- **Refresh rotation:** Her refresh'ta yeni token. Eski token reuse → suspicious activity → tüm device logout.
- **Device-bound:** Refresh token bir device fingerprint'e bağlı. Başka cihazda geçersiz.
- **Revocation:** Server-side blacklist (Redis), TTL = original token expiry.

---

## 4. RBAC

```
Admin
  └─ All system actions
  
BarberOwner
  ├─ Manage own BarberShop, Stylists, ServiceCatalog
  ├─ View bookings of own shop
  └─ Manage own KYC submissions
  
Stylist
  ├─ View own bookings
  ├─ Update own working hours / availability
  └─ Toggle walk-in mode
  
Customer
  ├─ Search + book
  ├─ Manage own bookings
  └─ Write reviews for completed bookings
```

**Policy-based authorization** (.NET):
```csharp
services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", p => p.RequireRole("Admin"));
    options.AddPolicy("BarberOwner", p => p.RequireRole("BarberOwner"));
    options.AddPolicy("Stylist", p => p.RequireRole("Stylist", "BarberOwner"));
    options.AddPolicy("Customer", p => p.RequireRole("Customer"));
});
```

**Resource-based authorization:** `IAuthorizationHandler<MyBookingRequirement, Booking>` — kullanıcı kendi booking'ini görebilir.

---

## 5. Rate Limiting (Auth-specific)

| Endpoint | Limit | Block stratejisi |
|---|---|---|
| OTP send | 3/saat per telefon | Soft block 1 saat |
| OTP verify | 5/saat per telefon | Soft block 1 saat |
| Login | 5/dk per IP | Hard block 15 dk |
| Refresh | 10/dk per user | Hard block 5 dk |
| Password reset | 3/saat per e-posta | Soft block 1 saat |

---

## 6. Security Headers

- `Strict-Transport-Security` HSTS preload.
- `X-Content-Type-Options: nosniff`.
- `X-Frame-Options: DENY` (web admin için).
- `Content-Security-Policy` strict (web admin).
- `Referrer-Policy: strict-origin-when-cross-origin`.

---

## 7. Tehditler & Mitigasyon

| Tehdit | Mitigasyon |
|---|---|
| **Brute force OTP** | Rate limit + exponential backoff |
| **Token theft (XSS)** | SecureStore (mobile), HttpOnly cookie (web) |
| **CSRF** | SameSite=Strict cookie, anti-forgery token |
| **Session hijack** | Device-bound refresh, IP change → re-auth |
| **Password leak** | OAuth tercihi, bilgisayar parolası asla mobile'da yok |
| **Replay attack** | Idempotency-Key + token tek kullanım |
| **Account takeover** | Telefon değişikliği akışında çift faktör |

---

## 8. KVKK İlişkisi

- Kayıt sırasında **parçalı açık rıza** zorunlu (her amaç ayrı toggle).
- Aydınlatma metni link her kayıt ekranında.
- Hesap silme → 30 gün soft delete + hard delete (anonimize).
- Detay: [`docs/08-security/02-kvkk-overview.md`](02-kvkk-overview.md).

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- M1 Auth modül spec (Phase 1 başında yazılır)

# API Konvansiyonları

> RESTful + verb-tabanlı URL + JSON. Detaylı endpoint listesi modül spec'lerinde.

---

## 1. Genel Kurallar

- **Base URL:** `https://api.kuaforum.com/`.
- **Versiyon:** URL path. `/v1/` MVP. Breaking change'de `/v2/`.
- **Format:** JSON in/out. UTF-8.
- **Tarih:** ISO 8601 UTC (`2026-05-03T14:30:00Z`).
- **Para:** En küçük birim integer (kuruş). 500.50 TL → `50050`.
- **ID:** UUID v7 string. `123e4567-e89b-12d3-a456-426614174000`.
- **HTTP method anlamı:** GET (read), POST (create or action), PATCH (partial update), PUT (full replace, nadir), DELETE.

---

## 2. URL Yapısı

- **Resource-based:** `/v1/barbers/{id}/services`.
- **Action-based** (gerekirse): `/v1/bookings/{id}/cancel`, `/v1/payments/{id}/refund`.
- **kebab-case** path. **camelCase** query param.

Örnekler:
- `GET /v1/barbers?cityId=...&page=1&pageSize=20`
- `POST /v1/bookings`
- `PATCH /v1/bookings/{id}` (partial update)
- `DELETE /v1/users/me` (KVKK silme)

---

## 3. Request Headers

| Header | Açıklama |
|---|---|
| `Authorization: Bearer <jwt>` | Auth zorunlu (public endpoint'ler hariç). |
| `Idempotency-Key: <uuid>` | Mutating endpoint'lerde **zorunlu** (POST + PATCH + DELETE). |
| `Accept-Language: tr-TR` | i18n. |
| `X-Client-Version: 1.2.3` | Mobile sürüm (force update kontrolü). |
| `X-Device-Id: <uuid>` | Device-bound refresh token için. |

---

## 4. Response Format

### Success
```json
{
  "data": { ... },
  "meta": { "requestId": "abc-123", "timestamp": "2026-05-03T14:30:00Z" }
}
```

### Liste (paginated)
```json
{
  "data": [ ... ],
  "pagination": {
    "cursor": "eyJsYXN0SWQiOiJ4eXoifQ==",
    "hasMore": true,
    "total": null  // optional, performans için bazen yok
  },
  "meta": { "requestId": "abc-123", "timestamp": "..." }
}
```

### Hata (ProblemDetails — RFC 7807)
```json
{
  "type": "https://kuaforum.com/errors/validation",
  "title": "Validation Failed",
  "status": 400,
  "detail": "PhoneNumber must be valid TR mobile.",
  "instance": "/v1/auth/register",
  "errors": {
    "phoneNumber": ["PhoneNumber must be valid TR mobile."]
  },
  "requestId": "abc-123"
}
```

---

## 5. Status Code'ları

| Kod | Anlam | Kullanım |
|---|---|---|
| 200 | OK | Success with body |
| 201 | Created | POST resource yarattı |
| 204 | No Content | DELETE veya body'siz success |
| 400 | Bad Request | Validation veya business rule fail |
| 401 | Unauthorized | Token eksik/geçersiz |
| 403 | Forbidden | Yetki yok |
| 404 | Not Found | Resource yok |
| 409 | Conflict | Concurrent edit / duplicate |
| 422 | Unprocessable | Semantic hatası (ör. timezone yanlış) |
| 429 | Too Many Requests | Rate limit |
| 500 | Internal | Sunucu hatası — Sentry'e gider |
| 503 | Service Unavailable | Bakım veya bağımlı servis down |

---

## 6. Pagination

- **Cursor-based** (id > last_id). Performans avantajı (no OFFSET).
- `cursor` opaque base64 token.
- `pageSize` max 50, default 20.
- **Asla** `OFFSET` kullanma (1M+ row hot table'da sayma yavaş).

---

## 7. Filtreleme & Sıralama

- Query param: `?cityId=ist&minRating=4&sort=distance,asc`.
- `sort` formatı: `field,direction` (camelCase, asc/desc).
- Birden fazla sort: `?sort=distance,asc&sort=rating,desc`.
- Filtreleme allowlist: her endpoint kendi spec'inde tanımlı (SQL injection koruması).

---

## 8. Idempotency

- **Mutating endpoint'lerde** `Idempotency-Key` header zorunlu.
- 24h Redis cache: aynı key → aynı response.
- Önemli: PSP webhook idempotency için **PSP'nin verdiği transaction ID** kullanılır.

---

## 9. Rate Limiting

- **Per IP:** 100 req/dk genel, 10 req/dk auth, 5 req/dk OTP.
- **Per User:** 200 req/dk authenticated.
- **Algoritma:** Redis sliding window.
- **429 response** + `Retry-After` header.

---

## 10. Auth

- **JWT bearer** (RS256, 15dk).
- **Refresh token** ayrı endpoint `/v1/auth/refresh`. Body'de değil, HTTP-only secure cookie veya request body (mobile).
- **Public endpoint'ler:** kayıt, login, OTP, password reset, public catalog.
- **Authenticated endpoint'ler:** her diğer.
- **Admin endpoint'ler:** `[Authorize(Policy = "Admin")]`.

---

## 11. CORS

- Production: `Access-Control-Allow-Origin` sadece `https://kuaforum.com`, `https://app.kuaforum.com`, `https://admin.kuaforum.com`.
- Mobile native: CORS gerekmez.
- Dev: `localhost:*` allow.

---

## 12. Webhooks

- **Inbound (PSP, Nilvera):**
  - URL: `/v1/webhooks/{provider}`.
  - Signature header doğrulaması zorunlu.
  - Idempotency key = PSP transaction ID.
  - 200 OK ack ≤ 200ms.
- **Outbound (V1.5):** Kuaförün entegrasyon hookları.

---

## 13. Versiyonlama Disiplini

- Yeni field eklemek backward-compatible (clients ignore).
- Field silme veya semantic değişikliği breaking → yeni versiyon path.
- Deprecation: 6 ay önceden `Deprecation` ve `Sunset` header.

---

## 14. Documentation

- **Swagger / OpenAPI 3** otomatik üretim.
- **Scalar UI** Swagger'dan daha modern alternatif (V1.5).
- **Bruno collection** repo'da `infra/bruno/` altında.

---

## 15. Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/03-architecture/04-design-patterns.md`](../03-architecture/04-design-patterns.md)
- Her modül spec'i (kendi endpoint listesi)

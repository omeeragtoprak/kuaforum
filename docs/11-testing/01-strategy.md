# Test Stratejisi

> Hızlı, güvenilir, anlamlı test pyramid. Coverage **gerçek kalite**, **CI gate** ve **production confidence** için.

---

## 1. Test Pyramid

```
              ▲
              │
       E2E (Detox + smoke)         ← %10 — kritik akışlar
              │
   Integration tests               ← %30 — DB + dış servis (mock veya sandbox)
              │
       Unit tests                  ← %60 — domain + validator + mapper
              ▼
```

**Hedef coverage:**
- Backend domain: **%70+**
- Backend infrastructure: **%50+**
- Mobile component + hook: **%50+**
- Mobile critical flow E2E: **8-10 senaryo**

---

## 2. Backend Test (xUnit + NSubstitute + Testcontainers)

### Unit Test
- Domain entity invariants.
- Value object equality + behavior.
- FluentValidation rules.
- Mapster mapping.
- Pure function utilities.

```csharp
public class BookingTests
{
    [Fact]
    public void Cancel_WhenMoreThan24Hours_RefundsFully()
    {
        var booking = BookingBuilder.Confirmed(scheduledAt: DateTime.UtcNow.AddDays(2));
        var refund = booking.CalculateRefund(at: DateTime.UtcNow);
        refund.Percentage.Should().Be(100);
    }
}
```

### Integration Test
- DbContext + repository + handler.
- Testcontainers ile gerçek Postgres + Redis.

```csharp
public class CreateBookingHandlerTests : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Handle_ValidRequest_CreatesBooking()
    {
        // arrange: seed barber + stylist + service
        // act: handler invoke
        // assert: bookings table'da satır + outbox event
    }
}
```

### Contract Test
- PSP webhook payload → snapshot test.
- Nilvera response → schema validation.

---

## 3. Mobile Test (Jest + RNTL + Detox)

### Unit
- Hook (useBookings, useAuth) — mock TanStack Query client.
- Zod schema parsing.
- Utility (date format, currency format).

### Component
- Screen rendering (RNTL).
- User interaction (press, type).
- Conditional render (loading/error/empty).

### E2E (Detox)
**Critical flows (Phase 2 sonu):**
1. Customer kayıt + login (telefon OTP).
2. Keşif + filtre + slot seçimi.
3. Booking + 3DS + ödeme (sandbox PSP).
4. Booking iptal + iade.
5. Yorum yapma.
6. Kuaför login + KYC submit + randevu kabul.
7. Walk-in toggle + müşteri bookingto.
8. KVKK silme.

**Run:** Local + CI ARM macOS runner (iOS) + Android emulator.

---

## 4. API Contract Test (Bruno)

`infra/bruno/` klasörü repo'da:
- Her endpoint için happy + 4-5 negatif case.
- Bruno CLI ile CI'da çalışır.

---

## 5. Performans Test (k6)

**Hedef senaryolar (Phase 2 sonu):**

```js
// k6 senaryo: 100 concurrent search
import http from 'k6/http';
export const options = {
  vus: 100,
  duration: '30s',
  thresholds: {
    'http_req_duration{status:200}': ['p(95)<500'],
    'http_req_failed': ['rate<0.01'],
  },
};
export default function() {
  http.get('https://staging-api.kuaforum.com/v1/barbers?cityId=ist');
}
```

Senaryolar:
- 100 concurrent search → P95 < 500ms.
- 50 concurrent booking init → no race conditions.
- 200 concurrent webhook ingest → no message loss.

---

## 6. Security Test

- **Pen-test:** 3rd party firma, soft-launch öncesi.
- **DAST:** OWASP ZAP CI'da haftalık (V1.5).
- **SAST:** SonarQube veya Semgrep CI'da her PR (V1.5).
- **Dependency scan:** Dependabot + Snyk free tier.

---

## 7. Manual Test (UAT)

- Phase 2 sonu founder kendi yapar.
- Customer discovery participants (5-10 kişi) staging'de test.
- App Store review öncesi 1 hafta agresif manuel test.

---

## 8. CI Gate

```yaml
# .github/workflows/ci.yml (özet)
- dotnet test --collect:"XPlat Code Coverage"
- pnpm test (mobile + admin)
- bruno run (API contract)
- (sadece nightly) k6 run
- (sadece nightly) detox build + test

# Gate'ler:
- Test green olmadan merge YASAK.
- Coverage delta -%5 → PR rejected.
- Flaky test 3 fail sonrası @Skip + 7 gün SLA.
```

---

## 9. Test Verisi Yönetimi

- **Local dev:** Seed script (Bogus library, gerçekçi sahte veri).
- **Staging:** Sentetik veri seti (anonymized customer discovery participants).
- **Production:** Asla test verisi.
- **Test data builder:** Fluent API her ana entity için.

---

## 10. Flaky Test Disiplini

- 3 fail sonrası `[Trait("flaky", "true")]` + Issue.
- 7 gün SLA — düzelt veya kaldır.
- "Çalışıyor bir tekrar dene" YASAK.

---

## 11. Test Hızı

- Unit + integration: **< 3 dk** (CI).
- Detox E2E: **< 15 dk**.
- k6 nightly: **< 30 dk**.

Aşılırsa optimize et (parallel execution, test fixture caching, vs.).

---

## 12. Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/02-requirements/03-non-functional.md`](../02-requirements/03-non-functional.md)

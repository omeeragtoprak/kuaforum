# Yazılım Tasarım Desenleri (Design Patterns)

> Kuaforum modüler monolitinde **kabul edilen** ve **reddedilen** desenler. Yeni desen önerisi RFC ile.

---

## 1. Mimari Stiller (kabul)

### Clean Architecture
- **Katman:** Domain → Application → Infrastructure → API.
- **Bağımlılık yönü:** İçeriden dışarıya **doğru sadece**.
- **Domain** dış dünyayı bilmez.
- **Infrastructure** Application interface'lerini implement eder.

### Modular Monolith
- Her modül bağımsız aggregate root + kendi `Module.{Name}` namespace.
- Modüller arası bağımlılık **public Contracts** (DTO + interface) üzerinden.
- Modül içsel entity'si dışarı sızmaz.

### CQRS (Light)
- **Command:** state-mutating, validator + handler + domain event.
- **Query:** read-only, IQueryable projection ile DTO.
- Aynı DB (event sourcing değil) — sadece kod ayrımı.

---

## 2. Domain-Driven Design (kabul, light)

- **Aggregate root:** `BarberShop`, `Booking`, `Payment`, `User`.
- **Value object:** `Money`, `PhoneNumber`, `Email`, `Coordinate`, `TimeRange`, `IbanNumber`.
- **Domain event:** `BookingConfirmed`, `PaymentRefunded`, `KycApproved`. MediatR `INotification`.
- **Bounded context:** Modül = bounded context.
- **Repository:** Sadece aggregate root'lar için. EF Core direct context kullanımı **küçük query'lerde tercih**.

---

## 3. Cross-Cutting (kabul)

### Mediator Pattern (MediatR)
- Tüm command/query handler'lar.
- **Pipeline behavior:** Validation → Logging → Transaction → DomainEventDispatch.

### Outbox Pattern
- DB transaction + event publish atomicity.
- `outbox_messages` table, Hangfire job 5 saniyede bir.

### Specification Pattern (selectively)
- Karmaşık search query için `IBarberSpecification` (ileride Phase 2).
- MVP'de basit predicate yeter, premature.

### Repository + Unit of Work
- Repository sadece aggregate root erişimi (Booking, Payment).
- Unit of Work = `DbContext.SaveChangesAsync()`.
- Light pattern; her handler kendi DbContext scope.

### Strategy Pattern
- `IPaymentProvider` (PayTR / Iyzico / Mock).
- `INotificationChannel` (FCM / SMS / Email).
- `IDocumentValidator` (KYC belge tipleri).

### Observer / Domain Event
- Domain event MediatR INotification — in-process subscriber.
- Cross-module event integration event Outbox üzerinden.

### Idempotency Key
- PSP webhook + mutation komutlarında.
- Redis SET NX EX 24h — aynı key tekrar gelirse cached response.

---

## 4. Reddedilen Desenler

| Desen | Neden Yasak |
|---|---|
| **Microservices** | Solo + MVP — premature optimization. Phase 3+ değerlendirilebilir. |
| **Event Sourcing** | Karmaşık + ölçek gereksinim yok MVP'de. Audit log ayrı çözüm. |
| **CQRS write/read DB ayrımı** | Phase 1-2'de gereksiz. Aynı DB üzerinde projection. |
| **Anemic domain model** | Domain'de iş mantığı olsun (entity method'ları). Sadece getter/setter aggregate kabul edilemez. |
| **Service Locator / DI lookup at runtime** | Constructor injection only. |
| **Generic Repository<T>** | Specific repository tercih (`IBookingRepository`). Generic anti-pattern (LINQ ile zaten generic). |
| **Stored procedure heavy** | EF Core LINQ tercih. Sadece optimization edilemez sorgular için raw SQL. |
| **God class / 1000-line handler** | Handler max ~200 satır. Aşılırsa parçala. |

---

## 5. Validation Pattern

- **FluentValidation** Application katmanında.
- MediatR pipeline behavior'da otomatik çalışır.
- Validation hatası → `ValidationException` → middleware → ProblemDetails.
- DTO validation, domain validation, business rule validation **ayrı katmanlarda**.

---

## 6. Mapping Pattern

- **Mapster** Entity ↔ DTO.
- AutoMapper'dan ~10x hızlı.
- Source-gen olgun.
- **Asla** entity doğrudan API response'a sızmasın — DTO project layer'da.

---

## 7. Logging Pattern

- **Serilog** structured logging.
- **Sink:** Seq (development + production).
- **Enrichment:** RequestId, UserId (anonymized), CorrelationId.
- **PII mask:** Telefon `+90 5•• ••• •• 23`, e-posta `a***@example.com`.
- **Log level:**
  - Information: business event (booking created, payment captured).
  - Warning: anomali (rate limit hit, slow query).
  - Error: exception, recovery'siz hatalar.
  - Debug: dev only, prod'da kapalı.

---

## 8. Configuration Pattern

- **Options Pattern** (`IOptions<T>`).
- ASP.NET `appsettings.{Environment}.json` + environment variable override.
- Secret asla `appsettings`'te — env var veya secret manager.

---

## 9. Authentication / Authorization Pattern

- **JWT bearer + refresh** ASP.NET Identity üzerinde.
- **Policy-based authorization:** `[Authorize(Policy = "BarberOwner")]`.
- **Resource-based authorization:** `IAuthorizationHandler<T>` — kendi randevunu silebilirsin, başkasınınkini değil.

---

## 10. Error Handling Pattern

- **Custom exception hierarchy:**
  - `DomainException` (business rule violation, 400).
  - `NotFoundException` (404).
  - `ValidationException` (400).
  - `UnauthorizedException` (401/403).
  - `ConflictException` (409 — concurrent edit).
- **Middleware** her exception'ı `ProblemDetails`'a dönüştürür.
- **Sentry** unhandled exception otomatik capture.

---

## 11. Concurrency Pattern

- **Optimistic concurrency:** `RowVersion` (timestamp) entity'lerde.
- **Distributed lock (Redis):** Slot rezervasyonu, payment intent.
- **Eventual consistency:** Outbox sayesinde event publish gecikmeli ama garantili.

---

## 12. Test Pattern

- **AAA (Arrange, Act, Assert)** isim formatı.
- **Test naming:** `MethodName_Scenario_ExpectedResult` veya `Given_X_When_Y_Then_Z`.
- **Fixture sharing:** xUnit `IClassFixture<>` for Testcontainers.
- **Test data builder:** Fluent API for entity creation in tests.

---

## 13. API Design Pattern

- **REST + verb-based URL** (resource-oriented).
- **Pagination:** Cursor-based (id > last_id LIMIT N).
- **Versioning:** URL path `/api/v1/`.
- **Content negotiation:** JSON only MVP, GraphQL değerlendirilmedi.
- **Idempotency-Key header** mutating endpoint'lerde.
- Detay: [`docs/07-api/01-conventions.md`](../07-api/01-conventions.md).

---

## 14. Frontend Pattern (Mobile)

- **Container/Presentational** ayrımı (gerek olduğunda).
- **Custom hooks:** Business logic + side-effect.
- **Component composition** > inheritance.
- **Server state ≠ client state** (TanStack Query vs Zustand).

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/03-architecture/01-system-architecture.md`](01-system-architecture.md)
- [`docs/03-architecture/02-tech-stack.md`](02-tech-stack.md)

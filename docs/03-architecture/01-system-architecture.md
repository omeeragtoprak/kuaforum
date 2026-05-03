# Sistem Mimarisi

## Mimari Stili: Modular Monolith + Clean Architecture

Tek bir .NET 8 process içinde, **modüller arası bağımlılık tek yönlü** ve **modül içi domain mantığı kapalı** olarak çalışan modüler monolittir. Microservices'e geçiş kararı **Phase 3+'da metric-driven** alınır.

### Niye microservices değil (Phase 1-2 için)

| Argüman | Değerlendirme |
|---|---|
| Solo founder kapasitesi | Microservices = N service deploy + N CI/CD + cross-service test + observability matriksi. Tek başına yönetmek 2-3 ay ek yük. |
| Premature optimization | Ölçek henüz yok; modül sınırları net olduğu için ileride extract zor değil (modül başına 1-2 hafta efor). |
| Transaction boundary | Booking + Payment + Availability tek transaction'da olmalı. Microservices bunu **distributed saga + outbox** ile çözüyor — gereksiz karmaşıklık. |
| Latency | Microservices'in network hop'u + ölçek olmadığında pure overhead. |
| Maliyet | N service = N container = N Hetzner instance. Tek monolith $25/ay vs microservices $200+/ay. |

### Niye monolith ama "modular"

- Modüller `Kuaforum.Modules.{Module}` projesi olarak ayrılır.
- Modüller arası **public contract** (interface + DTO) sadece `Kuaforum.Modules.{Module}.Contracts` üzerinden.
- Modül içi entity/private DTO **dışarı sızmaz**.
- Modül başına **kendi DbContext** veya tek DbContext + schema-prefix (basit MVP için tek DbContext + migration namespace). Karar: Phase 1 sonu.
- Cross-module communication tercihi: **MediatR INotification** (in-process event) → Outbox (persisted) → ileride RabbitMQ/Kafka'ya extract.

---

## C4 Düzey 1 — Sistem Bağlamı

```
                     ┌─────────────────────────────┐
                     │  Müşteri (mobile, Expo/RN)  │
                     └──────────────┬──────────────┘
                                    │
   ┌─────────────────┐               │              ┌─────────────────────┐
   │ Kuaför / Stilist│───────────────┼──────────────│  Admin (web, Next.js)│
   │ (mobile + web)  │               │              └─────────────────────┘
   └─────────────────┘               │
                                     │ HTTPS / REST + WebSocket
                                     ▼
                     ┌─────────────────────────────┐
                     │      Kuaforum API           │
                     │ (.NET 8 modular monolith)    │
                     └──────┬──────────────────┬───┘
                            │                  │
            ┌───────────────┼─────────┬────────┼───────────────┐
            ▼               ▼         ▼        ▼               ▼
       ┌─────────┐    ┌─────────┐ ┌────────┐ ┌────────┐  ┌──────────┐
       │PostgreSQL│   │ Redis   │ │Meili   │ │  R2    │  │   Seq    │
       │+PostGIS  │   │ cache   │ │ search │ │  files │  │  logs    │
       └──────────┘   └─────────┘ └────────┘ └────────┘  └──────────┘

   Dış Servisler:
   PayTR Pazaryeri · Iyzico (fallback) · Nilvera e-Arşiv · 
   FCM (push) · NetGSM (SMS) · Postmark (mail) · Google Maps (geocoding) · Sentry
```

---

## C4 Düzey 2 — Konteyner / Modül Görünümü

```
┌────────────────── Kuaforum.Api (ASP.NET Core 8) ──────────────────┐
│                                                                    │
│  ┌────────────────────────── Modules ──────────────────────────┐  │
│  │                                                              │  │
│  │  Identity ── User ── Geography ── Barber ── Catalog          │  │
│  │     │         │          │           │         │             │  │
│  │     └─────────┴──────────┼───────────┘         │             │  │
│  │                          ▼                      ▼             │  │
│  │                    Availability ──────────► Booking          │  │
│  │                                                  │            │  │
│  │                                                  ▼            │  │
│  │                                              Payment          │  │
│  │                                                  │            │  │
│  │                                                  ▼            │  │
│  │                                              Review ◄── Search │
│  │                                                  │            │  │
│  │                                                  ▼            │  │
│  │                                            Notification       │  │
│  │                                                               │  │
│  │                          (cross-cutting):                     │  │
│  │                          Audit · Outbox · Admin              │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                    │
│  Cross-cutting servisler:                                          │
│  - JWT auth middleware                                             │
│  - Rate limiting (Redis sliding window)                            │
│  - Request validation (FluentValidation MediatR pipeline)          │
│  - Exception → ProblemDetails                                      │
│  - OpenTelemetry trace export → Seq + Sentry                      │
└────────────────────────────────────────────────────────────────────┘
```

**Bağımlılık yönü:** Identity → User → diğerleri. Booking → Availability + Payment + Notification. Asla aşağıdan yukarıya bağımlılık (Auth, Booking'i bilmez).

---

## Layer Yapısı (Clean Architecture)

```
src/
├── Kuaforum.Api/                  ← entrypoint, controllers/minimal API, swagger, middleware
├── Kuaforum.Application/          ← use cases (MediatR handlers), validators, DTOs, interfaces
├── Kuaforum.Domain/               ← entities, value objects, domain events, business rules
└── Kuaforum.Infrastructure/       ← EF Core, PSP impl, SMS impl, e-Arşiv impl
```

- **Domain layer** dış dünyayı bilmez (DB, HTTP, framework yok).
- **Application layer** Domain'e bağımlı, dış servisleri sadece **interface** üzerinden bilir.
- **Infrastructure layer** Application interface'lerini implement eder, dış servislerle konuşur.
- **API layer** sadece HTTP'yi Application'a köprü kurar; iş mantığı yok.

### Module-specific code

Her modülün hem `Application` hem `Infrastructure` katmanında karşılığı olur:
```
Kuaforum.Application/
├── Modules/
│   ├── Auth/
│   ├── Booking/
│   ├── Payment/
│   └── ...
Kuaforum.Infrastructure/
├── Modules/
│   ├── Auth/
│   ├── Booking/
│   ├── Payment/
│   └── ...
```

---

## CQRS + MediatR

- **Command** = state-mutating action (CreateBookingCommand, RefundPaymentCommand). Validator → Handler → Domain → Persist → Domain Event.
- **Query** = read-only (GetBarberAvailabilityQuery, SearchBarbersQuery). Handler doğrudan EF Core IQueryable üzerinden projection ile DTO döner.
- **Pipeline behaviors:** Validation → Logging → Transaction → DomainEventDispatch → ResponseCaching.

---

## Outbox Pattern

Booking confirmed olduğunda:
1. **Aynı SQL transaction'da:** `bookings` + `outbox_messages` (event payload).
2. **Background:** Hangfire job her 5 saniyede bir outbox'tan unhandled mesajları çeker, MediatR `INotification` olarak publish eder, başarılıysa `processed_at` damgalar.
3. Bu pattern, **DB'ye yaz + event publish** atomicity'sini garanti eder. İleride RabbitMQ/Kafka'ya extract edilebilir.

---

## Real-time (Phase 3 / V1.5)

Walk-in full real-time queue için **SignalR**:
- `/hubs/walk-in` endpoint
- Kuaför "şu an boşum" toggle → broadcast: `WalkInAvailableEvent`
- Müşteri tarafı subscribe: yakındaki açık kuaförlerin canlı queue uzunluğu.
- Connection scale: Redis backplane (V1.5 için zorunlu).

---

## Multi-tenant Yapısı

- **Tek tenant model** (Kuaforum işletmecisi sensin). Kuaför = kullanıcı, ayrı tenant değil.
- Bu, ileride white-label opsiyon istersek değişir; şu anda gereksiz karmaşıklık.

---

## Erişim Katmanları

| Katman | Yöntem |
|---|---|
| Mobil → API | HTTPS + JWT Bearer |
| Admin → API | HTTPS + JWT Bearer + IP whitelist (Cloudflare WAF) |
| API → DB | SSL connection (sslmode=require), connection pool 50-100 |
| API → Redis | TLS + ACL, AUTH password |
| API → Meilisearch | API key, internal network |
| API → PSP/Nilvera/FCM | HTTPS, secret in vault |

---

## Failure Modes & Recovery

| Senaryo | Davranış | Recovery |
|---|---|---|
| DB unavailable | API 503 + retry middleware kuyruğa alır | Hetzner managed Postgres failover (max 5dk) |
| Redis unavailable | Cache bypass (DB direct), rate limiting fail-open + alert | Restart container |
| PSP down | Booking confirmed ama payment "pending" state'de bekler | Hangfire 1dk/5dk/15dk retry, 3 fail sonrası admin alert |
| Meilisearch down | Search disabled, basit DB LIKE fallback | Restart, reindex |
| Nilvera down | Fatura kuyruğa alınır (max 7 gün — VUK 509 SLA), retry | Manuel admin override |
| FCM/SMS down | Message kuyruğa, alternatif kanal denenir | — |

---

## Detaylı Diyagramlar

- C4 detayları: `docs/03-architecture/03-deployment-topology.md`
- Design pattern'ler: `docs/03-architecture/04-design-patterns.md`
- ER diagram: `docs/05-data/01-er-overview.md`
- Akışlar: `docs/06-flows/01-user-journeys.md`

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/03-architecture/02-tech-stack.md`](02-tech-stack.md)

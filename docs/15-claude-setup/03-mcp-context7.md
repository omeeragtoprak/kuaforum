# context7 MCP — Library Docs Sorgulama

> **MCP** = Model Context Protocol — Claude'un dış servislere bağlandığı protokol.
> **context7** = Library / framework / SDK / cloud servis için **güncel** dokümantasyon sağlayan MCP server.

Bu projede **zorunlu kullanım**: kütüphane API'lerinin training data'da eski olabileceğini varsayarız.

---

## 1. Ne Zaman Kullan

`mcp__context7__query-docs` çağrısını **şu durumlarda her seferinde** yap:

- **Yeni library kullanımı** (ilk import).
- **API çağrısı** (signature, parameter şeması).
- **Configuration** (appsettings.json, expo.json, vs.).
- **Migration** (v8 → v9 sürüm farkı).
- **Library-specific debugging** (bug nasıl avlanır).
- **CLI tool kullanımı** (dotnet ef, eas build).

> "Bu kütüphaneyi biliyorum" varsayımı **risk**. context7 ucuz, dakikalar tasarruf ettirir.

---

## 2. Kullanma Disiplini

```
# Doğru: önce sorgula, sonra implementation yaz
1. mcp__context7__resolve-library-id (library adı verip ID al)
2. mcp__context7__query-docs (sorgu yap)
3. Implementation

# Yanlış: training data'ya güvenip yaz, sonra hata
```

---

## 3. Bu Projede Sık Sorgulanan Library'ler

| Library | Tipi | Hangi konuda |
|---|---|---|
| .NET 8 / ASP.NET Core | runtime | minimal API, middleware, DI |
| EF Core 8 | ORM | migrations, JSON, performance |
| MediatR 12+ | mediator | pipeline behavior, lisans durumu |
| FluentValidation | validation | rules, async |
| Mapster | mapping | source-gen, custom |
| Hangfire | jobs | retry, dashboard, postgres-backed |
| StackExchange.Redis | client | sliding window, distributed lock |
| NPGSQL + PostGIS | provider | spatial queries |
| Serilog + Seq | logging | enrichment, structured |
| Sentry .NET | error | release tag, performance |
| Expo SDK 52+ | RN | router, build, OTA |
| React Navigation 7 | nav | Expo Router integration |
| TanStack Query 5 | data | hydration, mutation, invalidation |
| Zustand 5 | state | persist, slices |
| react-hook-form 7 | forms | resolver, refs |
| zod 3 | schema | refine, transform |
| Tamagui | UI | theme, compile-time |
| i18next | i18n | namespaces, plurals |
| axios 1 | http | interceptor, cancel |
| Detox | E2E | iOS + Android run |
| Playwright | web E2E | (admin paneli için) |
| k6 | load | scenarios, thresholds |
| GitHub Actions | CI | workflow, secrets |
| Cloudflare API | infra | DNS, R2, WAF |
| Iyzico .NET SDK | PSP | sandbox, prod |
| PayTR API | PSP | iframe, marketplace |
| Nilvera API | e-Arşiv | invoice flow |
| FCM Admin SDK | push | tokens, batch |
| NetGSM REST | SMS | balance, OTP |

---

## 4. Yanlış Kullanımdan Kaçın

- **Genel programlama bilgisi** için context7 kullanma. Örnek: "C# async pattern" → context7 değil, kendi bilgi.
- **Iş mantığı debug** için kullanma. Örnek: "kullanıcı login olamıyor" → kod bak, log oku, context7 değil.
- **Refactoring** için kullanma — kod yapısı project-specific.

---

## 5. Bağlantılar

- [`docs/15-claude-setup/01-overview.md`](01-overview.md)
- [`CLAUDE.md`](../../CLAUDE.md)

# RFC-001 — Backend Target Framework: .NET 10 → .NET 8 LTS

**Status:** Accepted (retrospektif — founder imzası bekliyor)
**Tarih:** 2026-07-23 (RFC yazım tarihi; kod değişikliği working tree'de commit edilmemiş halde bulundu, gerçek uygulama tarihi tespit edilemiyor)
**Kapsam:** `backend/` — tüm `.csproj` hedefleri ve ilişkili paket sürümleri

---

## Karar

Backend'in hedef framework'ü, `docs/PHASE-0-MASTER-SPEC.md` § 2 karar #3'te kilitlenen **.NET 10**'dan **.NET 8 LTS**'e düşürüldü. Etkilenen tüm projelerde (`src/` altında 4, `tests/` altında 3 — toplam 7 `.csproj`) `<TargetFramework>` `net10.0` → `net8.0` olarak değiştirildi, ilişkili NuGet paket sürümleri (EF Core, Npgsql, ASP.NET JwtBearer, Microsoft.Extensions.*) 10.0.x bandından 8.0.x bandına indirildi.

Bu değişiklik şu ana kadar **hiçbir RFC ile belgelenmedi**; CLAUDE.md'nin stack tablosunda tek satırlık bir not var, ama `PHASE-0-MASTER-SPEC.md` hâlâ .NET 10'u "doğru karar" olarak işaretliyor — spec ile kod arasında çözülmemiş çelişki bu RFC'nin ana konusu.

## Bağlam

- **Master spec çelişkisi:** `docs/PHASE-0-MASTER-SPEC.md:39` — "**.NET 10 / ASP.NET Core** (LTS Kasım 2028'e kadar) | Founder orijinal mesajda '.NET Core 8' yazdı; ... .NET 8 LTS Kasım 2026'da bitiyor → launch'tan önce. **.NET 10 LTS** doğru karar." Bu satır hâlâ değişmedi; kod ise net8.0'da.
- **CLAUDE.md'nin kaydettiği (ama RFC'ye dönüştürülmemiş) not:** "Daha önce `net10.0` hedefliydi; lokal SDK 8 + `Guid.NewGuid` + OpenApi paketi sökülerek `net8.0`'a alındı."
- **Yerel ortam kanıtı:** `dotnet --list-sdks` yalnızca `8.0.125` döndürüyor; makinede .NET 10 SDK kurulu değil. `backend/` altında `global.json` yok — SDK sürümü pinlenmemiş, farklı bir makinede (ör. CI runner) hangi SDK'nın seçileceği garanti değil.
- **Working tree diff'i (henüz commit edilmemiş):**
  - `Kuaforum.Api.csproj`, `Kuaforum.Application.csproj`, `Kuaforum.Domain.csproj`, `Kuaforum.Infrastructure.csproj` ve 3 test projesi: `net10.0` → `net8.0`.
  - `Microsoft.EntityFrameworkCore` / `.Design`, `Npgsql.EntityFrameworkCore.PostgreSQL` (+`.NetTopologySuite`), `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options.ConfigurationExtensions`: 10.0.x → 8.0.11/8.2.0 aralığı.
  - `Microsoft.AspNetCore.OpenApi` (10.0.7) paketi **tamamen kaldırıldı** — bu paket .NET 9+'ta framework'e dahil, net8'de yok. `Program.cs`'ten `builder.Services.AddOpenApi()` ve `app.MapOpenApi()` çağrıları silindi. **Sonuç: dev ortamında hiçbir OpenAPI/Swagger endpoint'i yok.** Mobile ve web-admin geliştiricileri API sözleşmesini şu an yalnızca kod okuyarak veya Bruno collection'dan çıkarabiliyor.
  - `System.Security.Cryptography.Xml` (10.0.7) paketi kaldırıldı (net8'de kullanılmıyor olmalı — kullanım noktası doğrulanmadı, referans temizliği).
- **Yan etki — UUID v7 kararının kırılması:** `Entity.cs`, `IDomainEvent.cs` (`DomainEvent` record), `RequestIdMiddleware.cs` — üçünde de `Guid.CreateVersion7()` (.NET 9+ API, zaman sıralı UUID v7) → `Guid.NewGuid()` (random UUID v4) değişti. Bu, ayrı bir kilitlenmiş kararı kırıyor:
  - `docs/04-modules/01-auth.md:133` — "Id | Guid (**UUID v7**) | PK"
  - `docs/07-api/01-conventions.md:14` — "**ID:** UUID v7 string."
  - Random UUID'yi Postgres primary key olarak kullanmak, sıralı (v7) UUID'ye göre B-tree index'te sayfa parçalanmasına (write amplification, cache miss artışı) yol açar — bu PostgreSQL'in kendi UUID v7 desteği eklerken belgelediği bilinen bir performans deseni ([PostgreSQL 18 sürüm notları, `uuidv7()` fonksiyonu ekleniyor](https://www.postgresql.org/docs/18/functions-uuid.html)). Bu RFC kapsamında ölçülmedi, ama mimari niyetle (UUID v7 seçimi) doğrudan çelişiyor.

## Alternatifler ve Trade-off

| Alternatif | Artı | Eksi |
|---|---|---|
| **A — .NET 10'da kal, SDK'yı yükselt** | Master spec kararına sadık kalır; EF Core 10 + native UUID v7 API (`Guid.CreateVersion7()`) korunur; OpenAPI built-in kalır. | Lokal + CI ortamında .NET 10 SDK kurulumu gerekir (tek seferlik ~30 dk iş). GitHub Actions `ubuntu-latest` image'ında .NET 10 SDK'nın preinstalled olup olmadığı doğrulanmadı `[unverified 2026-07-23 — needs GitHub Actions runner image / actions/setup-dotnet check]`. |
| **B — .NET 8 LTS'e düş (mevcut, fiili karar)** | Anında çalışıyor; ekosistem (Hangfire, Npgsql, Testcontainers, Sentry.AspNetCore) net8 ile daha uzun süredir olgun ve test edilmiş. | LTS bitişi **10 Kasım 2026** ([.NET 8 and .NET 9 will reach End of Support on November 10, 2026 — .NET Blog](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)) — plana göre launch (9-15 ay, ~2027 Şubat-Nisan) **öncesinde**. UUID v7 ve OpenAPI kaybı yan etkileri var (yukarıda). |
| **C — .NET 9'a geç (ara adım)** | .NET 10'a göre daha az paket/SDK farkı; UUID v7 API'si mevcut (.NET 9'da eklendi). | .NET 9 **STS** (Standard Term Support) — yalnızca 18 ay destekleniyor, LTS değil ([.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)); launch'a kadar zaten EOL olur. Kazanç yok, ek karmaşıklık var. |

## Why

Lokal geliştirme makinesinde yalnızca .NET SDK 8 kurulu olması ve backend ekosisteminin (EF Core, Npgsql, Hangfire, Testcontainers) net8 ile daha az sürtünmeli çalışması, günlük geliştirme hızını doğrudan etkiliyor — bu gerçek ve meşru bir mühendislik gerekçesi. Ancak bu teknik kolaylık, `PHASE-0-MASTER-SPEC.md` § 2'de **kilitlenmiş** bir kararı hiçbir RFC veya spec güncellemesi olmadan geçersiz kıldı. CLAUDE.md'deki tek cümlelik not, "değişiklik bir RFC ile yapılır" (master spec § 0) kuralını karşılamıyor. Bu RFC, sapmayı retrospektif olarak belgeler ve **launch öncesi zorunlu bir yükseltme borcu yarattığını** açıkça işaretler — sessizce kabul edilirse launch tarihiyle EOL tarihi çakışması gözden kaçabilir.

## How to Apply

1. Bu RFC onaylanınca `docs/PHASE-0-MASTER-SPEC.md` § 2 karar #3 satırı güncellenmeli: ".NET 10" yerine "**.NET 8 LTS** (geçici — bkz. RFC-001, launch öncesi .NET 10/güncel LTS'e planlı geçiş)".
2. **Zorunlu takip riski (roadmap'e eklenmeli, `docs/13-project-management/01-roadmap.md`):** .NET 8 EOL'ı (2026-11-10) launch penceresiyle çakışıyor. Yükseltme sprint'inin ne zaman yapılacağı founder kararı — bu RFC tarih vermez, yalnızca riski işaretler.
3. `backend/global.json` eklenmeli, SDK sürümünü `8.0.1xx` bandına pinlemeli — CI/lokal SDK tutarlılığı için.
4. `Guid.NewGuid()` → UUID v7 üretimi: .NET 8'de framework'te yok; ya manuel implementasyon ya da bir NuGet paketi (ör. `UUIDNext`) gerekir. Bu bir backend-developer görevi, ayrı RFC gerektirmez ama bu RFC'nin "açık iş" listesine girer.
5. `Microsoft.AspNetCore.OpenApi` yerine net8-uyumlu bir alternatif (ör. `Swashbuckle.AspNetCore`) eklenmeli — API sözleşmesi görünürlüğü olmadan mobile/web-admin/Bruno contract testleri kör ilerliyor.

## Geri Dönüş Yolu (Revert Path)

1. 7 `.csproj`'da `<TargetFramework>` → `net10.0`.
2. Paket sürümleri 10.0.x bandına geri (EF Core, Npgsql, JwtBearer, Mvc.Testing, Extensions.*).
3. `AddOpenApi()` / `MapOpenApi()` çağrıları `Program.cs`'e geri eklenir, `Microsoft.AspNetCore.OpenApi` paketi geri gelir.
4. `Guid.NewGuid()` → `Guid.CreateVersion7()` üç dosyada geri çevrilir (`Entity.cs`, `IDomainEvent.cs`, `RequestIdMiddleware.cs`).
5. **Ön koşul:** geliştirme + CI ortamında .NET 10 SDK kurulu olmalı; `global.json` varsa (bkz. How to Apply #3) `8.0.1xx` → `10.0.1xx` güncellenmeli.

## Etkilenen Dosyalar

```
backend/src/Kuaforum.Api/Kuaforum.Api.csproj
backend/src/Kuaforum.Application/Kuaforum.Application.csproj
backend/src/Kuaforum.Domain/Kuaforum.Domain.csproj
backend/src/Kuaforum.Infrastructure/Kuaforum.Infrastructure.csproj
backend/tests/Kuaforum.Tests.Application/Kuaforum.Tests.Application.csproj
backend/tests/Kuaforum.Tests.Domain/Kuaforum.Tests.Domain.csproj
backend/tests/Kuaforum.Tests.Integration/Kuaforum.Tests.Integration.csproj
backend/src/Kuaforum.Api/Program.cs                          (AddOpenApi/MapOpenApi kaldırıldı)
backend/src/Kuaforum.Domain/Common/Entity.cs                 (Guid.CreateVersion7 → Guid.NewGuid)
backend/src/Kuaforum.Domain/Common/IDomainEvent.cs            (Guid.CreateVersion7 → Guid.NewGuid)
backend/src/Kuaforum.Api/Middleware/RequestIdMiddleware.cs   (Guid.CreateVersion7 → Guid.NewGuid)
```

Dolaylı etkilenen (güncellenmesi önerilen, bu RFC kapsamında değiştirilmedi):
```
docs/PHASE-0-MASTER-SPEC.md   (§ 2 karar #3 — .NET 10 hâlâ yazılı, çelişki)
docs/04-modules/01-auth.md    (UUID v7 kararı — Guid.NewGuid ile fiilen kırıldı)
docs/07-api/01-conventions.md (UUID v7 kararı — aynı)
```

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md) § 2, karar #3
- [`docs/GAP-ANALYSIS.md`](../GAP-ANALYSIS.md) — bu sapmayı ilk tespit eden doküman
- [`docs/04-modules/01-auth.md`](../04-modules/01-auth.md) — UUID v7 kararı
- [.NET 8 and .NET 9 will reach End of Support on November 10, 2026 — .NET Blog](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)
- [.NET support policy — Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

---

Son güncelleme: 2026-07-23

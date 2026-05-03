# Kuaforum

> Türkiye geneli kuaför/berber pazaryeri mobil uygulaması.

**Durum:** Phase 0 — Foundation. Henüz kod yok, dokümantasyon ve karar fazı.

## Üç Yüzey

- **Müşteri uygulaması** (Expo / React Native) — keşif, randevu, ödeme, navigasyon, değerlendirme.
- **Kuaför paneli** (mobil-first responsive) — işletme, stilist, hizmet, müsaitlik, randevu, raporlama.
- **Admin paneli** (web — Next.js) — kullanıcı/kuaför/ihtilaf/finans yönetimi.

## Stack

- **Backend:** .NET 8 / ASP.NET Core + EF Core 8 + Modular Monolith + CQRS
- **DB & Cache:** PostgreSQL 16 + PostGIS · Redis 7 · Meilisearch
- **Mobile:** Expo SDK 52+ · React Native 0.76+
- **Web admin:** Next.js 15
- **Hosting:** Hetzner Cloud Helsinki · Cloudflare CDN+WAF
- **PSP:** PayTR Pazaryeri (primary, T+1) · Iyzico (fallback) — `IPaymentProvider` abstraction
- **E-Arşiv:** Nilvera REST API
- **Push/SMS:** FCM · NetGSM

Detaylı stack ve gerekçeler: [`docs/03-architecture/02-tech-stack.md`](docs/03-architecture/02-tech-stack.md).

## Doküman Haritası

Tüm Phase 0 kararları için **[`docs/PHASE-0-MASTER-SPEC.md`](docs/PHASE-0-MASTER-SPEC.md)** baş referans.

Diğer alanlar için: [`docs/README.md`](docs/README.md).

## Geliştirme

> Bu repo henüz initialize edilmedi. Phase 0 onayı sonrasında `git init` + ilk commit + ardından Phase 1 (M1 Auth) başlar.

## Lisans

TBD — Phase 0 kapanışında karar.

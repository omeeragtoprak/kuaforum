# RFC-011 — Web Landing + Kuaför Public Profil Sayfaları

**Status:** ✅ Accepted — founder onayı 2026-08-05
**Tarih:** 2026-08-05
**Kapsam:** yeni `web/` projesi (Next.js, repo kökünde `web-admin/`'in yanında); backend: `Kuaforum.Application/Modules/Barber/GetBarberShopQueries.cs` (yeni slug-bazlı public query), `Kuaforum.Api/Endpoints/BarberEndpoints.cs` (yeni public route), `Kuaforum.Api/appsettings.Production.json` (`Cors:AllowedOrigins`)
**İlgili:** [`docs/GAP-ANALYSIS.md`](../GAP-ANALYSIS.md) (referans, dokunulmuyor), `docs/01-research/` (referans, dokunulmuyor), mevcut `web-admin/` (Next.js 15 App Router konvansiyonu referans alınır)

---

## Karar (founder, 2026-08-05)

Müşteriye dönük web yüzeyi iki parçadan oluşur: **(1)** SEO'lu landing page (platform tanıtımı) + **(2)** kuaför başına paylaşılabilir public profil sayfası `kuaforum.com/salon/{slug}`. Randevu CTA'sı native app'e yönlendirir (deep-link/store fallback) — web'de randevu/ödeme/giriş akışı **yok**. Tam web-app (web'den uçtan uca randevu) **reddedildi** — V2.

## Tasarım

### Kapsam sınırı

- **Landing:** platform tanıtımı, "nasıl çalışır", kuaför onboarding'e CTA. `[ara karar]` Şehir/hizmet bazlı programmatic SEO sayfaları (örn. `/erzurum/sac-kesimi`) bu RFC'nin kapsamı dışında tutuldu — scope'u şişirmemek için V1.5'e itiyorum; founder bunu ayrıca istemedi, tersini de söylemedi, bu yüzden karar sayılmaz, yalnızca bu doc'un sınırı.
- **İmplementasyon deltası (2026-08-06, review M1/M4):** RFC'nin kapsamı yalnız slug-bazlı profil sayfasıydı; implementasyonda ayrıca **sayfalı public liste** (`GET /v1/public/barber-shops`, il filtreli — landing'in "şehirdeki salonlar" dizini ve `/kuaforler` sayfası için) eklendi. Ayrıca profil sayfasında **tekil yorumlar YOK** — yalnız `ratingAverage`/`ratingCount` gösteriliyor (RFC "yorumlar (mevcut Review modülü)" diyordu); tekil yorum listesi bilinçli olarak V1.5'e bırakıldı (moderasyon/spam yüzeyi + sayfa ağırlığı). Public DTO'lar dar tutuldu: `OwnerId`/`CommissionTier`/`SubMerchantKey`/`Status` public yüzeye ÇIKMAZ (ham-JSON kontrat testiyle pinli).
- **Kuaför profil sayfası** (`kuaforum.com/salon/{slug}`): salon adı, adres (il/ilçe/mahalle), hizmet listesi + fiyat aralığı, stylist listesi, yorumlar (mevcut Review modülü). Fotoğraf galerisi **yok** — RFC-007'de zaten "salon fotoğraf galerisi V1.5" olarak dışarıda bırakıldı, bu profil sayfası da aynı kısıtla yaşıyor. CTA "Randevu Al" → deep-link (`kuaforum://salon/{slug}`), başarısızsa app store / play store.
- Web'de randevu, ödeme, giriş **hiçbiri yok** — bilinçli: RFC-010'un misafir-gezinme kararı **mobil uygulama** kapsamındadır, web ayrı ve daha dar bir yüzey.

### Backend — bir eksik var, geri kalanı hazır

- `GET /v1/barber-shops/{id:guid}` zaten `AllowAnonymous` (`BarberEndpoints.cs:42-46`) ve yalnızca `Status == Active` salonları dönüyor (`GetBarberShopByIdQuery` filtresi). Ancak yalnızca GUID ile sorgulanabiliyor — **slug ile değil**. `BarberShop.Slug` alanı mevcut, unique index'li (`ux_barber_shops_slug`, `BarberConfigurations.cs:38`) ama hiçbir query handler'da lookup key olarak kullanılmıyor, yalnızca output field.
- **Yeni:** `GetBarberShopBySlugQuery(string Slug)` + `owned.MapGet("/by-slug/{slug}", ...).AllowAnonymous()` — mevcut `GetBarberShopByIdQuery`'nin birebir kopyası, filtre `s.Slug == slug && s.Status == Active`. `Why:` SEO URL'i GUID değil slug taşımalı; GUID'li URL hem SEO'da hem paylaşımda (WhatsApp/Instagram bio) anlamsız.
- **Implementasyon notu (güvenlik):** `BarberShopDto` bugün `OwnerId` ve `CommissionTier` gibi iç alanlar taşıyor (`GetBarberShopQueries.cs:9-23`). Bu DTO **olduğu gibi** public web'e döndürülmemeli. Web-facing endpoint dar bir `PublicBarberShopDto` kullanmalı (Id, Name, Slug, Description, GenderType, adres alanları, Latitude/Longitude — `OwnerId` ve `CommissionTier` hariç). DTO'nun kesin şekli implementasyon planında netleşir, ama "iç alan sızdırma" bu RFC'de zorunlu kılınan bir kısıt — security-auditor review'u implementasyon kapanışında gerekir.
- **CORS:** `appsettings.Production.json:41-44` bugün yalnız `app.kuaforum.com` + `admin.kuaforum.com` içeriyor; yeni landing domaini (`kuaforum.com` / `www.kuaforum.com`) eklenmezse tarayıcı-taraflı API çağrıları CORS'a takılır. Not: SSR/SSG'de sunucu-taraflı fetch tarayıcı CORS kısıtına tabi değildir — yalnız client-side hydration/arama gibi tarayıcıdan yapılan çağrılarda gerekir. Next.js'te ISR/SSG tercih edilirse CORS ihtiyacı büyük ölçüde daralır; kesin mimari implementasyon planında netleşir.

### Web projesi

- `web/` — repo kökünde `web-admin/`'in yanında, ayrı Next.js projesi. `web-admin/app/` App Router konvansiyonu referans alınır; sürüm hizalaması için Next 15 + React 19 önerilir (tutarlılık, zorunlu değil).
- Profil sayfaları ISR (Incremental Static Regeneration) adayı — salon bilgisi sık değişmiyor. `[ara karar]` `revalidate` süresi implementasyon planında netleşir, bu doc bir değer sabitlemiyor. Landing tamamen statik (SSG).
- Deploy: mevcut Hetzner/Docker Compose/Caddy pipeline'ına (CLAUDE.md §3) ek bir servis — ayrı Dockerfile + Caddy route (`kuaforum.com` → yeni container, `admin.kuaforum.com` → mevcut `web-admin`, `api.kuaforum.com` → backend). Bu, devops-engineer kapsamında implementasyon planında detaylanır.

### Tasarım kalite çıtası

Founder şartı — alıntı: *"responsive tasarım muazzam olmalı."* Bu "yeterli" değil "öne çıkan" bir çıta:

| Kriter | Hedef |
|---|---|
| Core Web Vitals | LCP < 2.5s, CLS < 0.1 (Google sıralama sinyali, SEO hedefiyle doğrudan bağlı) |
| Responsive öncelik | Mobil-first — profil linki büyük olasılıkla WhatsApp/Instagram bio'sundan mobil tarayıcıda açılır, masaüstü ikincil |
| Tasarım süreci | Implementasyon aşamasında `frontend-design` / `web-design-guidelines` skill'leri zorunlu kullanılmalı — bu RFC talebi belirtiyor, uygulamayı yapmıyor |
| CI gate | Lighthouse audit (performance + SEO skoru eşiği) implementasyon planına eklenmeli |

Bu bölüm bir "how to apply" adımı değil, implementasyon planının review gate'i olarak taşınması gereken bir **kabul kriteri**.

### Test

- Backend: slug-lookup 200 (Active salon) + 404 (Draft/Suspended salon — slug var ama status uygun değil) + 404 (bilinmeyen slug); `PublicBarberShopDto`'nun `OwnerId`/`CommissionTier` **içermediğine** dair contract testi.
- Web: Lighthouse CI gate (implementasyon planında eşik netleşir).

## Why

Tam web-app reddedildi çünkü CLAUDE.md §13 push-back mantığıyla örtüşüyor: solo founder kapasitesinde üçüncü bir tam-özellikli yüzey (customer app zaten olgun, RFC-006 kapora checkout ile) bakım yükünü katlar, randevu akışı zaten mobile'da çalışıyor. SEO'lu landing + paylaşılabilir profil, düşük mühendislik maliyetiyle en yüksek GTM getirisini veriyor: kuaförler kendi salon linkini sosyal medya bio'suna koyabilir, arama motoru trafiği önce güven kurmaya hizmet eder, uygulama kurulumuna zorlamaz (deep-link → store fallback, pazaryerlerinde standart desen).

## How to apply

Sıra: backend `GetBarberShopBySlugQuery` + `PublicBarberShopDto` + route → CORS origin ekleme (Production) → `web/` proje iskeleti (Next.js 15 App Router, `web-admin` konvansiyonlarından türetilmiş) → landing statik sayfalar → profil sayfası (ISR) + deep-link CTA → devops (Dockerfile + Caddy route) → Lighthouse CI gate. Domain/DNS founder aksiyonu implementasyon başlamadan önce netleşmeli — Caddy route'u domaine bağlı.

## Founder Aksiyonları (bloklayıcı)

| Aksiyon | Neden gerekli |
|---|---|
| `kuaforum.com` domain DNS yönlendirmesi (mevcut Cloudflare CDN+WAF katmanına) | Landing'in canlı domaini |
| App Store / Play Store linkleri (deep-link fallback hedefi) — henüz yayınlanmış app yoksa geçici TestFlight/internal track linki | "Randevu Al" CTA'sının hedefi |
| Landing metin/marka içeriği (value prop, "nasıl çalışır" adımları, marka görselleri) | Placeholder ile canlıya çıkılmaz — RFC-008'in "placeholder legal metinlerle canlıya çıkılmaz" emsaliyle aynı disiplin |

## Bilinçli dışarıda bırakılanlar

Web'den randevu/ödeme/giriş (V2 — tam web-app reddedildi) · şehir/hizmet bazlı programmatic SEO sayfaları (V1.5, scope şişirmemek için bu RFC'de yok) · salon fotoğraf galerisi (RFC-007'de zaten V1.5) · `web-admin` ile kod paylaşımı/monorepo tooling (Turborepo vb. — iki ayrı Next.js projesi, şimdilik YAGNI) · çoklu dil (i18n) landing (TR-only launch, Erzurum pilot ile tutarlı — [RFC-004](RFC-004-erzurum-pilot.md)).

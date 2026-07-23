# RFC-002 — Arama: Meilisearch Yerine Yapısal SQL Filtreleme (MVP)

**Status:** Accepted (retrospektif — founder imzası bekliyor)
**Tarih:** 2026-07-23 (RFC yazım tarihi; kod değişikliği tarihi tespit edilemiyor — commit geçmişinde ayrı bir "Meilisearch eklendi" işlemi hiç yok)
**Kapsam:** `backend/src/Kuaforum.Application/Modules/Barber/SearchBarbersQuery.cs`, `infra/docker-compose.dev.yml`

---

## Karar

Kuaför keşif/arama akışı, `docs/PHASE-0-MASTER-SPEC.md`'de MVP differentiator olarak kilitlenen **Meilisearch tabanlı Türkçe NLP tam metin arama** yerine, **tamamen yapısal (structured) EF Core LINQ filtrelemesi** ile implement edildi: şehir, ilçe, cinsiyet tipi, walk-in, minimum puan, maksimum fiyat. Backend'de Meilisearch'e **sıfır referans** var.

**Önemli düzeltme (brief'teki çerçevelemeye göre):** Gerçek durum "Meilisearch yerine ILIKE" değil, **"Meilisearch yerine hiç free-text arama yok"**. `SearchBarbersQuery` içinde arama terimi (keyword) parametresi bile tanımlı değil — yalnızca yapısal filtreler var. Mobile `search.tsx` ekranında da bir metin arama kutusu yok, yalnızca filtre çipleri (cinsiyet/walk-in/puan/fiyat). Türkçe NLP arama iddiası (master spec § 3, "Temel iddialarımız" #3: "Türkçe NLP arama") şu an **implement edilmemiş durumda**, ILIKE'a bile düşürülmemiş.

İstisna: admin panelinde iki ayrı basit case-insensitive `Contains()` araması var — `AdminBarberShopQueries.cs:49` (shop adı/slug) ve `UserAdminQueries.cs:41-43` (telefon/e-posta/isim). Bunlar EF Core üzerinden `LOWER(x) LIKE '%terim%'` şeklinde SQL'e çevrilir; müşteri/customer tarafı keşif akışıyla ilgisi yok, yalnızca admin arka ofis aracı.

## Bağlam

- **Master spec kilitli kararı:**
  - § 2, karar #16: "MVP differentiators | **3'ü de MVP'de**: (a) cinsiyet+mahalle ayrımı, (b) walk-in simplified, (c) KVKK self-servis + **Türkçe NLP arama**"
  - § 2, satır 95: "KVKK self-servis veri silme + **Türkçe NLP arama (Meilisearch)**"
  - § 3, "Temel iddialarımız" #3: "**TR-özgü UX** — cinsiyet ayrımlı ... walk-in toggle, **Türkçe NLP arama**. Global oyuncuların yerelleştirme borcu."
  - Stack tablosu (§ 5, satır 154): "**DB:** PostgreSQL 16 + PostGIS · Redis 7 · **Meilisearch** (full-text + Turkish tokenizer)"
  - Mimari diyagram (§ 5, satır 135-143): Meilisearch ayrı bir servis kutusu olarak çizili.
- **Kod kanıtı:** `grep -rni "meilisearch" backend/` → 0 sonuç. `SearchBarbersQuery.cs` yalnızca `CityId`, `DistrictId`, `GenderType`, `OpenWalkInOnly`, `MinRating`, `MaxPriceMinor`, `Page`, `PageSize` parametreleri alıyor — free-text alan yok.
- **Infra tutarsızlığı:** `infra/docker-compose.dev.yml:36-46`'da `meilisearch` container'ı (`getmeili/meilisearch:v1.11`) hâlâ tanımlı ve `up -d` ile ayağa kalkıyor; `infra/.env.example:18-20`'de `MEILISEARCH_URL`/`MEILISEARCH_MASTER_KEY` env değişkenleri var; `infra/README.md:12`'de servis listesinde geçiyor. **Backend hiçbir zaman bu container'a bağlanmıyor** — boşuna RAM/CPU tüketiyor ve "arama Meilisearch ile çalışıyor" yanlış sinyali veriyor (yeni katılan geliştirici veya founder yanılabilir).
- **Rating hesaplama — denormalizasyon eksik:** `SearchBarbersQuery.cs:83-85` (MinRating filtresi) ve `:106-108` (response projeksiyonu), `BarberShop` başına **korelasyonlu subquery** ile `Review` tablosundan `Average(r.Rating)` hesaplıyor — her arama isteğinde, sayfadaki her shop için ayrı ayrı. `BarberShop` entity'sinde (`backend/src/Kuaforum.Application/.../BarberShop.cs`) `RatingAverage`/`RatingCount` gibi cache/denormalize edilmiş bir alan **yok**. Küçük veri hacminde (mevcut demo seed: 13 shop) sorun yaratmaz; shop sayısı ve review hacmi büyüdükçe her arama isteği N adet `AVG()` subquery çalıştırır — N+1 benzeri bir performans deseni.

## Alternatifler ve Trade-off

| Alternatif | Artı | Eksi |
|---|---|---|
| **A — Meilisearch'ü şimdi bağla** | Master spec kararına sadık; Türkçe tokenizer + typo-tolerance + facet search gerçek "arama" deneyimi verir. | Ek servis operasyon yükü (container, backup, sürüm takibi); solo founder kapasitesi için MVP'de gerekçesi zayıf — 13-birkaç yüz shop'ta EF Core filtreleme yeterli. |
| **B — SQL yapısal filtre (mevcut, fiili karar)** | Ek servis yok, EF Core ile tek query stack; küçük veri hacminde performans sorunu yok. | Free-text/Türkçe NLP arama **hiç yok** — master spec'in "kategori sahibi" iddiasının bir ayağı (TR-özgü UX, Türkçe NLP arama) implement edilmemiş durumda. Kullanıcı "Kadıköy'de ucuz erkek kuaförü" gibi serbest metin yazamaz, yalnızca filtre çipi tıklayabilir. |
| **C — PostgreSQL native full-text search (`tsvector`/`pg_trgm`)** | Meilisearch'ten daha hafif, ek servis yok, Türkçe için `unaccent` + trigram ile makul typo-tolerance sağlanabilir [PostgreSQL Turkish text search config — `pg_catalog.turkish`]. | Meilisearch'ün relevance ranking/facet UX'i kadar gelişmiş değil; Türkçe morfoloji (ek çekimleri) için ek tuning gerekir. |

## Why

Mevcut demo veri hacminde (13 shop, birkaç düzine review) yapısal filtreleme yeterli ve Meilisearch'ü şimdiden operasyona almak solo founder için erken karmaşıklık — bu MVP kapsamında savunulabilir bir mühendislik tercihi. Ama şu an implement edilen şey, master spec'te vaat edilen "Türkçe NLP arama" değil, **filtre-only bir liste ekranı**. Bu fark founder'a hiç raporlanmadı; `docker-compose.dev.yml`'de Meilisearch'ün ayakta kalması da yanlış "zaten var" izlenimi yaratıyor. RFC'siz sapma + yanıltıcı infra sinyali birlikte, izlenebilirlik açısından § 5 doküman disiplinini ihlal ediyor.

## How to Apply

1. **`infra/docker-compose.dev.yml`'den `meilisearch` servisi ve `meilisearch-data` volume'u kaldırılmalı** (backend hiç kullanmıyor — boşuna RAM/CPU). `infra/.env.example` ve `infra/README.md`'deki Meilisearch satırları da temizlenmeli. *(Not: Bu RFC yalnızca `docs/rfc/` altına yazım yetkisiyle hazırlandı — infra dosyalarındaki fiili silme işlemi ayrı bir görev/PR olarak yapılmalı.)*
2. **`docs/PHASE-0-MASTER-SPEC.md`** § 2 karar #16 ve § 3 madde 3 güncellenmeli: "Türkçe NLP arama (Meilisearch)" → "Yapısal filtre arama (MVP) — bkz. RFC-002; Meilisearch/full-text V1.5'e ertelendi."
3. **V1.5'te Meilisearch'e (veya Postgres full-text'e) dönüş kriterleri** — bu RFC'nin önerdiği eşikler (founder onayı gerekir, bağlayıcı değil):
   - Aktif shop sayısı **> 500** (arama sonuç listesi filtre-only ile kullanılamaz hale gelir) **VEYA**
   - Customer discovery/support talebi olarak "kuaför adı/hizmet adıyla arama yapamıyorum" şikâyeti tekrar ederse **VEYA**
   - `MinRating` filtresi P95 latency > 300ms'yi geçerse (performans bütçesi, CLAUDE.md § 9) — bu durumda önce rating denormalizasyonu (madde 4), sonra Meilisearch değerlendirilir.
4. **Rating denormalizasyonu (ayrı backend task, bu RFC kapsamında değil):** `BarberShop` entity'sine `RatingAverage`/`RatingCount` alanı eklenip, review approve/reject/delete event'lerinde (Outbox veya doğrudan handler) güncellenmesi önerilir — korelasyonlu subquery yerine O(1) okuma.
5. Mobile `search.tsx`'e bir free-text arama kutusu eklenmesi ayrı bir backend+mobile task'tır (bu RFC'nin V1.5 kriterlerinden bağımsız, MVP'de küçük bir iyileştirme olarak da düşünülebilir — founder kararı).

## Geri Dönüş Yolu (Revert Path)

Bu RFC bir "silme" kararı değil, "hiç eklenmemiş bir şeyi resmileştirme" kararı olduğu için klasik revert yok. Meilisearch'e geçiş **ileri yönlü bir yeni RFC** gerektirir (V1.5 kriterleri karşılandığında): Meilisearch client entegrasyonu, index sync job'ı (Outbox üzerinden), arama endpoint'inin free-text parametresi alacak şekilde genişletilmesi, mobile arama kutusu UI'ı.

## Etkilenen Dosyalar

```
backend/src/Kuaforum.Application/Modules/Barber/SearchBarbersQuery.cs   (yapısal filtre, free-text yok)
backend/src/Kuaforum.Application/Modules/Admin/AdminBarberShopQueries.cs (admin-only Contains() arama, satır 49)
backend/src/Kuaforum.Application/Modules/Admin/UserAdminQueries.cs       (admin-only Contains() arama, satır 41-43)
infra/docker-compose.dev.yml       (satır 36-46 — kullanılmayan meilisearch container)
infra/.env.example                 (satır 18-20 — kullanılmayan Meilisearch env değişkenleri)
infra/README.md                    (satır 12 — Meilisearch servis listesinde, yanıltıcı)
mobile/app/(tabs)/search.tsx       (filtre-only UI, free-text kutusu yok)
```

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md) § 2 karar #16, § 3, § 5
- [`docs/GAP-ANALYSIS.md`](../GAP-ANALYSIS.md) — "Türkçe NLP arama (Meilisearch) backend'de sıfır referans" tespiti
- [Meilisearch documentation](https://www.meilisearch.com/docs)
- [PostgreSQL full-text search](https://www.postgresql.org/docs/current/textsearch.html)

---

Son güncelleme: 2026-07-23

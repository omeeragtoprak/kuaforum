# RFC-004 — Fiili Pilot Şehir: Erzurum

**Status:** ✅ Accepted — founder onayı 2026-07-23 ("şimdilik Erzurum" kaydıyla, aşağıdaki founder kararı bloğuna bakınız)
**Tarih:** 2026-07-23 (RFC yazım tarihi; kod değişikliği tarihi tespit edilemiyor)
**Kapsam:** `backend/src/Kuaforum.Infrastructure/Persistence/Seeding/Tr81CitySeed.cs`, `DemoBarberSeeder.cs`, `mobile/src/store/discovery-store.ts`, `mobile/app/(tabs)/index.tsx`

---

## Karar

Kod tabanında **Erzurum** (plaka 25), demo müşteri deneyiminin ve mobile keşfet ekranının fiili varsayılan/pilot şehri olarak belirlendi — seed verisi ve mobile default-city mantığı bu kararı somutlaştırıyor. Bu, master spec'in kendi öngördüğü **pilot şehir seçim süreci** (Phase 1 sonu, customer discovery sprint çıktısı) hiç çalıştırılmadan yapıldı.

### Founder kararı (2026-07-23)

Founder onayı şu kayıtla verildi: **"Şimdilik Erzurum"** — ancak ürün Erzurum'a kilitli DEĞİLDİR:
- **81 ilin tamamı açık kalır** (zaten teknik olarak böyle — şehir seçici tüm illeri listeler); Erzurum yalnız varsayılan/onboarding odağıdır. Kim nereden bağlanırsa hizmet alabilmelidir.
- **Yurtdışı** founder tarafından olasılık olarak dile getirildi. Bilinçli kayıt: uluslararası kapsam **V2+ backlog** maddesidir — para birimi, PSP (PayTR TR-only), KVKK↔GDPR ayrımı, i18n (bkz. i18n kararı) ve vergi/e-Arşiv rejimi tamamen farklı iş paketleridir; MVP scope'una alınmamıştır. `Why:` solo founder kapasitesi + TR pazarı doğrulanmadan uluslararası yatırım premature.
- `How to apply:` Discovery store'a persistence eklenirken "son seçilen şehir hatırlanır, ilk açılış default'u Erzurum" davranışı korunur; GTM dokümanındaki aday-şehir analizi launch planlamasında yeniden değerlendirilebilir (bu RFC onu iptal etmez, erteler).

## Bağlam

- **Master spec'in öngördüğü süreç (henüz işletilmedi):**
  - § 2, karar #15: "Coğrafi kapsam | Yazılım: 81 il/ilçe açık ... **Onboarding fokus: 2-3 pilot şehir (kararı Phase 1 sonu)**"
  - § 13 (Açık Sorular), madde 2: "**MVP coğrafi pilot şehri** — kararı Phase 1 sonu (kuaför keşif sprint'i sonucu); şu anda erken."
  - `docs/13-project-management/05-customer-discovery-plan.md`: pilot şehir kararının **30 kuaför + 30 müşteri görüşmesi** (İstanbul 15 + Ankara 8 + İzmir 4 + Bursa 3 dağılımlı) sonrasında verilmesi planlanmış — Erzurum bu görüşme havuzunda **hiç yok**.
  - `docs/14-go-to-market/01-launch-strategy.md:44,49`: "Pilot şehir seçim kriterleri — Şehir nüfusu 500K-2M ... Adaylar (Phase 1 sonu kararı): **Bursa · Eskişehir · Konya · Antalya · Adana**." Erzurum bu aday listesinde de **yok** (Erzurum nüfusu ~760K — kriter aralığına teknik olarak girer, ama listede değil).
  - `docs/01-research/customer-discovery-findings.md` (planlı çıktı dokümanı) **hiç yazılmamış** — `docs/README.md`'de "⏳ Phase 0 sprint sonunda" olarak işaretli, henüz üretilmedi.
- **Kod kanıtı — Erzurum'un fiilen kilitlendiği yerler:**
  - `Tr81CitySeed.cs:67` — Erzurum için özel ilçe listesi tanımlı ("3 merkez ilçe + sık kullanılanlar"), diğer illerin çoğunda böyle özel bir ayrım yok.
  - `DemoBarberSeeder.cs:11-13` — "Creates 13 demo BarberShops (2 İstanbul + 2 Ankara + 2 İzmir + 1 Draft İstanbul + **6 Erzurum**) ... Erzurum yoğun seed çünkü demo customer'ın [varsayılan şehri]." Erzurum, diğer şehirlerin **3 katı** yoğunlukta seed edilmiş (6 shop / diğerlerinde şehir başına 2).
  - `mobile/src/store/discovery-store.ts:26-28` — `DEFAULT_CITY_PLATE = 25` (Erzurum), `DEFAULT_CITY_NAME = 'Erzurum'` sabit tanımlı; dosya başındaki yorum: "Default null — cities query yüklenince Erzurum (plate 25) otomatik seçilir (**test demo customer için**)."
  - `mobile/app/(tabs)/index.tsx:40-44` — cities query yüklendiğinde, kullanıcı henüz bir şehir seçmemişse, `useEffect` içinde Erzurum otomatik `setCity()` ile seçiliyor.
- **Bilinen sınır — persistence yok:** `discovery-store.ts:9` (kod yorumu) — "Persistence yok (uygulama açıldıkça default Erzurum tekrar seçilir)." Zustand store `persist` middleware'i olmadan tanımlı (`create<DiscoveryState>((set) => ({...}))` — AsyncStorage/SecureStore entegrasyonu yok). Kullanıcı başka bir şehir seçse bile, **uygulama her yeniden açılışta Erzurum'a geri döner.** Bu, gerçek kullanıcı testinde fark edilecek bir UX kusuru — dev/demo bağlamında zararsız, prod'da öyle değil.

## Alternatifler ve Trade-off

| Alternatif | Artı | Eksi |
|---|---|---|
| **A — Master spec'in sürecini işlet: customer discovery sprint'i çalıştır, sonra pilot şehir seç** | Veri odaklı karar; marketplace cold-start riskini (docs/13-project-management/02-risk-register.md) azaltır; founder'ın 30+30 görüşme yatırımı boşa gitmez. | Şu an zaten Erzurum'a kilitlenmiş kod/demo veri var — sprint çalıştırılıp farklı bir şehir çıkarsa (ör. Bursa), seed + mobile default yeniden yazılır. Zaman kaybı riski var ama tek seferlik. |
| **B — Erzurum'u fiili pilot olarak resmileştir (mevcut, fiili durum)** | Kod zaten hazır; founder'ın kişisel/bölgesel bağlantısı olabilir (doğrulanmadı) — hıza avantaj. | Master spec'in kendi tasarladığı doğrulama sürecini (30 kuaför + 30 müşteri görüşmesi) atlıyor. `docs/14-go-to-market/01-launch-strategy.md`'deki aday listesinde (Bursa/Eskişehir/Konya/Antalya/Adana) Erzurum yok — GTM planı ile kod arasında hedef şehir çelişkisi oluşuyor. Erzurum'un tek-şehir yoğunluk/likidite avantajları (ör. daha düşük rekabet, daha yüksek yerel görünürlük ihtimali) **hiç araştırılmadı** — bu RFC bunu doğrulayamıyor. |
| **C — Demo veriyi şehir-agnostik yap** (Erzurum yerine "Demo Şehir" adında kurgusal bir test şehri) | Pilot şehir kararını hiç ön yargılamaz, dev/demo amaçlı olduğu açık. | Gerçekçi TR il/ilçe verisiyle test etme avantajı kaybolur; ek iş. |

## Why

Erzurum'un demo veri ve mobile default'u olarak seçilmesinin **gerçek gerekçesi kod yorumlarında dahi belirtilmemiş** ("test demo customer için" — kimin test customer'ı, neden Erzurum, tespit edilemiyor `[unverified 2026-07-23 — needs founder açıklaması]`). Bu, master spec'in bilinçli olarak Phase 1 sonuna ertelediği bir stratejik karara (pilot şehir) kod seviyesinde fiilen cevap vermiş oluyor — GTM launch-strategy dokümanındaki aday listesiyle çelişiyor. Bu RFC, sapmayı belgeler ama **Erzurum'u pilot şehir olarak onaylamıyor**; founder'ın bunu bilinçli bir GTM kararı olarak mı yoksa yalnızca "elimde tanıdık test verisi olsun" amaçlı bir dev kısayolu olarak mı seçtiğini netleştirmesi gerekiyor.

## How to Apply

1. **Founder'a soru (bu RFC'nin onayı buna bağlı):** Erzurum, gerçek bir GTM/pilot şehir adayı olarak mı düşünülüyor, yoksa yalnızca demo/test verisi kolaylığı mı? Cevaba göre iki yol:
   - **Gerçek pilot adayıysa:** `docs/14-go-to-market/01-launch-strategy.md:49`'daki aday listesine Erzurum eklenmeli, nüfus/rekabet/lojistik açısından diğer adaylarla (Bursa, Eskişehir, Konya, Antalya, Adana) karşılaştırmalı bir not düşülmeli — bu ayrı bir GTM analiz işi (`product-strategist` agent kapsamı), bu RFC'nin kapsamında değil.
   - **Yalnızca demo/test verisiyse:** `docs/13-project-management/05-customer-discovery-plan.md`'deki süreç bozulmadan çalıştırılmalı; Erzurum seed verisi "demo/test amaçlı, pilot şehir kararını temsil etmez" diye net bir yorum satırıyla işaretlenmeli (kısmen zaten var, ama "pilot" kelimesiyle karıştırılmaması için güçlendirilebilir).
2. **Bilinen sınır olarak kayda geçirilmesi gereken:** `discovery-store.ts`'nin persist edilmemesi — mobile-developer agent'a devredilecek bir iyileştirme (Zustand `persist` middleware + `AsyncStorage`), bu RFC'nin kapsamı dışında ama burada açıkça not düşülüyor ki gerçek kullanıcı testinde sürpriz olmasın.
3. Bu RFC onaylanınca `docs/PHASE-0-MASTER-SPEC.md` § 13 madde 2 ("MVP coğrafi pilot şehri ... şu anda erken") güncellenerek bu RFC'ye referans verilmeli — "erken" durumu kısmen kod tarafından fiilen aşılmış durumda.

## Geri Dönüş Yolu (Revert Path)

1. `DemoBarberSeeder.cs`'teki Erzurum seed bloğu (6 shop) kaldırılır veya başka bir şehre taşınır; diğer şehirlerle (İstanbul/Ankara/İzmir) eşit dağılıma (şehir başına 2) çekilir.
2. `discovery-store.ts`'teki `DEFAULT_CITY_PLATE`/`DEFAULT_CITY_NAME` sabitleri kaldırılır veya nötr bir değere (`null`, kullanıcı manuel seçime zorlanır) çekilir.
3. `mobile/app/(tabs)/index.tsx:40-44`'teki otomatik Erzurum seçim `useEffect`'i kaldırılır.
4. `Tr81CitySeed.cs`'teki Erzurum'a özel ilçe genişletmesi kalabilir (81 il için genel veri kalitesi iyileştirmesi, pilot kararından bağımsız) — revert gerektirmez.

## Etkilenen Dosyalar

```
backend/src/Kuaforum.Infrastructure/Persistence/Seeding/Tr81CitySeed.cs      (satır 67 — Erzurum ilçe listesi)
backend/src/Kuaforum.Infrastructure/Persistence/Seeding/DemoBarberSeeder.cs  (satır 11-13, 237-317 — 6 Erzurum shop)
mobile/src/store/discovery-store.ts                                          (satır 26-28 — DEFAULT_CITY_PLATE/NAME, persistence yok)
mobile/app/(tabs)/index.tsx                                                  (satır 40-44 — otomatik Erzurum auto-select)
```

Çelişkili GTM dokümanı (dolaylı etkilenen, bu RFC'de değiştirilmedi):
```
docs/14-go-to-market/01-launch-strategy.md   (satır 49 — pilot şehir adayları listesinde Erzurum yok)
docs/13-project-management/05-customer-discovery-plan.md   (görüşme havuzunda Erzurum yok)
```

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md) § 2 karar #15, § 13 madde 2
- [`docs/14-go-to-market/01-launch-strategy.md`](../14-go-to-market/01-launch-strategy.md) § 4 (pilot şehir seçim kriterleri)
- [`docs/13-project-management/05-customer-discovery-plan.md`](../13-project-management/05-customer-discovery-plan.md)
- [`docs/GAP-ANALYSIS.md`](../GAP-ANALYSIS.md) — "Erzurum fiili pilot" tespiti

---

Son güncelleme: 2026-07-23

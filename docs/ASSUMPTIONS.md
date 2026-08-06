# ASSUMPTIONS — Otonom Geliştirme Direktifi (PROMPT) ↔ Mevcut Proje Eşlemesi

**Tarih:** 2026-08-06
**Bağlam:** Founder, greenfield bir proje için yazılmış "Otonom Geliştirme Direktifi"ni verdi. Proje ise greenfield DEĞİL — Cycle 12'de, 440 backend testi, 13 kabul edilmiş RFC, çalışan bir Android test APK'sı var. Direktif bu yüzden **sıfırdan kurulum planı olarak değil, denetim + tamamlama çerçevesi** olarak uygulanıyor. Bu dosya, direktifin BÖLÜM A'sını mevcut gerçekle doldurur ve çıkarılan varsayımları kayda geçirir.

---

## BÖLÜM A — dolduruldu (mevcut gerçekten çıkarıldı, tahmin değil)

```yaml
proje_adi:          "Kuaforum"
platform:           "React Native (Expo SDK 52) — mobile/ · Next.js 15 — web/ + web-admin/"
backend:            ".NET 8 + PostgreSQL 16 (PostGIS) + Redis + Hangfire"   # Supabase DEĞİL
pazar:              "Türkiye"    # dil TR, para TRY, saat Europe/Istanbul
v1_hedef_tarih:     "[AÇIK — founder belirlemedi]"
```

**Neden Supabase değil:** Direktifin varsayılan önerisi Supabase; ancak .NET 8 + Postgres mimarisi 440 testle çalışıyor, `EXCLUDE USING gist` çift-rezervasyon garantisi (M15) zaten kurulu, PostGIS/Hangfire/Outbox bağımlılıkları mevcut. Geçiş 6-8 haftalık yeniden yazım demek olurdu; founder bunu 2026-08-05'te açıkça reddetti (bkz. `docs/01-research/free-tier-hosting.md` + o günün kararı: "Mevcut stack + ücretsiz tier").

---

## Direktif ↔ Mevcut durum eşlemesi

| Direktif maddesi | Mevcut durum | Sonuç |
|---|---|---|
| **D4** çift-rezervasyon DB garantisi (`EXCLUDE USING gist`) | ✅ M15 migration'da kurulu, concurrent testlerle kanıtlı | Karşılandı |
| **D4** 15dk slot, Europe/Istanbul, UTC saklama | ✅ `ComputeAvailabilityQuery` | Karşılandı |
| **D4** `slot_holds` (5dk TTL geçici tutma) | ❌ YOK — Pending booking slot kilitliyor, 30dk/randevu-saati timeout'u var | **Gerçek boşluk** → GAPS |
| **D8** KVKK aydınlatma + parçalı rıza + hesap silme | ✅ RFC-008 (versiyonlu legal servis, consent gate, anonimleştirme) | Karşılandı |
| **E1** `ui-ux-pro-max` skill | ✅ 2026-08-05'te kuruldu, C5b'de kullanıldı | Karşılandı |
| **D2** Müşteri / Salon Sahibi / Platform Admin | ✅ Üçü de var (rol policy'leri RFC-007) | Karşılandı |
| **D2** **Çalışan/Stilist kullanıcı tipi** (kendi takvimi, salon geneline erişemez) | ⚠️ `Stylist` **entity** var ama **kullanıcı hesabı/girişi/paneli YOK**; `UserRole.Stylist` enum'da tanımlı fakat hiçbir yerde atanmıyor | **Gerçek boşluk** → GAPS + founder kararı |
| **D3** `REJECTED` ayrı durum | ⚠️ Reddetme `Cancelled(BarberRequested)` olarak modelleniyor | Semantik fark → DOMAIN'de karara bağlanacak |
| **D3** `EXPIRED` (onaysız kaldı) | ⚠️ `Cancelled(PendingTimeout)` olarak modelleniyor | Semantik fark |
| **D3** `auto_confirm=true` salon ayarı | ❌ YOK **ve RFC-013 ile ÇELİŞİYOR** (founder 2026-08-05: "her randevuda kuaför onayı zorunlu") | **Çelişki** → founder sorusu |
| **D3** `appointment_events` audit tablosu | ⚠️ Genel `audit_logs` var, randevu-özel event tablosu yok | Kısmi |
| **D5** Türkçe arama normalizasyonu (`search_normalized` + trigram) | ⚠️ Mobilde `normalizeTr` var; **backend aramada normalize kolon/index YOK**, free-text arama hiç yok (RFC-002: MVP'de yalnız yapısal filtre) | **Gerçek boşluk** → GAPS |
| **D6** sessiz saat (22:00–08:00), günün özeti, idempotency | ⚠️ Booking bildirimleri var; sessiz saat + günlük özet + tercih yönetimi yok | Kısmi |
| **D7** Değerlendirme (14 gün, tek yanıt, moderasyon) | ✅ Review modülü + moderasyon + owner yanıtı var; 14 gün penceresi doğrulanacak | Büyük ölçüde |
| **D1** V1'de müşteri in-app ödeme YAPMAZ | ✅ RFC-009: `Payment:OnlinePaymentsEnabled=false` (varsayılan kapalı) — direktifle **birebir uyumlu** | Karşılandı |
| **D1** Ödeme sağlayıcı soyutlaması (`PaymentProvider`) | ✅ `IPaymentProvider` + Mock/PayTR/Iyzico registry | Karşılandı |
| **D1** `subscriptions`/`entitlements` tabloları, özellik sınırları koda gömülmez | ⚠️ `PlatformSettings` + `BookingPolicy` (per-shop ayarlar DB'de) var; **plan/entitlement kavramı yok** | Kısmi → founder kararına bağlı |
| **D1** V2 gelir modeli = **aylık sabit abonelik** | ❌ **ÇELİŞKİ:** RFC-012 (founder onayı 2026-08-05) = **aylık ciro komisyonu** (%20, tamamlanan randevular üzerinden), implement edildi ve test edildi | **Çelişki** → founder sorusu |
| **D1** V2'de opsiyonel kapora | ⚠️ RFC-013 kapora **zaten implement edildi** (per-shop oran, ödeme-sonra-onay, %100 iade) ama RFC-009 flag'iyle kapalı | Direktifle uyumlu sayılabilir (V1 kapalı) |

---

## Çıkarılan varsayımlar (founder aksi söylemedikçe geçerli)

1. **Direktif, mevcut RFC'leri otomatik geçersiz kılmaz.** RFC-001..013 founder onaylı kararlardır; direktifle çeliştikleri yerler tek tek sorulur, sessizce ezilmez. (Direktif BÖLÜM H/4: "sessizce kapsam daraltma" yasak — tersi de geçerli: sessizce kapsam değiştirme de yasak.)
2. **"V1" = launch sürümü.** RFC-009'un ücretsiz büyüme modu = direktifin V1 tanımı. Bu iki kavram aynı şeydir.
3. **Sıfırdan FAZ 0-2 kurulumu yapılmayacak.** Proje iskeleti, veri modeli ve API zaten var; bu fazlar "denetle ve eksiği tamamla" olarak yorumlanır.
4. **`docs/` yapısı:** Direktifin istediği `DOMAIN.md`, `PROGRESS.md`, `GAPS.md`, `decisions/` üretilir; mevcut `docs/rfc/` (kararlar) ve `docs/GAP-ANALYSIS.md` (boşluklar) bunların yerine geçen olgun karşılıklarıdır — yeni dosyalar bunlara **referans verir**, içerik kopyalamaz.
5. **Ödeme SDK'sı kurulmayacak** (direktif D1 ile uyumlu): PayTR entegrasyonu iskelet + webhook seviyesinde duruyor, gerçek merchant hesabı gelmeden aktive edilmeyecek.

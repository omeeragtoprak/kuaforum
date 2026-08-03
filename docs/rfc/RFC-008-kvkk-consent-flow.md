# RFC-008 — KVKK Aydınlatma + Parçalı Rıza Akışı

**Status:** ✅ Accepted — founder onayı 2026-07-31 (brainstorming: 2 karar + tasarım onayı)
**Tarih:** 2026-08-03
**Kapsam:** `Kuaforum.Api` (legal endpoint), `Kuaforum.Application/Modules/Identity` (initial-consent, profil DTO), `mobile/app/legal/` + `app/consent.tsx` + AuthGate, login/profil metin düzeltmeleri
**İlgili:** GAP P0-11 (KVKK aydınlatma/ToS görüntülenemiyor, parçalı rıza yok); mevcut Consent entity (Version alanlı) + rıza yönetim UI'ı (revoke) hazır altyapı; İYS (GAP P1-16) AYRI iş

---

## Kararlar (founder, 2026-07-31)

1. **Rıza toplama yeri:** Yeni kullanıcıda **OTP doğrulama sonrası tek seferlik rıza ekranı** — Aydınlatma metni linki + ToS zorunlu onay + Marketing opsiyonel checkbox. Login'de checkbox ve "sadece metin linkleri" seçenekleri reddedildi (bundled consent riski / login kalabalığı).
2. **Mevcut kullanıcılar:** ToS rızası (güncel versiyonla) olmayan HERKESE ilk açılışta aynı ekran bir kez gösterilir; onaylamadan uygulamaya geçilemez (KVKK m.5 açık rıza). Version değişince otomatik yeniden sorulur.

## Tasarım

### Backend

- **Legal metin servisi:** `backend/src/Kuaforum.Api/Legal/` altında statik markdown (`aydinlatma-v1.md`, `kullanim-kosullari-v1.md` — içerik `[PLACEHOLDER — hukukçu metni bekleniyor, founder aksiyonu]` işaretli iskelet). `GET /v1/legal/{doc}` (AllowAnonymous, doc ∈ {aydinlatma, kullanim-kosullari}) → `{ doc, version, title, markdown }`. Versiyon dosya adından değil koddaki sabitten (`LegalDocuments.CurrentTosVersion = "v1"` vb.) — rıza kaydıyla aynı kaynak.
- **`requiresTosConsent`:** `GET /v1/users/me` DTO'suna eklenir; server-side türetilir: kullanıcının `ConsentType.TermsOfService` için `Version == CurrentTosVersion` olan granted rızası yoksa `true`. Mobile karşılaştırma YAPMAZ — tek doğruluk kaynağı.
- **`POST /v1/users/me/consents/initial`** — `{ acceptTos: true, acceptMarketing: bool }`: tek command'da ToS grant (CurrentTosVersion ile; `acceptTos != true` → 400) + Marketing true ise grant / false ise dokunma; idempotent (güncel ToS rızası zaten varsa no-op success); audit. Mevcut `UpdateConsent`/revoke akışlarına dokunulmaz.

### Mobile

- **`app/legal/[doc].tsx`:** metin görüntüleme — başlık/paragraf ayrımlı basit render (markdown kütüphanesi EKLENMEZ; hukukçu metni gelince gerekirse V1.5). Girişler: login legal linkleri (artık tıklanabilir), profil "Kullanım Koşulları" (disabled kalkıyor), consent ekranı.
- **`app/consent.tsx`:** iki metne link + zorunlu ToS onayı ("okudum, kabul ediyorum") + opsiyonel Marketing checkbox + "Onayla ve devam et" → initial endpoint → hydrate → uygulamaya. "Vazgeç (çıkış yap)" alternatifi. a11y: checkbox role + checked state.
- **Gate:** AuthGate/root yönlendirmesinde `user.requiresTosConsent` ise `/consent`'e — hem OTP sonrası hem mevcut kullanıcının ilk açılışında aynı mekanizma (tek gate, iki senaryo).
- **Temizlik:** login'deki "Devam ederek ... onay vermiş olursun" bundled cümlesi KALKAR; confirm.tsx'teki benzer KVKK cümlesi de "kişisel verilerin işlenmesine ilişkin aydınlatma metni" linkine döner.

### Test

- Integration: legal endpoint (200 + version; bilinmeyen doc 404); initial-consent (grant + audit + idempotency + acceptTos=false 400); `requiresTosConsent` türetimi (rıza yok → true; eski versiyon → true; güncel → false; initial sonrası → false).
- Mobile: gate yönlendirme helper'ı jest (requiresTosConsent true/false × rol senaryoları).

## Why

Bundled consent ("devam ederek kabul") KVKK'da geçersiz rıza sayılma riski taşır; ekranların hiç açılamaması aydınlatma yükümlülüğünü de karşılamaz. Versiyonlu tek-kaynak tasarım, hukukçu metni geldiğinde yalnız dosya + versiyon sabiti değişikliğiyle yeniden rıza turunu tetikler.

## Bilinçli dışarıda bırakılanlar

Gerçek hukukçu metinleri (founder aksiyonu — placeholder ile canlıya ÇIKILMAZ, launch checklist'ine bağlı) · İYS entegrasyonu (Marketing rızası toplanır, İYS bildirimi kampanya öncesi ayrı iş — GAP P1-16) · web-admin rıza raporu (V1.5) · Location/HealthData rızaları (mevcut yönetim ekranında kalır; kayıtta sorulmaz — kullanılmıyorlar).

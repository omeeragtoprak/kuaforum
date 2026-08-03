# KVKK Consent Flow (RFC-008) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Versiyonlu legal metin servisi + OTP-sonrası/ilk-açılış parçalı rıza ekranı + bundled-consent temizliği (RFC-008, founder onaylı — tüm davranış detayı RFC'de, plan yalnız görev sınırları + anchor'lar).

**Ön koşullar:** Suite: 126 domain + 66 application + 92 integration + 76 mobile jest yeşil. Working tree temiz, main origin'den 39 commit ileride. Docker açık. Testler proje bazında.

---

### Task K1: Backend — legal servis + initial-consent + requiresTosConsent

**Files:**
- Create: `backend/src/Kuaforum.Api/Legal/aydinlatma-v1.md` + `kullanim-kosullari-v1.md` (placeholder iskelet: başlık + 4-6 bölümlük gerçekçi KVKK aydınlatma yapısı [veri sorumlusu/işlenen veriler/amaçlar/aktarım/haklar] — her bölümde `> [PLACEHOLDER — hukukçu metni bekleniyor]`; csproj'a `CopyToOutputDirectory` gerekirse ekle, embedded resource da olur — karar implementer'ın, raporla)
- Create: `backend/src/Kuaforum.Application/Common/LegalDocuments.cs` (`CurrentTosVersion = "v1"`, `CurrentAydinlatmaVersion = "v1"`, doc-slug sabitleri — no magic strings)
- Create: `backend/src/Kuaforum.Api/Endpoints/LegalEndpoints.cs` (`GET /v1/legal/{doc}` AllowAnonymous → `{doc, version, title, markdown}`; bilinmeyen doc 404; Program.cs'e Map)
- Create: `backend/src/Kuaforum.Application/Modules/Identity/GrantInitialConsentsCommand.cs` (RFC §Backend: acceptTos zorunlu-true, Marketing opsiyonel, idempotent, audit "consent.initial_granted"; mevcut Consent grant kalıbını `UpdateConsentCommand`'dan incele — ConsentType/Version kullanımı birebir)
- Modify: `GET /v1/users/me` DTO + query (`grep -rn "GetMyProfile" backend/src`) — `RequiresTosConsent` bool (türetim: TermsOfService + granted + Version == CurrentTosVersion yoksa true)
- Modify: `backend/src/Kuaforum.Api/Endpoints/UserEndpoints.cs` — `POST /v1/users/me/consents/initial`
- Test: `backend/tests/Kuaforum.Tests.Integration/ConsentFlowTests.cs` (yeni; RFC §Test'in integration listesi — 6+ fact; mevcut Consent testi varsa kalıbını izle `grep -rn "Consent" backend/tests/Kuaforum.Tests.Integration`)

- [ ] TDD: testler önce, FAIL gör → implement → 3 backend projesi TAMAMI yeşil → commit `feat(kvkk): legal metin servisi + initial-consent + requiresTosConsent (RFC-008 K1)` + footer.

DİKKAT: ToS Consent'in mevcut "revoke edilemez" kuralı var mı (`grep -rn "TermsOfService" backend/src` — KVKK m.7 blokajı memory'de geçiyordu) — initial grant bu kuralla çakışmamalı; ayrıca daha önce ToS rızası HİÇ toplanmadıysa Consent tablosunda ToS satırı olmayabilir, türetim null-safe olmalı.

### Task K2: Mobile — legal ekran + consent ekranı + gate + temizlik

**Files:**
- Create: `mobile/app/legal/[doc].tsx` (LegalApi.get(doc) → başlık + bölüm render: markdown'ı satır bazlı işle — `#`/`##` başlık, `>` placeholder-uyarı kutusu, düz paragraf; kütüphane EKLEME)
- Create: `mobile/app/consent.tsx` (RFC §Mobile: iki link + zorunlu ToS onayı [checkbox role + checked] + Marketing checkbox + "Onayla ve devam et" → `AuthApi.grantInitialConsents` → `hydrate()` → router.replace uygulamaya; "Vazgeç (çıkış yap)" → signOut; loading/error state'ler)
- Create: `mobile/src/api/legal.ts` + AuthApi'ye `grantInitialConsents`; `Profile` tipine `requiresTosConsent`
- Modify: AuthGate/root yönlendirme (`grep -rn "AuthGate\|Redirect" mobile/app/_layout.tsx mobile/app/index.tsx` — mevcut yönlendirme mantığını bul): `user.requiresTosConsent` → `/consent` (hem OTP sonrası hem açılışta — tek gate; helper: `mobile/src/lib/consent-gate.ts` `resolvePostAuthRoute(user)` saf fonksiyon + jest)
- Modify: `mobile/app/(auth)/login.tsx` — bundled "devam ederek kabul" cümlesi KALKAR, legal linkler `/legal/aydinlatma` + `/legal/kullanim-kosullari`'na tıklanabilir olur; `(tabs)/profile.tsx` disabled "Kullanım Koşulları" → gerçek ekran; `booking/confirm.tsx` KVKK cümlesi → aydınlatma linkli kısa metin

- [ ] jest (consent-gate 4+ case) + `npx tsc --noEmit` temiz → curl smoke: legal endpoint + yeni kullanıcı OTP → profile requiresTosConsent:true → initial → false (backend Development'ta, bitince kapat) → commit `feat(mobile): KVKK rıza ekranı + legal görüntüleme + consent gate (RFC-008 K2)` + footer.

### Task K3: Kapanış

- [ ] Tam suite (backend 3 + mobile) → `docs/GAP-ANALYSIS.md` P0-11'e kapanış bloğu ("placeholder metinlerle canlıya çıkılmaz" launch-checklist notuyla) + RFC-008 İmplementasyon satırı + CLAUDE.md Cycle 10 cümlesi → commit `docs(rfc-008): KVKK rıza kapanışı — GAP P0-11` + footer.

## Self-Review
RFC kapsaması: legal servis (K1), rıza ekranı+gate (K2), temizlik (K2), docs (K3) — tam. Riskler: (1) ToS revoke-blokajı ile grant etkileşimi (K1 DİKKAT notu); (2) AuthGate değişikliği onboarding sihirbazının rol-değişim akışını bozmamalı (K2'de sihirbaz senaryosu elle düşünülecek: requiresTosConsent'li kullanıcı onboarding'e girebilir mi — HAYIR, önce consent; test notu); (3) login temizliği store-review metin beklentilerini etkilemez (metin kaldırma).

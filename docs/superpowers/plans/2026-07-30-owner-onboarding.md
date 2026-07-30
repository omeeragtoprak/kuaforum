# Owner Onboarding (RFC-007) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gerçek bir kuaförün mobile'dan tek başına Active olabilmesi: self-serve Customer→BarberOwner geçişi + 3 adımlı sihirbaz + gerçek KYC upload + rol policy'lerinin uygulanması (RFC-007, founder onaylı).

**Architecture:** Rol geçişi `CreateBarberShopCommand` içinde atomik (`User.PromoteToBarberOwner` — domain'de MEVCUT, Stylist reddi eklenecek); create-shop endpoint'i policy istisnası için ayrı gruba alınır, kalan owner grubu `BarberOwner` policy'sine geçer. Mobile'da `app/onboarding/` sihirbazı (Zustand adım state'i, idempotent devam), create sonrası token refresh + hydrate ile kuaför sekmeleri açılır. KYC upload mevcut `BarberApi.uploadKycDocument` üzerinden gerçek picker'larla.

**Tech Stack:** .NET 8 + MediatR + xUnit/Testcontainers · Expo RN + expo-image-picker + expo-document-picker + Zustand

**Ön koşullar:** 262 backend + 53 mobile test yeşil; working tree temiz; main origin'den 31 commit ileride; Docker açık. Testler proje bazında koşulur (slnx sorunu).

---

### Task 1: Backend — atomik rol geçişi

**Files:**
- Modify: `backend/src/Kuaforum.Domain/Modules/Identity/User.cs:106-115` (PromoteToBarberOwner — Stylist reddi)
- Modify: `backend/src/Kuaforum.Application/Modules/Barber/CreateBarberShopCommand.cs` (handler'a promote)
- Test: `backend/tests/Kuaforum.Tests.Domain/Identity/UserTests.cs` (varsa; yoksa mevcut Identity test dosyasını grep'le bul) + `backend/tests/Kuaforum.Tests.Integration/OwnerOnboardingTests.cs` (yeni)

- [ ] **Step 1: Failing testler.** Domain: `PromoteToBarberOwner`'ın mevcut testlerini bul (`grep -rn "PromoteToBarberOwner" backend/tests`), yoksa ekle:

```csharp
[Fact]
public void PromoteToBarberOwner_from_stylist_fails()
{
    // Stylist rolünde bir User kur (mevcut factory'lerle Customer yaratıp Role'ü Stylist'e
    // getiren mevcut bir yol var mı bak — yoksa reflection DEĞİL, domain'e uygun kurulum;
    // AddStylist akışında user'a Stylist rolü atanıyorsa o yolu kullan, atanmıyorsa bu
    // testi integration seviyesinde yaz ve raporda belirt).
    var r = stylistUser.PromoteToBarberOwner();
    Assert.True(r.IsFailure);
}

[Fact]
public void PromoteToBarberOwner_from_customer_succeeds()
{
    var user = User.CreateCustomerByPhone(PhoneE164.Create("+905551110011").Value).Value;
    var r = user.PromoteToBarberOwner();
    Assert.True(r.IsSuccess);
    Assert.Equal(UserRole.BarberOwner, user.Role);
}
```

Integration (`OwnerOnboardingTests.cs` — DepositPaymentTests'teki factory/dev-login kalıplarını izle; customer dev-login ile):

```csharp
// 1) Customer_create_shop_promotes_to_barber_owner_and_creates_draft:
//    customer login → POST /v1/owner/barber-shop {name, genderType, phone} → 200/201 + shopId;
//    DB: user.Role == BarberOwner, shop.Status == Draft, shop.OwnerId == user.
// 2) Second_shop_returns_409: aynı kullanıcı ikinci POST → 409.
// 3) Refresh_after_promotion_issues_token_with_new_role:
//    create sonrası POST /v1/auth/refresh (customer'ın refresh token'ı + AYNI deviceFingerprint) →
//    yeni accessToken'ın role claim'i "BarberOwner" (JWT'yi parse et — System.IdentityModel.Tokens.Jwt
//    zaten test projesinde mi bak, değilse base64 payload decode yeterli).
```

- [ ] **Step 2: FAIL gör** — `cd backend && dotnet test tests/Kuaforum.Tests.Integration --filter OwnerOnboarding -v minimal` (+ domain filtresi).

- [ ] **Step 3: Implementasyon.** `User.cs` — mevcut metoda Stylist guard'ı ekle (Admin guard'ının yanına):

```csharp
if (Role == UserRole.Stylist)
    return Result.Failure(Error.Validation("Stilist hesabı işletme sahipliğine yükseltilemez — işletme sahibiyle iletişime geçin."));
```

`CreateBarberShopCommand.cs` handler — `existing` kontrolünden SONRA, `BarberShop.Create`'ten ÖNCE:

```csharp
if (owner.Role == UserRole.Customer)
{
    var promote = owner.PromoteToBarberOwner();
    if (promote.IsFailure) return Result.Failure<Guid>(promote.Error);
}
else if (owner.Role != UserRole.BarberOwner)
{
    return Result.Failure<Guid>(Error.Forbidden("Bu hesap türü işletme oluşturamaz."));
}
```

Audit payload'ına `promoted = owner.Role.ToString()` benzeri iz ekle. (`Error.Forbidden` mevcut mu → Result.cs'e bak; yoksa mevcut Forbidden kalıbını kullan — InitiatePaymentCommand'da örneği var.)

- [ ] **Step 4: PASS** — ilgili filtreler + 3 backend projesi TAMAMI yeşil (DevLogin/seeder'lar Customer'dan promote ettiği için Stylist guard'ı onları KIRMAZ — yine de tam suite şart).
- [ ] **Step 5: Commit** — `feat(identity): self-serve Customer→BarberOwner — create-shop içinde atomik promote (RFC-007 Task 1)` + footer.

---

### Task 2: Backend — rol policy'leri (GAP P1-4)

**Files:**
- Modify: `backend/src/Kuaforum.Api/Endpoints/BarberEndpoints.cs:61,89,155`
- Test: `OwnerOnboardingTests.cs`'e ekle

- [ ] **Step 1: Failing testler:**

```csharp
// 4) Customer_cannot_call_owner_endpoints_but_can_create_shop:
//    customer login → PATCH /v1/owner/barber-shop/{rastgele-guid} → 403 (BarberOwner policy);
//    GET /v1/owner/barber-shop → 403; ama POST /v1/owner/barber-shop → 200 (istisna).
// 5) Customer_cannot_call_stylist_endpoints: PATCH /v1/stylists/{guid} → 403.
```

- [ ] **Step 2: FAIL gör** (bugün 404/400 dönüyor olabilir — assert 403 bekleyecek).

- [ ] **Step 3: Implementasyon.** `BarberEndpoints.cs`:
  - Satır 61: `var ownerGroup = ... .RequireAuthorization("BarberOwner");`
  - Satır 89'daki `ownerGroup.MapPost("", ...)` create endpoint'ini AYRI bir gruba taşı (policy istisnası — grup policy'si endpoint'ten geri alınamaz):

```csharp
// Create-shop: rol geçiş tetiği — Customer da çağırabilir (RFC-007); guard handler'da.
var onboardGroup = app.MapGroup("/v1/owner/barber-shop").WithTags("BarberShops").RequireAuthorization();
onboardGroup.MapPost("", /* mevcut delegate AYNEN taşınır */);
```

  - Satır 155: `stylistGroup ... .RequireAuthorization("Stylist");` (policy "Stylist" zaten BarberOwner'ı da kapsıyor — AuthSetup.cs:50).
  - DİKKAT: ownerGroup'ta müşteri-erişimli bir GET var mı tara (ör. kyc/status owner'a özel — sorun yok); `owned` grubu (satır 15, public listeler) DOKUNULMAZ.

- [ ] **Step 4: PASS** — yeni testler + TAM suite (mevcut owner testleri BarberOwner dev-login kullanıyor, kırılmamalı; kırılan olursa nedenini raporla — muhtemelen Customer token'ıyla owner endpoint'i çağıran eski bir test).
- [ ] **Step 5: Commit** — `feat(auth): rol policy'leri owner/stylist gruplarına — create-shop istisnalı (RFC-007 Task 2, GAP P1-4)` + footer.

---

### Task 3: Backend — KYC tip-bazlı zorunluluk

**Files:**
- Modify: `backend/src/Kuaforum.Application/Modules/Barber/SubmitKycCommand.cs` (min-2 kuralını bul: `grep -n "2" SubmitKycCommand.cs` — belge sayısı kontrolü)
- Test: `OwnerOnboardingTests.cs` veya `KycFlowTests.cs`'e ekle

- [ ] **Step 1: Failing testler:** kimliksiz submit (yalnız TaxLicense + SanitaryPermit) → 400 + mesaj "Kimlik" içerir; vergisiz submit → 400 + "Vergi"; TaxLicense+IdentityCard → mevcut akış başarılı; TaxLicense+IdentityCard+MykCertificate → başarılı (opsiyoneller serbest).
- [ ] **Step 2: FAIL gör.**
- [ ] **Step 3: Implementasyon** — min-2 kuralı yerine:

```csharp
var types = request.Documents.Select(d => d.Type).ToHashSet();
if (!types.Contains(DocumentType.TaxLicense))
    return Result.Failure<Guid>(Error.Validation("Vergi Levhası belgesi zorunludur."));
if (!types.Contains(DocumentType.IdentityCard))
    return Result.Failure<Guid>(Error.Validation("Kimlik belgesi zorunludur."));
```

(KycFlowTests'teki mevcut submit'ler 2 belgeyle çalışıyor — hangi tipleri kullandıklarına bak; TaxLicense+IdentityCard değilse testleri yeni kurala göre güncelle ve raporla.)
- [ ] **Step 4: PASS** — tam suite. **Step 5: Commit** — `feat(kyc): tip-bazlı zorunluluk — TaxLicense + IdentityCard (RFC-007 Task 3)` + footer.

---

### Task 4: Mobile — picker altyapısı + gerçek KYC upload

**Files:**
- `cd mobile && npx expo install expo-image-picker expo-document-picker`
- Create: `mobile/src/components/kyc/KycDocumentPicker.tsx` (~120 satır: tek belge kartı — başlık, durum [boş/yükleniyor/yüklendi/hata], "Kamera / Galeri / PDF" aksiyonları, yeniden dene; `BarberApi.uploadKycDocument` çağırır, sonuç `{ storageKey, fileName, contentType }` callback'ler)
- Create: `mobile/src/lib/kyc.ts` (belge tipi sabitleri + `REQUIRED_KYC_TYPES = ['TaxLicense','IdentityCard'] as const` + `canSubmitKyc(uploaded: {type: string}[]): boolean` saf helper)
- Modify: `mobile/app/(barber)/kyc.tsx` (256 satır) — manuel storage-key text input'ları KycDocumentPicker kartlarıyla değiştir: 2 zorunlu kart + "Belge ekle" (SanitaryPermit/MykCertificate/Other seçmeli); submit butonu `canSubmitKyc` sağlanana dek disabled; mevcut durum-takibi/re-submit UI'ı KORUNUR.
- Test: `mobile/src/lib/__tests__/kyc.test.ts` — `canSubmitKyc` (eksik kimlik → false; ikisi de var → true; opsiyonel eklenince hâlâ true)

- [ ] Adımlar: jest FAIL→implement→PASS; `npx tsc --noEmit` temiz; kamera/galeri izin akışı (`requestCameraPermissionsAsync` vb.) reddedilirse Türkçe açıklamalı Alert (crash/beyaz ekran yasak); app.json'a plugin gerekiyorsa (`expo-image-picker` plugin'i) ekle ve raporla.
- [ ] **Commit** — `feat(mobile): gerçek KYC upload — picker/kamera/PDF kartları (RFC-007 Task 4)` + footer.

---

### Task 5: Mobile — onboarding sihirbazı

**Files:**
- Create: `mobile/src/store/onboarding-store.ts` (Zustand: `step: 1|2|3`, adım 1-2 form alanları, `shopId: string|null`, `uploadedDocs`, `reset()`)
- Create: `mobile/app/onboarding/_layout.tsx` + `shop.tsx` (adım 1) + `address.tsx` (adım 2) + `documents.tsx` (adım 3)
- Modify: `mobile/app/(tabs)/profile.tsx` (275 satır) — `isCustomer()` iken "💈 Kuaför müsün? Salonunu ekle" satırı → `/onboarding/shop`
- Modify: `mobile/src/api/barber.ts` — eksik çağrı varsa ekle (createShop/setAddress zaten var mı grep'le bak; `GET /v1/owner/barber-shop` ile mevcut Draft shop sorgusu — idempotent devam için)

**Akış:** Adım 1 (ad + SalonGenderType seçimi + telefon) → `POST /v1/owner/barber-shop` → `shopId` store'a + **token refresh**: `mobile/src/api/client.ts`'teki refresh fonksiyonunu bul (`grep -n "refresh" client.ts`) ve dışa açık değilse minimal export ekle; refresh sonrası `useAuthStore.getState().hydrate()` → rol BarberOwner olur ama sihirbaz akışı BOZULMAZ (kullanıcı adım 2'ye devam eder; kuaför sekmelerine yönlendirme YALNIZ adım 3 submit sonrası). Adım 2: il/ilçe (GeographyApi.getCities/getDistricts — mevcut city-picker kalıbına bak, keşfet ekranında var) + açık adres → `PUT /v1/owner/barber-shop/{id}/address`. Adım 3: Task 4'ün KycDocumentPicker kartları → `POST /v1/owner/barber-shop/{id}/kyc/submit` → başarı ekranı "Başvurun incelemede — sonuç bildirimle gelecek" → `router.replace('/(barber)/today')`.

**İdempotent devam:** sihirbaz açılışında `GET /v1/owner/barber-shop` (BarberOwner rolündeyse) — Draft shop varsa adım 2'den başlat (adres var mı kontrolüne göre 2 veya 3). Customer rolünde her zaman adım 1.

**Kurallar:** her ekran <300 satır; her async'te loading/error state; accessibility (radio kartlarda `checked`); form validasyonları Türkçe; hata durumunda kullanıcı asla çıkmazda kalmaz (geri + tekrar dene).

- [ ] tsc + jest yeşil; mock uçtan uca curl smoke (customer dev-login → create → address → upload → submit → admin approve → shop Active) — backend Development'ta, işin bitince kapat.
- [ ] **Commit** — `feat(mobile): 3 adımlı owner onboarding sihirbazı + profil girişi + rol geçişi refresh (RFC-007 Task 5)` + footer.

---

### Task 6: Kapanış — tam doğrulama + docs

- [ ] Backend 3 proje + mobile tsc/jest TAMAMI yeşil (beklenen: 262+~12 backend, 53+~6 mobile).
- [ ] `docs/GAP-ANALYSIS.md` P0-9 maddesine "Güncelleme 2026-07-30" bloğu: (a) salon oluşturma + (b) gerçek KYC upload + (c) reject-kilidi ÜÇÜ DE kapandı — owner mobile'dan uçtan uca Active olabiliyor; P1-4 rol policy maddesi kapandı. `docs/rfc/RFC-007`'ye İmplementasyon satırı. CLAUDE.md Cycle 10 özetine tek cümle ek.
- [ ] **Commit** — `docs(rfc-007): owner onboarding kapanış — GAP P0-9 + P1-4 güncellemeleri` + footer.

---

## Self-Review Notları

- **Spec kapsaması:** RFC-007'nin tüm bölümleri eşlendi — rol geçişi (T1), policy'ler (T2), KYC kuralı (T3), upload (T4), sihirbaz+giriş+refresh+devam (T5), test/docs (T6). "Stylist reddi" domain'e T1'de ekleniyor (mevcut metot yalnız Admin'i reddediyordu — plan yazımında keşfedildi).
- **Riskler:** (1) Stylist rolünde gerçek kullanıcı henüz üretilmiyor olabilir (AddStylist user-rol ataması yapmıyorsa domain testi integration'a kayar — T1 Step 1 notu); (2) grup policy değişikliği eski testleri kırabilir — T2 Step 4 raporlama şartı; (3) picker'lar native modül — Expo Go'da çalışmaz, dev build gerekir (T4 raporda not); (4) refresh çağrısı deviceFingerprint eşleşmesi ister — client.ts mevcut fingerprint'iyle sorunsuz olmalı, T5'te doğrulanır.
- **Tip tutarlılığı:** `REQUIRED_KYC_TYPES` string'leri backend `DocumentType` adlarıyla ("TaxLicense"/"IdentityCard") birebir; SalonGenderType/il-ilçe tipleri mevcut API tiplerinden alınır.

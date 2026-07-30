# RFC-007 — Owner Onboarding: Self-Serve Rol Geçişi + Mobile Sihirbaz

**Status:** ✅ Accepted — founder onayı 2026-07-30 (brainstorming döngüsü, 2 karar + tasarım onayı)
**İmplementasyon:** tamamlandı 2026-07-30 (curl E2E doğrulamalı: create→refresh(BarberOwner)→address→upload→submit→approve→Active; mahalle verisi Erzurum-pilot kapsamında — mahalle adres adımında opsiyonel, genişletme GAP P2-12 backlog'unda)
**Tarih:** 2026-07-30
**Kapsam:** `Kuaforum.Domain/Modules/Identity` (User), `Kuaforum.Application/Modules/Barber` (CreateBarberShop, SubmitKyc), `Kuaforum.Api` (rol policy'leri), `mobile/app/onboarding/` (yeni sihirbaz), `mobile/app/(barber)/kyc.tsx`, müşteri profil girişi
**İlgili:** GAP-ANALYSIS P0-9 (owner onboarding kopuk) + P1-4 (rol policy'leri kullanılmıyor); Cycle 9 KYC onarımı (reject→re-submit, admin belge görüntüleme, sonuç bildirimleri — hepsi hazır altyapı)

---

## Kararlar (founder, 2026-07-30)

1. **Self-serve geçiş:** Müşteri, profilindeki "Kuaför müsün? Salonunu ekle" girişinden sihirbazı başlatır; ayrı kuaför kaydı ve admin onaylı başvuru REDDEDİLDİ (KYC zaten kalite kapısı; sürtünme minimum olmalı — kuaför arzı marketplace darboğazı).
2. **KYC zorunlu belgeler:** **Vergi Levhası (TaxLicense) + Kimlik (IdentityCard)** zorunlu; Ruhsat (SanitaryPermit) / MYK Ustalık / Diğer opsiyonel ek. "Herhangi 2 belge" kuralı tip-bazlı kurala evrilir (server-side). PayTR sub-merchant evrak eşlemesi `[unverified 2026-07-30 — merchant hesabı açılınca netleşir]`.

## Tasarım

### Rol geçişi — atomik, create-shop içinde

- `User.PromoteToBarberOwner()` domain metodu: yalnız `Customer → BarberOwner`; Stylist/Admin çağrısı reddedilir (Result.Failure). `Why:` ayrı "become-owner" endpoint'i salonu olmayan owner ara durumu (yetim state) yaratır — YAGNI.
- `CreateBarberShopCommandHandler`: çağıran Customer ise aynı transaction'da promote + shop Draft; kullanıcının zaten shop'u varsa 409; Stylist/Admin → 403.
- **Token yenileme:** rol JWT claim'inde — geçiş sonrası eski access token 15 dk "Customer" der. Mobile, create-shop başarısında mevcut refresh akışını tetikler (refresh handler kullanıcıyı DB'den okuyup taze claim üretir) → profil hydrate → kuaför sekmeleri açılır. Yeni endpoint YOK.

### Rol policy'leri (GAP P1-4 kapanışı)

- Owner endpoint grubu `RequireAuthorization("BarberOwner")` — **tek istisna:** salon oluşturma (geçiş tetiği; authenticated-any kalır, guard handler'da). Stylist endpoint'lerine de ilgili policy. `How to apply:` policy grupta, istisna endpoint'te explicit — gelecekte gruba eklenen endpoint'ler varsayılan korumalı doğar.

### KYC kural değişikliği

- `SubmitKycCommand`: mevcut "min 2 belge" → "TaxLicense VE IdentityCard mevcut" (+ opsiyoneller serbest). Hata mesajı hangi tipin eksik olduğunu söyler (Türkçe).

### Mobile — 3 adımlı sihirbaz (`app/onboarding/`)

- **Giriş:** müşteri profilinde "💈 Kuaför müsün? Salonunu ekle" satırı (yalnız `isCustomer`).
- **Adım 1 Salon:** ad + hizmet segmenti (SalonGenderType) + işletme telefonu. **Adım 2 Adres:** il/ilçe (mevcut geography API) + açık adres — geocoding YOK (V1.5, Maps key founder-bloklu). **Adım 3 Belgeler:** iki zorunlu kart (Vergi Levhası, Kimlik) + "belge ekle" ile opsiyoneller; kart başına kamera / galeri / PDF (`expo-image-picker` + `expo-document-picker`), yükleme durumu + hata/yeniden-dene; `POST /v1/uploads/kyc` mevcut.
- **Devam edilebilirlik:** adım state'i Zustand'da; adım 1 sonrası shop Draft backend'de olduğundan çıkıp dönen kullanıcı kaldığı adımdan sürer (idempotent devam — mevcut Draft shop'u olan kullanıcı sihirbazın 2. adımından başlar).
- Adım 3 submit → kuaför paneli + "Başvurun incelemede" durumu (rol geçişi + token refresh Adım 1'de, yukarıdaki "atomik create-shop" bölümünde gerçekleşir). Mevcut `(barber)/kyc.tsx` manuel storage-key input'u gerçek picker'lı upload'la değiştirilir (re-submit akışı Cycle 9 onarımıyla çalışıyor; onay/red bildirimleri mevcut).

### Test

- Integration: Customer create-shop → rol BarberOwner + shop Draft + refresh'te yeni rol claim'i; Stylist create-shop 403; ikinci shop 409; kimliksiz KYC submit 400 (tip-bazlı mesaj); Customer'ın owner-endpoint'ine erişimi 403 (policy pin'i); uçtan uca: create → upload → submit → reject → re-submit → approve → Active.
- Mobile: sihirbaz adım validasyon helper'ları jest.

## Bilinçli dışarıda bırakılanlar

Salon fotoğraf galerisi (V1.5) · geocoding/harita pin'i (V1.5) · stylist davet akışı (V1.5) · owner'ın rolü geri bırakması (V2 — süpheli senaryo, admin işi) · PayTR sub-merchant evrak otomasyonu (merchant hesabı sonrası).

# RFC-009 — Ücretsiz Büyüme Modu: Online Ödeme Feature-Flag'i

**Status:** ✅ Accepted — founder onayı 2026-08-05
**Tarih:** 2026-08-05
**Kapsam:** `Kuaforum.Application/Common/PaymentOptions.cs`, `Kuaforum.Infrastructure/DependencyInjection.cs`, `Kuaforum.Application/Modules/Payment/InitiatePaymentCommand.cs`, `Kuaforum.Infrastructure/BackgroundJobs/ExpirePendingBookingsJob.cs`, `Kuaforum.Application/Modules/Identity/Users/GetMyProfileQuery.cs`, `mobile/app/booking/confirm.tsx`, `Kuaforum.Api/appsettings*.json`
**İlgili:** [RFC-006](RFC-006-kapora-checkout.md) (kapora checkout — süperseden EDİLMİYOR, aktivasyonu bu RFC'nin flag'ine bağlanıyor), [`docs/GAP-ANALYSIS.md`](../GAP-ANALYSIS.md) P0-5 (referans, bu RFC'de değiştirilmiyor)

---

## Kararlar (founder, 2026-08-05)

1. Alıntı: *"şimdilik ücretsiz büyüme modu ama tüm altyapı hazır olsun ilerisi için, kuaför/kullanıcı sayısı arttıktan sonra."*
2. Launch'ta kuaför kaydı ücretsiz "abonelik". `BarberShop.CommissionTier` alanı (bugün tek değer `"standard"`) gelecekteki tier modelinin temeli sayılır — bu RFC yeni tier eklemiyor, alanı değiştirmiyor.
3. **Online ödeme feature-flag ile kapalı** — `Payment:OnlinePaymentsEnabled = false` default. Randevu salonda ödenir, komisyon tahsil edilmez.
4. RFC-006'nın kapora + komisyon + webhook altyapısı **koddan sökülmüyor** — flag açıldığında aynen devreye girer. Bu RFC, RFC-006'ya süperseden değil **askıya alma** ilişkisidir: RFC-006 geçerliliğini korur, yalnızca aktivasyonu bu flag'e bağlanır.

## Mevcut durum (kod kanıtı — bu RFC'nin üzerine kurulduğu zemin)

- Payment config bugün yalnız `DefaultProvider` / `CommissionRate` / `DepositRate` (`appsettings.json:5-9`, `PaymentOptions.cs:9-18`) — enable/disable alanı yok (`grep -rn "OnlinePaymentsEnabled\|Payment:Enabled"` → sonuç yok).
- Booking `Pending → Confirmed` bugün **iki** yoldan geçiyor:
  - **Otomatik:** `PaymentCompletionService.CaptureAndConfirmAsync` → `Booking.Confirm(paymentId)` (ödeme capture edildiğinde; `PaymentCompletionService.cs:29-42`, `Booking.cs:80-91`).
  - **Manuel:** `Booking.OwnerConfirm()` (`Booking.cs:98-109`) — kod yorumu birebir bu senaryoyu tarif ediyor: *"Owner manually confirms a Pending booking without an associated payment authorization. Used when PSP integration is unavailable (dev/Phase-1) or for in-person/walk-in flows."* Bu RFC'nin ihtiyaç duyduğu ikinci yol **zaten üretim kodunda var** ve tam bu senaryo için tasarlanmış — yeni bir onay mekanizması icat edilmiyor.
- `ExpirePendingBookingsJob` (`ExpirePendingBookingsJob.cs:34-36`) tamamen zaman bazlı: `Status == Pending && CreatedAt < UtcNow-30dk` → `Cancel(PendingTimeout)`. Ödeme tablosuna hiç bakmıyor; job'un kendi yorum satırı bunu zaten "ödenmemiş" varsayımına dayandırıyor (satır 15-17: *"since a Pending booking never had a captured payment, the downstream refund job is a documented no-op"*). **Flag kapalıyken bu varsayım geçersizleşir** — aşağıda detaylandırılıyor.

## Tasarım

### Backend

- `PaymentOptions`'a yeni alan: `public bool OnlinePaymentsEnabled { get; set; } = false;`. Config katmanlaması: `appsettings.json` (base, tüm ortamların mirasladığı) → **fail-closed default `false`**; `appsettings.Development.json`'a **açık `true` override** eklenir ki mevcut RFC-006 integration test suite'i (kapora/webhook/idempotency testleri, `Environment=Development` ile çalışıyor — bkz. `IntegrationTestFactory.cs:57`) regresyonsuz geçsin. Production dosyasında ayrıca `false` yazılmaz (base zaten `false`, açıkça yazmak niyeti daha okunur kılar — implementasyon tercihi).
- `InitiatePaymentCommandHandler.Handle`: flag `false` iken `Handle` başında `Result.Failure<InitiatePaymentResponse>(Error.Conflict("Online ödeme şu an kapalı; randevu salonda ödenir."))`. Bu, mevcut "zaten ödenmiş" 409 idiomunun birebir kopyası (`InitiatePaymentCommand.cs:73-79` ile aynı `Error.Conflict(...)` deseni; `PaymentEndpoints.cs:48-50` zaten `Error.Conflict` kodunu 409'a map ediyor — endpoint katmanında değişiklik gerekmiyor). Bu guard, mobile flag'i yanlış okusa/eski sürüm hâlâ initiate çağırsa bile ikinci savunma hattı işlevi görür.
- **Booking akışı flag kapalıyken değişmiyor:** `InitiateBookingCommand` aynen kalır — booking yine `Pending` yaratılır (mobile, booking oluşturma ile ödeme başlatmayı zaten iki ayrı çağrı olarak yapıyor, bkz. `confirm.tsx:38-56`). Flag kapalıyken mobile ikinci çağrıyı (`PaymentApi.initiate`) hiç yapmaz; booking `Pending` kalır ve **yalnızca `[OwnerConfirm]` aksiyonuyla** `Confirmed`'e geçer.
- **Implementasyon notu — `ExpirePendingBookingsJob` (kritik, davranış değişikliği zorunlu):** Flag açıkken job'un "30 dk'da ödeme gelmezse iptal et" mantığı doğruydu çünkü `Pending` = "ödeme bekleniyor" varsayımı geçerliydi. Flag **kapalıyken** `Pending` artık "kuaför onayı bekleniyor" anlamına gelir ve bir kuaförün telefonuna bakıp onaylaması dakikalar değil saatler sürebilir. Job'u olduğu gibi bırakırsak flag kapalıyken oluşan **her** randevu 30 dk sonra otomatik iptal olur — sessiz veri/talep kaybı, gerçek bir regresyon. Zorunlu değişiklik: job'un eşiği flag'e göre dallanmalı.

  **Açık soru → Karar (2026-08-05):** `Payment:OnlinePaymentsEnabled == false` iken owner onay penceresi = **randevu saatine kadar** (`ScheduledAt` geçmiş hâlâ `Pending` olan randevular `PendingTimeout` reason'ıyla iptal edilir). Ayrı bir `Booking:OwnerConfirmTimeout` config'i **eklenmiyor** (YAGNI). Gerekçe: ek config yükü olmadan doğal bir deadline zaten var — randevu saati; kuaför o saate kadar onaylamazsa randevu zaten anlamsızlaşıyor (müşteri salona gelse dahi onaysız hizmet başlamaz), dolayısıyla saatin geçmesi hem "kuaför yanıt vermedi" sinyali hem "artık gerçekleşemez" sinyali aynı anda — iki ayrı eşik icat etmeye gerek yok. Job'un kendi yorum satırındaki DoS/takvim-kilidi endişesi (üstteki genel açıklama) flag kapalıyken de geçerli kalmaya devam ediyor; bu karar onu da kapatıyor çünkü her Pending randevunun kendi `ScheduledAt`'i zaten sonlu bir üst sınır.
- Owner tarafı "Salonda tahsil edilecek: Y TL" rozeti (RFC-006 §Tasarım/Owner) kod değişikliği gerektirmiyor — rozet `bookingTotal − onlinePaid` türetiyor; `onlinePaid = 0` olduğunda otomatik tam tutarı gösterir.

### Mobile

- `booking/confirm.tsx`: ödeme seçim kartı (Kapora/Tam ödeme, RFC-006) flag `false` iken tamamen gizlenir; yerine "Randevu kuaför onayına gönderilecek, ödeme salonda yapılır" bilgi satırı. `handleConfirm()` flag kapalıyken `PaymentApi.initiate(...)` çağrısını atlar — mevcut "ödeme başlatılamadı ama randevu oluştu" hata dalı (`confirm.tsx:44-52`) bu senaryoda "hata" değil "beklenen akış" olduğundan ayrı, olumlu bir mesajla ("Randevun kuaföre iletildi, onay bekleniyor") gösterilmeli; mevcut Alert metni yeniden kullanılmamalı.
- Flag'in mobile'a taşınma yolu: **yeni endpoint açılmıyor.** `MyProfileDto` (`GetMyProfileQuery.cs:12-25`, zaten server-türetilmiş `RequiresTosConsent` gibi bool alanlar taşıyor — RFC-008 emsali) içine `OnlinePaymentsEnabled` eklenir. `[ara karar]` Ayrı bir authsuz "public config" endpoint'i açmak yerine profile DTO'sunu seçtim çünkü RFC-010'un misafir-gezinme kararı gereği booking oluşturma zaten login gerektiriyor (RFC-010 §Kararlar madde 2) — confirm ekranına yalnız login'li kullanıcı ulaşır, dolayısıyla authsuz bir config kaynağına ihtiyaç yok.

### Test

- Integration: flag `false` iken `InitiatePaymentCommand` → 409 "ödeme şu an kapalı"; flag `false` iken booking create → `Pending` → `OwnerConfirm` → `Confirmed` (ödeme hiç yok); flag `false` iken `ExpirePendingBookingsJob`'un 35dk eski ama `ScheduledAt` gelecekte olan bir Pending'i İPTAL ETMEMESİ, `ScheduledAt` geçmiş bir Pending'i iptal etmesi (reason `PendingTimeout`); flag `true` iken mevcut RFC-006 test suite'i regresyonsuz (appsettings.Development.json override sayesinde).
- Mobile: `confirm.tsx` flag-false render (ödeme kartı yok, bilgi satırı var) jest testi.

## Why

Founder net: launch'ta sürtünme (PayTR merchant hesabı, KYC evrak eşlemesi, PSP onayı) büyüme hızını riske atmasın; ama RFC-006'nın kapora+komisyon+webhook mimarisini söküp sonradan yeniden yazmak hem gereksiz iş hem de "büyüdükten sonra ödeme aç" günü geldiğinde çok haftalık yeniden inşa riski taşır. Feature-flag, PSP'yi kritik yoldan çıkarırken kodu canlı tutuyor — tek config değişikliğiyle tersine çevrilebilir. Riskin asıl kaynağı flag'in kendisi değil, `ExpirePendingBookingsJob`'un ödeme-varsayımlı tasarımı; bu satırda sessizce yanlış davranacağı için implementasyon notunda ayrıca vurgulandı — dokunulmazsa flag kapalı her randevu 30 dk'da kaybolur.

## How to apply

Sıra: `PaymentOptions.OnlinePaymentsEnabled` + config katmanlama (base `false`, Development `true`) → `InitiatePaymentCommandHandler` guard → `ExpirePendingBookingsJob` flag-bazlı eşik ayrımı (flag kapalıyken `ScheduledAt` geçmiş Pending'ler — ayrı config yok, bkz. yukarıdaki karar) → `MyProfileDto.OnlinePaymentsEnabled` → mobile `confirm.tsx` dallanma. Flag'in launch-sonrası açılışı yalnızca config değişikliği + PayTR merchant hesabı bağlanmasıyla olmalı, kod değişikliği gerektirmemeli — bu RFC'nin başarı kriteri budur.

## Bilinçli dışarıda bırakılanlar

Gerçek ücretli "tier" modeli / abonelik planları (V1.5+, `CommissionTier` bugünkü tek `"standard"` değerinde kalıyor, bu RFC yeni tier eklemiyor) · flag'i admin panelinden runtime'da açıp kapatma (bu RFC'de yalnız config+restart; runtime toggle ayrı bir feature-flag servisi/Redis gerektirir, şimdilik YAGNI) · ayrı bir owner-onay timeout config'i (`Booking:OwnerConfirmTimeout`) — karar verildi: EKLENMİYOR, randevu saati doğal deadline olarak kullanılıyor (yukarıdaki "Açık soru → Karar") · walk-in akışı — DİKKAT (review bulgusu 2026-08-05): flag-kapalı job dalı `ScheduledAt` geçmiş Pending'leri iptal ettiğinden `BookingType.WalkIn` (ScheduledAt≈now) ilk 15dk tick'inde iptal kapsamına girer; mobile şu an isWalkIn göndermediği için canlı yol yok, walk-in aktive edilirse grace/test şart · İyzico fallback aktivasyonu (RFC-006'da zaten V1.5).

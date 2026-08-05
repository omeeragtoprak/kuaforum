# RFC-013 — Per-Shop Kapora Ayarı + Dinamik Randevu Kuralları: Önce Kapora, Sonra Onay

**Status:** ✅ Accepted — founder onayı 2026-08-05
**Tarih:** 2026-08-05
**Kapsam:** `Kuaforum.Domain/Modules/Barber` (`BarberShop` — `BookingPolicy` value object), `Kuaforum.Domain/Modules/Booking` (`Booking` — ödeme/onay ayrımı), `Kuaforum.Domain/Modules/Payment` (`PaymentCompletionService` davranış değişikliği), `Kuaforum.Application/Modules/Booking` (`InitiateBookingCommand` — policy enforcement), `Kuaforum.Infrastructure/BackgroundJobs` (`ExpirePendingBookingsJob` — per-booking dallanma), `Kuaforum.Api`, `mobile/app/(barber)/` (policy ayar ekranı), `mobile/app/booking/` (confirm/payment ekranları — kapora bilgisi)
**İlgili:** [RFC-006](RFC-006-kapora-checkout.md) (kapora checkout altyapısı — bu RFC ödeme-sonrası otomatik onay davranışını değiştiriyor, kapora oranı artık global değil per-shop), [RFC-009](RFC-009-free-growth-mode.md) (online ödeme flag'i — bu RFC'nin master-switch'i, aynen kalıyor), [RFC-012](RFC-012-monthly-commission-billing.md) (`PlatformSettings` admin-sınır mekanizması paylaşılıyor)

---

## Kararlar (founder, 2026-08-05)

1. **Kapora per-shop ayarı:** kuaför panelinden aktif/pasif + yüzde (admin'in koyduğu min-max aralığında, örn. %5-50); pasifse müşteri direkt (ödemesiz) talep oluşturur. Global `Payment:OnlinePaymentsEnabled` (RFC-009) **master-switch** — PSP yokken kimse kapora aktif edemez; ayar ekranı "PSP aktif değil" bilgisi gösterir.
2. **Sıra: ÖNCE KAPORA, SONRA ONAY.** Kapora-aktif kuaförde müşteri kaporayı öder → talep `Pending` kalır → kuaför onaylar → `Confirmed` + bildirim. Kuaför reddederse (`BarberRequested`, %100) veya randevu saatine kadar onaylamazsa (`PendingTimeout`, %100) **otomatik tam iade** (mevcut oto-refund zinciri). Mevcut "ödeme → otomatik Confirm" davranışı **değişir**: kapora ödemesi booking'i Confirm ETMEZ, yalnız "ödendi" işaretler; Confirm her zaman kuaför onayıyla. İade sürecinin dürüst iletişimi (PSP'ye göre birkaç gün) şart.
3. **Her randevuda kuaför onayı zorunlu** (kapora'lı/kaporasız): `Pending → OwnerConfirm → Confirmed` + müşteri bildirimi. Onay penceresi: randevu saatine kadar (RFC-009 kararı geçerli).
4. **Dinamik anti-spam (per-shop, kuaför panelinden):** (a) müşteri başına eşzamanlı bekleyen talep limiti (**default 2**), (b) aynı müşteri iki randevu arası min gün (**default kapalı**), (c) müşteri başına aylık randevu limiti (**default kapalı**) — hepsi aç/kapa + değer; admin global tavan/taban koyar. **Mevcut global 3-Pending limiti korunur** (platform tabanı).

## Mevcut durum (kod kanıtı — bu RFC'nin üzerine kurulduğu zemin)

- Bugün `PaymentCompletionService.CaptureAndConfirmAsync` ödeme capture edildiğinde **doğrudan** `booking.Confirm(payment.Id)` çağırıyor (`PaymentCompletionService.cs:38-42`) — bu RFC'nin retire ettiği tam olarak bu satır.
- `Booking.Confirm(Guid paymentId)` (`Booking.cs:80-91`) ve `Booking.OwnerConfirm()` (`Booking.cs:98-109`) bugün **iki ayrı** metod; ikisi de `Pending → Confirmed`, ikisi de `BookingConfirmedEvent` yayıyor. `OwnerConfirm()` `PaymentId`'ye dokunmuyor — bu RFC'nin tasarımının çalışmasını sağlayan kritik detay (aşağıda kullanılıyor).
- `Booking.ComputeRefundPercentage` **zaten** `BarberRequested` ve `PendingTimeout` reason'larında %100 döndürüyor (`Booking.cs:194,196`) — founder'ın istediği "otomatik tam iade" davranışı **halihazırda üretim kodunda var**, yeni bir iş kuralı icat edilmiyor, yalnızca bu RFC bu path'i daha sık/rutin tetikleyecek.
- Global bekleyen-talep limiti bugün sabit kod: `InitiateBookingCommand.MaxActivePendingBookingsPerCustomer = 3`, **shop'tan bağımsız** sayıyor (`CustomerId == request.CustomerId && Status == Pending`, hiç `BarberShopId` filtresi yok — `InitiateBookingCommand.cs:56-66`).
- `ExpirePendingBookingsJob` bugün (RFC-009 sonrası) global flag'e göre dallanıyor: flag açıkken 30dk unpaid-Pending eşiği, flag kapalıyken `ScheduledAt` eşiği. Bu RFC bunu **per-shop** hale getirdiği için (aynı platformda hem kapora-aktif hem kapora-pasif shop aynı anda var olabilir) global flag tek başına yetersiz kalıyor — aşağıda detaylandırılıyor.
- `Payment.PlatformCommissionMinor` bugün `NetToBarberMinor` hesabında düşülüyor (`Payment.cs:78`) — [RFC-012](RFC-012-monthly-commission-billing.md) ile komisyon artık ödeme anında değil ay sonu statement'ta kesildiğinden, bu RFC'nin kapora akışında bu alan **sıfırlanır** (aşağıda).

## Tasarım

### `BarberShop.BookingPolicy` (yeni value object)

| Alan | Tip | Varsayılan | Not |
|---|---|---|---|
| `DepositEnabled` | bool | `false` | Global `Payment:OnlinePaymentsEnabled == false` iken bu alan `true` olamaz (validasyon reddeder) — **master-switch** |
| `DepositPercent` | int | — (yalnız `DepositEnabled` iken anlamlı) | Admin sınırı: `PlatformSettings.DepositPercentMin/Max` (örn. 5-50) — [RFC-012](RFC-012-monthly-commission-billing.md)'nin `PlatformSettings` mekanizmasını paylaşır |
| `MaxPendingPerCustomer` | int | **2** | Açık by default (founder'ın "(default 2)" ifadesi — (b)/(c)'nin "(default kapalı)" ifadesiyle kasıtlı tezat) |
| `MinDaysBetweenBookings` | int? | `null` (kapalı) | |
| `MaxBookingsPerMonth` | int? | `null` (kapalı) | |

`RFC-006`'nın global `PaymentOptions.DepositRate` (config, `%15`) **kalkmıyor** ama artık yalnızca *başlangıç değeri / fallback* anlamına geliyor — gerçek oran artık her shop'un kendi `DepositPercent`'i. `[ara karar]` Mevcut shop'lar için migration: `DepositEnabled = false` (yeni davranış kimseyi zorlamaz, kuaför bilinçli olarak açar).

### Ödeme → Onay ayrımı (madde 2, kritik domain değişikliği)

- **Yeni domain metodu:** `Booking.MarkPaymentReceived(Guid paymentId)` — guard: `Status == Pending` (aksi halde Failure; aynı `paymentId` ile ikinci çağrı idempotent no-op — webhook retry güvenliği, RFC-006'nın `confirmOnlyIfPending` ruhuyla aynı). **Status'u değiştirmez**, yalnız `PaymentId` alanını set eder + `BookingPaymentReceivedEvent` yayar (yeni event — "ödeme alındı, onay bekleniyor" bildirimini tetikler). `ConfirmedAt` set edilmez.
- **`PaymentCompletionService.CaptureAndConfirmAsync`** (`PaymentCompletionService.cs:38-42`) değişir: `booking.Confirm(payment.Id)` çağrısı **`booking.MarkPaymentReceived(payment.Id)`** ile değiştirilir.
- `[ara karar — founder'ın "her randevuda" ifadesinin en tutarlı okuması]` Bu değişiklik **hem `PaymentKind.Deposit` hem `PaymentKind.FullOnline`** için geçerli — founder'ın madde 3'teki "her randevuda kuaför onayı zorunlu" ifadesi ödeme türünden bağımsız okunuyor. Yani kapora-aktif bir shop'ta müşteri "tamamını öde" seçse bile booking otomatik Confirmed olmaz, owner onayı bekler. Bu, RFC-006'nın orijinal "tam ödeme = eski as-built davranış" varsayımını da değiştirir — **founder'a açıkça doğrulanmalı**, yanlışsa yalnız `Deposit` kind'ına daraltmak tek satırlık değişiklik.
- `OwnerConfirm()` (`Booking.cs:98-109`) **değişmiyor** — zaten `PaymentId`'ye dokunmuyor, dolayısıyla `MarkPaymentReceived` sonrası çağrıldığında önceden set edilmiş `PaymentId`'yi korur. Tek onay yolu budur artık.
- **İmplementasyon notu:** `Booking.Confirm(Guid paymentId)` metodu bu değişiklik sonrası **çağrılmayan kod** haline gelir (dead code). Silinip silinmeyeceği veya test/geçiş amaçlı bırakılıp bırakılmayacağı implementasyon kararı — bu RFC'de kilitlenmiyor.
- **Komisyon:** `InitiatePaymentCommand`'ın `Payment.Initiate(..., platformCommissionMinor: ...)` çağrısı artık **sabit `0`** geçer (booking'in `PlatformCommissionMinor`'ı değil) — [RFC-012](RFC-012-monthly-commission-billing.md) çapraz notu: kapora tamamen kuaföre akar, `NetToBarberMinor = GrossAmountMinor - 0 - PspMdrMinor`.

### Reddet / onaylamama → otomatik tam iade

Davranış değişikliği **gerekmiyor** — `ComputeRefundPercentage` zaten `BarberRequested`/`PendingTimeout`'ta %100 döndürüyor. Bu RFC'nin eklediği tek şey: owner'ın "reddet" aksiyonunun `CancelBookingCommand`'ın owner-yolunda `BarberRequested` reason'ını üretmesi gerektiğinin **doğrulanması** (mevcut `ResolveReasonAsync` zaten owner iptalini bu reason'a bağlıyor — `BookingActions.cs`) ve mobile'da "iade X gün içinde PSP'ye göre yapılır" mesajının müşteri iptal ekranına eklenmesi (dürüst iletişim, founder şartı).

### `ExpirePendingBookingsJob` — per-booking dallanma (kritik, RFC-009'un tek-global-flag varsayımını genişletir)

RFC-009 job'u global flag'e göre iki dallı: flag açık → 30dk unpaid eşiği; flag kapalı → `ScheduledAt` eşiği. Bu RFC ile **aynı platformda aynı anda** kapora-aktif ve kapora-pasif shop'lar bir arada var olabildiğinden global flag tek başına yetersiz — job artık booking bazında karar vermeli.

`[ara karar]` **Yeni snapshot alanı:** `Booking.RequiresOnlinePayment` (bool, `Initiate()` anında set edilir: global flag açık **VE** shop'un o anki `BookingPolicy.DepositEnabled == true` ise `true`, aksi halde `false` — RFC-006'nın "oran değişimi mevcut Pending'i etkilemez, initiate anındaki değer yazılır" presedentiyle aynı ilke: sonradan owner kaporayı kapatsa bile zaten oluşmuş Pending booking'in davranışı değişmez). Job üç dallı hale gelir:

1. `RequiresOnlinePayment == true && PaymentId == null && yaş > 30dk` → `Cancel(PendingTimeout)` (kapora bekleniyor, ödenmedi — RFC-009'un flag-açık dalıyla aynı).
2. `RequiresOnlinePayment == true && PaymentId != null && ScheduledAt geçti` → `Cancel(PendingTimeout)` (kapora ödendi, owner onaylamadı — **yeni dal**, madde 2'nin "randevu saatine kadar onaylamazsa" kuralı).
3. `RequiresOnlinePayment == false && ScheduledAt geçti` → `Cancel(PendingTimeout)` (kapora hiç yok, owner onaylamadı — RFC-009'un flag-kapalı dalıyla aynı, artık per-booking).

`Why:` per-booking snapshot, job'un her tick'te shop policy'sini ayrıca sorgulamasını (N+1) engelliyor ve geçmiş booking'lerin davranışını policy değişikliklerinden izole ediyor — RFC-006'nın kapora-oranı snapshot mantığıyla birebir aynı gerekçe.

### Anti-spam (madde 4)

- **(a) `MaxPendingPerCustomer` (per-shop, default 2):** `InitiateBookingCommandHandler`'a mevcut global kontrolün (`InitiateBookingCommand.cs:56-66`, shop'tan bağımsız, tavan 3) **yanına**, shop-scoped ikinci bir sayım eklenir: `CustomerId == X && BarberShopId == Y && Status == Pending`, `shop.BookingPolicy.MaxPendingPerCustomer` ile karşılaştırılır. **İkisi de** çalışır (AND) — hangisi önce ihlal edilirse o Türkçe hata döner. Platform tabanı (3) her zaman geçerli; per-shop limit onu daha da sıkılaştırabilir, gevşetemez (admin tavanı `MaxPendingPerCustomer` için de var, muhtemelen ≤3 — global taban zaten üst sınırı koyuyor).
- **(b) `MinDaysBetweenBookings`, (c) `MaxBookingsPerMonth`:** `[ara karar — founder'a doğrulanmalı]` (b) için "iki randevu arası" = customer'ın o shop'taki en son **iptal edilmemiş** booking'inin `ScheduledAt`'i ile yeni talebin `ScheduledAt`'i arası fark; (c) için ay içi sayım **`Cancelled` hariç tüm statüler** (Pending/Confirmed/Started/Completed/NoShow) — `NoShow` dahil çünkü müşterinin kusuruyla boşa giden slot yine de "o ayki hakkını" kullanmış sayılmalı (aksi halde no-show yapıp limitten muaf kalma kaçağı doğar). Bu iki yorum founder onayı olmadan implementasyona geçmemeli.
- Admin tavan/taban: `PlatformSettings` genişlemesi (RFC-012'nin tanıttığı mekanizma) — her ayarlanabilir alan için `[Min, Max]` çifti (örn. `MaxPendingPerCustomerCeiling`), owner panel FluentValidation bu sınırları okur.

### Owner paneli — policy ayar ekranı

- Kapora aç/kapa + yüzde slider (admin sınırı içinde) — global flag kapalıyken **devre dışı** + "PSP aktif değil, online ödeme yakında" bilgi metni (founder'ın kendi ifadesi).
- Anti-spam üç satır (aç/kapa + değer), her biri admin sınırı içinde.

### Mobile — müşteri tarafı

- Availability/confirm ekranı: kapora-aktif shop'ta RFC-006'nın mevcut seçim kartı (Kapora varsayılan / Tam ödeme) **korunuyor**, yalnız oran artık shop'a özel (`DepositPercent`) ve **ikisi de artık otomatik Confirm üretmiyor** — ödeme sonrası ekran metni "Ödemen alındı, kuaförün onayını bekliyor" olarak güncellenir (RFC-006'nın "başarı" ekranındaki "randevun onaylandı" ifadesi yanlış olur, düzeltilmeli).
- Kapora-pasif shop'ta: RFC-009'un flag-kapalı mobile davranışı (ödeme kartı yok, "randevun kuaföre iletildi, onay bekleniyor" — `confirm.tsx`, RFC-009 §Mobile) **aynen** kullanılır, ayrıca per-shop bazlı tetiklenir.
- İptal/red durumunda: booking detayında "İade süreci başladı, PSP'ye göre birkaç gün sürebilir" mesajı (founder şartı, dürüst iletişim — RFC-006'nın refund UI'ı bugün bu mesajı içermiyor, eklenmeli).

### Test (implementasyon aşamasında)

- Domain: `MarkPaymentReceived` idempotency + guard; `OwnerConfirm` sonrası `PaymentId` korunuyor.
- Integration: kapora ödendi → booking `Pending` kalıyor (Confirm YOK) → `OwnerConfirm` → `Confirmed`; owner reddet → %100 iade tetiklenir (mevcut oto-refund zinciri regresyonsuz); `ExpirePendingBookingsJob` üç dal — (1) unpaid 30dk, (2) paid+`ScheduledAt` geçti, (3) kaporasız+`ScheduledAt` geçti; per-shop `MaxPendingPerCustomer` + global 3 limiti birlikte; policy `DepositEnabled=true` iken global flag kapalıysa validasyon reddi.
- Mobile: confirm ekranı "onay bekleniyor" metni jest; policy ayar ekranı admin-sınır validasyonu.

## Alternatifler ve Trade-off

| Alternatif | Artı | Eksi | Sonuç |
|---|---|---|---|
| Önce onay, sonra kapora (owner onaylar → müşteri öder) | Kuaför spam/hayalet randevudan tam korunur | Ekstra adım + gecikme; RFC-006'nın "hemen öde, kilitlensin" dönüşüm avantajı kaybolur | Reddedildi (founder açıkça "önce kapora" seçti) |
| Mevcut davranış: ödeme = otomatik onay | En az sürtünme | Owner'ın hiç söz hakkı yok — kapasite/uygunluk hatası, sahte/kötü niyetli randevu riski | Reddedildi (madde 3, "her randevuda onay zorunlu") |
| Global (platform-çapında) tek kapora oranı, per-shop kapalı/açık yok | Basit | Her kuaförün risk iştahı/nakit akışı farklı — tek oran ya çok yüksek ya çok düşük sürtünme yaratır | Reddedildi |
| **Seçilen: per-shop kapora + önce-öde-sonra-onay + otomatik tam iade** | Kuaför kendi kapora politikasını seçer, owner onay hakkı korunur, red/timeout'ta müşteri asla parasını kaybetmez | Job üç-dallı hale gelir (karmaşıklık artışı), FullOnline'ın da onay bekletilmesi RFC-006'nın orijinal UX varsayımını değiştirir | **Kabul** |

## Why

Founder'ın "önce kapora, sonra onay" tercihi iki gerçek ihtiyacı aynı anda çözüyor: RFC-006'nın kanıtlanmış "hemen öde, dönüşüm kaybetme" UX'i korunuyor, ama owner artık her randevuyu gözden geçirebiliyor (kapasite, uygunsuz müşteri, hizmet netleştirme). Otomatik tam iade zaten mevcut kodda olan bir davranışı yeniden kullandığı için (`ComputeRefundPercentage`), bu RFC'nin riskli kısmı yeni bir iş kuralı değil, **var olan iki bağımsız mekanizmanın (ödeme capture + owner confirm) ayrıştırılması** — `ExpirePendingBookingsJob`'un per-booking dallanması olmadan bu ayrıştırma sessizce yanlış davranır (kapora ödeyen müşterinin randevusu 30dk'da haksız yere iptal olur), bu yüzden job değişikliği "nice to have" değil bu RFC'nin **çalışması için zorunlu** parça.

## How to apply

Sıra: `PlatformSettings` genişlemesi (RFC-012 ile paylaşılan, `DepositPercentMin/Max` + anti-spam tavanları) → `BarberShop.BookingPolicy` value object + migration (mevcut shop'lar `DepositEnabled=false`) → `Booking.RequiresOnlinePayment` snapshot alanı + `MarkPaymentReceived` metodu → `PaymentCompletionService` çağrı değişikliği (**önce founder'a FullOnline kapsamı doğrulatılmalı**, yukarıdaki `[ara karar]`) → `ExpirePendingBookingsJob` üç-dallı mantık → `InitiateBookingCommand` per-shop `MaxPendingPerCustomer` kontrolü → (opsiyonel, founder onayı sonrası) `MinDaysBetweenBookings`/`MaxBookingsPerMonth` → owner ayar ekranı → mobile confirm/iade metinleri.

## Bilinçli dışarıda bırakılanlar

`MinDaysBetweenBookings`/`MaxBookingsPerMonth`'un tam sayım semantiği (yukarıdaki `[ara karar]`, founder onayı implementasyondan önce şart) · owner'ın onay penceresini shop bazlı özelleştirmesi (RFC-009'un "randevu saati = doğal deadline" kararı platform-genelinde sabit kalıyor, per-shop config eklenmiyor — YAGNI) · kapora iade edilirken PSP-spesifik gerçek süre tahmini (PayTR/Iyzico SLA'sı merchant hesabı olmadan doğrulanamıyor, `[unverified]`) · Nilvera fatura zamanlaması (ödeme capture ile onay artık ayrı anlar — fatura hangi ana bağlanacak, Nilvera entegrasyonu kurulduğunda ayrıca kararlaştırılmalı, bu RFC'yi bloklamıyor çünkü Nilvera entegrasyonu bugün kod tabanında hiç yok) · `Booking.Confirm(paymentId)` dead code temizliği (implementasyon detayı).

## Founder Aksiyonları

- **PayTR:** bu RFC'nin kapora akışı RFC-006 ile aynı merchant hesabına bağımlı — ayrıca bir aksiyon gerekmiyor, RFC-006'nın founder-bloklu maddesiyle aynı.
- **Nilvera:** bu RFC'yi bloklamıyor (entegrasyon henüz kod tabanında yok) ama kurulduğunda "ödeme capture ↔ owner onay artık ayrı anlar" gerçeğinin fatura zamanlamasına etkisi ayrıca değerlendirilmeli (yukarıda not edildi).
- **IBAN:** bu RFC'de gerekmiyor (havale değil, PSP checkout akışı) — IBAN ihtiyacı [RFC-012](RFC-012-monthly-commission-billing.md)'de.
- **Doğrulama gerekli (implementasyondan önce):** "her randevuda onay zorunlu" kuralının `PaymentKind.FullOnline`'ı da kapsayıp kapsamadığı (yukarıdaki `[ara karar]`) — yanlış varsayılırsa RFC-006'nın "tam ödeme = anında onay" UX'i istemeden bozulur.
- **Doğrulama gerekli:** admin'in `DepositPercent`, `MaxPendingPerCustomer` vb. için koyacağı ilk min-max tavan/taban rakamları (bu RFC yalnızca örnek %5-50 verdi).

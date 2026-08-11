# GAPS.md — FAZ 1 Domain Denetiminde Bulunan YENİ Boşluklar

**Tarih:** 2026-08-06
**Kapsam:** Bu doküman `docs/GAP-ANALYSIS.md`'nin (2026-07-23 tam tarama, 2026-08-06'ya kadar nokta güncellemeleri) TEKRARI DEĞİL. Orada zaten kayıtlı maddeler (P0-1..13, P1-1..16, P2-1..14) burada yeniden yazılmadı; ilgili yerlerde yalnız referans verildi. Aşağıdaki maddeler `docs/DOMAIN.md`'nin FAZ 1 taramasında ortaya çıkan ve mevcut GAP-ANALYSIS'te **kaydı olmayan** yeni bulgulardır. Her madde dosya:satır kanıtına dayanır.

**Format:** Başlık / Kanıt / Etki / Önerilen çözüm / Tahmini efor.

---

## G1. Müşteri aynı saat aralığında iki farklı salona randevu alabiliyor — cross-shop çakışma kontrolü yok

- **Kanıt:** `backend/src/Kuaforum.Application/Modules/Booking/InitiateBookingCommand.cs:112` ve `backend/src/Kuaforum.Infrastructure/BackgroundJobs/BookingBusyRangeQuery.cs:29` yalnız `StylistId` bazlı çakışma kontrolü yapıyor; sorguda `CustomerId` filtresi yok. Aktif Pending sayacı (global 3 / per-shop 2) zamandan bağımsız, yalnız toplam kotayı sınırlıyor.
- **Etki:** Orta. (1) Müşteri deneyimi/adalet sorunu: aynı müşteri 14:00'da hem A salonunda hem B salonunda randevu tutabilir, birine gitmeyip no-show/iade zincirini tetikleyebilir. (2) Walk-in + randevu kombinasyonunda ölçüm/analitik kirlenir. Para kaybı riski düşük (her booking kendi kapora/iade kurallarına tabi) ama operasyonel güven riski var — kuaförler "müşterim gelmedi, başka yere gitmiş" şikayeti edebilir.
- **Önerilen çözüm:** `InitiateBookingCommand` handler'ına ek bir sorgu: `CustomerId` bazlı, `[Pending, Confirmed, Started]` durumundaki booking'lerin zaman aralığı yeni istekle çakışıyorsa reddet (409 veya uyarı). Sert engel yerine "başka bir randevunla çakışıyor, devam etmek istiyor musun?" gibi soft-warning de founder tercih edebilir — ürün kararı gerekli.
- **Tahmini efor:** 3-5 saat (sorgu + validasyon + integration test).

**Durum:** ✅ **Kapatıldı 2026-08-07/10.** Founder kararı (RFC-014 §2 **D4**): **uyar, ENGELLEME**. Meşru senaryo korunuyor (eş/çocuk adına randevu, art arda iki farklı hizmet).

Uygulama iki uçta:
- `POST /v1/bookings/check-conflict` (`94881da`) — randevu **oluşmadan önce**, özet adımında. İlk sürüm yalnız oluşturma yanıtında bilgi veriyordu; bu haliyle uyarı değil olan bitenin duyurusuydu ve müşteriyi istemediği randevuyu iptal etmeye zorluyordu (kapora akışında gereksiz tahsilat+iade zinciri).
- `InitiateBookingResponse.conflictingBooking` (`3f5eaa5`) — **bilerek kaldı**: ön-kontrol ile oluşturma arasında başka bir randevu araya girebilir; son söz burada.

Sorgu mantığı `CrossShopConflictFinder`'da tek yerde; iki yol da onu çağırıyor (ikizlenmiş kurallar zamanla ayrışır). Yalnız **cross-shop** — aynı salon/aynı stylist çakışması zaten `EXCLUDE` constraint ile engelleniyor, ona dokunulmadı. Müşteri kimliği her zaman JWT'den, gövdeden değil (salon adı + saat de kişisel veri).

Mobil onay diyaloğu `97aa39d`: "Yine de devam et" her zaman mümkün; ön-kontrol ağ hatası verirse akış **bloklanmıyor** (tavsiye niteliğinde bir kontrolün başarısızlığı asıl işi durdurmamalı).

**Kapsam dışı bırakılan (yeni kayıt):** Aynı salonda **farklı çalışanlara** çakışan saatte randevu — bugün hiçbir uyarı/engel yok. Cross-shop'tan farklı bir senaryo; müşteri aynı salonda aynı anda iki koltukta olamaz. Düşük öncelikli, ayrı madde açmaya değer.

---

## G2. Çalışma saati (WorkingHours) değişikliği mevcut onaylı randevuları kontrol etmiyor

- **Kanıt:** `SetWorkingHoursCommandHandler` (`backend/src/Kuaforum.Application/Modules/Barber/ServiceCommands.cs:181-210`) yalnız `WorkingHours` entity'sini create/update ediyor ve audit log yazıyor; `Booking` tablosuna hiçbir sorgu veya yazma yok.
- **Etki:** Orta. Owner çalışma saatini daraltırsa (ör. akşam saatlerini kapatırsa), o saatte zaten Confirmed randevusu olan müşteriler hiç haberdar edilmez; owner randevu saatinde artık "kapalı" olabilir → S9'daki no-show algısı riskiyle birleşiyor (bkz. `docs/DOMAIN.md` §3 S9, G4).
- **Önerilen çözüm:** `SetWorkingHoursCommandHandler`'a "yeni saat aralığı dışında kalan gelecekteki Confirmed/Pending booking var mı" kontrolü ekle; varsa (a) işlemi reddet ve owner'a listele, veya (b) izin ver ama etkilenen randevuları owner'a göster + müşteriye bildirim tetikle. Ürün kararı: engelle mi, uyar mı — founder'a sorulmalı.
- **Tahmini efor:** 0.5-1 gün (sorgu + UI uyarı + karar).

**Durum:** ✅ **Kapatıldı 2026-08-10** (`3710774` backend, `31e7848` mobil). Founder kararı RFC-015 §2 **D1**: **uyar, izin ver, müşteriye haber ver.** Engellemek reddedildi — hastalık/acil kapanışta kuaförü ayarı hiç değiştiremez hale getirirdi.

Kuaför kaydetmeden önce `POST /v1/stylists/{id}/working-hours/preview` ile etkilenen randevuları görüyor; onaylarsa kayıt yapılıyor ve **etkilenen müşterilere bildirim gidiyor**. Randevu otomatik iptal EDİLMİYOR — karar taraflara ait. Bildirim idempotent (kayıtlı değer fiilen değişmediyse tekrar gitmiyor, curl ile doğrulandı).

**Kırıcı değişiklik:** `PUT /v1/stylists/{id}/working-hours` artık 204 değil, 200 + `{affectedBookings}`.

**Bilinçli sapma:** Bildirim, projenin yerleşik "aggregate olay yayınlar → Notification handler" kalıbı yerine doğrudan dispatcher ile gönderiliyor. Gerekçe: durumu değişmeyen N randevuya sırf olay taşımak için dokunmak gereksiz karmaşıklık. Projenin mevcut Outbox borcunun parçası olarak kayıtlı.

---

## G3. Stylist deaktive edilince gelecek randevuları sessizce askıda kalıyor — otomatik iptal/devir yok

- **Kanıt:** `Stylist.SetActive(bool)` var (`backend/src/Kuaforum.Domain/Modules/Barber/Stylist.cs:47-53`), `UpdateStylistCommandHandler` (`backend/src/Kuaforum.Application/Modules/Barber/StylistCommands.cs:63-93`) bunu çağırıyor ama booking tablosuna dokunan hiçbir kod yok. `StylistDeactivat*`/`ReassignStylist`/`CancelBookingsForStylist` grep'i sıfır sonuç. Mobile UI bunu bilinçli ürün kararı olarak sunuyor: `mobile/app/(barber)/availability.tsx:515` → *"Pasif çalışan müşterilere görünmez. Aktif randevuları etkilenmez."*
- **Etki:** Orta-Kritik. Bu `docs/GAP-ANALYSIS.md` P1-16'daki "StylistService enforce edilmiyor" maddesinden **farklı bir bulgu** — P1-16 stylist'in vermediği hizmete rezerve edilebilmesiyle ilgili, burası stylist'in **işten ayrılması sonrası var olan randevunun** ne olacağıyla ilgili. Müşteri randevu saatinde gelir, stylist artık orada çalışmıyor olabilir — owner'ın bunu fark edip elle iptal/reschedule etmesi gerekiyor, sistem hatırlatmıyor.
- **Önerilen çözüm:** Stylist `SetActive(false)` çağrıldığında, o stylist'e bağlı gelecekteki Confirmed/Pending randevuları owner'a bir uyarı listesi olarak göster (zorla iptal etme — owner devir kararını kendi verir) + varsa müşteriye "kuaförün değişebilir, salonla iletişime geç" bildirimi. Minimum versiyon: yalnız owner-tarafı uyarı ekranı (booking'e dokunmadan).
- **Tahmini efor:** 0.5-1 gün (owner uyarı listesi); tam otomatik devir/iptal akışı ayrı ve daha büyük bir ürün kararı (1-2 gün).

**Durum:** ✅ **Kapatıldı 2026-08-10.** Founder kararı RFC-015 §2 **D2**: kuaföre **etkilenen randevu listesi gösterilir**, pasifleştirme engellenmez, **otomatik devretme YOK**.

Otomatik devretme bilerek reddedildi: müşteri belirli bir kişiyi seçmiş olabilir, habersiz değişim "istediğim kişiye gidemedim" deneyimi yaratır ve marketplace güvenini doğrudan vurur. Sistem karar vermiyor, körlüğü gideriyor.

Ayrıca mobildeki yanıltıcı metin düzeltildi: eski hali *"Aktif randevuları etkilenmez"* kuaföre "ilgilenmem gereken bir şey yok" hissi veriyordu; randevular hâlâ o çalışana bağlı ve birinin halletmesi gerekiyor.

**Kırıcı değişiklik:** `PATCH /v1/stylists/{id}` artık 204 değil, 200 + `{affectedBookings}`.

---

## G4. Müşteri tarafında salon/kuaför no-show'unu bildirebileceği bir mekanizma hiç yok

- **Kanıt:** `Dispute` terimi yalnız `CommissionStatement.Dispute(...)` içinde var (`backend/src/Kuaforum.Domain/Modules/Billing/CommissionStatement.cs:126`) — owner'ın platforma komisyon faturasına itirazı, müşteri-tarafı şikayet değil. `MarkNoShowCommand` tam tersi yönde çalışıyor: yalnız owner çağırabiliyor, müşterinin gelmediğini işaretliyor (`backend/src/Kuaforum.Application/Modules/Booking/BookingActions.cs:169-199`). `Complaint`/`SupportTicket`/`BarberNoShow` grep'i backend genelinde sıfır sonuç.
- **Etki:** Orta. Müşteri salona gidip kapalı bulursa veya kuaför gelmezse, uygulama içinde bunu bildirecek/kapora iadesi talep edecek hiçbir yol yok — destek talebi tamamen uygulama dışı kanallara (telefon, e-posta) düşüyor. Marketplace güveni ve KVKK/tüketici hukuku açısından (6502 sayılı kanun, hizmet ifa edilmezse iade hakkı) orta-vadede zorunlu bir akış.
- **Önerilen çözüm:** MVP için minimum: booking detay ekranında "Kuaför gelmedi / salon kapalıydı" butonu → `CustomerReportedNoShow` gibi bir flag + admin kuyruğuna düşen bir kayıt (otomatik iade tetiklemesin, insan incelemesi gerektirsin — sahte şikayet riskine karşı). Tam otomatik iade akışı V1.5'e ertelenebilir.
- **Tahmini efor:** flag + admin kuyruğu: 1 gün; tam iş akışı (bildirim, SLA, otomatik eskalasyon): 2-3 gün.

**Durum:** ✅ **Kapatıldı 2026-08-10** (minimum sürüm). Founder kararı RFC-015 §2 **D3**. `POST /v1/bookings/{id}/complaint` + admin kuyruğu; **otomatik iade TETİKLENMİYOR** — bu ayrı bir testle sabitlendi, çünkü otomatik iade kötüye kullanıma açık bir para musluğu olurdu.

Yalnız geçmiş randevularda; randevu başına tek kayıt (uygulama katmanı guard + unique index — yarışta da tek). Kuaföre bildirim **nötr dille** ("bir sorun bildirdi, inceleniyor") — henüz doğrulanmış bir şey yok, suçlayıcı metin kullanılmıyor.

**Zamanlama gerekçesi:** Online ödeme launch'ta kapalı, yani şu an kaybedilecek para yok. Ama **kapora açıldığı gün bu kanal tüketici hukuku açısından zorunlu hale gelir** (6502 sayılı kanun — hizmet ifa edilmezse iade hakkı). Minimum sürümü şimdi kurmak, o gün hazırlıksız yakalanmamayı sağlıyor.

**Bilinçli kapsam daraltması:** Şikayeti "çözüldü" olarak kapatma akışı YOK. `Status` alanı ileriye dönük var ama geçiş metodu yok; admin mevcut iptal/iade akışını kullanıyor. Kuyruk operasyonel olarak büyürse (V1.5) eklenmeli.

---

## G5. Owner tek-çalışan senaryosunda otomatik Stylist kaydı oluşturulmuyor — onboarding tamamlanmazsa walk-in sessizce kapalı kalır

- **Kanıt:** `CreateBarberShopCommand` (`backend/src/Kuaforum.Application/Modules/Barber/CreateBarberShopCommand.cs:37-89`) yalnız `BarberShop.Create` + rol terfisi yapıyor, `Stylist.Create` çağrısı yok. Owner mobile'da kendini elle "+ Yeni çalışan" ile eklemek zorunda (`mobile/app/(barber)/availability.tsx:284-291,460-492`). Bu adım atlanırsa salon `StylistCount=0`, `HasWalkInAvailable=false` olarak arama sonuçlarında görünür (`backend/src/Kuaforum.Application/Modules/Barber/SearchBarbersQuery.cs:97-105`).
- **Etki:** Düşük-Orta. Solo-kuaför (Türkiye pazarında yaygın segment) onboarding'i tamamlayıp "salon Active" olsa bile bu adımı atlarsa arama sonuçlarında görünmez/randevu alınamaz hale gelebilir — kayıp olduğunu fark etmeden churn riski. RFC-007 owner onboarding kapanışında (`docs/GAP-ANALYSIS.md` P0-9 güncellemesi) bu adım akışa dahil edilmemiş.
- **Önerilen çözüm:** (a) Ucuz: `CreateBarberShopCommand`'da owner'ı otomatik ilk `Stylist` olarak ekle (owner adı/soyadını kullan, sonradan owner isterse pasifleştirir/düzenler); (b) veya onboarding sihirbazına 4. zorunlu adım olarak "en az 1 çalışan ekle" koy. (a) daha az sürtünmeli, "sahip = tek çalışan" varsayılan senaryoyu (Türkiye pazarında muhtemelen en yaygın) day-1 karşılar.
- **Tahmini efor:** (a) 2-4 saat; (b) 0.5 gün.

**Durum:** ✅ **Kapatıldı 2026-08-11** (`5e69bbc`). (a) seçeneği uygulandı: `CreateBarberShopCommandHandler` owner'ı aynı `SaveChanges` içinde aktif ilk `Stylist` olarak ekliyor + varsayılan çalışma saati veriyor (`DefaultWorkingHoursFactory`, Pzt-Cmt 09:00-19:00 — hem bu handler hem `AddStylistCommandHandler` aynı kaynağı kullanıyor). Yalnız `Stylist` satırı eklemek yetmezdi: `WorkingHours` olmadan uygunluk sorgusu hiç slot üretmez ve salon yine "randevu alınamaz" kalırdı.

**Yol boyunca bir teşhis hatası yapıldı — kayda geçiyor, çünkü tekrarlanabilir bir hata:**

Uygulama sırasında `RichDemoSeederTests.SeedAsync_DevLoginOwnerShop_...` kırıldı (dev kuaförün salonunda bugüne ait `Completed` randevu üretilmiyordu). Tek bir `git stash` / `unstash` döngüsüyle "G5 kaynaklı" sonucuna varıldı ve değişiklik geri alındı. **Bu sonuç yanlıştı.**

Gerçek kök neden: `RichDemoSeeder` Step 9'da bugünün Confirmed/Pending randevularını `RealisticSlotUtc` hedefini **doğrudan** `Booking.Initiate`'e vererek üretiyordu; `Initiate`'in "geçmişte olamaz" guard'ı ise enjekte edilen `nowOverride`'a değil **her zaman gerçek `DateTimeOffset.UtcNow`'a** bakar. Yani test, günün gerçek saatine bağlıydı — G5 diff'i bu seeder'ın hiçbir kod yolunu tetiklemiyor (`DemoBarberSeeder`/`RichDemoSeeder` entity'leri doğrudan kurar, `CreateBarberShopCommandHandler`'ı hiç çağırmaz; bu ayrıca doğrulandı). Düzeltme: bugünün randevuları da Completed/geçmiş randevularla **aynı güvenli kalıba** geçirildi (placeholder `now+1sa` ile Initiate, sonra hedef saate backdate).

**Ders:** Zamana bağlı bir testte tek bir stash/unstash döngüsü **nedensellik kanıtı değildir** — iki koşum arasında gerçek saat de değişir. Aynı hata "kararsız test doğası gereği zamanlamaya duyarlı" yanlış teşhisiyle (P2-14) aynı aileden. Kanıt istenirken *aynı koşulda tekrarlanabilirlik* aranmalı.

**İkinci tuzak:** Tanı testi yazılırken **tüm seeder zinciri** çalıştırılmalı (`AdminSeeder` → `DemoBarberSeeder` → `RichDemoSeeder`); yalnız `RichDemoSeeder` çağıran bir tanı dev salonu hiç oluşturmadığı için yanıltıcı veri üretir — bu tuzağa da bir kez düşüldü ve çıktısına dayanarak yanlış yorum yapıldı.

---

## G6. Salon askıya alınınca müşteriye bildirim gitmiyor — yalnız owner bilgilendiriliyor

- **Kanıt:** `CommissionStatementSuspendedNotificationHandler` (`backend/src/Kuaforum.Application/Modules/Notification/EventHandlers/BillingNotificationHandlers.cs:44-68`) yalnız `shop.OwnerId`'e In-App+Push gönderiyor; kod içinde CustomerId'e giden bir dispatch yok. Ama askıya alınmış salonun mevcut müşterisi randevu detayına erişmeye devam ediyor (`GetBookingByIdQueryHandler` shop status'üne bakmıyor, `backend/src/Kuaforum.Application/Modules/Booking/GetBookingQueries.cs:144-206`).
- **Etki:** Orta. Müşterinin gelecekte Confirmed randevusu varken salon askıya alınırsa, müşteri hiçbir bildirim almaz; randevu saatinde salona gider ve muhtemelen kapalı/hizmet veremiyor bulur — G4'teki (no-show raporlama yok) boşlukla birleşince müşteri şikayetini bildirecek hiçbir kanalı olmadan mağdur kalır.
- **Önerilen çözüm:** `SuspendOverdueCommissionAccountsJob` (veya suspend event handler'ı) askıya alınan salonun gelecekteki Confirmed/Pending randevularını sorgulayıp ilgili müşterilere "salon geçici olarak hizmet veremiyor, randevun iptal/iade ediliyor" bildirimi + otomatik `Cancel(AdminOverride)` (→ %100 iade) tetiklemeli. Bu aynı zamanda §G3/S4'teki "mevcut randevulara dokunulmuyor" davranışının bilinçli mi yoksa gözden kaçan bir karar mı olduğunu netleştirir — founder onayı gerekli (iş modeli: borç yüzünden askıya alınan salonun müşterisi mağdur edilmemeli mi, yoksa "salon telafi eder" mi bekleniyor?).
- **Tahmini efor:** 0.5-1 gün (event handler + booking sorgusu + bildirim + iade).

---

## G7. "Stylist" auth policy'si yanıltıcı isimlendirilmiş — gerçek Stylist rolü hiç atanmıyor, founder karar bekliyor

- **Kanıt:** `AuthSetup.cs:50` → `options.AddPolicy("Stylist", p => p.RequireRole("Stylist", "BarberOwner"))`; `UserRole.Stylist` (`UserRole.cs:7`) repo genelinde 3 yerde geçiyor, üçü de negatif/açıklayıcı bağlam (`User.cs:112` guard + 2 test yorumu) — hiçbir factory/seeder/endpoint gerçekten bu rolü bir kullanıcıya atamıyor. `BarberEndpoints.cs:221` kod yorumunda bunu kendisi itiraf ediyor: *"'Stylist' policy'si BarberOwner'ı da kapsar"* — yani `/v1/stylists` grubunu fiilen yalnız owner çağırabiliyor.
- **Etki:** Düşük (bugün için) ama founder karar gerektiriyor. `docs/ASSUMPTIONS.md` D2 satırında zaten "gerçek boşluk → founder kararı" olarak işaretli; buradaki ek bulgu şu: kod, var olmayan bir konsept üzerine (policy ismi, enum değeri) inşa edilmiş — ileride biri "Stylist policy var, o zaman stylist login çalışıyor" yanılgısına düşebilir (nitekim `docs/ASSUMPTIONS.md`'nin kendisi bu direktif karşılaştırmasını yaparken bunu ayrıca doğrulamak zorunda kaldı).
- **Önerilen çözüm:** Founder kararı gerekiyor (bu doküman karar vermez): (a) Stylist gerçek bir kullanıcı tipi olacaksa (kendi telefon+OTP girişi, kendi JWT'si, salon geneline değil yalnız kendi takvimine erişim) — ayrı bir RFC + backend (User→Stylist bağlama, davet akışı, endpoint kapsamı daraltma) + mobile (yeni panel) gerektirir, büyük iş (1-2 hafta). (b) V1'de terk ediliyorsa — `UserRole.Stylist` enum değerini ve `"Stylist"` policy adını `docs/rfc`'ye "kod içinde kalan ama kullanılmayan konsept" olarak kaydet, policy adını `"OwnerOrStylist"` gibi gerçeği yansıtan bir isimle değiştirmeyi düşün (kozmetik, acil değil).
- **Tahmini efor:** karar kaydı (RFC) 1 saat; (a) seçilirse 1-2 hafta, (b) seçilirse 1-2 saat kozmetik.

---

## G8. Bildirim idempotency'si merkezi değil — yalnız BookingReminderJob'da doğrulandı, diğer event-driven handler'lar teyit edilmedi

- **Kanıt:** `NotificationDispatcher.DispatchAsync` (`backend/src/Kuaforum.Infrastructure/Notifications/NotificationDispatcher.cs:16-51`) kendi içinde dedup yapmıyor, her çağrıda koşulsuz yeni `NotificationLog` yazıyor. `BookingReminderJob` (`backend/src/Kuaforum.Infrastructure/BackgroundJobs/BookingReminderJob.cs:44-51`) gönderim öncesi `NotificationLog`'da `TemplateKey+RelatedBookingId` kontrolü yapıyor — ama bu koruma job-level, dispatcher-level değil.
- **Etki:** Düşük `[unverified 2026-08-06 — needs handler-by-handler check]`. Event-driven handler'lar (booking cancel/confirm, KYC approve/reject, billing suspend) genelde tek seferlik domain event'e tepki verdiği için pratikte duplicate riski düşük olabilir — ama Hangfire retry (`ProcessBookingRefundJob` gibi `AutomaticRetry(5)`) veya event replay senaryosunda bir handler'ın notification adımı retry'da tekrar tetiklenirse (SaveChanges başarılı ama sonraki adım hata verirse) çift bildirim riski teorik olarak var. Bu tur bunu kanıtlamadı, yalnız işaret etti.
- **Önerilen çözüm:** Her notification-gönderen event handler'ı `NotificationLog` idempotency kontrolüyle taramak (kısa bir audit, 2-3 saat) + gerekirse `NotificationDispatcher`'a merkezi bir `(TemplateKey, RelatedEntityId)` unique constraint/guard eklemek.
- **Tahmini efor:** audit 2-3 saat; merkezi guard (gerekirse) 0.5 gün.

---

## G9. PSP iade 5 otomatik deneme sonrası kalıcı başarısız olursa görünürlük/alarm mekanizması doğrulanmadı

- **Kanıt:** `ProcessBookingRefundJob` `[AutomaticRetry(Attempts = 5)]` (`backend/src/Kuaforum.Infrastructure/BackgroundJobs/ProcessBookingRefundJob.cs:24`). 5 deneme de başarısız olursa Hangfire job'ı "Failed" state'ine düşürür — bu FAZ 1 taramasında job'ın failed-state'te bir alarm/admin bildirimi/manuel retry ekranı tetikleyip tetiklemediği doğrulanmadı.
- **Etki:** Orta `[unverified 2026-08-06 — needs Hangfire dashboard + alerting doğrulaması]`. Eğer failed job sessizce Hangfire dashboard'da kalıyorsa (kimse bakmadıkça fark edilmez) — müşteri parası PSP tarafında iade edilmemiş halde kalabilir, bu `docs/GAP-ANALYSIS.md` P0-4'ün kapattığı riskin bir alt-kümesi olarak geri açılabilir. Not: bu madde P0-4'ün "otomasyon var mı yok mu" sorusundan farklı — otomasyon var (doğrulandı, bkz. `docs/DOMAIN.md` §S2), burada sorulan "otomasyon başarısız olursa kim fark eder".
- **Önerilen çözüm:** Hangfire dashboard'da failed-job filtresi + (P2-10'daki monitoring paketiyle birlikte) Better Stack/Sentry'ye failed-refund-job alarmı bağla; alternatif: 5 deneme sonrası `Payment` üzerinde `RefundFailed` durumu + admin kuyruğuna manuel-müdahale kaydı düş.
- **Tahmini efor:** doğrulama 1-2 saat; alarm/manuel-kuyruk eklenmesi gerekiyorsa 0.5 gün.

---

## G10. Test APK'sındaki API adresi derleme anında gömülü — ağ değişince uygulama tümüyle ölüyor

**Kanıt (canlı, 2026-08-06):** Geliştirme makinesinin LAN IP'si `192.168.2.15` → `192.168.1.11` olarak değişti (router/ağ değişimi). `EXPO_PUBLIC_API_URL` JS bundle'a derleme anında gömüldüğü için founder'ın telefonundaki APK'nın **tüm ekranları** "sunucuya ulaşılamadı" vermeye başladı. Aynı gün içinde ikinci kez yaşandı (ilki Android cleartext engeliydi, `bc388cb` ile çözüldü).

**Etki:** Orta — yalnız test sürecini vurur, üretimi değil (üretimde sabit HTTPS alan adı olacak). Ama founder'ın elle test döngüsünü tamamen durdurduğu için pratik maliyeti yüksek: her ağ değişiminde ~75 MB'lık APK yeniden derlenip aktarılmalı.

**Çözüm:** Test build'lerinde (`EXPO_PUBLIC_ENABLE_TEST_LOGIN=1`) API taban adresi çalışma zamanında değiştirilebilir olmalı — kalıcı ayar → derleme varsayılanı öncelik zinciri, giriş ekranından erişilebilen adres ekranı, "bağlantıyı test et", emülatör için `10.0.2.2` kısayolu. Production build'lerinde kalıcı ayar **okunmamalı** (aksi halde saldırgan kontrolündeki bir sunucuya yönlendirme primitifi doğar).

**Durum:** Çözüm 2026-08-06'da uygulandı (`mobile/src/lib/runtime-base-url.ts`, `mobile/app/(auth)/server-settings.tsx`). Bu madde tarihçe ve regresyon uyarısı olarak duruyor: **bu davranışın production'a sızmadığını doğrulayan jest testi silinmemeli.**

---

## G11. Yükleme durumlarında iskelet (skeleton) yerine düz "Yükleniyor..." metni

**Kanıt (emülatör, 2026-08-06):** Kuaför detay ekranı yüklenirken ekranın tamamı boş, ortada tek satır "Yükleniyor..." metni görünüyor. Direktif E4 teslim kriteri açıkça "skeleton — spinner değil" diyor; düz metin ikisinden de zayıf.

**Etki:** Düşük-Orta — işlevi bozmuyor ama algılanan hızı düşürüyor ve "yapay zekâ ürünü gibi görünme" riskini artırıyor (direktif E3).

**Çözüm:** Liste/detay ekranları için içerik iskeletleri. Önce en çok görülen üç ekran: kuaför detay, keşfet listesi, randevularım.

**Efor:** 0.5 gün.

---

## G12. "Bulunamadı" ile "sunucuya ulaşılamadı" aynı ekranda birleşiyor

**Kanıt (emülatör, 2026-08-06):** Backend yeniden başlatılırken açılan kuaför detayında ekran "**Bulunamadı** — Bu kuaför yüklenemedi" gösterdi. Oysa kuaför silinmemişti; sunucu geçici olarak erişilemezdi. Kullanıcıya yanlış zihinsel model veriyor: "bu kuaför artık yok" sanıp geri dönüyor, oysa doğru aksiyon "tekrar dene".

**Etki:** Orta — yanlış hata mesajı, kullanıcıyı yanlış aksiyona yönlendiriyor. `docs/GAP-ANALYSIS.md` P2-14'te "404 disambiguation" olarak kayıtlıydı; bu madde **canlı tekrar üretim kanıtını** ekliyor ve önceliği yükseltiyor.

**Çözüm:** Hata durumunu üçe ayır: (a) 404 → "Bu kuaför artık yayında değil" + listeye dön, (b) ağ/5xx → "Bağlantı kurulamadı" + **tekrar dene** butonu, (c) çevrimdışı → çevrimdışı durumu. Direktif E4 zaten üçünü de zorunlu kılıyor.

**Efor:** 0.5 gün (tüm ekranlarda ortak hata bileşeni olarak).

---

## G13. Entitlement kota kontrolünde yarış koşulu — eşzamanlı istekler limiti aşabilir

**Kanıt:** `backend/src/Kuaforum.Application/Modules/Barber/StylistCommands.cs` — `AddStylistCommandHandler` deseni "oku (`CountAsync`) → karşılaştır → yaz (`Add` + `SaveChanges`)". Arada kilit, `SERIALIZABLE` izolasyon ya da DB-seviyesi kısıt yok; migration'larda da böyle bir kısıt bulunmuyor. İki eşzamanlı istek aynı sayıyı okuyup ikisi de eklenebilir → limit +1 aşılır.

**Etki:** Şu an **düşük**. V1'de tüm salonlar `LAUNCH_FREE` planında, `max_staff=20` ve ödemeli plan yok — kota fiilen uykuda, aşılması gelir kaybı yaratmıyor. **Ödemeli planlar açıldığında etki yükselir** (ücretli bir sınırın atlatılması).

**Neden şimdi düzeltilmedi (bilinçli karar, 2026-08-06):** Uykuda olan bir kontrolü advisory lock / DB kısıtıyla sertleştirmek şu an gold-plating (CLAUDE.md §13). Aynı turda bulunan **gerçek bypass** (pasif stylist'i yeniden aktifleştirerek limiti aşma — `UpdateStylistCommandHandler`'ın `SetActive(true)` dalında kontrol yoktu) düzeltildi; yarış koşulu ertelendi.

**Çözüm (ödemeli plan öncesi yapılacak):** PostgreSQL advisory lock (`pg_advisory_xact_lock(shopId hash)`) veya `stylists` üzerinde şart bazlı DB kısıtı — booking'lerdeki M15 `EXCLUDE` deseniyle aynı ruhta. Uygulama katmanı kontrolü tek başına yeterli değildir.

**Tetikleyici:** İlk ücretli abonelik planı aktifleştirilmeden önce.

**Efor:** 3-5 saat.

---

## G14. Gerçek çevrimdışı algısı yok — "uçak modu" ile "sunucu erişilemez" ayırt edilemiyor

**Kanıt:** `mobile/src/lib/classify-error.ts` (2026-08-06) hata sınıflandırmasını axios hata nesnesinin şeklinden yapıyor. Cihaz çevrimdışıyken de sunucu erişilemezken de aynı `ERR_NETWORK` sinyali geliyor; ikisi ayrıştırılamıyor. Direktif E4 her ekran için ayrı bir "çevrimdışı durumu" istiyor.

**Etki:** Düşük. Kullanıcı zaten "Bağlantı kurulamadı + Tekrar dene" görüyor — değerin büyük kısmı alınmış durumda. Eksik olan yalnız (a) "internetin kapalı" gibi daha net metin ve (b) bağlantı geri gelince otomatik yeniden deneme.

**Neden şimdi yapılmadı (bilinçli karar):** Gerçek çevrimdışı algısı `expo-network` veya `@react-native-community/netinfo` gerektiriyor; ikisi de kurulu değil. Yeni bir native bağımlılık + APK yeniden derlemesi demek. Kazanç (daha iyi metin + otomatik retry) maliyeti şu an karşılamıyor; G11/G12 düzeltmesi kullanıcıyı yanlış yönlendirme sorununu zaten çözdü.

**Tetikleyici:** Gerçek kullanıcı testlerinde "internetim yokken ne olduğunu anlamadım" geri bildirimi gelirse, ya da native bağımlılık gerektiren başka bir iş için zaten rebuild yapılacaksa aynı pakete alınır.

**Efor:** 3-4 saat (bağımlılık + rebuild dahil).

---

## G15. Aynı hata/yükleme kalıbı kapsam dışı bırakılan ekranlarda sürüyor

G11/G12 düzeltmesi yalnız üç ekranı kapsadı (kuaför detay, keşfet, randevularım). Aynı kusur şu ekranlarda **duruyor** — kayda geçiriliyor ki "çözüldü" sanılmasın:

- `mobile/app/booking/[id].tsx` — G12 kalıbı birebir aynı: `isError || !data` → "Bulunamadı / Bu randevu yüklenemedi". Randevu detayı kullanıcının en sık açtığı ekranlardan; **öncelikli**.
- `mobile/app/(barber)/profile.tsx:104` ve `mobile/src/components/kyc/KycDocumentPicker.tsx:155` — düz "Yükleniyor…" metni.
- `mobile/app/(tabs)/bookings.tsx:65` — başlık altındaki tali "Yükleniyor…" metni (ana gövde artık skeleton).

**Durum:** ✅ **Kapatıldı 2026-08-07** (`9f19550`). Randevu detayı `ErrorState` + `SkeletonBookingDetail` kullanıyor; randevu listesi başlığı ve kuaför profili salon kartı skeleton'a geçti. `KycDocumentPicker` bilerek değiştirilmedi (göstergesi zaten `ActivityIndicator`'lı; sabit boyutlu tek-aksiyon kart, skeleton abartı olurdu — gerekçe koda yazıldı).

**Kalan tek örnek (kapsam dışı, zararsız):** `mobile/app/(barber)/booking-settings.tsx` aynı `isError || !data` kalıbını kullanıyor ama her zaman "bağlantı hatası" diyor — yani "kayıt yok" yalanını söylemiyor. Paylaşılan bileşeni kullanmayan bir tutarsızlık, davranışsal kusur değil.

**Bilinen test borcu:** `booking/[id].tsx` için ekran seviyesinde RNTL testi yok. Ayrım mantığı merkezî olarak test ediliyor (`classify-error` 13 test + `ErrorState` 4 test, "404'te retry yok / ağ hatasında var ve basınca çağrılır" dahil); ekrandaki bağlama (wiring) kod incelemesiyle doğrulandı. Repo'nun `jest.config.js`'i şu an bilinçli olarak yalnız saf mantık testlerini hedefliyor — ekran seviyesi harness ayrı bir iş.

**Efor:** 2-3 saat (bileşenler hazır, sadece uygulanacak). → harcanan: ~1 saat.

---

## G16. Alan adı değişince istek gövdesi sessizce sıfırlanıyor — sınıfsal risk

**Kanıt (canlı üretildi, 2026-08-07):** RFC-014 ile `AdjustAmountRequest`'in alanı `NewAmountDueMinor` → `NewAmountMinor` oldu; web-admin eski adı göndermeye devam etti. System.Text.Json eksik alanı `default(long)=0` ile doldurdu, validator `>= 0` kabul etti, endpoint **204** döndü. Sonuç: admin 1.499,00 TL yazıp gerekçeyle kaydediyor, ekranda hiçbir hata yok, fatura **0 TL** oluyor ve tahsil edilemez hale geliyor. Aynı fatura üzerinde doğrulandı: `149900` → eski gövde → `0` → yeni gövde → `99900`.

**Etki:** Yüksek (para). Bu tekil hata düzeltildi (`65fa697` istemci + backend `> 0` doğrulaması), ama **kalıp** duruyor: her DTO alan adı değişiminde aynı sessiz sıfırlama tekrar doğabilir.

**Değerlendirilen ve REDDEDİLEN çözüm:** `JsonSerializerOptions`'a global `UnmappedMemberHandling.Disallow` (bilinmeyen/eşleşmeyen alanı 400 ile reddet). Reddedilme gerekçesi: sahada güncellenemeyen mobil sürümler var; sürüm kayması olduğunda eski uygulamalar toptan 400 almaya başlar. Alan-adı katılığı, geriye dönük uyumluluğu kırma pahasına gelir.

**Uygulanan savunma:** zorunlu alan + değer aralığı doğrulaması (sıfır/eksik değer geçerli sayılmaz). Yeni bir para alanı eklerken **kural: "0 anlamlı bir değer mi?" sorusu cevaplanmadan validator yazılmaz.**

**Açık kalan (daha güçlü seçenek):** yalnız admin endpoint'lerinde (istemcisini biz kontrol ediyoruz, sürüm kayması yok) katı deserialization açılabilir. Mobil/public uçlar esnek kalır. Yapılmadı; değerlendirilmeye değer.

**Efor:** 2-3 saat (admin-only katı mod + testler).

---

## G17. Demo verisi bir gecede bayatlıyordu — founder her sabah boş panel görüyordu

**Kanıt (canlı, 2026-08-07):** `RichDemoSeeder` demo verisini üretiyor ama idempotent kapısı yüzünden ikinci kez çalışmıyordu. Verinin bir kısmı "bugüne" çapalı: kuaförün bugünkü randevuları, haftalık ciro, faturalama dönemi. Ertesi sabah:
```
GET /v1/owner/barber-shop/bookings  → 5 randevu, en erkeni 2026-08-08 (bugün 08-07 — bugüne HİÇBİRİ yok)
GET /v1/owner/billing/statements    → {"items":[]}
```
Faturalar ayrıca `M23` migration'ı eski tabloları DROP ettiği için tamamen boşalmıştı.

**Etki:** Yüksek (süreç). Founder bu ortamı elle test ediyor; her sabah "Bugün boş" ve boş faturalama ekranı görmek, hem test döngüsünü durduruyor hem de **gerçek bir ürün hatası mı yoksa bayat veri mi** ayrımını imkânsızlaştırıyordu — yanlış hata avına yol açar.

**Çözüm (uygulandı, `dd0b3d1`):** İki katman:
1. **Otomatik bayatlama tespiti** — her açılışta dev-login kuaförünün bugüne (Europe/Istanbul) ait randevusu var mı diye bakılır; yoksa demo verisi TRUNCATE edilip seeder zinciri "şimdi"ye göre yeniden çalışır.
2. **Dev-only `POST /v1/dev/reseed-demo`** — yeniden başlatmadan anlık tazeleme (Admin token + `IsDevelopment()`).

Coğrafya (81 il/ilçe/mahalle) ve plan tanımlarına dokunulmaz — yeniden üretmeleri pahalı.

**Production güvenliği (üç bağımsız katman, hepsi testli):** (a) resetter servisi `IsProduction()` ise exception atar — çağıran kim olursa olsun; (b) tek çağıran taraf kendi ortam kontrolüyle bloğu hiç devreye sokmaz; (c) endpoint Production'da **hiç map edilmez** — 404, yani yetkisiz değil, fiziksel olarak yok. Bu bir veri silme yolu; sızması felaket olurdu.

**Kalan bilinen sınır:** Randevu tarihleri doğası gereği mutlak; "çapasız veri" mümkün değil. Geniş pencere üretmek yerine otomatik tespit seçildi — "kaç gün dayanır" sorusunu ortadan kaldırıyor. Demo IBAN'ı gerçek bir IBAN değil (checksum doğrulanmadı), yalnız görsel amaçlı.

---

## G18. Yeni müşteri kaydında isim hiç sorulmuyor — kuaför "kim geliyor" bilmiyor

**Kanıt:** `backend/src/Kuaforum.Application/Modules/Identity/Auth/VerifyOtpCommand.cs:76` →
`User.CreateCustomerByPhone(phoneResult.Value)` — ikinci parametre (`displayName`) **geçilmiyor**.
Oysa `User.CreateCustomerByPhone(PhoneE164 phone, string? displayName = null)` (`User.cs:29`) ismi
kabul ediyor ve `VerifyOtpCommand` kaydının kendisinde `string? DisplayName` alanı **var** (satır 21) —
yani alan taşınıyor ama kullanılmadan düşüyor. Mobil taraf da OTP doğrulamasında isim göndermiyor
(`mobile/app/(auth)/otp.tsx`).

**Etki:** Orta-Yüksek (ürün). Telefonla kaydolan her yeni müşteri **isimsiz** oluşuyor. Sonuçları:
- Kuaför panelinde randevu satırında müşteri adı boş → "kim geliyor" belli değil. Kuaförün en temel
  ihtiyacı bu; demo veride isimler seeder'dan geldiği için sorun **görünmüyor**, gerçek kayıtta çıkacak.
- Bildirim ve hatırlatma metinlerinde hitap yok.
- Yorum yazıldığında yazar adı boş.

**Neden şimdiye kadar görünmedi:** Tüm test verisi seeder'dan geliyor ve seeder isim atıyor. Gerçek
kayıt akışı (telefon → OTP → hesap oluştur) hiç uçtan uca denenmedi. `[unverified 2026-08-11]`
canlı OTP akışıyla doğrulanmadı — üretimde OTP kodu loglanmadığı için (doğru davranış) dışarıdan
test edilemedi; kanıt kod okumasına dayanıyor.

**Çözüm:** OTP doğrulaması **yeni kullanıcı** yarattığında isim iste. İki seçenek:
(a) Doğrulama ekranında kod girildikten sonra aynı ekranda "Adın nedir?" alanı belirir (tek adım, az sürtünme).
(b) Doğrulama sonrası ayrı bir "Seni nasıl çağıralım?" ekranı — KVKK rıza ekranıyla birleştirilebilir,
zaten yeni kullanıcı oradan geçiyor.
Backend değişikliği küçük: `VerifyOtpCommand`'daki `DisplayName`'i `CreateCustomerByPhone`'a geçir.
İsim **zorunlu mu opsiyonel mi** — ürün kararı; opsiyonelse kuaför yine boş görebilir, zorunluysa
kayıt sürtünmesi artar.

**Efor:** backend 1 saat + mobil 2-3 saat + testler.

---

## Özet Tablo

| # | Başlık | Etki | Efor |
|---|---|---|---|
| G1 | Cross-shop çift randevu koruması yok | Orta | ✅ kapatıldı 2026-08-10 (uyar, engelleme) |
| G2 | WorkingHours değişimi mevcut randevuları kontrol etmiyor | Orta | ✅ kapatıldı 2026-08-10 |
| G3 | Stylist deaktivasyonu randevuları sessizce askıda bırakıyor | Orta-Kritik | ✅ kapatıldı 2026-08-10 |
| G4 | Müşteri-tarafı no-show/şikayet bildirimi yok | Orta | ✅ kapatıldı 2026-08-10 (min sürüm) |
| G5 | Owner=tek-çalışan otomatik stylist yaratılmıyor | Düşük-Orta | ✅ kapatıldı 2026-08-11 |
| G6 | Askıya alınan salonun müşterisine bildirim yok | — | ❌ kusur DEĞİL (RFC-015 D4) |
| G7 | "Stylist" policy/rol isimlendirmesi yanıltıcı — founder kararı bekliyor | Düşük | 1 saat (karar) + değişken |
| G8 | Bildirim idempotency merkezi değil (kısmi doğrulama) | Düşük `[unverified]` | 2-3 saat audit |
| G9 | PSP iade retry-tükenince alarm mekanizması doğrulanmadı | Orta `[unverified]` | 1-2 saat doğrulama |
| G10 | Test APK'sında API adresi derleme anında gömülü (ağ değişince ölüyor) | Orta | ✅ çözüldü 2026-08-06 |
| G11 | Yükleme durumlarında skeleton yerine düz "Yükleniyor..." metni | Düşük-Orta | 0.5 gün |
| G12 | "Bulunamadı" ile "sunucuya ulaşılamadı" ayrışmıyor | Orta | 0.5 gün |
| G13 | Entitlement kota kontrolünde yarış koşulu (ödemeli plan öncesi kapatılmalı) | Düşük→Orta | 3-5 saat |
| G14 | Gerçek çevrimdışı algısı yok (native bağımlılık gerekiyor, bilinçli erteleme) | Düşük | 3-4 saat |
| G15 | G11/G12 düzeltmesi kapsam dışı ekranlarda uygulanmadı (randevu detayı öncelikli) | Orta | ✅ kapatıldı 2026-08-07 |
| G16 | DTO alan adı değişince gövde sessizce sıfırlanıyor (kalıpsal para riski) | Yüksek → tekil düzeltildi | 2-3 saat (admin-only katı mod) |
| G17 | Demo verisi bir gecede bayatlıyordu (founder her sabah boş panel) | Yüksek (süreç) | ✅ kapatıldı 2026-08-07 |
| G18 | Yeni müşteri kaydında isim sorulmuyor — kuaför kimin geldiğini bilmiyor | Orta-Yüksek | 1 gün |

---

## Bağlantılar

- `docs/DOMAIN.md` — bu maddelerin kanıt bağlamı (S1-S12, A-D doğrulamaları)
- `docs/GAP-ANALYSIS.md` — mevcut, kapsamlı gap envanteri (P0/P1/P2) — burada tekrar edilmedi
- `docs/PROGRESS.md` — hangi GAP-ANALYSIS maddelerinin bu tur itibarıyla kapalı doğrulandığı
- `docs/ASSUMPTIONS.md` — direktif ↔ proje eşlemesi

**Son güncelleme:** 2026-08-06

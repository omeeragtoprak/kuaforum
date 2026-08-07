# RFC-014 — Abonelik gelir modeline geçiş + FAZ 1 domain düzeltmeleri

- **Durum:** Accepted (founder kararları 2026-08-06)
- **Yazar:** Claude Code (founder kararlarıyla)
- **Supersedes:** RFC-012 (aylık ciro komisyonu) — gelir modeli kısmı
- **Etkiler:** RFC-013 (dinamik randevu kuralları) — D2 maddesi genişletiyor
- **Kaynak:** Founder tarafından verilen "Otonom Geliştirme Direktifi" (bkz. `docs/ASSUMPTIONS.md`) + FAZ 1 domain denetimi (`docs/DOMAIN.md`, `docs/GAPS.md`)

---

## 1. Bağlam

Founder, greenfield bir proje için yazılmış bir otonom geliştirme direktifi verdi. Direktif mevcut projeye denetim çerçevesi olarak uyarlandı (ADR-001). Uyarlama sırasında direktif ile onaylı RFC'ler arasında **4 çelişki** tespit edildi ve founder'a toplu olarak soruldu. Bu RFC o kararları kayda geçirir.

FAZ 1 domain denetimi ayrıca kodda 5 yeni boşluk buldu (`docs/GAPS.md` G1-G6); ikisi bu RFC kapsamında çözülüyor, üçü backlog'a alınıyor.

---

## 2. Kararlar

### D1 — Gelir modeli: sabit aylık abonelik (ciro komisyonu iptal)

**Karar:** Platform gelirini **salon başına sabit aylık abonelik** olarak alır. Ciro üzerinden komisyon **alınmaz**.

| | V1 (launch) | V2 |
|---|---|---|
| **Müşteri** | Uygulama içinde hiç ödeme yapmaz. Ödeme salonda, hizmetten sonra. | Opsiyonel kapora (RFC-013 altyapısı hazır, bayrakla kapalı) |
| **Salon** | **Ücretsiz** — herkes `LAUNCH_FREE` planında | Aylık abonelik (Başlangıç / Pro) |

**Why:** Founder 2026-08-06'da direktifin D1 maddesini seçti. Gerekçe kayda geçirilmedi; muhtemel motivasyon: salonun ne ödeyeceğini önceden bilmesi (komisyon değişken, abonelik sabit) ve satış konuşmasının basitleşmesi.

**Dürüst not (unresolved concern):** Sabit abonelik, **düşük hacimli salon için giriş engelidir** — ayda 8 randevu alan bir berber, aylık ücreti randevu başına ~yüksek maliyet olarak hisseder ve churn eder. Komisyon modeli bu riski taşımıyordu (kazanmayan ödemiyor). V1 tamamen ücretsiz olduğu için bu risk **launch'ta gerçekleşmez**; V2 fiyatlaması yapılırken tekrar tartışılmalı. Bu not silinmemeli.

**How to apply:**

1. **Ücret hesabı tek noktadan:** Fatura tutarı artık `ciro × oran` değil, **plan fiyatı**. `PaymentAmounts`/komisyon hesabı gelir modeli olarak devre dışı; kapora komisyonu kavramı da düşer (kapora artık salonun kendi tahsilatıdır, platform payı yoktur).
2. **Faturalama omurgası korunur, tabanı değişir.** RFC-012 ile kurulan fatura → 7 gün vade → hatırlatma → askı → itiraz → ödeme zinciri **aynen geçerlidir**; yalnızca tutarın nereden geldiği değişir.

   > **Uygulama sapması (2026-08-07, bilinçli karar).** Bu madde ilk yazımında *"`CommissionStatement` aggregate'i `Kind` ayrımıyla genelleştirilir (`Commission` legacy, `Subscription` yeni)… aggregate silinip yeniden yazılmaz"* diyordu. Uygulama sırasında bu karar **değiştirildi**: aggregate `SubscriptionInvoice` olarak yeniden adlandırıldı, `commission_statements`/`commission_payments` tabloları migration'da **DROP** edildi, `basisAmountMinor`/`rateApplied` kolonları kaldırılıp yerlerine `planCode`/`planPriceMinor` geldi.
   >
   > **Gerekçe:** komisyon modeli tamamen ölü ve **üretimde tek satır veri yok** (canlıya çıkılmadı). `Kind` discriminator'ı, hiçbir zaman kullanılmayacak bir ikinci kolu ve her okuma noktasında bir dallanmayı kalıcı olarak taşımak demekti — sonu gelmeyen "bu alan hangi Kind'da dolu?" sorusu. Temiz yeniden adlandırma bu borcu hiç doğurmadı.
   >
   > **Bedeli:** `Down()` migration'ı şemayı geri kuruyor ama **veriyi değil** — geri alma çift yönlü kayıplı. Üretimde satır olmadığı için kabul edildi. Ayrıca `Down()`, `commission_rate_global` kolonunu `NOT NULL DEFAULT 0` ile geri ekliyor; eski domain invariantı `rate > 0` olduğu için rollback edilen kod bu satırla tutarsız olur. **Bu migration production'da geri alınabilir sayılmamalıdır.**
   >
   > Bu blok, dokümanın koddan sapmaması için eklendi — asıl karar yukarıdaki metin değil, buradaki uygulamadır.
3. **Plan ve hak (entitlement) modeli:** Özellik sınırları **koda gömülmez**, DB'den okunur.
   - `subscription_plans`: `code` (LAUNCH_FREE / STARTER / PRO), `name`, `priceMinor`, `isActive`
   - `plan_entitlements`: `planCode`, `key`, `value` — ilk anahtarlar: `max_staff`, `max_monthly_bookings`, `can_use_sms`, `can_use_deposit`
   - `shop_subscriptions`: `barberShopId`, `planCode`, `status`, `currentPeriodStart/End`
   - Tüm mevcut ve yeni salonlar `LAUNCH_FREE` planına bağlanır; `LAUNCH_FREE` fiyatı `0` olduğu için fatura üretilmez (job sıfır tutarlı fatura yaratmaz).
4. **Askı davranışı korunur:** Ödenmemiş abonelik faturası → 7 gün vade → askı. V1'de fiyat 0 olduğu için pratikte tetiklenmez, ama yol canlı ve testli kalır.
5. **Ödeme sağlayıcı soyutlaması korunur** (`IPaymentProvider`). Abonelik tahsilatı V2'de takılır; V1'de ödeme SDK'sı kurulmaz.

**Migration:** `M22_SubscriptionBilling`. Mevcut `CommissionStatement` satırları `Kind=Commission` olarak işaretlenir (demo verisi dışında üretimde satır yok — canlıya çıkılmadı, veri kaybı riski yok).

---

### D2 — Salon başına otomatik onay (`auto_confirm`)

**Karar:** Salon, randevuları **otomatik onaylama** ayarını açabilir. **Varsayılan KAPALI.**

**Why:** Direktif D3: yoğun salonlar her talebi elle onaylamak istemez. RFC-013'ün "her randevuda kuaför onayı" ilkesi **varsayılan davranış** olarak korunur; otomatik onay salonun bilinçli tercihidir.

**How to apply:**

1. `BookingPolicy`'ye `AutoConfirmEnabled` (bool, default `false`) eklenir — RFC-013'ün per-shop ayar yapısıyla aynı yerde, aynı owner ekranından yönetilir.
2. Randevu oluşturulurken:
   - `AutoConfirmEnabled == false` → mevcut davranış: `Pending`, kuaför onaylar.
   - `AutoConfirmEnabled == true` → doğrudan `Confirmed`, `Pending` atlanır. Slot kilidi anında kesinleşir (EXCLUDE constraint zaten `Confirmed`'ı kapsıyor).
3. **Kapora ile etkileşimi (kritik):** Salon hem kapora hem otomatik onay açtıysa, randevu **ödeme tamamlanana kadar** `Pending` kalır; ödeme onayı geldiğinde kuaför onayı beklemeden `Confirmed` olur. Yani otomatik onay, *kuaför onayını* atlar, *ödemeyi* atlamaz. (RFC-013'ün ödeme-sonra-onay akışı korunur.)
4. **Anti-spam kuralları otomatik onayı BYPASS ETMEZ.** RFC-013'ün `MinDaysBetweenBookings` / aylık limit kuralları önce çalışır; kural ihlali varsa randevu hiç oluşmaz.
5. Otomatik onaylanan randevuda kuaföre yine bildirim gider (metin farklı: "Yeni randevu — otomatik onaylandı").
6. Müşteri arayüzünde randevu özet ekranı, salonun ayarına göre doğru vaadi göstermeli: otomatik onaylı salonda "Randevun anında kesinleşir", diğerinde "Kuaför onayladığında bildirim gelecek". Landing/web metinleri **genel vaat olarak "kuaför onayı" demeyi sürdürür** (varsayılan bu).

---

### D3 — Çalışan/Stilist kullanıcı hesabı: V1.5'a ertelendi

**Karar:** Stilistin kendi telefonuyla giriş yapıp yalnız kendi takvimini gördüğü ayrı kullanıcı tipi **V1'de yapılmayacak**.

**Why:** Launch'ta salon sahibi tüm takvimi yönetir; tek kişilik ve küçük salonlarda zaten fiilî durum bu. Auth + kaynak-bazlı yetki + ayrı mobil panel + testler ~1 haftalık iş ve launch'ı geciktirir.

**How to apply:**
- `UserRole.Stylist` enum değeri **kaldırılmaz** ama hiçbir yerde atanmadığı açıkça dokümante edilir (`docs/DOMAIN.md`).
- `docs/GAPS.md` G7 olarak açık kalır; V1.5 planına girer.
- **Ertelemenin bedeli kabul edildi:** 3+ çalışanlı salonlarda sahip, çalışanların randevularını da kendisi takip etmek zorunda. Satış görüşmelerinde bu bir itiraz olarak gelirse önceliklendirme yeniden değerlendirilir.

---

### D4 — Müşterinin aynı saate iki salondan randevusu: uyar, engelleme

**Karar:** Müşteri aynı zaman aralığına ikinci bir salondan randevu alabilir; sistem **uyarır ama engellemez**.

**Why:** Founder, meşru senaryoyu korumak istedi (eş/çocuk adına randevu almak, art arda iki farklı hizmet). Sert engelleme bu kullanıcıları bloke ederdi.

**How to apply:**
1. Randevu özet adımında, müşterinin **çakışan** (zaman aralığı kesişen) aktif randevusu (`Pending`/`Confirmed`) varsa uyarı gösterilir: *"Bu saatte X Kuaför'de randevun var. Yine de devam etmek istiyor musun?"*
2. Kontrol **backend'de** yapılır (frontend'de gizlemek yeterli değildir) — özet/uygunluk yanıtında `conflictingBooking` alanı döner. Backend randevuyu **reddetmez**, yalnız bilgi verir.
3. Uyarıyı geçen müşteri için ek bir kayıt tutulmaz; no-show olursa mevcut güvenilirlik mekanizması işler.

---

## 3. Kapsam dışı bırakılanlar (bu RFC ile karar verilmedi)

`docs/GAPS.md`'de açık kalıyor, ayrı önceliklendirme bekliyor:

- **G2** — Salon çalışma saatini değiştirdiğinde o aralıktaki onaylı randevular sessizce etkisiz kalıyor; uyarı yok.
- **G3** — Stylist pasifleştirildiğinde gelecekteki randevuları askıda kalıyor; devir/iptal mekanizması yok.
- **G4** — Müşteri tarafında no-show/şikayet bildirimi yok (salon kapalı çıkarsa yapacak bir şey yok).
- **G6** — Askıya alınan salonun mevcut müşterilerine bildirim gitmiyor.

Bu dördü aynı kök soruna bağlanıyor: **salon tarafındaki değişiklik müşteriye yansımıyor.** Tek bir "randevu etkilendi" bildirim/iş akışıyla toplu çözülmeleri öneriliyor.

---

## 4. Uygulama sırası

| # | İş | Bağımlılık |
|---|---|---|
| 1 | `M22_SubscriptionBilling` — plan/entitlement/subscription tabloları + `CommissionStatement.Kind` | — |
| 2 | Fatura üretim job'ının plan fiyatına geçmesi; komisyon hesabının gelir yolundan çıkarılması | 1 |
| 3 | Entitlement okuma servisi + `LAUNCH_FREE` seed + mevcut salonların bağlanması | 1 |
| 4 | Owner/admin faturalama ekranlarının abonelik diline geçmesi (web-admin + mobile) | 2 |
| 5 | `BookingPolicy.AutoConfirmEnabled` + randevu akışı + kapora etkileşimi + testler | — |
| 6 | Cross-shop çakışma uyarısı (backend alanı + mobil onay diyaloğu) | — |
| 7 | Landing/web + mobil metinlerin gelir modeline göre gözden geçirilmesi ("komisyon" geçen her yer) | 2 |

**Yeniden kullanım notu (maliyet düzeltmesi):** Founder'a bu karar sunulurken "~1-2 hafta yeniden yazım" denmişti. FAZ 1 denetiminden sonra bu tahmin **fazla yüksek**: faturalama omurgasının %80'i (vade, hatırlatma, askı job'ı, itiraz, ödeme başlatma, admin/owner endpoint'leri, testler) taban değişikliğinden bağımsız. Gerçekçi tahmin **3-4 gün**. Asıl yeni iş plan/entitlement modeli ve arayüz metinleri.

# İYS / 6563 ETK Uyum Araştırması — Ticari Elektronik İleti ve İleti Yönetim Sistemi

> **Kapsam:** GAP P1 — "Marketing kanalı açılmadan ÖNCE İYS gereksinim araştırması + in-app consent ↔ İYS senkron stratejisi; transactional/marketing sınırını spec'e yaz."
> **Durum:** Araştırma tamamlandı, 2026-08-05. Hukuki kesinlik gerektiren noktalar etiketli.
> **Bağlantılı doc'lar:** `docs/rfc/RFC-008-kvkk-consent-flow.md` (in-app rıza altyapısı), `docs/01-research/turkey-compliance.md` (§İYS satırı bu doc ile detaylandı).

---

## 1. Yükümlülük Kapsamı — Ne "Ticari Elektronik İleti"dir?

**Yasal çerçeve:** [6563 sayılı Elektronik Ticaretin Düzenlenmesi Hakkında Kanun](https://www.mevzuat.gov.tr/MevzuatMetin/1.5.6563.pdf) + [Ticari İletişim ve Ticari Elektronik İletiler Hakkında Yönetmelik](https://www.lexpera.com.tr/mevzuat/yonetmelikler/ticari-iletisim-ve-ticari-elektronik-iletiler-hakkinda-yonetmelik) (RG 15.07.2015/29417, önemli değişiklik 04.01.2020).

- **Ana kural (opt-in):** Alıcının **önceden onayı olmadan** ticari elektronik ileti gönderilemez (6563 m.6). Onaylar ve ret bildirimleri **İYS (İleti Yönetim Sistemi, iys.org.tr)** üzerinden yönetilir; [İYS Ticaret Bakanlığı denetiminde çalışır](https://iys.org.tr/iys/kanun).
- **Tanım örnekleyicidir:** "Telefon, çağrı merkezleri, faks, otomatik arama makineleri, akıllı ses kaydedici sistemler, elektronik posta, kısa mesaj hizmeti **gibi** vasıtalar" — kanal listesi sınırlı sayıda (numerus clausus) değildir; yeni kanallar da kapsama girebilir ([Ersan Şen analizi](https://sen.av.tr/tr/makale/mevzuat-kapsaminda-ticari-elektronik-iletiler)).
- **İçerik belirleyicidir:** Promosyon, kampanya, indirim, hediye, **kutlama/tebrik/temenni**, reklam içeren "bilgilendirme", **anket ve memnuniyet aramaları** ticari ileti sayılır ([NetGSM sınıflandırması](https://www.netgsm.com.tr/sms/ileti-yonetim-sistemi-iys)). Doğum günü kutlaması bile ticari kapsamda.

### 1.1 Transactional muafiyet sınırı (Yönetmelik m.6/2)

Onay **aranmayan** iletiler: "Devam eden abonelik, üyelik veya ortaklık durumu ile **tahsilat, borç hatırlatma, bilgi güncelleme, satın alma ve teslimat veya benzeri durumlara ilişkin bildirimler**" + mevzuattan doğan bilgi verme yükümlülükleri ([Kayseri Ticaret İl Md. yönetmelik metni](https://kayseri.ticaret.gov.tr/yayinlar/tuketici/ticari-iletisim-ve-ticari-elektronik-iletiler-hakkinda-yonetmelik)).

**Kritik şart:** Bu bildirimlerde **"herhangi bir mal veya hizmet özendirilemez veya bunların tanıtımı yapılamaz."** Yani muafiyet ileti *tipine* değil *içeriğine* bağlı — randevu hatırlatma SMS'ine tek bir "%20 indirim" cümlesi eklemek iletiyi ticari yapar ve onaysızsa ihlal doğurur.

Ek muafiyetler:
- Alıcı iletişim bilgisini kendisi vermişse, **temin edilen mal/hizmete ilişkin değişiklik, kullanım ve bakım** iletileri onaysız gönderilebilir.
- **Tacir ve esnaf alıcılara** önceden onay zorunlu değildir (ret hakkı saklı; ret sonrası gönderim yasak). Kuaforum'un **kuaför işletmelerine** (B2B taraf) göndereceği iletiler için önemli — ancak 2020 değişikliği sonrası tacir/esnafa gönderimde de İYS ret kontrolü beklenir `[unverified 2026-08-05 — tacir/esnaf istisnasının İYS kontrol yükümlülüğüyle etkileşimi; hukukçu teyidi gerek]`.

### 1.2 Push notification İYS kapsamında mı? (kritik soru)

İki ayrı soru var, cevaplar farklı:

1. **Push için İYS kaydı/senkronu gerekir mi? → Hayır.** İYS'nin yönettiği kanallar **arama (sesli), mesaj (SMS) ve e-posta** ile sınırlıdır; push alıcıları İYS'ye yüklenmez ([MC Legal İYS Rehberi](https://www.mclegal.com.tr/wp-content/uploads/2021/02/ileti-yonetim-sistemi-rehberi.pdf), [euromsg SSS](https://www.euromsgexpress.com/15-soruda-ileti-yonetim-sistemi-iys-nedir/)).
2. **Ticari amaçlı push için onay gerekir mi? → Evet (güvenli yorum).** Kanal listesi örnekleyici olduğundan ticari amaçlı push, ticari elektronik ileti tanımına girer; opt-in onay + ret imkânı gerekir. Bizim in-app `ConsentPurpose.Marketing` rızası + OS-level bildirim izni bu şartı karşılar; İYS'ye bildirim gerekmez.

`[unverified 2026-08-05 — Ticaret Bakanlığı'nın push'a özgü bağlayıcı yazılı görüşü bulunamadı; ikincil kaynaklar yukarıdaki iki-katmanlı yorumda birleşiyor. İlk push kampanyası öncesi hukukçu teyidi gerek.]`

**Why:** Sınır içerik-bazlı olduğu için "hangi bildirim hangi tarafta" sorusu şablon bazında sabitlenmeli; aksi hâlde bir copywriting değişikliği farkında olmadan ihlal üretir.
**How to apply:** § 6'daki tabloyu notification şablonlarının spec'i say; transactional şablonlara tanıtım dili eklemeyi PR review checklist'ine yaz ("İYS sınır kontrolü").

---

## 2. İYS Kayıt Süreci

- **Kimler kaydolmak zorunda:** SMS/e-posta/sesli arama ile **ticari** elektronik ileti gönderen tüm gerçek/tüzel kişiler ("hizmet sağlayıcı"). **Hiç ticari ileti göndermeyen kayıt olmak zorunda değil** ([BTS Legal SSS](https://www.bts-legal.com/insights/publications/hizmet-saglayicilar-icin-ticari-elektronik-ileti-yonetim-sistemi-kayit-yukumlulugu-hakkinda-sikca-sorulan-sorular/)). → Kuaforum kampanya göndermediği sürece İYS kaydı **zorunlu değil**.
- **Başvuru:** iys.org.tr üzerinden; **MERSİS numarası**, marka/başlık bilgileri, yetkili kimlik bilgileri ve taahhütname istenir ([NetGSM başvuru rehberi](https://blog.netgsm.com.tr/ileti-yonetim-sistemi-iys-basvuru-sureci/)). Başvuru sonucu **İYS kodu** ve **marka kodu** verilir.
- **Ücretlendirme:**
  - **Temel Hizmetler ücretsiz** — mevzuatın zorunlu kıldığı tüm işlemler web arayüzünden **manuel** yapılır ([iys.org.tr Kurumsal Hizmetler](https://iys.org.tr/hizmet-saglayici/kurumsal-hizmetler), [Mysoft özet](https://www.mysoft.com.tr/ileti-yonetim-sistemi-iys-temel-hizmetlerin-icerigi-ve-sartlari-nedir)).
  - **API/entegrasyon (İLETİ paketleri, 2026 KDV dahil):** İLETİ-5 (5.000 adres) **4.601 TL**, İLETİ-25 **8.313 TL**, İLETİ-75 **14.921 TL**, İLETİ-150 **23.055 TL**, İLETİ-250 **29.483 TL** ([NetGSM fiyat sayfası](https://www.netgsm.com.tr/fiyatlar/ileti-yonetim-sistemi), [VatanSMS tarifesi](https://www.vatansms.com/iys-fiyat-tarifesi) — iki iş ortağı aynı rakamları listeliyor; süre sınırı yok, adres yüklendikçe düşer). `[unverified 2026-08-05 — iys.org.tr fiyat sayfası SPA olduğundan doğrudan doğrulanamadı; resmi tarife başvuru sırasında teyit edilmeli]`
  - İş ortağı (NetGSM) üzerinden çalışmak da entegrasyon sayılır → İLETİ paketi gerekir; iş ortağının kendi paket/servis ücreti ayrıdır.
- **Süre kuralları (Yönetmelik, 04.01.2020 değişikliği):**
  - İYS **dışında** alınan onay (bizim in-app consent dahil) **3 iş günü içinde İYS'ye kaydedilmek zorunda; kaydedilmeyen onay geçersizdir** ([Kesikli Hukuk](https://kesikli.com/tr/news-insight/2021-05-27-ticari-elektronik-iletiler-ve-ileti-yonetim-sistemi/), [Bener bülteni](https://bener.com/tr/ticari-iletisim-ve-ticari-elektronik-iletiler-hakkinda-yonetmelikte-degisiklik-yapilmasina-dair-yonetmelike-iliskin-bulten/)).
  - Alıcının **ret** bildirimi **3 iş günü** içinde İYS'ye iletilir; ret sonrası gönderim **3 iş günü** içinde durdurulur.
  - İYS üzerinden (kısa link, e-Devlet vb.) alınan onaylar için ayrıca yükleme gerekmez.

**⚠️ Kritik bulgu — 3 iş günü penceresi ve bizim mevcut onaylarımız:** Bugün in-app topladığımız Marketing rızaları, ileride İYS kaydı açıldığında 3 iş günü penceresini çoktan aşmış olacak. "İYS'ye kaydedilmeyen onay geçersizdir" kuralının lafzi okumasında bu onaylar SMS/e-posta kampanyası için **kullanılamaz** olabilir; kayıt sonrası toplu geriye dönük yüklemenin kabul edilip edilmeyeceği belirsiz. `[unverified 2026-08-05 — İYS'nin geç/toplu onay yüklemeye operasyonel yaklaşımı; hukukçu teyidi gerek]`. Güvenli iki yol: **(a)** İYS kaydını ilk gerçek kullanıcı onayı üretime düşmeden açıp senkronu baştan kurmak (temel kayıt ücretsiz), **(b)** ilk kampanya öncesi mevcut kullanıcılardan İYS üzerinden yeniden onay toplamak. Öneri: (a).

---

## 3. Teknik Entegrasyon

### 3.1 NetGSM = resmi İYS iş ortağı (doğrulandı)

NetGSM, İYS **iş ortağıdır**; NetİYS ürünüyle izinli rehber yönetimi + otomatik İYS senkronu sağlar ([NetGSM İYS sayfası](https://www.netgsm.com.tr/sms/ileti-yonetim-sistemi-iys), [NetİYS kayıt](https://bilgibankasi.netgsm.com.tr/bilgi-bankasi/netiys-kayit-netiys-kullanicisi-olmak/)):

- Akış: İYS'ye hizmet sağlayıcı kaydı → İYS panelinden **NetGSM'i iş ortağı olarak yetkilendir** → marka kodu ile NetGSM'e NetİYS başvurusu.
- Gönderimde: mesaj niteliği (ticari/bilgilendirme) gönderen tarafından beyan edilir; **ticari** seçilirse NetGSM İYS veritabanından otomatik kontrol yapar, **izinsiz numaralara gönderimi bloke eder**. Ret'ler (3338 kısa numara, 0800 IVR) otomatik İYS'ye işlenir.
- **Sorumluluk bizde:** "Ticari mi bilgilendirme mi" ayrımını yapmak hizmet sağlayıcının (Kuaforum) sorumluluğu — NetGSM'in otomatik kontrolü yanlış "bilgilendirme" beyanını yakalamaz.

### 3.2 E-posta kanalı (Resend) uyarısı

E-posta İYS kapsamındadır, ancak **Resend (yabancı ESP) İYS kontrolü yapmaz**. E-posta marketing açılırsa send-time İYS kontrolünü kendimiz yapmak zorundayız (İYS API sorgusu veya günlük snapshot diff). **Öneri:** ilk kampanya dalgasını SMS + push ile sınırla; e-posta marketing'i İYS e-posta senkronu kurulana kadar açma.

### 3.3 In-app consent ↔ İYS senkron mimarisi (öneri)

Mevcut altyapı: `Consent` entity (`backend/src/Kuaforum.Domain/Modules/Identity/Consent.cs`) — `ConsentPurpose.Marketing`, versiyonlu, `ConsentGrantedEvent`/`ConsentRevokedEvent` domain event'leri, `GrantedAt`/`RevokedAt`/`Source` alanları mevcut. RFC-008 bu akışı kurdu; İYS senkronu üstüne şu şekilde oturur:

```
ConsentGranted/Revoked(Purpose=Marketing)
   → Outbox (mevcut pattern)
   → IysSyncJob (Hangfire): NetİYS API addConsent / addRejection
       - SLA: mevzuat 3 iş günü; hedef < 15 dk
       - AutomaticRetry + dead-letter alarmı (Seq/Sentry)
       - Idempotency: (userId, purpose, version, action) anahtarıyla
   → Günlük ReconciliationJob: İYS'den değişiklik çek (pull)
       - İYS-kaynaklı ret (vatandas.iys.org.tr / e-Devlet / 3338) → local Consent.Revoke()
       - Diff raporu: local granted ∧ İYS'de yok → yeniden gönder + alarm
```

- **Source of truth ayrımı:** SMS/e-posta kampanyası için **İYS** nihai otoritedir (send-time kontrol zaten NetGSM'de); push + UI toggle için **local Consent** otoritedir. Çift yönlü senkron şart çünkü kullanıcı İYS/e-Devlet'ten ret verebilir — sadece kendi UI'mıza bakmak yeterli değildir.
- **Kanal bazlı izin:** İYS onayları kanal bazlıdır (arama/mesaj/e-posta ayrı). Mevcut tek `Marketing` toggle → İYS'ye **mesaj + e-posta** onayı olarak yazılır; arama kanalı hiç açılmaz. İleride ayrı switch'ler gerekirse `ConsentPurpose` değil, Consent'e kanal alanı eklemek gerekir — İYS pull'unda kanal bazlı ret gelebileceği için local modelin buna hazırlanması V1.5 işi.
- **İspat yükü:** İhlal iddiasında onayın varlığını **hizmet sağlayıcı ispatlar**. Mevcut kayıt (versiyon + kaynak + zaman) iyi; İYS senkronu açılırken consent kaydına IP/cihaz bilgisi eklemek ispatı güçlendirir (KVKK veri minimizasyonuyla dengele).

---

## 4. Cezalar ve Şikayet Mekanizması (2026)

2026 tutarları %25,49 yeniden değerleme ile güncellendi (Tebliğ, RG 25.12.2025/33118 — [Erdem&Erdem](https://www.erdem-erdem.av.tr/bilgi-bankasi/elektronik-ticaret-kanunu-kapsaminda-idari-para-cezalari-2026-yili-icin-guncellendi), [Alomaliye](https://www.alomaliye.com/2025/12/25/2026-yili-elektronik-ticaretin-duzenlenmesi-hakkinda-kanunun-12-nci-maddesi-ipc/)):

| İhlal | 2026 ceza |
|---|---|
| Onaysız / onaya aykırı ticari ileti (m.12/1-a) | 2.859 – 14.309 TL |
| İletide gönderici kimliği / içerik / ret imkânı eksikliği | 2.859 – 28.620 TL |
| **Bir defada birden fazla kimseye onaysız gönderim** (m.12/2) | (a) bendi cezası **10 katına kadar** artırılır (~143.000 TL'ye kadar) ([6563 metni](https://www.mevzuat.gov.tr/MevzuatMetin/1.5.6563.pdf), [JetSMS özeti](http://www.jetsms.net/index.php/iys-cezalari/)) |

- Ceza yetkisi Ticaret Bakanlığı / il ticaret müdürlüklerindedir; ceza **ihlal başına** kesilebilir, tek kampanya birden çok şikayet üretebilir.
- **Şikayet:** Alıcı, iletinin gönderilmesinden itibaren **3 ay** içinde [e-Devlet "Ticari Elektronik İleti Şikayet Sistemi"](https://www.turkiye.gov.tr/gtb-ticari-elektronik-ileti-sikayet-sistemi) veya il ticaret müdürlüğü üzerinden şikayet eder; ön inceleme İYS kayıtları üzerinden yapılır ([Uşak Ticaret İl Md. SSS](https://usak.ticaret.gov.tr/sikca-sorulan-sorular/ticari-elektronik-ileti-ile-ilgili-sorular)). Pratik risk: tek şikayetçi bile denetim tetikler; savunma tamamen İYS kayıtlarına dayanır — "onayımız vardı ama İYS'de yoktu" savunması geçersizdir.

---

## 5. V2 Uyarısı — Kuaför Adına Kampanya (aracı hizmet sağlayıcı tuzağı)

İleride "kuaför kendi müşterilerine kampanya göndersin" (CRM) feature'ı açılırsa Kuaforum **aracı hizmet sağlayıcı** rolüne girer ([Mysoft AHS yazısı](https://www.mysoft.com.tr/ileti-yonetim-sistemi-iys-araci-hizmet-saglayici-kimdir-yukumlulukleri-nelerdir)):

- Aracı hizmet sağlayıcı **başkası adına onay toplayamaz** — her kuaförün **kendi İYS kaydı** (kendi hizmet sağlayıcı sıfatı, kendi onayları) gerekir.
- **İYS'ye kayıtsız kuaför adına gönderim başlatılamaz**; sistemimizin İYS-uyumlu olması ve iletide en az bir marka/unvan bilgisine yer verilmesi zorunlu.
- **Push-back:** Bu, binlerce esnafın İYS onboarding'i demek — feature maliyeti UI'dan ibaret değil; spec aşamasında compliance maliyeti ayrıca hesaplanmalı. MVP/V1.5 kapsamına alınmamalı.

---

## 6. Kuaforum Uygulama Planı

### 6.1 Transactional / marketing sınır tablosu (mevcut bildirimler)

| İleti | Kanallar (bugün) | Sınıf | Dayanak / şart |
|---|---|---|---|
| Booking confirmed | Push + InApp + SMS | **Transactional** | m.6/2 "satın alma ve teslimat benzeri durum bildirimi"; tanıtım içeremez |
| Booking cancelled (+iade bilgisi) | Push + InApp | **Transactional** | m.6/2 durum bildirimi + tahsilat/iade |
| Booking reminder (T-24h) | Push + SMS | **Transactional** | m.6/2 bilgi güncelleme; **şablona kampanya/indirim cümlesi eklenemez** |
| Booking started / completed | Push + InApp | **Transactional** | m.6/2 hizmet ifası bildirimi |
| No-show bildirimi | Push + InApp | **Transactional** | m.6/2 üyelik/durum bildirimi |
| KYC sonuç (owner onay/red) | Push + InApp | **Transactional** | m.6/2 üyelik durumu; alıcı esnaf/tacir (§1.1 istisnası ayrıca geçerli) |
| OTP / güvenlik SMS'i | SMS | **Kapsam dışı** | Ticari amaç yok; sözleşmenin ifası |
| Review/memnuniyet isteği (ileride) | — | **Gri alan → Marketing say** | Anket/memnuniyet iletileri ticari sayılıyor (NetGSM sınıflandırması); Marketing consent'e bağla `[hukukçu teyidi gerek]` |
| Kampanya, indirim, duyuru, kutlama, re-engagement ("seni özledik") | Push/SMS/e-posta (ileride) | **Marketing** | Onay + (SMS/e-posta için) İYS kaydı ve send-time kontrol |

### 6.2 Launch öncesi (kampanya yokken) — yapılacaklar

1. **İYS kaydı zorunlu değil** — ticari ileti gönderilmiyor. Maliyet: 0.
2. Transactional şablon disiplini: tanıtım dili yasağı PR checklist'ine ("İYS sınır kontrolü") — bu doc referans.
3. In-app Marketing consent toplanmaya devam (RFC-008 akışı değişmez).
4. **Önerilen (ucuz sigorta):** Founder, şirket MERSİS + marka bilgisiyle **İYS temel kaydını erkenden** açar (ücretsiz) → 3-iş-günü senkron penceresi ilk gerçek onaydan itibaren tutturulabilir (§2 kritik bulgu). Karar founder'da; açılırsa senkron job'ı da birlikte devreye alınmalı, yoksa kayıt tek başına pencereyi kurtarmaz.

### 6.3 İlk kampanya öncesi checklist (hard gate)

- [ ] Hukukçu teyidi: push kapsamı (§1.2), geriye dönük onay yükleme (§2), review-isteği sınıfı (§6.1).
- [ ] İYS hizmet sağlayıcı kaydı + marka/İYS kodu (founder: MERSİS, imza yetkilisi, taahhütname).
- [ ] İLETİ paketi satın alımı (başlangıç: İLETİ-5, 2026'da 4.601 TL) — tarife iys.org.tr'den teyit.
- [ ] İYS panelinden NetGSM iş ortağı yetkilendirmesi + NetİYS başvurusu; ticari SMS başlığı (marka tescil belgesi istenebilir `[unverified 2026-08-05 — başlık-tescil şartı NetGSM onboarding'inde teyit edilecek]`).
- [ ] `IysSyncJob` + `ReconciliationJob` (§3.3) implement + staging'de İYS test ortamıyla doğrulama; mevcut Marketing onaylarının backfill kararı (§2'deki a/b yoluna göre).
- [ ] Kampanya şablonlarında zorunlu unsurlar: gönderici kimliği (marka), ret yolu ("3338'e ... yaz" / e-posta list-unsubscribe).
- [ ] E-posta kampanyası bu gate'e dahil DEĞİL — İYS e-posta send-time kontrolü kurulana kadar SMS + push ile sınırlı (§3.2).
- [ ] İlk gönderim küçük segmentle (ör. 100 kullanıcı) smoke: İYS bloke loglarını ve ret akışını gözle.

**Why:** Ceza modeli "kampanya başına 10x artırım + şikayet başına dosya" şeklinde ölçeklendiği ve ispat tamamen İYS kaydına dayandığı için, senkron altyapısı kampanyadan *önce* bitmek zorunda; sonradan tamir edilemez (geçersiz onayla atılan ileti atılmıştır).
**How to apply:** Bu checklist'i ilk kampanya epic'inin Definition of Done'ına kopyala; GAP-ANALYSIS'teki ilgili P1 maddesini bu doc'a link'le kapat.

---

**Son güncelleme:** 2026-08-05 · Araştırma: WebSearch/WebFetch, birincil kaynak öncelikli (mevzuat.gov.tr, iys.org.tr, ticaret.gov.tr, netgsm.com.tr); ikincil hukuk bürosu analizleri çapraz doğrulama için kullanıldı.

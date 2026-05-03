# Türkiye Uyum Manzarası — Kuaför Randevu + Ödeme Marketplace

**Tarih:** 2026-05-02
**Kapsam:** Mobil app (iOS/Android) + backend; iki yanlı pazaryeri (kuaför ↔ müşteri); in-app ödeme.
**Uyarı:** Tüm parasal eşik/oran rakamları `[unverified 2026-05-02 — needs lawyer/CPA confirmation]` etiketi ile ele alınmalıdır.

---

## 1. KVKK (6698 sayılı Kanun)

### 1.1 Açık Rıza — Mobil
KVKK 14 Ocak 2026 tarihli kamuoyu duyurusunda, mobil uygulamalar için **parçalı (granular) açık rıza** ilkesini netleştirdi: birden fazla işleme amacı varsa her biri için **ayrı seçim** zorunlu ([KVKK Push Bildirim](https://www.kvkk.gov.tr/Icerik/8578/mobil-uygulamalar-uzerinden-gonderilen-anlik-bildirimlere-iliskin-kamuoyu-duyurusu); [KVKK Açık Rıza Rehberi](https://www.kvkk.gov.tr/yayinlar/A%C3%87IK%20RIZA.pdf)).

Açık rıza zorunlu kategoriler:
- **Konum (background/precise):** "yakındaki kuaförler" için 5/2-c (sözleşme ifası) yeterli; pazarlama amaçlı ise **ayrı** açık rıza.
- **Kamera/Fotoğraf:** açık rıza zorunlu.
- **Rehber/Kişiler:** açık rıza zorunlu (üçüncü kişi verisi).
- **Pazarlama (push/SMS/e-posta):** İYS + KVKK açık rıza birlikte.
- **Profilleme/çapraz satış:** ayrı parçalı rıza.

### 1.2 VERBİS Eşiği (2026)
Genel veri sorumluları için: çalışan **>50** *veya* yıllık bilanço **>100M TL** ise zorunlu ([Esenyel — VERBİS 2026](https://www.esenyelpartners.com/tr/2026-verbis-kayit-istisnalari-100-milyon-tl-esigi-ve-guncel-kurallar/); [KVKK İstisna Duyurusu](https://www.kvkk.gov.tr/Icerik/8388/KAMUOYU-DUYURUSU)). Özel nitelikli veri ana faaliyet ise eşik 10 çalışan / 10M TL'ye düşer. Cezalar 272K–13,6M TL arası ([Nesil 2026](https://www.nesilteknoloji.com/verbis-kayit-suresi-ve-cezalari-2026/)).

**Yorum:** Phase 0/1'de zorunlu değil; Phase 2'de tetiklenir. `[unverified 2026-05-02]`

### 1.3 Aydınlatma Şablonu
Resmi kaynak: KVKK "Aydınlatma Yükümlülüğünün Yerine Getirilmesi Rehberi" + 10 Mart 2018 / 30356 sayılı RG Tebliği ([KVKK Aydınlatma](https://www.kvkk.gov.tr/Icerik/2033/Aydinlatma-Yukumlulugu-)). Hazır şablon yok; her veri sorumlusu kendine uyarlamalı.

### 1.4 Veri İhlali Bildirimi
**72 saat** içinde Kurum'a bildirim ([KVKK 2019/10](https://www.kvkk.gov.tr/Icerik/5362/Veri-Ihlali-Bildirimi)). Online: `ihlalbildirim.kvkk.gov.tr`. İlgili kişilere de "uygun yol" ile bildirim zorunlu.

### 1.5 Yurt Dışı Aktarım — AWS Frankfurt / Azure NL
KVKK 2026 itibariyle **hiçbir ülke için yeterlilik kararı vermemiştir** ([KVKK Yurt Dışı Aktarım](https://www.kvkk.gov.tr/Icerik/2053/Yurtdisina-Aktarim)). 7499 sayılı Kanun (Mart 2024) "uygun güvenceler" rejimi:
- **Standart Sözleşme** (Kurul yayımlı, 04.06.2024) imza + 5 iş günü içinde Kurum bildirimi, *veya*
- **BCR**, *veya*
- Açık rıza istisnası (arızi).

**Pratik:** AWS Frankfurt için Türkiye'ye özel Kurul-onaylı standart sözleşme imzalanmalı. `[unverified 2026-05-02]`

### 1.6 Hassas Veri Sınıflandırması
KVKK m.6 sınırlı listelidir ([KVKK Özel Nitelikli](https://www.kvkk.gov.tr/Icerik/2051/Ozel-Nitelikli-Kisisel-Veriler)).
- **Konum:** özel nitelikli **DEĞİL** (genel kişisel veri).
- **Saç/cilt tipi, alerji notu, kronik hastalık:** **sağlık verisi** → **özel nitelikli** → açık rıza + ek güvenlik (KVKK 2018/10) zorunlu. Salon "alerji notu" tutacaksa profil seviyesinde ayrı rıza ekranı şart.

### 1.7 İlgili Kişi Hakları SLA
Erişim/silme/taşınabilirlik/düzeltme için **30 gün** içinde ücretsiz cevap ([KVKK Başvuru](https://www.kvkk.gov.tr/Icerik/2046/Ilgili-Kisiler-Tarafindan-Yapilan-Basvurularin-Cevaplanmasi-Yukumlulugu)). Backend'de `data-export` ve `account-deletion` 30 gün SLA'ya uygun, audit-loglu olmalı.

---

## 2. E-Arşiv Fatura (GİB)

### 2.1 Eşik (2026)
**Genel:** 2022+ dönemler brüt hasılatı **3M TL ve üzeri** olanlar e-Fatura + e-Arşiv zorunlu ([Paraşüt 2026](https://www.parasut.com/blog/e-fatura-ve-e-arsiv-zorunlulugu); [Mikro 2026](https://www.mikro.com.tr/e-arsiv-fatura-siniri/)).
**E-ticaret/aracı hizmet sağlayıcı:** **500K TL** brüt hasılat (VUK 509 GT). Pazaryeri muhtemelen bu kategoride. `[unverified 2026-05-02 — CPA teyidi]`

### 2.2 Çift Fatura Modeli
Pazaryeri klasik modelinde **iki ayrı fatura** doğrudur (Trendyol/Hepsiburada modeli):

| # | Düzenleyen | Alıcı | Konu | KDV |
|---|---|---|---|---|
| 1 | Kuaför | Müşteri | Hizmet (saç kesim, vb.) | %20 |
| 2 | Platform | Kuaför | Aracılık komisyonu | %20 |

Tek fatura mümkün değil; kuaförün KDV mükellefiyeti var, platform yalnız aracı ([Alomaliye — Komisyon KDV](https://www.alomaliye.com/2025/12/16/e-ticaretteki-komisyon-faturalarinda-kdv/)).

**Stopaj:** 2026'da pazaryerleri satıcıya yapılan ödemelerden **%1 ön gelir vergisi tevkifatı** kesiyor (e-ticaret tevkifat rejimi) ([Hellac](https://hellac.com/blogdetay/tum-yonleriyle-e-ticarette-tevkifat-uygulamasi-ve-ornekler)). Hizmet pazaryerine genişlemesi `[unverified 2026-05-02]`.

### 2.3 KDV Oranı (2026)
Genel KDV %20 (Temmuz 2023'ten beri). Saç kesim/sakal/boya/makyaj 2007/13033 BKK ekli I/II sayılı listede yok → **%20 genel oran** ([Dopigo 2026](https://www.dopigo.com/2026-kdv-oranlari-nedir/)). Komisyon faturası da %20.

### 2.4 Anlık Fatura Süresi
VUK 509: hizmet teslim tarihinden **azami 7 gün** içinde fatura ([Esas Denetim — 7 gün](https://esasdenetim.com/2024-sirkuler/geriye-donuk-7-gunu-asan-surede-efatura-duzenlenmesi-halinde-usulsuzluk-cezasi-1771)). Aşıldığında "hiç düzenlenmemiş" sayılır + VUK 353 cezası (≥3.400 TL veya %10).

**Pratik:** Ödeme anında otomatik kesim en güvenli; gün sonu toplu da kabul ama 7 gün limiti içinde.

### 2.5 Özel Entegratör (Nisan 2026 fiyatları)
KOBİ paketleri **150–800 TL/ay**; kurulum 5K–50K TL ([WionSoft 2026](https://wionsoft.com/blog/e-fatura-e-arsiv-maliyetleri-2026-kontor-fiyatlari); [KobiTime](https://kobitime.com/e-fatura-entegrator-2026/)).

| Sağlayıcı | REST API | Notlar |
|---|---|---|
| **Nilvera** | Evet (modern) | [VKN API](https://developer.nilvera.com/api/genel-api/mukellef-islemleri/vkn-ile-sorgular) dahil |
| **Logo eLogo** | Evet | Logo ürünleriyle entegre |
| **Uyumsoft** | Evet | Kurumsal odak |
| **Foriba (QNB)** | Evet | Finans ekosistem |
| **Mikro** | Kısmi | ERP odaklı |
| **EDM** | Evet | Klasik |

**Tavsiye:** Nilvera REST-first, modern stack için en uygun. `[unverified 2026-05-02]`

### 2.6 Basit Usul Kuaför Geçişi
Berber/kuaför büyükşehirde dahi basit usulde kalabilir.
- **2026:** KDV dahil 3.000 TL'yi aşan fatura için GİB e-Belge portalı zorunlu.
- **1 Ocak 2027:** Tutar bağımsız **tüm faturalar** GİB e-arşiv portalı zorunlu ([Vergi Mevzuat 2026](https://www.vergimevzuat.com/2026/01/bilgi-notu-2026-yili-basit-usul-mukelleflerin-e-arsiv-fatura-duzenlemesi-hakkinda/); [Bizim Hesap](https://bizimhesap.com/blog/basit-usul-e-arsiv-zorunlulugu)).

**Pazaryeri sonucu:** 2027'den sonra her randevu için e-arşiv zorunlu — Phase 1 must-have.

---

## 3. Tüketici Hakları (6502 sayılı Kanun)

### 3.1 Cayma Hakkı İstisnası
Online randevu = mesafeli sözleşme. Mesafeli Sözleşmeler Yön. **m.15/g**: "belirli bir tarihte/dönemde yapılması gereken... eğlence/dinlenme amaçlı boş zaman" sözleşmeleri istisna ([Resmi Gazete](https://www.resmigazete.gov.tr/eskiler/2014/11/20141127-6.htm); [Ticaret Bakanlığı](https://tuketici.ticaret.gov.tr/yayinlar/tuketici-bilgi-rehberi/mesafeli-sozlesmeler-hakkinda-bilgilendirme)).

**Kuaför yorumu:** "Belirli tarih/saatte kişisel bakım hizmeti" istisna kapsamına yakın; içtihat netleşmemiş. Daha güvenli zemin **m.15/h** "tüketici onayı ile ifasına başlanan hizmetler" — randevuya gelmek = ifaya başlanma. `[unverified 2026-05-02]`

### 3.2 No-Show / İptal Politikası
- Cayma süresi içinde tüketiciden **hiçbir ad altında ödeme alınamaz** (yön. m.20/2). Ön ödeme alınacaksa şeffaf koşul + iade taahhüdü.
- **Cezai şart:** TBK m.182 + 6502 m.5 (haksız şart) — aşırı şart geçersiz. %100 stopaj muhtemelen haksız; %25–50 makul aralık `[unverified 2026-05-02]`.
- **İade süresi:** 14 gün, aynı ödeme aracına.
- **Önerilen model:** Kademeli iade penceresi (24h öncesi %100, 12h %50, 0–12h %0).

---

## 4. Kuaför Doğrulama (Esnaf Mevzuatı)

### 4.1 5362 Sayılı Esnaf Kanunu
Kuaförlük "esnaf ve sanatkâr" kategorisi ([5362 SK](https://www.mevzuat.gov.tr/mevzuatmetin/1.5.5362.pdf)). Belediyeler ruhsat verirken sicil tasdiknamesi + oda üye belgesi ister (m.68).

### 4.2 Sıhhi Müessese Ruhsatı
İşyeri Açma Yön. kapsamında **sıhhi işyeri** ([Yön.](https://www.mevzuat.gov.tr/mevzuat?MevzuatNo=20059207&MevzuatTur=21&MevzuatTertip=5); [Beyoğlu Bld](https://beyoglu.bel.tr/hizmetlerimiz/basvuru-kaynaklari/isyeri-acma-ve-calisma-ruhsatlari/sihhi-muesseseler-ve-aranacak-sartlar/)). Belediyeden zorunlu, 1 ay içinde yerinde denetim. **1 Ocak 2026** itibariyle IPL/Diode lazer için TÜRKAK akreditasyonu yıllık ([Menajer.im 2026](https://www.menajer.im/blog/guzellik-salonu-is-yeri-acma-ruhsati-sartlari-ve-ucreti-2026/)).

### 4.3 ESBİS
T.C. Ticaret Bakanlığı ([esbis.ticaret.gov.tr](https://esbis.ticaret.gov.tr/)). Belge doğrulama web form var (TC kimlik + belge no + güvenlik kodu). **Public REST API yok.** Phase 0/1: belge upload + manuel onay; Phase 2: bakanlığa API başvurusu. `[unverified 2026-05-02]`

### 4.4 MYK Kuaför Seviye 4
"Kuaför Seviye 4" ulusal yeterliliği ([TYC](https://www.tyc.gov.tr/yeterlilik/kuafor-seviye-4-myk-mesleki-yeterlilik-belgesi-TR00102854.html)). 23 Mart 2026 RG'de 2026/1 Tebliğ ile zorunlu meslek 204→244 ([KobiTime](https://kobitime.com/myk-mesleki-yeterlilik-belgesi-zorunlulugu-2026/)). Kuaförlük zorunlu listede `[unverified — portal.myk.gov.tr kontrol]`. Platform onay akışında belge upload edilmeli.

### 4.5 VKN Doğrulama (GİB)
GİB Dijital Vergi Dairesi'nde VKN form var ([dijital.gib.gov.tr](https://dijital.gib.gov.tr/)). **Public REST API yok**, ama özel entegratörler (Nilvera vd.) üzerinden VKN sorgu API mevcut. Önerilen: VKN gir → entegratör API → mükellef adı/durumu doğrulama.

---

## 5. Compliance Action List

| # | Madde | Eşik / Tetikleyici | Sorumlu | Phase | Maliyet (TL) |
|---|---|---|---|---|---|
| 1 | KVKK aydınlatma + parçalı açık rıza UI | Launch | Hukuk + Mobile | 0 | 15K–30K |
| 2 | Veri ihlali müdahale prosedürü (72h) | Launch | DPO/CTO | 0 | 5K–15K |
| 3 | Standart sözleşme (AWS/Azure yurt dışı) | Launch | Hukuk | 0 | 10K–25K |
| 4 | Sağlık verisi (alerji notu) açık rıza + güvenlik | Profil özelliği | Backend + Hukuk | 1 | 5K–10K |
| 5 | İlgili kişi hakları API (export/delete) | Launch | Backend | 0 | 20K–40K |
| 6 | İYS entegrasyonu (pazarlama ileti) | İlk push/SMS kampanyası | Backend | 1 | 1.5K/yıl + efor |
| 7 | E-Arşiv özel entegratör (Nilvera) | Hasılat 500K TL veya launch | Finans + Backend | 1 | 150–800/ay + 5K–50K kurulum |
| 8 | Çift fatura akışı + KDV %20 | İlk randevu ödemesi | Backend + SMMM | 1 | 25K–50K efor |
| 9 | E-ticaret tevkifat (%1) | Marketplace satış başı | Backend + SMMM | 1 | 10K–20K |
| 10 | Kuaför doğrulama (VKN + ESBİS belge + MYK) | Tedarikçi kayıt | Backend + Ops | 0 | 15K–30K |
| 11 | Sıhhi ruhsat upload + manuel onay | Tedarikçi kayıt | Ops | 0 | İş gücü |
| 12 | Mesafeli sözleşme + cayma istisnası metni | Launch | Hukuk | 0 | Item 1'e dahil |
| 13 | İptal/no-show kademeli iade penceresi | Launch | PM + Hukuk | 0 | Item 1'e dahil |
| 14 | VERBİS kaydı | >50 çalışan VEYA >100M TL | DPO | 2 | 8K–20K/yıl |
| 15 | Bağlayıcı Şirket Kuralları (BCR) | Yurt dışı grup kurulunca | Hukuk | 2 | 100K+ |

**Phase 0+1 toplam:** 110K–280K TL kurulum + 200–1.000 TL/ay tekrarlayan. `[unverified 2026-05-02]`

---

## Sınırlamalar

- Bu özet **tarama düzeyi**dir; kesin uygulama için KVKK uzmanı avukat + SMMM/YMM + DPO danışmanlığı şart.
- Her `[unverified 2026-05-02]` etiketi en az iki bağımsız profesyonel teyidi ister.
- GDPR/CCPA (yabancı kullanıcı) ve Ödeme Hizmetleri Kanunu 6493 (PSD2-benzeri) bu kapsam dışıdır.
- In-app ödeme PayTR/iyzico üstünden tek-müşteri akışında yapılırsa platform "yardımcı hizmet sağlayıcı" konumunda kalır.

---

## Kaynaklar

**KVKK (resmi):** [Push Bildirim](https://www.kvkk.gov.tr/Icerik/8578/mobil-uygulamalar-uzerinden-gonderilen-anlik-bildirimlere-iliskin-kamuoyu-duyurusu) · [VERBİS İstisna](https://www.kvkk.gov.tr/Icerik/8388/KAMUOYU-DUYURUSU) · [Yurt Dışı Aktarım](https://www.kvkk.gov.tr/Icerik/2053/Yurtdisina-Aktarim) · [Veri İhlali](https://www.kvkk.gov.tr/Icerik/5362/Veri-Ihlali-Bildirimi) · [Aydınlatma](https://www.kvkk.gov.tr/Icerik/2033/Aydinlatma-Yukumlulugu-) · [Özel Nitelikli](https://www.kvkk.gov.tr/Icerik/2051/Ozel-Nitelikli-Kisisel-Veriler) · [İlgili Kişi Başvuru](https://www.kvkk.gov.tr/Icerik/2046/Ilgili-Kisiler-Tarafindan-Yapilan-Basvurularin-Cevaplanmasi-Yukumlulugu)

**Mevzuat (resmi):** [Mesafeli Sözleşmeler Yön.](https://www.resmigazete.gov.tr/eskiler/2014/11/20141127-6.htm) · [6502 TKHK](https://www.mevzuat.gov.tr/mevzuat?MevzuatNo=6502&MevzuatTur=1&MevzuatTertip=5) · [5362 Esnaf](https://www.mevzuat.gov.tr/mevzuatmetin/1.5.5362.pdf) · [İşyeri Açma Yön.](https://www.mevzuat.gov.tr/mevzuat?MevzuatNo=20059207&MevzuatTur=21&MevzuatTertip=5)

**Bakanlık/GİB:** [Ticaret — Tüketici](https://tuketici.ticaret.gov.tr/yayinlar/tuketici-bilgi-rehberi/mesafeli-sozlesmeler-hakkinda-bilgilendirme) · [ESBİS](https://esbis.ticaret.gov.tr/) · [GİB Dijital](https://dijital.gib.gov.tr/) · [MYK Portal](https://portal.myk.gov.tr/) · [TYC Kuaför S4](https://www.tyc.gov.tr/yeterlilik/kuafor-seviye-4-myk-mesleki-yeterlilik-belgesi-TR00102854.html)

**Sektörel:** [Esenyel VERBİS 2026](https://www.esenyelpartners.com/tr/2026-verbis-kayit-istisnalari-100-milyon-tl-esigi-ve-guncel-kurallar/) · [Paraşüt E-Arşiv](https://www.parasut.com/blog/e-fatura-ve-e-arsiv-zorunlulugu) · [Mikro Eşik](https://www.mikro.com.tr/e-arsiv-fatura-siniri/) · [Vergi Mevzuat Basit Usul](https://www.vergimevzuat.com/2026/01/bilgi-notu-2026-yili-basit-usul-mukelleflerin-e-arsiv-fatura-duzenlemesi-hakkinda/) · [Alomaliye Komisyon KDV](https://www.alomaliye.com/2025/12/16/e-ticaretteki-komisyon-faturalarinda-kdv/) · [Hellac Tevkifat](https://hellac.com/blogdetay/tum-yonleriyle-e-ticarette-tevkifat-uygulamasi-ve-ornekler) · [Esas Denetim 7 gün](https://esasdenetim.com/2024-sirkuler/geriye-donuk-7-gunu-asan-surede-efatura-duzenlenmesi-halinde-usulsuzluk-cezasi-1771) · [Dopigo KDV 2026](https://www.dopigo.com/2026-kdv-oranlari-nedir/) · [Nilvera VKN API](https://developer.nilvera.com/api/genel-api/mukellef-islemleri/vkn-ile-sorgular) · [WionSoft Entegratör 2026](https://wionsoft.com/blog/e-fatura-e-arsiv-maliyetleri-2026-kontor-fiyatlari) · [Menajer.im GS Ruhsat](https://www.menajer.im/blog/guzellik-salonu-is-yeri-acma-ruhsati-sartlari-ve-ucreti-2026/) · [KobiTime MYK 2026](https://kobitime.com/myk-mesleki-yeterlilik-belgesi-zorunlulugu-2026/) · [Beyoğlu Sıhhi](https://beyoglu.bel.tr/hizmetlerimiz/basvuru-kaynaklari/isyeri-acma-ve-calisma-ruhsatlari/sihhi-muesseseler-ve-aranacak-sartlar/)

# RFC-015 — Salon tarafındaki değişiklikler müşteriye yansısın

- **Durum:** Accepted (founder kararları 2026-08-10)
- **Kaynak:** `docs/GAPS.md` G2, G3, G4, G6 — FAZ 1 domain denetiminde bulunan dört ayrı bulgu
- **İlgili:** RFC-013 (per-shop kurallar), RFC-014 (auto_confirm, çakışma uyarısı)

---

## 1. Bağlam — dört bulgu, tek kök sorun

FAZ 1 denetimi dört ayrı boşluk buldu. Ayrı ayrı bakınca küçük görünüyorlar; birlikte bakınca aynı kök soruna çıkıyorlar: **salon tarafında bir şey değiştiğinde müşteri bundan hiç haberdar olmuyor, ve müşterinin bunu bildirecek bir kanalı da yok.**

| # | Bulgu | Müşterinin yaşadığı |
|---|---|---|
| G2 | Çalışma saati daraltılınca mevcut onaylı randevular sessiz kalıyor | Randevu saatinde geliyor, salon kapalı |
| G3 | Çalışan pasife alınınca gelecek randevuları askıda kalıyor | Geliyor, seçtiği kişi artık orada çalışmıyor |
| G4 | Müşterinin "kuaför gelmedi" diyebileceği kanal yok | Mağdur, yapacak bir şey yok |
| G6 | Salon askıya alınınca müşteriye bildirim gitmiyor | (bkz. §2 D4 — bu bir kusur değil) |

Sistem şu an **yalnız tek yönde** çalışıyor: kuaför müşteriyi "gelmedi" diye işaretleyebiliyor (`MarkNoShowCommand`), tersi yok. Marketplace güveni tek taraflı kurulamaz.

---

## 2. Kararlar

### D1 — Çalışma saati değişikliği: uyar, izin ver, müşteriye haber ver

**Karar:** Kuaför çalışma saatini daraltırken etkilenen randevular varsa **uyarılır ama engellenmez**; kaydettiğinde etkilenen müşterilere bildirim gider.

**Why:** Engellemek en güvenli görünüyor ama kuaförü köşeye sıkıştırır — hastalık, cenaze, acil kapanış gibi durumlarda ayarı hiç değiştiremez hale gelir. Kontrol kuaförde kalmalı; sistemin işi kuaförün kör kalmasını önlemek ve müşteriyi mağdur etmemek.

**How to apply:**
1. Çalışma saati kaydedilirken, yeni aralığın dışında kalan gelecekteki `Pending`/`Confirmed` randevular sorgulanır.
2. Varsa kuaföre **kaydetmeden önce** liste gösterilir: kaç randevu, hangi müşteri, hangi saat. Onay diyaloğu ne olacağını yazar ("Bu değişiklik 3 randevunu etkiliyor"), "Emin misiniz?" demez.
3. Kuaför onaylarsa kayıt yapılır ve **etkilenen müşterilere bildirim gider**: salonun o saatte artık kapalı olduğunu ve salonla iletişime geçmesi gerektiğini söyleyen bir metin. Randevu **otomatik iptal EDİLMEZ** — iptal/erteleme kararı taraflara ait.
4. Bildirim idempotent olsun: aynı değişiklik için aynı müşteriye tekrar tekrar gitmesin.

---

### D2 — Çalışan pasifleştirme: kuaföre etkilenen randevu listesi

**Karar:** Çalışan pasife alınırken gelecekteki randevuları varsa kuaföre **liste gösterilir**; devretme/iptal kararı kuaförün.

**Why:** Otomatik devretme sürtünmesiz görünüyor ama riskli — müşteri belirli bir kişiyi seçmiş olabilir; habersiz değişim şikayet üretir ve "istediğim kişiye gidemedim" deneyimi marketplace güvenini doğrudan vurur. Sistem karar vermemeli, körlüğü gidermeli.

**How to apply:**
1. `Stylist.SetActive(false)` yolunda, o çalışanın gelecekteki `Pending`/`Confirmed` randevuları sorgulanır.
2. Varsa kuaföre liste gösterilir; pasifleştirme yine de yapılabilir.
3. **Mobil metni düzeltilecek:** `availability.tsx` şu an "Pasif çalışan müşterilere görünmez. Aktif randevuları etkilenmez." diyor. Bu doğru ama eksik — "etkilenmez" ifadesi kuaföre "ilgilenmem gereken bir şey yok" hissi veriyor. Metin, mevcut randevuların **hâlâ o çalışana bağlı olduğunu ve kuaförün bunları halletmesi gerektiğini** söylemeli.
4. Otomatik devretme **yapılmaz** (V1).

---

### D3 — Müşteri şikayet kanalı: minimum sürüm şimdi

**Karar:** Randevu detayında "Kuaför gelmedi / salon kapalıydı" bildirimi. Admin kuyruğuna düşer, **otomatik iade YOK**.

**Why:** Sahte şikayet riski gerçek — otomatik iade, kötüye kullanıma açık bir para musluğu olur. İnsan incelemesi V1'de yeterli (hacim düşük). Online ödeme launch'ta kapalı olduğu için acil para riski yok, ama **kapora açıldığında tüketici hukuku açısından bu kanal zorunlu hale gelir** (6502 sayılı kanun — hizmet ifa edilmezse iade hakkı). Şimdi minimum sürümü kurmak, o gün hazırlıksız yakalanmamayı sağlar.

**How to apply:**
1. Yalnız **geçmiş** randevularda görünür (gelecekteki bir randevu için "gelmedi" denemez).
2. Randevu başına **tek** bildirim; tekrar gönderim no-op.
3. Admin kuyruğuna düşer; admin görür, inceler, gerekirse elle iade başlatır (mevcut iade akışı kullanılır).
4. **Otomatik iade tetiklenmez.** Bu bilinçli — otomatikleştirme ancak gerçek kullanım verisi ve kötüye kullanım sinyali görüldükten sonra tartışılır.
5. Kuaföre bildirim gider mi? **Evet, ama nötr dille** — "müşteri bir sorun bildirdi, inceleniyor". Suçlayıcı metin kullanılmaz; henüz doğrulanmış bir şey yok.

---

### D4 — Askıya alınan salon: bildirim GEREKMİYOR (G6 kusur değil)

**Karar:** G6 için ek iş yapılmayacak. Gerekçe kayda geçiyor ki ileride "unutuldu" sanılmasın.

**Why:** Askıya alma **yalnız yeni randevu almayı ve arama görünürlüğünü** durduruyor; mevcut randevular aynen geçerli ve salon onları vermeye devam ediyor. Bu durumda müşteriye "salon askıya alındı" bildirimi göndermek **gereksiz panik** yaratır — müşterinin randevusuna bir şey olmamıştır. Ayrıca V1'de tüm planlar ücretsiz olduğu için askıya alma pratikte hiç tetiklenmiyor (RFC-014).

**Tekrar açılma koşulu:** Askıya alınmış bir salonun mevcut randevularını **veremediği** bir senaryo tanımlanırsa (örn. hesap tamamen kapatılırsa) bu karar yeniden değerlendirilir.

---

## 3. Kapsam dışı

- Otomatik randevu devretme (D2) — V1.5.
- Şikayet akışında SLA, otomatik eskalasyon, otomatik iade (D3) — gerçek kullanım verisi görülene kadar.
- Çalışma saati değişikliğinde otomatik iptal/erteleme önerisi (D1) — kuaför elle yapıyor.

---

## 4. Uygulama sırası

| # | İş | Not |
|---|---|---|
| 1 | Etkilenen randevu sorgusu (ortak) — çalışma saati + çalışan pasifleştirme aynı mantığı kullanacak | Kopyalama; tek yerde yaşasın |
| 2 | D1: çalışma saati kaydında uyarı + müşteri bildirimi | |
| 3 | D2: çalışan pasifleştirmede uyarı listesi + mobil metin düzeltmesi | |
| 4 | D3: müşteri şikayet bildirimi + admin kuyruğu | Migration gerekir |
| 5 | Mobil: üç akışın arayüzü | |

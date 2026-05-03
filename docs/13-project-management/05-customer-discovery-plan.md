# Müşteri Keşif Sprint Planı

> **2 hafta · 60 görüşme (30 kuaför + 30 müşteri) · Kod yazmadan önce**

Phase 0'ın sonunda, Phase 1 kod yazımına başlamadan önce yapılır. "Fikir yeni, doğrulama yok" başlangıç durumuna karşı **koruma katmanı**.

---

## 1. Hipotezler (test edilecek)

### 1.1 Müşteri tarafı
- **H1:** Türk son müşteriler kuaför randevu deneyiminden **memnuniyetsiz**. (Şu an Instagram DM, telefon, walk-in)
- **H2:** Müşteriler "yakında + açık + uygun fiyat" filtresine **mobil app aracılığıyla** ulaşmak ister.
- **H3:** Müşteriler **in-app ödeme + kademeli iade** güvencesini değer veriyor.
- **H4:** Müşteriler **walk-in modunu** (rezervasyonsuz "şu an boş kuaför" arama) en az haftada 1 kullanır.

### 1.2 Kuaför tarafı
- **H5:** Türk kuaförler mevcut SaaS araçlardan (Kolay Randevu, Salon Randevu) memnun değil **veya** bunları kullanmıyor.
- **H6:** Kuaförler **T+1 ödeme settlement'ını** rekabet avantajı olarak görür (T+haftalar değil).
- **H7:** Kuaförler **e-Arşiv 2027 zorunluluğunu** biliyor ve buna hazır değil.
- **H8:** Kuaförler **%10 komisyon** karşılığında app'e müşteri kazanmaya razı.
- **H9:** Kuaförler **walk-in toggle**'ı pratikte kullanır (boş zamanı doldurmak için).

---

## 2. Hedef Kitle

### Müşteri (30 kişi)
- **Yaş:** 18-55, dengeli
- **Cinsiyet:** %50 kadın, %50 erkek
- **Şehir:** İstanbul (15) + Ankara (8) + İzmir (4) + Bursa (3)
- **Kuaför ziyaret sıklığı:** Ayda en az 1
- **Mobil app kullanıcısı:** Aktif

### Kuaför / Stilist (30 kişi)
- **İşletme tipi:** %40 erkek berberi · %40 kadın kuaförü · %20 unisex
- **Büyüklük:** %50 1-2 kişilik · %30 3-5 kişilik · %20 6+ kişilik
- **Şehir:** İstanbul (15) + Ankara (8) + İzmir (4) + Bursa (3)
- **Şu an SaaS kullanım:** %30 var (Kolay Randevu / Salon Randevu / kâğıt) · %70 yok

---

## 3. Görüşme Rehberi

### 3.1 Müşteri (45 dakika · semi-structured)

**Açılış (5 dk):**
- Adı, mesleği, hangi şehir, ne sıklıkla kuaföre gider.

**Mevcut deneyim (15 dk — sorma odaklı):**
- "Son kuaför randevunu nasıl yaptın? Anlat baştan sona."
- "Sıkıntı yaşadığın bir an oldu mu?"
- "Telefon mu, Instagram mı, walk-in mi?"
- "Fiyatı nasıl öğrendin? Sürpriz oldu mu?"
- "Yorum/puan baktın mı? Nereden?"

**Mock-up gösterimi (15 dk — sezgi al):**
- Basit kâğıt mock'lar (3 ekran):
  1. Liste/harita keşif + filtre.
  2. Kuaför detay + slot seçimi.
  3. Ödeme + kademeli iade bilgisi.
- Tepkileri al — anlamlı mı, eksik bir şey var mı, kafası karışan?

**Walk-in özellik testi (5 dk):**
- "Diyelim ki 30 dakika boş vaktin var ve saç kestirmek istiyorsun. App'te 'şu an boş kuaför' filtresi olsa kullanır mısın?"
- "Hangi durumda kullanmazsın?"

**Kapanış (5 dk):**
- "Bu uygulama olsaydı kullanır mısın? Hangi sıklıkta?"
- "Hangi özellik olmazsa olmaz?"
- "Hangi özellik fazla / gereksiz?"

### 3.2 Kuaför (60 dakika · semi-structured)

**Açılış (5 dk):**
- İşletme tarihi, kaç kişi çalışıyor, ortalama günlük müşteri.

**Mevcut iş akışı (15 dk):**
- "Randevu nasıl alıyorsunuz? Telefon mu, Instagram mı?"
- "Sıkıntı yaşadığınız anlar?"
- "Müşteri gelmediğinde ne yapıyorsunuz?"
- "Tahsilat nakit/kart oranı? Kart sıkıntısı?"
- "Fatura kesimi nasıl? E-Arşiv biliyor musunuz?"

**SaaS kullanımı (10 dk):**
- "Şu an dijital randevu yazılımı kullanıyor musunuz?"
- "Hangi? Memnun musunuz?"
- "Aylık ne kadar ödüyorsunuz?"
- "Ne eksik?"

**Mock-up gösterimi (15 dk):**
- 3 ekran:
  1. Kuaför paneli ana ekran (bugünün randevuları, "boşum" toggle).
  2. Hizmet katalog + fiyat + süre yönetimi.
  3. Aylık gelir + e-Arşiv durumu.
- Tepkileri al.

**Komisyon + ödeme (10 dk):**
- "Müşteri uygulamadan ödeme yapıyor, biz **%10 komisyon** alıyoruz, parayı **1 gün sonra** alıyorsunuz. Nasıl?"
- "Komisyon yerine aylık abonelik ister misiniz?"
- "T+1 settlement gerçek mi, yoksa T+haftalar mı yaşıyorsunuz şu an?"

**Walk-in (5 dk):**
- "İşletmenizde boş slot olduğunda 'şu an boşum' butonuna basıp app üzerinden müşteri çekme fikri?"
- "Pratikte kullanır mısınız?"

---

## 4. Logistics

- **Süre:** 2 hafta (Phase 0 sonunda).
- **Yöntem:** %50 yüz yüze (pilot şehirlerde) + %50 video görüşme (Zoom/Meet).
- **Teşvik:** Müşteri için **₺200 kuaför hediye çeki** (görüşme sonrası), kuaför için **erken kayıt + ₺500 marketing kredisi**.
- **Toplam teşvik bütçesi:** ~₺25.000.
- **Görüşme kaydı:** Audio recording (rıza alınmış), transcript Whisper / OpenAI ile.
- **Veri analizi:** Affinity diagram + theme clustering (Notion).

---

## 5. Çıktı

`docs/01-research/customer-discovery-findings.md`:
- **Hipotez başına onay/red durumu**.
- **Sürpriz bulgular** (beklenmeyen ihtiyaçlar, kullanım pattern'leri).
- **Pivot önerileri** (varsa).
- **Pilot şehir önerisi** (kuaför havuzu yoğunluğuna göre).
- **MVP scope güncellemeleri** (eksik olan çıkarsa eklenir, gereksiz çıkarsa V1.5'a alınır).

---

## 6. Go / No-Go Kararı (G0 Gate)

Phase 0 → Phase 1 geçiş **G0** kapısı kriteri:

| Hipotez | Onay eşiği | Tetiklenme |
|---|---|---|
| H1-H4 müşteri | 30 kişiden 18+ "kullanırım" | Onayla / pivot |
| H5-H9 kuaför | 30 kişiden 18+ "katılırım" | Onayla / pivot |
| Komisyon kabul | 30 kuaförden 15+ %10'u kabul | Onayla / abonelik modeline geç |
| Walk-in talep | 30 müşteriden 12+ "kullanırım" | MVP'de tut / V1.5'a al |

Eşik altında kalırsa: **pivot kararı** master spec'e RFC ile yansır.

---

## 7. Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/01-research/competitor-analysis.md`](../01-research/competitor-analysis.md)
- [`docs/02-requirements/01-mvp-scope.md`](../02-requirements/01-mvp-scope.md)

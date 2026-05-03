# Risk Kaydı (Risk Register)

> Yaşayan doküman. Her sprint sonu gözden geçirilir, yeni risk eklenir, mevcut risk re-skore edilir.

**Skor:** Etki (1-5) × Olasılık (1-5) = Risk skoru (1-25). 15+ kırmızı, 9-14 sarı, 1-8 yeşil.

---

## Aktif Riskler

### R1 — Booksy/Fresha Türkiye'ye agresif giriş
- **Etki:** 5 — Pazaryeri kategori sahipliği yarışı kayıp.
- **Olasılık:** 3 — 18-24 ay penceresi, Booksy 2025 Series C $269M.
- **Skor:** 15 (kırmızı)
- **Mitigasyon:** (a) Hız — MVP 9-15 ay'da launch. (b) Compliance moat — KVKK + e-Arşiv 2027 hazır. (c) Lokal kuaför ilişkileri — kişisel onboarding sprint'leri.
- **İzleme:** Booksy resmi haberler aylık, App Store TR bölgesi 2 haftada bir.

### R2 — Marketplace cold-start (likidite yetersiz)
- **Etki:** 5 — Yeni kullanıcı boş listeye düşer, uninstall.
- **Olasılık:** 5 — Standart marketplace riski, supply önden inşa edilmeli.
- **Skor:** 25 (kırmızı, en yüksek)
- **Mitigasyon:** (a) Pilot şehir + supply odaklı onboarding. (b) Customer discovery sprint sonunda 30 kuaför ön niyet beyanı. (c) Concierge MVP fazı (manuel eşleştirme test). (d) Soft-launch ilk 3 ay agresif kuaför kazanım.
- **İzleme:** Pilot şehirde haftalık kuaför sayısı + müşteri arama ↔ randevu dönüşüm oranı.

### R3 — Solo founder burnout
- **Etki:** 5 — Proje durur.
- **Olasılık:** 3 — 9-15 ay solo yoğun yük; tipik startup riski.
- **Skor:** 15 (kırmızı)
- **Mitigasyon:** (a) Kapsam disiplini — Claude push-back'i + RFC süreci. (b) Phase gate — her gate dur/değerlendir. (c) Otomasyon — agent-driven implementation. (d) "V1.5 erteleme" hakkı — agresif scope cut.
- **İzleme:** Aylık founder self-check (yorgunluk, motivasyon, kişisel hayat dengesi).

### R4 — KVKK denetim cezası (eksik aydınlatma / yetkisiz işleme)
- **Etki:** 4 — Yüksek para cezası + itibar kaybı.
- **Olasılık:** 2 — Avukat onaylı şablonlarla riski azaltabiliriz.
- **Skor:** 8 (sarı)
- **Mitigasyon:** (a) Avukat onaylı aydınlatma + parçalı açık rıza. (b) Yıllık compliance review. (c) Veri ihlali SOP yazılı (72 saat bildirim). (d) VERBİS başvurusu eşik aşılınca derhal.
- **İzleme:** Aylık compliance review.

### R5 — PSP (PayTR) onboarding gecikmesi
- **Etki:** 3 — Launch ertelenir.
- **Olasılık:** 2 — Doğru belge ile kabul edilir genelde.
- **Skor:** 6 (yeşil)
- **Mitigasyon:** (a) Iyzico paralel onboarding. (b) `IPaymentProvider` abstraction PSP swap maliyetini sıfırlar.
- **İzleme:** PSP başvuru sonrası 48h SLA takibi.

### R6 — Iyzico T+3w fallback'a düşeriz, kuaförler çıkar
- **Etki:** 4 — Kuaför memnuniyetsizliği, churn.
- **Olasılık:** 2 — PayTR primary olduğu sürece az.
- **Skor:** 8 (sarı)
- **Mitigasyon:** (a) `IPaymentProvider` abstraction. (b) Kuaföre proaktif iletişim + alternatif PSP swap UI. (c) Iyzico ile holdback negotiation.

### R7 — E-Arşiv 500K TL eşik beklediğimizden hızlı geçilir
- **Etki:** 3 — Geç tetiklersek yasal ihlal.
- **Olasılık:** 4 — Pazaryeri olarak hızlı tetikleneceğiz.
- **Skor:** 12 (sarı)
- **Mitigasyon:** Day-1 Nilvera entegrasyonu — eşik beklemeden uyumlu.

### R8 — Walk-in simplified yetersiz, kullanıcı talep eder
- **Etki:** 2 — UX şikâyeti.
- **Olasılık:** 3 — Mümkün.
- **Skor:** 6 (yeşil)
- **Mitigasyon:** V1.5 SignalR full real-time queue scaffolding hazır.

### R9 — Mahalle/ilçe veri kalitesi
- **Etki:** 3 — Yanlış konumlar UX'i kırar.
- **Olasılık:** 4 — NVI verisi temiz değil; coğrafi gerçekle eşleşmeyebilir.
- **Skor:** 12 (sarı)
- **Mitigasyon:** (a) NVI açık veri + manuel düzeltme paneli. (b) Geocoding API (Google Maps) cross-check. (c) Kullanıcıdan "yanlış konum bildir" akışı.

### R10 — Kuaför sahte profil (fake KYC)
- **Etki:** 5 — Müşteri zarar görür, marka itibarı.
- **Olasılık:** 1 — Manuel KYC + belge OCR azaltır.
- **Skor:** 5 (yeşil)
- **Mitigasyon:** (a) Belge upload + admin manuel onay. (b) İlk randevu retroaktif kontrol. (c) ESBİS web doğrulama (manuel).

### R11 — Hetzner'in TR'ye ulaşamaması (BGP hijack, DDoS, vs.)
- **Etki:** 4 — Tüm servis düşer.
- **Olasılık:** 1 — Düşük ama gerçek risk.
- **Skor:** 4 (yeşil)
- **Mitigasyon:** (a) Cloudflare CDN + WAF arada. (b) DR plan: AWS Frankfurt'a 24h migration playbook (V1.5).

### R12 — App Store / Google Play reddi (KVKK manifest, kapsayıcılık)
- **Etki:** 3 — Launch gecikir 2-4 hafta.
- **Olasılık:** 3 — İlk submission'da olur sıkça.
- **Skor:** 9 (sarı)
- **Mitigasyon:** (a) Privacy manifest, App Tracking Transparency hazır. (b) Submit öncesi compliance checklist. (c) İlk submission staging'den.

### R13 — Founder kişisel hesabı + şirket hesabı karışırsa vergi sorunu
- **Etki:** 4 — Vergi cezası, gelir karışımı.
- **Olasılık:** 2 — SMMM disiplini ile az.
- **Skor:** 8 (sarı)
- **Mitigasyon:** SMMM aylık kontrol + ayrı banka hesabı.

### R14 — Sigortasız dönem (kuruluş ↔ poliçe arası boşluk)
- **Etki:** 3 — Davaya açıkken yangın etrafında.
- **Olasılık:** 2 — Kısa süreli.
- **Skor:** 6 (yeşil)
- **Mitigasyon:** Şirket kuruluşundan **2 hafta içinde** poliçe.

### R15 — Compliance maliyeti tahmini ₺110-280K aşılır
- **Etki:** 3 — Bütçe sıkıntısı.
- **Olasılık:** 3 — Yasal danışmanlık + pen-test maliyeti değişken.
- **Skor:** 9 (sarı)
- **Mitigasyon:** (a) 3 farklı tedarikçiden teklif. (b) KOSGEB destekleri.

---

## Risk Skoru Özet

| Skor Aralığı | Renk | Sayı |
|---|---|---|
| 15+ | 🔴 Kırmızı | 3 (R1, R2, R3) |
| 9-14 | 🟡 Sarı | 6 (R7, R9, R12, R13, R15, R4-yakın 8) |
| 1-8 | 🟢 Yeşil | 6 |

**En yüksek 3 risk: marketplace cold-start, Booksy giriş, founder burnout.** Hepsi insan/strateji riskleri, kod riskleri değil. MVP yazımı sırasında bu üçünü gözeten **operasyonel** kararlar (pilot şehir kuaför outreach, hız disiplini, scope cut hakkı) zorunlu.

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/13-project-management/01-roadmap.md`](01-roadmap.md)

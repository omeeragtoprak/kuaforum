# Şirket Kuruluş Yol Haritası

> Phase 0 → Phase 2 arasında tamamlanması gereken yasal kuruluş + lisanslama süreci. **Solo founder + tam ticari launch hedefi**.

---

## 0. Strateji

- **Şirket türü:** **Limited Şirket (Ltd. Şti.)**, tek ortaklı.
- **Gerekçe:**
  - Iyzico/PayTR Pazaryeri sub-merchant onboarding tüzel kişilik kabul (şahıs şirketinde sınırlı, riskli).
  - Marka tescili + sözleşmeler + sigorta tüzel kişilikle daha temiz.
  - KDV mükellefi olarak çift fatura modeli (komisyon faturası) gerekli.
  - 2 yıl içinde A.Ş.'ye dönüştürme opsiyonu açık (yatırım turu için).
- **Sermaye:** Asgari **50.000 TL** (2024 sonu eşiği — 2026 itibarıyla doğrula). [unverified — needs CPA confirm]
- **Faaliyet konusu:** "Bilgisayar programlama, danışmanlık ve ilgili faaliyetler" + "Aracı hizmet sağlayıcılığı" + "Reklam ve pazarlama hizmetleri" (NACE kodları SMMM ile netleştir).

---

## 1. Phase 0 (şu an) — Hazırlık (1-2 hafta)

| Eylem | Süre | Maliyet | Çıktı |
|---|---|---|---|
| **SMMM** (mali müşavir) seçimi | 3-5 gün | Ücretsiz görüşme | İlk konsultasyon |
| **Avukat** seçimi (KVKK + ticari hukuk uzmanlığı) | 3-5 gün | Ücretsiz görüşme | İlk konsultasyon |
| Şirket adı arama (TPMK + Ticaret Sicili) | 1 gün | 0 | "Kuaforum Bilişim Ltd. Şti." adının müsait olduğu doğrulandı (Phase 0 task) |
| NACE kodları + faaliyet konusu listesi | 1 gün | 0 | SMMM ile mutabık liste |
| Adres tespiti (sanal ofis veya gerçek) | 5-10 gün | ₺500-2.500/ay (sanal) | Kira sözleşmesi |
| Banka hesabı için ön araştırma | 1 gün | 0 | İş Bankası, Garanti, Akbank kıyas |
| **TOPLAM** | **2 hafta** | **~3-7K TL** | Kuruluş başvurusu hazır |

---

## 2. Phase 1 (Ay 2-3) — Resmi Kuruluş (2-4 hafta)

| Eylem | Süre | Maliyet | Çıktı |
|---|---|---|---|
| Esas sözleşme hazırlığı (avukat) | 3-5 gün | 5-10K TL | İmzalı esas sözleşme |
| MERSİS başvurusu (online) | 1 gün | 0 | Ticaret sicili numarası |
| Noter onayı + imza beyanı | 1 gün | ~3-5K TL | Noter onaylı belge |
| Vergi dairesi tescil | 3-5 gün | 0 | VKN + mükellefiyet |
| Bağ-Kur (kendi adına) | 1 gün | Aylık ~₺2.500 | Sigorta giriş |
| İşletme defteri açılış (e-defter) | 1 gün | SMMM hizmet | E-defter aktif |
| KOSGEB **veri tabanı kaydı** (destek başvuruları için) | 1 gün | 0 | Aktif kayıt |
| **Banka hesabı açılması** | 1 gün | 0 | İBAN |
| **Teknopark / Teknokent** ön değerlendirme (vergi avantajı için, opsiyonel) | 1 hafta | 0 başvuru | Karar |
| **TOPLAM** | **2-4 hafta** | **~25-40K TL (ilk 6 ay SMMM dahil)** | Kuruluş tamam |

> **Önemli:** Bağ-Kur + SMMM hizmet bedeli ilk aylar yüksek görünür ama esnaf vergisi optimizasyonu sayesinde ilk yıl vergi avantajı çıkar. SMMM ile tartışılır.

---

## 3. Phase 1 sonu / Phase 2 başı — Lisanslar & Kayıtlar (1-2 hafta)

### 3.1 KVKK VERBİS başvurusu
- **Eşik:** 50 çalışan VEYA 100M TL bilanço.
- **Phase 0/1'de muafsın.** Phase 2 launch'a yakın (eğer bilanço 100M'a yaklaşırsa veya çalışan eklersen) tetiklenir.
- **Eylem:** kvkk.gov.tr/verbis üzerinden başvuru.
- **Maliyet:** 0 (resmi); SMMM/avukat hizmet bedeli ile yapılırsa ~3-5K TL.

### 3.2 Aydınlatma + Açık Rıza Metni
- Avukat hazırlar.
- **Süre:** 1-2 hafta.
- **Maliyet:** 10-25K TL.
- **Çıktı:** Mobil app + web admin'in kayıt akışına yerleştirilen, parçalı toggle'lı KVKK metinleri.

### 3.3 ETBİS (Elektronik Ticaret Bilgi Sistemi) kayıt
- **Tetikleyici:** Mesafeli sözleşme yapan veya aracı hizmet sağlayan e-ticaret sitesi.
- **Eylem:** ticaret.gov.tr/etbis üzerinden başvuru.
- **Maliyet:** 0 resmi; SMMM 1-2K TL.

### 3.4 PSP başvuruları (Pazaryeri sub-merchant ana hesap)
- **PayTR Pazaryeri:** Şirket kuruluşu sonrası başvuru. **24-48h** onboarding.
- **Iyzico Pazaryeri:** Aynı şekilde.
- **Çıktı:** İki PSP'de aktif main account, sub-merchant onboarding API hazır.

### 3.5 E-Arşiv mükellefiyeti
- **Tetikleyici:** Yıllık ciro 500K TL eşiği (e-ticaret aracı).
- **Hızlı tetiklenir** — Phase 2 launch'tan kısa süre sonra.
- **Eylem:** Nilvera ile abonelik + GİB e-arşiv mükellefi başvurusu.
- **Maliyet:** 150-800 TL/ay + 5K TL kurulum.

### 3.6 Marka tescili (TPMK)
- **Eylem:** Sınıf 35 (reklam/pazarlama) + Sınıf 42 (yazılım) + Sınıf 44 (kuaför hizmeti aracılığı).
- **Maliyet:** ~3-7K TL (resmi harç + danışmanlık).
- **Süre:** Ön sorgu 1 gün, tam tescil 6-12 ay (paralel kullanılabilir).
- **Aksiyon:** Phase 0'da ön sorgu, Phase 1 sonu başvuru.

### 3.7 Sigorta
- **Siber sorumluluk** — KVKK ihlal kapsama.
- **Mesleki sorumluluk** — yazılım hatasından kaynaklı zarar.
- **Maliyet:** Yıllık 5-15K TL.
- **Tetikleyici:** Soft-launch öncesi.

### 3.8 Pen-test (3rd party)
- **Tedarikçi adayları:** Pentester (TR), Securrency, INVICTI gözlem altında.
- **Süre:** 1 hafta test + 1 hafta düzeltme.
- **Maliyet:** 25-50K TL.
- **Çıktı:** Rapor + clean re-test.

---

## 4. Tahmini Toplam Yasal/Kuruluş Yatırımı

| Kalem | Bedel (TL) |
|---|---|
| Şirket kuruluş + noter + sermaye blokesi | 25-40K |
| Avukat (KVKK + sözleşmeler) | 15-30K |
| SMMM (ilk 6 ay paket) | 6-15K |
| Pen-test | 25-50K |
| Sigorta (yıllık) | 5-15K |
| Marka tescili | 3-7K |
| ETBİS + VERBİS başvuru danışmanlık | 2-5K |
| Sanal ofis (yıllık) | 6-30K |
| Bağ-Kur (yıllık, founder) | 30K |
| **TOPLAM (12 aylık)** | **~₺120-220K TL** |

> **Not:** Yatırım almadığın için bu maliyetler bootstrapping. Soft-launch öncesi tüm kalemler tamamlanmış olmalı.

---

## 5. Kontrol Listesi

### Phase 0 sonunda olmalı:
- [ ] SMMM seçimi
- [ ] Avukat seçimi
- [ ] Şirket adı uygunluk doğrulama (TPMK ön + Ticaret Sicili sorgu)
- [ ] Marka tescili ön sorgu (TPMK)
- [ ] NACE kodu listesi netleştirme
- [ ] Adres seçimi

### Phase 1 sonunda olmalı:
- [ ] Şirket kuruldu (ticaret sicili numarası)
- [ ] Banka hesabı + IBAN
- [ ] VKN + vergi mükellefiyeti aktif
- [ ] E-defter aktif
- [ ] Bağ-Kur giriş
- [ ] KOSGEB veri tabanı kaydı

### Phase 2 ortasında olmalı:
- [ ] KVKK aydınlatma + açık rıza avukat onaylı, app'a entegre
- [ ] ETBİS kaydı
- [ ] Marka tescili başvurusu
- [ ] PayTR + Iyzico Pazaryeri ana hesap aktif

### Soft-launch öncesi olmalı:
- [ ] Pen-test temiz
- [ ] Sigorta poliçesi aktif
- [ ] E-Arşiv mükellefiyeti + Nilvera prod entegrasyonu
- [ ] Kullanım sözleşmesi + gizlilik politikası prod'da

---

## 6. Riskler

| Risk | Mitigasyon |
|---|---|
| Şirket kuruluş gecikirse Phase 1 launch sarkar | Phase 0'da SMMM/avukat seçimi + ön kuruluş başvurusu paralel |
| KVKK aydınlatma metinleri eksik kalırsa launch ertelenir | Avukat ile **sözleşmeli teslim tarihi** |
| PSP onboarding KYC reddi | İki PSP'ye paralel başvuru |
| Marka tescili reddi | TPMK ön sorgu Phase 0'da yapılır |
| Bağ-Kur yükü cash-flow sıkıştırır | İlk 6 ay genç girişimci muafiyet (KOSGEB başvurusu ile) — SMMM'ye sor |

---

## 7. Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/13-project-management/03-budget-tiers.md`](03-budget-tiers.md)
- [`docs/01-research/turkey-compliance.md`](../01-research/turkey-compliance.md)
- [`docs/01-research/payment-infrastructure.md`](../01-research/payment-infrastructure.md)

# Stakeholder Listesi

> Kuaforum'un başarı/başarısızlığını belirleyen taraflar. Her birinin kararı, ihtiyacı, etki sınırı belgelenmiş.

---

## 1. Temel Stakeholder'lar (İç)

### 1.1 Founder (Solo)
- **Rol:** PM, dev, ops, müşteri keşif, GTM, finans.
- **Karar yetkisi:** Tüm stratejik + büyük teknik kararlar.
- **Sorumluluk:** Vizyon, kapsam, finans, yasal süreçler.
- **Risk:** Burnout (R3).

### 1.2 Claude Code (AI ortak)
- **Rol:** Mühendislik partner — research, design, implementation, review.
- **Karar yetkisi:** Implementation detayı, küçük scope. Mimari + compliance founder'a bağlı.
- **Sorumluluk:** Dürüst push-back, kalite, dokümantasyon disiplini.

---

## 2. Tedarikçi / Hizmet Sağlayıcı (Dış)

### 2.1 SMMM (Mali Müşavir)
- **Sorumluluk:** Aylık muhasebe, e-defter, vergi beyannameleri, KOSGEB destek başvuruları.
- **İletişim:** Aylık review + ad-hoc soru.

### 2.2 Avukat
- **Sorumluluk:** Şirket kuruluş esas sözleşmesi, KVKK aydınlatma + açık rıza, kullanım sözleşmesi, gizlilik politikası, ihtilaf danışmanlığı.
- **İletişim:** Phase 1 başında haftalık, sonrası retainer model.

### 2.3 PSP — PayTR
- **İlişki:** Pazaryeri sub-merchant ana hesap.
- **SLA:** Webhook 99.5%+ delivery, KYC 24-72h.

### 2.4 PSP — Iyzico (fallback)
- **İlişki:** Backup, abstraction sayesinde swap edilebilir.

### 2.5 Nilvera (E-Arşiv)
- **İlişki:** REST API entegrasyonu.
- **SLA:** Fatura kesim + GİB iletim 7 gün içinde (yasal).

### 2.6 NetGSM (SMS)
- **İlişki:** Pay-as-you-go SMS gönderim.

### 2.7 Hetzner
- **İlişki:** Cloud altyapı.
- **Risk:** Tek vendor lock-in (DR plan: AWS Frankfurt, V1.5).

### 2.8 Cloudflare
- **İlişki:** DNS + CDN + WAF + R2 storage.

### 2.9 Apple + Google
- **İlişki:** App Store + Play Store yayıncısı.
- **Etki:** Reddi launch'ı 2-4 hafta erteler (R12).

### 2.10 Sentry, Better Stack, Postmark
- **İlişki:** Observability + transactional mail.

---

## 3. Düzenleyici Otoriteler

### 3.1 KVKK Kurumu
- **İlişki:** Veri ihlali bildirimi (72 saat), ilgili kişi şikâyetleri, denetim.
- **Etki:** Ceza + itibar (R4).

### 3.2 GİB (Gelir İdaresi Başkanlığı)
- **İlişki:** E-Arşiv mükellefiyeti, vergi beyannameleri.

### 3.3 TCMB
- **İlişki:** 50M TL/12 ay GMV eşik bildirim.

### 3.4 Ticaret Bakanlığı
- **İlişki:** ETBİS kayıt, e-ticaret aracılık.

### 3.5 TPMK
- **İlişki:** Marka tescili.

---

## 4. Son Kullanıcılar (Pazaryeri Tarafı)

### 4.1 Müşteri (Customer)
- **Profil:** Detay → [`docs/02-requirements/02-personas.md`](../02-requirements/02-personas.md).
- **Beklenti:** Hızlı keşif + güvenli ödeme + iade güvencesi.
- **Etki:** App Store rating, NPS, retention.

### 4.2 Kuaför / İşletme Sahibi (BarberOwner)
- **Beklenti:** T+1 ödeme, kolay panel, e-Arşiv otomasyonu, müşteri akışı.
- **Etki:** Supply tarafı likiditesi.

### 4.3 Stilist (Stylist)
- **Beklenti:** Kendi takvimi, kendi randevuları, ipucu (V1.5).
- **Etki:** Birden fazla stilistli salonda kullanılabilirlik.

---

## 5. Pazarın Etkileyicileri

### 5.1 Booksy (potansiyel rakip)
- **Risk:** TR'ye 18-24 ay'da girebilir (R1).
- **İzleme:** Aylık haber takibi.

### 5.2 SalonAppy / Kolay Randevu (mevcut yerli rakip)
- **Tehdit:** B2B SaaS ile iyi konumlanmış. Pazaryeri tarafına dönerlerse rekabet artar.
- **Mitigasyon:** Compliance + UX moat.

### 5.3 Influencer / Salon Zincirleri
- **Etki:** Pilot şehir launch'ında kritik supply kaynağı.
- **Aksiyon:** Phase 1 sonu outreach planı.

---

## 6. RACI Matrisi (Sade)

| Karar | Founder | Claude | Avukat | SMMM |
|---|---|---|---|---|
| Vizyon, MVP scope | A | C | I | I |
| Mimari karar | A | R | I | I |
| Implementation detay | I | A | — | — |
| KVKK metinleri | A | C | R | I |
| Vergi / mali kararlar | A | I | I | R |
| Ürün karar (UI/UX) | A | R | — | — |
| Compliance (e-Arşiv vs.) | A | C | C | R |

R = Responsible · A = Accountable · C = Consulted · I = Informed.

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)

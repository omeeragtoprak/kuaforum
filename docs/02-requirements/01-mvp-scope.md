# MVP Kapsamı (Phase 1 + Phase 2)

> **MVP tanımı (bu projede):** "İlk pilot şehirde 30+ kuaför + 1.000+ müşteri ile organik döngünün test edilebileceği, regülasyona uyumlu, gerçek ödeme alabilen, gerçek randevu kabul eden uygulama."

MVP **Phase 1 (M1-M5 backbone)** + **Phase 2 (M6-M11 marketplace flow)** birleşimidir. Phase 3 modülleri (admin paneli derinliği, analytics, promo, loyalty, i18n) MVP'den sonra eklenir.

---

## MoSCoW Listesi

### MUST-have — MVP launch için zorunlu

| # | Özellik | Modül | Kapsam Notu |
|---|---|---|---|
| 1 | Telefon + e-posta kayıt, OTP | M1 Auth | NetGSM SMS OTP, e-posta doğrulama |
| 2 | Apple/Google OAuth | M1 Auth | iOS Apple zorunlu (review reddi) |
| 3 | JWT (15dk) + Refresh (30g) + device-bound | M1 Auth | Refresh rotation |
| 4 | RBAC: Customer / BarberOwner / Stylist / Admin | M1+M2 | Permission system |
| 5 | Kullanıcı profili (ad, telefon, foto, tercihler) | M2 User | KVKK aydınlatma + parçalı rıza |
| 6 | Kuaför işletme profili (BarberShop) | M2+M4 Barber | İşletme yöneticisi/sahibi |
| 7 | Stilist profili (BarberShop'a bağlı) | M4 | Birden fazla stilist destekler |
| 8 | Hizmet katalog (kesim/sakal/boya/...) + süre + fiyat + cinsiyet | M4 | Stilist bazlı override desteği |
| 9 | TR il/ilçe/mahalle bootstrap (NVI) + PostGIS | M3 Geo | Manuel düzeltme paneli |
| 10 | Kuaför doğrulama: belge upload + admin manuel onay | M4 | KYC akışı |
| 11 | **Müsaitlik motoru** (slot rezervasyonu, izin, tatil, blok) | **M5 Availability** | En kritik modül |
| 12 | Çalışma saatleri (haftalık + istisna) | M5 | Günlük override |
| 13 | Randevu yaşam döngüsü (state machine) | M6 Booking | pending/confirmed/started/completed/cancelled/no-show |
| 14 | Çoklu hizmet randevu (kesim + sakal birlikte) | M6 | Toplam süre + indirim opsiyonu |
| 15 | Randevu iptal (kademeli iade 24/12/0) | M6+M7 | Yasal uyumlu |
| 16 | Ödeme: PayTR Pazaryeri T+1 | M7 Payment | Sub-merchant onboarding |
| 17 | Komisyon hesabı + split | M7 | %10 default, kuaför tier ile değişebilir |
| 18 | E-Arşiv çift fatura: kuaför→müşteri + platform→kuaför | M7 | Nilvera entegrasyonu |
| 19 | Kademeli iade akışı | M7 | Otomatik PSP refund |
| 20 | Yorum & puan (5 yıldız + metin) | M8 Review | Tamamlanmış randevu sonrası |
| 21 | Yorum moderasyon (otomatik küfür filtresi + manuel review) | M8 | Toxic-content lib |
| 22 | Kuaförün cevap hakkı (her yoruma 1 cevap) | M8 | — |
| 23 | Arama & filtreleme | M9 Search | Cinsiyet, mahalle, fiyat, açık, walk-in, rating, mesafe, hizmet türü |
| 24 | Türkçe NLP arama (Meilisearch + tokenizer) | M9 | "saç kesimi" ↔ "kesim" eşleştir |
| 25 | Harita üzerinde kuaför keşfi | M9+M10 | Liste ↔ harita toggle |
| 26 | Yol tarifi handoff | M10 Maps | Apple Maps / Google Maps deep link |
| 27 | Push bildirim (randevu hatırlatma, kabul/red) | M11 Notification | FCM |
| 28 | SMS bildirim (kritik akışlar: OTP, randevu kabul) | M11 | NetGSM |
| 29 | E-posta bildirim (fatura, makbuz) | M11 | Postmark veya benzeri |
| 30 | **Walk-in simplified** (toggle + filtre) | M9+M4 | Real-time queue YOK, statik flag |
| 31 | KVKK self-servis veri silme | M2 | 30 gün SLA |
| 32 | KVKK aydınlatma + parçalı açık rıza akışı | M1 | Avukat onaylı şablon |
| 33 | Audit log (kritik aksiyonlar) | M1+M11 | Append-only, retention 5 yıl |
| 34 | Admin paneli temel: kullanıcı arama, KYC kuyruğu, randevu görme, refund | M12 minimal | Web (Next.js) |

### SHOULD-have — Phase 3 / V1.5 (launch sonrası ilk 3 ay)

| # | Özellik | Modül |
|---|---|---|
| 35 | Walk-in **full real-time queue** (SignalR + presence) | M9 |
| 36 | Kuaför analitik dashboard (gelir, müşteri, popüler hizmet) | M13 |
| 37 | Promo kodlar + indirim kuralları | M14 |
| 38 | Referans (referral) sistemi | M14 |
| 39 | Admin paneli ileri (gelişmiş ihtilaf, KYC OCR, finansal rapor) | M12 |
| 40 | Mobil ödeme yöntemi (Apple Pay / Google Pay token) | M7 |
| 41 | Tip / bahşiş entegrasyonu | M7 |

### COULD-have — V2 (6+ ay sonrası)

| # | Özellik | Modül |
|---|---|---|
| 42 | Sadakat / puan sistemi | M15 |
| 43 | Çoklu dil EN/AR/RU | M16 |
| 44 | AI stil öneri (foto upload + öneri) | (yeni) |
| 45 | B2B SaaS kuaför yönetim aracı (bağımsız) | (yeni) |
| 46 | Personel maaş/prim hesaplama | (yeni) |

### WON'T-have — Asla MVP'de

- POS / yazarkasa entegrasyonu
- WhatsApp Business API hatırlatma (SMS yeter)
- Stok yönetimi (boya, ekipman)
- Müşteri-kuaför doğrudan chat (V1.5 değerlendirilir)
- Marketplace içinde reklam / "öne çıkar" satışı
- Erişim kontrolü gerektiren sertifika doğrulama otomasyonu (manuel)

---

## MVP Cinsiyet/Hedef Kitle Kapsamı

- **Tüm cinsiyetler** desteklenir; salon kendi hedef kitlesini deklare eder (`salonGenderType`: female-only / male-only / unisex).
- Kullanıcı keşif filtrelerinde cinsiyetine göre uygun salon listesi öne çıkar.
- 18+ kullanıcı zorunlu; 18 altı kuaför randevusu için ebeveyn onayı (KVKK + KYC) — V1.5'a kalır.

---

## Coğrafi Kapsam

- **Yazılımsal: 81 il + 970+ ilçe + tüm mahalleler** açık. Kullanıcı serbest seçim veya konum izniyle otomatik tespit.
- **Onboarding fokus:** 2-3 pilot şehir (kararı Phase 1 sonu). Phase 2 sonunda **soft-launch**.
- Mahalle/ilçe verisi: **NVI (Nüfus İdaresi)** açık veri seti + manuel düzeltme.

---

## Performans Hedefleri (MVP)

| Boyut | Hedef |
|---|---|
| API P95 latency (CRUD) | < 300ms |
| API P95 latency (search) | < 500ms |
| Mobile cold start | < 3s |
| Mobile randevu akışı (keşif → kabul) | < 90s |
| Push delivery | < 5s |
| Backend uptime | %99.5 |

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/04-modules/00-module-list.md`](../04-modules/00-module-list.md)
- [`docs/13-project-management/01-roadmap.md`](../13-project-management/01-roadmap.md)

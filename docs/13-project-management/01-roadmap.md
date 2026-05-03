# Yol Haritası (Roadmap)

> 9-15 ay sonunda **production-grade MVP + 3 büyük şehirde aktif kullanıcı**.

---

## Faz Özet Çizelgesi

```
Ay 1   ▰▰              Phase 0: Foundation
Ay 2-3 ▱▱▰▰▰          Phase 1: Backbone (M1 Auth → M5 Availability)
Ay 4-7 ▰▰▰▰▰▰▰▰      Phase 2: Marketplace flow (M6 → M11)
Ay 8   ▰              Hardening + Pen-test + Soft-launch hazırlık
Ay 9-11 ▰▰▰          Soft-launch (1 pilot şehir)
Ay 12-15 ▰▰▰▰        Açık launch (3 büyük şehir + ulusal organic GTM)
Ay 16+              Phase 3: V1.5 + V2 (admin derinleşmesi, analytics, loyalty, i18n)
```

---

## Phase 0 — Foundation (Ay 1, 2-4 hafta)

**Durum:** Şu an buradayız.

**Çıktı:** Phase 0 master spec + tüm destekleyici dokümanlar + Claude Code setup + müşteri keşif sprint planı + şirket kuruluş roadmap'i.

**Çıkış Kriterleri:**
- [x] `docs/01-research/*` üç araştırma raporu
- [x] `docs/PHASE-0-MASTER-SPEC.md`
- [ ] Tüm § 9 deliverable doc'ları yazılmış
- [ ] Customer discovery rehberi + 60 kişi listesi (30 kuaför + 30 müşteri)
- [ ] Şirket kuruluş checklist + avukat ön-randevusu
- [ ] CLAUDE.md + .claude/agents kurulu, MCP context7 doğrulanmış
- [ ] Git init + ilk commit
- [ ] Founder yazılı onayı

---

## Phase 1 — Backbone (Ay 2-3, 8-12 hafta)

**Çıktı:** M1 Auth, M2 User/Role, M3 Geo, M4 Barber profile + verification, M5 Availability engine. Backend monolith yapısı + CI/CD + test framework.

**Modül Sırası:**

| # | Modül | Süre | Çıktı |
|---|---|---|---|
| 1 | M1 Auth | 1.5 hafta | JWT + refresh + OAuth + OTP + RBAC + KVKK aydınlatma |
| 2 | M2 User & Role | 1 hafta | Customer/BarberOwner/Stylist/Admin profilleri |
| 3 | M3 Geography | 1.5 hafta | TR il/ilçe/mahalle bootstrap + PostGIS + manuel düzeltme |
| 4 | M4 Barber + Verification | 2 hafta | Kayıt akışı + belge upload + admin manuel onay |
| 5 | M5 Availability Engine | 3 hafta | Slot yönetimi + izin + tatil + concurrency lock + service durations |

**Paralel:** Mobil app iskeleti (Expo init + navigation + auth ekranları) + admin paneli iskeleti (Next.js).

**Phase 1 Çıkış Kriterleri:**
- [ ] Her modülün spec + plan + kod + testleri tamam
- [ ] CI/CD pipeline çalışıyor (backend + mobile + web admin)
- [ ] Hetzner staging environment ayakta
- [ ] Auth + onboarding + barber profil kayıt akışı end-to-end mobil ve web admin
- [ ] Müşteri keşif sprint **sonuçları** raporlandı (go/no-go kararı)
- [ ] **Pilot şehir kararı verildi** (1 başlangıç + 1-2 gelecek)

---

## Phase 2 — Marketplace Flow (Ay 4-7, 12-16 hafta)

**Çıktı:** Müşteri tarafından gerçek randevu + ödeme + iade + yorum + arama akışı. Compliance day-1 production-grade.

**Modül Sırası:**

| # | Modül | Süre | Çıktı |
|---|---|---|---|
| 6 | M6 Booking Lifecycle | 2.5 hafta | State machine + multi-service + iptal |
| 7 | M7 Payment + e-Arşiv | 4 hafta | PayTR Pazaryeri + Iyzico abstraction + Nilvera çift fatura + kademeli iade |
| 8 | M8 Review + Moderation | 2 hafta | Yorum/puan + moderasyon + reply |
| 9 | M9 Search & Discovery | 3 hafta | Meilisearch + filtreler + harita view + walk-in simplified toggle |
| 10 | M10 Maps & Navigation | 1 hafta | Geocoding + native handoff |
| 11 | M11 Notification | 2 hafta | FCM push + NetGSM SMS + Postmark mail + tercih merkezi |

**Paralel:** Admin paneli minimal (KYC kuyruğu, refund onayı, kullanıcı arama). Mobil müşteri uygulaması full akış. Kuaför paneli mobil-responsive web (aynı kod).

**Phase 2 Çıkış Kriterleri:**
- [ ] End-to-end smoke test: müşteri kayıt → arama → randevu → ödeme → tamamlama → yorum
- [ ] PayTR Pazaryeri prod hesabı + KYC süreçleri test edildi
- [ ] Nilvera prod entegrasyonu + çift fatura test edildi
- [ ] **Pen-test** raporu temiz (3rd party firma)
- [ ] KVKK aydınlatma + parçalı rıza akışı avukat onaylı
- [ ] Hetzner prod environment deploy edildi
- [ ] Sentry + Seq + Better Stack monitoring çalışıyor
- [ ] **MVP Go-Live hazır** ✅

---

## Phase 2 sonu — Hardening (Ay 8, 4 hafta)

- Performance load test (k6, 200 concurrent users)
- Security audit + pen-test düzeltmeleri
- Disaster recovery test (DB restore)
- App Store + Google Play submission, rejection döngüsü
- Yasal hazırlık: kullanım sözleşmesi, gizlilik, KVKK metinleri prod'a
- Sigorta poliçesi (siber + meslek sorumluluk)

---

## Soft-launch (Ay 9-11, 12 hafta)

- **1 pilot şehir** (örn. Bursa veya Eskişehir — Phase 1 sonu kararı). 30+ kuaför + 1.000+ müşteri hedefi.
- Davetiye/kapalı kayıt; organic + minimal paid (₺5K/ay marketing bütçe).
- Haftalık metric raporu: aktivasyon, tamamlanma oranı, NPS, kuaför retention, no-show, refund.
- Bug fix + UX iterasyonu agresif.

**Soft-launch Çıkış Kriteri:**
- 30+ aktif kuaför, %85+ tamamlanma oranı, NPS > 35, < %10 no-show.

---

## Açık Launch (Ay 12-15)

- 3 büyük şehre yayılma (İstanbul + Ankara + İzmir).
- App Store ve Google Play public listing.
- Paid kanal devreye (Google Ads + Instagram) ₺25-50K/ay marketing.
- PR / influencer (1-2 saç & güzellik kategorisi).
- 300+ kuaför, 30K+ aylık aktif kullanıcı, 25K+ aylık randevu hedefi.

---

## Phase 3 — V1.5 (Ay 16+)

- M12 Admin paneli ileri (gelişmiş ihtilaf, KYC OCR otomatik, finansal rapor)
- M13 Kuaför analitik dashboard
- M14 Promo + referral
- Walk-in **full real-time queue** (SignalR + presence)
- Apple Pay / Google Pay
- Tip / bahşiş

## Phase 3 — V2 (Ay 24+)

- M15 Sadakat / puan
- M16 Çoklu dil (EN/AR/RU)
- AI stil öneri
- B2B SaaS modülü (kuaförler için bağımsız işletme yönetim aracı)
- Microservices'e ayrıştırma (gerekirse) — Booking, Payment, Search ayrı servis

---

## Karar Kapıları (Gates)

Her gate'de **dur, ölç, devam et / iptal et / pivot**:

| Gate | Tetikleyici | Karar |
|---|---|---|
| **G0** | Phase 0 sonu | Müşteri keşif olumlu mu? Devam / pivot |
| **G1** | Phase 1 sonu | Pilot şehir + 30 kuaför ön niyet sağlandı mı? Devam / kapsam azalt |
| **G2** | Phase 2 sonu | Pen-test + compliance hazır mı? Launch / 4 hafta hardening |
| **G3** | Soft-launch ay 11 sonu | Metric hedefleri tutuldu mu? Açık launch / iyileştirme |
| **G4** | Açık launch ay 15 | Booksy benzeri rakip TR'de mi? Defansif strateji / hız |

---

## Değişiklik Yönetimi

Roadmap RFC ile güncellenir (`docs/rfc/`). Founder kararı + Claude analizinin imzası gerekir.

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/02-requirements/01-mvp-scope.md`](../02-requirements/01-mvp-scope.md)
- [`docs/13-project-management/03-budget-tiers.md`](03-budget-tiers.md)

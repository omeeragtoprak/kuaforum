# Bütçe Tier Modeli

> 3 katmanlı dinamik bütçe — proje fazına göre otomatik genişler. Sabit gider hesabı her ayın 1'inde gözden geçirilir.

---

## Tier 1 — Bootstrap (Phase 0 + Phase 1, ~Ay 1-3)

**Hedef:** Geliştirme ortamı, hiçbir ödeme akışı yok, mock data.

| Kalem | Tedarikçi | Aylık ($) | Aylık (TL) |
|---|---|---|---|
| Hetzner Cloud CCX13 (4 vCPU, 16GB) | Hetzner | $25 | ~870 |
| Cloudflare Free | Cloudflare | $0 | 0 |
| GitHub Pro (private repos + Actions 3K min) | GitHub | $4 | 140 |
| Sentry Developer (free 5K err/ay) | Sentry | $0 | 0 |
| EAS Build (free tier) | Expo | $0 | 0 |
| Domain (.com + .com.tr) | Namecheap | $1 | 35 |
| NetGSM pay-as-you-go (test SMS) | NetGSM | $5 | 175 |
| **TOPLAM** | | **~$35-40** | **~₺1.220-1.400** |

**Bir defalık:** 0 TL (henüz şirket kurulmadı, yasal gider yok).

---

## Tier 2 — Soft-launch (Phase 2 sonu + Pilot şehir, ~Ay 8-11)

**Hedef:** Production trafiği 1 pilot şehirde, gerçek ödeme + KVKK + e-Arşiv.

| Kalem | Tedarikçi | Aylık ($) | Aylık (TL) |
|---|---|---|---|
| Hetzner Cloud CCX23 (8 vCPU, 32GB) | Hetzner | $58 | 2.030 |
| Hetzner Managed PostgreSQL (orta plan) | Hetzner | $25 | 870 |
| Hetzner Cloud Volume + snapshot | Hetzner | $10 | 350 |
| Cloudflare Pro | Cloudflare | $20 | 700 |
| Cloudflare R2 (storage + egress) | Cloudflare | $5 | 175 |
| Sentry Team | Sentry | $26 | 910 |
| Seq self-host (CCX13'den ayrı) | Hetzner | $25 | 870 |
| Better Stack uptime | Better Stack | $24 | 840 |
| Postmark (transactional mail) | Postmark | $15 | 525 |
| NetGSM SMS (5K SMS/ay) | NetGSM | — | ~500 |
| Nilvera e-Arşiv | Nilvera | — | ~400 |
| Google Maps API | Google | $30 | 1.050 |
| FCM (free tier yeter) | Firebase | $0 | 0 |
| Apple Developer Program | Apple | $9 | 315 (yıllık $99) |
| Google Play Console | Google | $0 | 0 (one-time $25) |
| **TOPLAM** | | **~$250** | **~₺9.000-12.000** |

**Bir defalık (Phase 2 sonunda):**
| Kalem | Bedel |
|---|---|
| Şirket kuruluş + noter + ticaret sicili + KOSGEB başvurusu | ~30-45K TL |
| Avukat: KVKK aydınlatma + kullanım şartları + gizlilik şablonu | ~15-30K TL |
| Pen-test (3rd party firma, 1 hafta) | ~25-50K TL |
| Sigorta (siber + meslek, 1 yıllık) | ~5-15K TL |
| Marka tescili (TPMK) | ~3-7K TL |
| **TOPLAM bir defalık** | **~₺78-147K TL** |

---

## Tier 3 — National Launch (Açık launch, ~Ay 12+)

**Hedef:** 3+ büyük şehir, 300+ kuaför, 30K+ aylık kullanıcı.

| Kalem | Tedarikçi | Aylık ($) | Aylık (TL) |
|---|---|---|---|
| Hetzner CCX33 (16 vCPU, 64GB) + replica | Hetzner | $200 | 7.000 |
| Managed PostgreSQL büyük plan + read replica | Hetzner | $80 | 2.800 |
| Cloudflare Business | Cloudflare | $200 | 7.000 |
| Cloudflare R2 (büyüyen storage) | Cloudflare | $30 | 1.050 |
| Sentry Business | Sentry | $80 | 2.800 |
| Better Stack | Better Stack | $50 | 1.750 |
| Postmark + ek e-mail | Postmark | $50 | 1.750 |
| NetGSM SMS (50K SMS/ay) | NetGSM | — | ~3.500 |
| Nilvera e-Arşiv (yüksek hacim) | Nilvera | — | ~1.500 |
| Google Maps API | Google | $200 | 7.000 |
| Marketing (Google Ads + Instagram) | — | $700-1.500 | 25-50K |
| Customer support tool (Crisp veya Zendesk Lite) | — | $50 | 1.750 |
| Doppler (secret manager) | Doppler | $30 | 1.050 |
| **TOPLAM (marketing dahil)** | | **~$1.700-2.500** | **~₺60-90K/ay** |

---

## Maliyet Optimizasyonu Disiplini

- Aylık 1'inde tüm faturaları **Notion/Airtable** veya basit Excel'de takip — %20 sapma alarm.
- Trafiği olmayan servisleri kapat (sandbox, dev environment'lar gece kapansın).
- Hetzner snapshot'lar 30 günden eski silinir.
- Sentry retention 30 gün; ek error budget aşılırsa rate limit aktive et.

---

## Compliance Yatırımı Özeti

Soft-launch öncesi **bir defalık ~₺110-280K TL** kalemleri:
- Şirket kuruluş + avukat
- Pen-test
- Sigorta
- Marka tescili

Tekrarlayan compliance kalemleri (Soft-launch+):
- Avukat retainer (yıllık 15-30K TL)
- Sigorta yenileme (yıllık 5-15K TL)
- Yıllık VERBİS tahkikatı (Phase 3'te)
- KVKK denetim sertifikası (opsiyonel, müşteri güveni için Tier 3)

---

## Tetikleyiciler — Tier Geçiş

| Tetikleyici | Geçiş |
|---|---|
| Phase 1 başladı | Tier 1 |
| Soft-launch hazırlık (Phase 2 sonu, Ay 8) | Tier 1 → Tier 2 |
| Aylık 100+ aktif kuaför veya 5K+ aylık aktif kullanıcı | Tier 2 → Tier 3 |

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/13-project-management/01-roadmap.md`](01-roadmap.md)
- [`docs/13-project-management/04-company-formation-roadmap.md`](04-company-formation-roadmap.md)

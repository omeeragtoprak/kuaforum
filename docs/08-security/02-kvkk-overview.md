# KVKK Uyum Çerçevesi

> 6698 sayılı KVKK kapsamında Kuaforum'un day-1 uyum duruşu. Detaylı yasal danışmanlık avukatla.

---

## 1. Veri Sorumlusu

**Kuaforum Bilişim Ltd. Şti.** (Phase 1 sonu kuruluş). 

VERBİS başvurusu: Eşik aşılınca derhal (50 çalışan VEYA 100M TL bilanço). Phase 0/1'de muafiyet.

---

## 2. İşlenen Kişisel Veri Kategorileri

| Kategori | Örnek | KVKK m. |
|---|---|---|
| Kimlik | Ad, soyad | m.5 |
| İletişim | Telefon, e-posta | m.5 |
| Lokasyon | GPS (mobil), seçili şehir/mahalle | m.5 (hassas DEĞİL) |
| Finansal | IBAN (kuaför), TCKN/VKN, fatura | m.5 |
| Görsel | Profil fotoğrafı, salon fotoğrafı | m.5 |
| Müşteri tercihleri | Saç tipi, tercih edilen hizmet | m.5 |
| **Sağlık (özel nitelikli)** | Alerji notu, saç-cilt durumu | **m.6** — özel nitelikli |
| Davranışsal | Arama, tıklama, randevu geçmişi | m.5 |

---

## 3. Hukuki Sebep (m.5/2)

| İşleme | Sebep |
|---|---|
| Kayıt + giriş | (a) Kanunlarda öngörülmesi (sözleşme + e-ticaret) |
| Randevu + ödeme | (c) Sözleşmenin kurulması |
| Bildirim (kritik akış) | (e) Hak tesisi/korunması |
| Marketing iletişimi | **(a) Açık rıza** zorunlu |
| Yorum & puan | (e) Hak tesisi/korunması |
| Konum (yakındaki kuaför) | **(a) Açık rıza** zorunlu (mobile permission) |
| Hassas veri (alerji notu) | **(a) Açık rıza** zorunlu (m.6/2) |

---

## 4. Açık Rıza Akışı (Parçalı)

Kayıt ekranında **ayrı toggle**'lar:
- ☐ Aydınlatma metnini okudum, hizmet sözleşmesini kabul ediyorum. (zorunlu)
- ☐ Marketing amaçlı bana iletişim yapılabilir. (opsiyonel)
- ☐ Konum bilgimin yakındaki kuaför için kullanılmasına onay veriyorum. (opsiyonel — denyse keşif "şehir seçimi" mod)
- ☐ Sağlık verilerimi (alerji notu, saç-cilt durumu) işlemenize onay veriyorum. (opsiyonel — sadece kuaför randevu öncesi notlar için)

**Versiyon kontrolü:** Her aydınlatma + rıza metni `Consent.version`. Metin değişirse yeniden onay.

---

## 5. Veri Sahibi Hakları (m.11)

| Hak | Akış | SLA |
|---|---|---|
| **Erişim** | "Verilerimi indir" — JSON export | 30 gün |
| **Düzeltme** | Profil ekranı | Anında |
| **Silme** | "Hesabı sil" → 30 gün soft delete + hard | 30 gün |
| **İşlemeyi sınırla** | Marketing toggle off | Anında |
| **Taşınabilirlik** | JSON export | 30 gün |
| **İtiraz** | Form üzerinden Kuaforum DPO | 30 gün |
| **Otomatik karar profilleme** | (V2'de relevant) | 30 gün |

---

## 6. Veri Saklama Süreleri

| Veri | Süre | Sebep |
|---|---|---|
| Kullanıcı hesabı (aktif) | Hesap aktif olduğu sürece | İşletme sürekliliği |
| Silinmiş hesap | 30 gün soft + hard delete | KVKK |
| Booking + Payment | 10 yıl | Vergi yasası (VUK) |
| E-Arşiv fatura | 10 yıl | VUK |
| Audit log | 5 yıl | İhtilaf + denetim |
| Notification log | 12 ay | Operasyonel |
| Search/click log | 12 ay agregat sonrası anonim | Ürün analitik |

---

## 7. Veri Güvenliği Önlemleri

- **At-rest encryption:** Postgres pgcrypto veya app layer (telefon, e-posta, TCKN).
- **In-transit:** TLS 1.3 zorunlu (DB, Redis, internal HTTPS).
- **Access control:** RBAC + audit log.
- **PII mask:** Loglarda telefon/e-posta/TCKN maske.
- **Backup encryption:** WAL-G + GPG.
- **Pen-test:** Soft-launch öncesi.
- **Çalışan eğitimi:** (Solo MVP'de kendine; ekip büyürse zorunlu).

---

## 8. Sınır Ötesi Veri Aktarımı

**Hetzner Helsinki / Frankfurt** seçimi ile veri AB'de.

KVKK 9. madde:
- Türkiye 2026 itibarıyla **hiçbir ülkeye yeterlilik kararı vermemiş**.
- AB'ye aktarım için: **Standart Sözleşme imzası** (Hetzner DPA + bizim taahhütname) + **Kuruma 5 iş günü bildirim**.
- Phase 1 launch öncesi tamamlanır (yasal aksiyon).

---

## 9. Veri İhlali Süreci (72 saat)

1. **Tespit** (Sentry alert, anomaly detection, manuel raporlama).
2. **Triaj** (1 saat içinde) — kapsam, etki.
3. **Containment** (4 saat içinde) — sızma durdur.
4. **KVK Kuruma bildirim** (72 saat içinde — kvkk.gov.tr/ihlal).
5. **İlgili kişilere bildirim** (72 saat içinde — uygulama içi + e-posta).
6. **Forensic + post-mortem** (1 hafta).

SOP detayı: `docs/08-security/03-incident-response.md` (Phase 1 yazılacak).

---

## 10. Çocuk (18 altı) Verisi

- **MVP:** 18+ kullanıcı zorunlu, kayıtta yaş onayı.
- **V1.5:** Ebeveyn onayı akışı (P-C3 persona için).

---

## 11. Çerez (Web admin için)

- Sadece **zorunlu çerez** opt-in olmadan.
- **Analytics çerez** explicit consent ile.
- **Marketing çerez** ayrı toggle.

---

## 12. DPO (Veri Koruma Görevlisi)

- KVKK kanununda DPO zorunlu değil (eşik altı).
- Founder = de facto DPO.
- E-posta: `kvkk@kuaforum.com` (Phase 1 sonu aktive).

---

## 13. Audit + Denetim

- **Yıllık iç review** (avukat + Claude security-auditor).
- **VERBİS** eşik aşılınca derhal.
- **3rd party denetim sertifikası** opsiyonel — müşteri güveni için Tier 3'te.

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/01-research/turkey-compliance.md`](../01-research/turkey-compliance.md)
- [`docs/08-security/01-auth-overview.md`](01-auth-overview.md)

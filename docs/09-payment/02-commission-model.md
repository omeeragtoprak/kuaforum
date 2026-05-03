# Komisyon Modeli — Detay

> Detaylı ödeme mimarisi: [`01-payment-architecture.md`](01-payment-architecture.md). Bu doküman komisyon hesaplama + tier + örnek senaryolar.

---

## 1. MVP Komisyon Modeli

- **Tek tier:** **%10 komisyon**, sabit.
- **Abonelik yok** MVP'de.
- **Kuaför net hesabı:**
  ```
  brüt_işlem (müşteri ödeme)   = 500 TL  (kuaförün belirlediği)
  KDV (kuaförün hizmetinde)    = 500 / 1.20 * 0.20 = 83.33 TL  (kuaförün gelirinin %20'si KDV)
  platform_komisyon (brüt)     = 500 * 0.10 = 50 TL
  PSP_MDR (PayTR ortalama)     = 500 * 0.0299 = 14.95 TL  (PayTR brüt'ten alır)
  kuaför_net (banka)           = 500 - 50 - 14.95 = 435.05 TL
  ```

> KDV kuaförün hizmet faturasında dahildir; kuaför KDV'yi GİB'e öder. Platform komisyon faturası ayrı (KDV %20 dahil).

---

## 2. V1.5+ Tier Yapısı

| Tier | Komisyon | Sabit ücret | Öne çıkan özellik |
|---|---|---|---|
| **Standart** | %10 | 0 | MVP default |
| **Premium** | %5 | ₺499/ay | Öncelikli görünüm, gelişmiş analitik |
| **Verified Pro** | %8 | 0 | Belge denetimi yıllık + "Pro" rozet |

> V1.5'a kalır. MVP launch'tan ~3 ay sonra A/B test ile lanse.

---

## 3. Örnek Senaryolar

### Senaryo 1: Tek hizmet randevu
```
Müşteri ödeme:           500 TL (saç kesim)
Platform komisyon:        50 TL (%10)
PSP MDR:                  14.95 TL (PayTR %2.99)
Kuaför net banka:        435.05 TL (T+1)

Faturalar:
- Kuaför → Müşteri: 500 TL (KDV dahil)
- Platform → Kuaför: 50 TL komisyon (KDV dahil, KDV 8.33 TL)
```

### Senaryo 2: Çoklu hizmet
```
Müşteri ödeme:           700 TL (kesim 300 + boya 400)
Platform komisyon:        70 TL (%10)
PSP MDR:                  20.93 TL (PayTR %2.99)
Kuaför net banka:        609.07 TL
```

### Senaryo 3: Kademeli iade (12-24 saat önce iptal)
```
Orijinal ödeme:          500 TL
İade tutarı:             250 TL (%50)
Platform komisyon iade:   25 TL (%10 prorated)
PSP refund fee:            0 (PayTR refund ücretsiz)
Kuaför net (sonuç):      217.50 TL (%50 iadeli işlem)
```

### Senaryo 4: Promo kod (V1.5)
```
Müşteri ödeme:           450 TL (50 TL promo indirim)
Platform komisyon:        50 TL (orijinal hizmet bedeli üzerinden — varsayılan)
PSP MDR:                  13.46 TL
Kuaför net:              436.54 TL
**Promo bedeli platform tarafından subvanse edilir.**
```

---

## 4. PSP Karşılaştırma (komisyon perspektifi)

| PSP | MDR | Settlement | Refund fee | Sub-merchant min |
|---|---|---|---|---|
| **PayTR Pazaryeri** | %0.99 - %4.00 dinamik (avg ~%2.99) | T+1 | Ücretsiz | TCKN/VKN + IBAN |
| **Iyzico Pazaryeri** | %4.29 + 0.25 TL standart, negotiable %2.49 | T+3 hafta | Ücretsiz | TCKN/VKN + IBAN |

> PayTR primary çünkü kuaför cash-flow.

---

## 5. Komisyon Faturası (Platform → Kuaför)

- **Aylık dönem:** Ay başında bir önceki ayın komisyonu için tek toplu fatura, **veya** randevu başına anlık fatura.
- **MVP:** Randevu başına anlık fatura (Nilvera otomatik).
- **V1.5:** Aylık toplu opsiyon (kuaför tercih).

**Fatura içeriği:**
```
Hizmet: Aracılık komisyonu - Randevu #ABC123
Bedel: 50 TL
KDV %20: 8.33 TL
Toplam: 50 TL (KDV dahil)
Mükellef: Kuaforum Bilişim Ltd. Şti.
Alıcı: <Kuaför VKN>
```

---

## 6. Kademeli İade & Komisyon

| Süre | Müşteri iade | Komisyon (kuaförden kesilen) |
|---|---|---|
| 24h+ önce | %100 (ödediği tutar) | %0 (iade ediliyor) |
| 12-24h önce | %50 | %50 (kuaför yarısını kesiyor) |
| 0-12h önce | %0 | %100 (komisyon korunuyor) |
| **Kuaför iptal** | %100 | %0 |
| **No-show (müşteri)** | %0 | %100 |

---

## 7. Tip / Bahşiş (V1.5)

- **Müşteri seçimli** %0/5/10/15/20 veya manuel.
- Bahşiş tutarı **komisyon dışı** — direkt kuaföre.
- Vergi: Kuaförün gelirinin parçası, kendi muhasebesinde.
- Detay V1.5 spec'inde.

---

## 8. Reconciliation

- **Günlük:** PSP statement → internal Booking + Payment ↔ kayıtlı tutar eşleştirme.
- **Anomali alarm:** %0.1+ sapma → admin alert.
- **Dashboard:** Kuaför "bu ay net kazanç" + admin "GMV + komisyon geliri".

---

## 9. Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/09-payment/01-payment-architecture.md`](01-payment-architecture.md)
- [`docs/01-research/payment-infrastructure.md`](../01-research/payment-infrastructure.md)

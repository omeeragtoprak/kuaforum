# Payment Infrastructure Research — Turkish Barber Marketplace

**Research date:** 2026-05-02
**Scope:** Customer books and pays barbers in-app; platform takes 5–15% commission.
**Verdict (TL;DR):** Use **Iyzico Pazaryeri** (sub-merchant model). **Do not** apply for a TCMB Ödeme Kuruluşu license — operate under 6493 sayılı Kanun Madde 12/2-(b) "ticari temsilci" exception, file an annual TCMB notification once GMV crosses 50M TL/12 months. Funds must never touch your platform's bank account.

---

## A. TCMB Ödeme Kuruluşu Lisansı

### A.1 When the license is required

The relevant law is **6493 sayılı "Ödeme ve Menkul Kıymet Mutabakat Sistemleri, Ödeme Hizmetleri ve Elektronik Para Kuruluşları Hakkında Kanun"**. Supervisory authority transferred from BDDK to **TCMB** on 1 January 2020. Source: [TCMB — Ödeme Sistemleri Mevzuatı](https://www.tcmb.gov.tr/wps/wcm/connect/TR/TCMB+TR/Main+Menu/Banka+Hakkinda/Mevzuat/Odeme+Sistemleri/).

**Madde 12** defines payment services: operating payment accounts, transferring funds in them, payment-instrument issuance/acquiring, money remittance, mobile payments, bill-payment intermediation. Doing any of these for third parties on a commercial basis triggers a licensing requirement unless an exemption applies. [TCMB — 6493 Sayılı Kanun](https://www.tcmb.gov.tr/wps/wcm/connect/TR/TCMB+TR/Main+Menu/Banka+Hakkinda/Mevzuat/6493/).

**Marketplace ("ticari temsilci") exception — Madde 12/2-(b):** A platform acting as a *commercial agent* (gönderen ya da alıcı namına mal/hizmet pazarlığına ya da alım satımına yetkili) is **outside the scope** of the law. Marketplace e-commerce models qualify. Sources: [Canpolat Legal](https://medium.com/canpolat-legal/tcmbye-yap%C4%B1lacak-ticari-temsilci-ve-s%C4%B1n%C4%B1rl%C4%B1-a%C4%9F-i%CC%87stisnalar%C4%B1-bildirimlerinde-dikkat-edilmesi-2e49bbadc0c5), [Yüksel Attorneys](https://www.yuksellaw.com.tr/merkez-bankasi-ticari-temsilci-ile-sinirli-ag-istisnalarina-iliskin-bildirim-yukumlulugu-hakkinda-bilgilendirme-rehberini-yayimladi/), [Fintechtime](https://fintechtime.com/2021/05/pazaryeri-istisnasi-hukumlerinin-yeni-duzenlemeler-isiginda-degerlendirmesi/).

**50M TL / 12 ay threshold:** Once gross transaction value (commission-inclusive GMV) exceeds **50,000,000 TL** in a rolling 12 months, the platform must file an annual report to TCMB every January. Crossing the threshold does *not* automatically reclassify the activity, but TCMB may reclassify it on review. [Canpolat Legal](https://medium.com/canpolat-legal/tcmbye-yap%C4%B1lacak-ticari-temsilci-ve-s%C4%B1n%C4%B1rl%C4%B1-a%C4%9F-i%CC%87stisnalar%C4%B1-bildirimlerinde-dikkat-edilmesi-2e49bbadc0c5).

### A.2 Does using a licensed PSP exempt the platform?

**Yes — provided funds never enter platform-controlled accounts.** Iyzico, PayTR, and Param are TCMB-licensed PSPs. Their Pazaryeri products keep all consumer funds in the PSP's escrow (havuz hesabı). The PSP performs the split, retains the platform's commission, and pays sub-merchants directly to their IBAN. The platform never has dispositive control over user funds, so it is not "providing payment services" in the legal sense. [Iyzico Pazaryeri](https://docs.iyzico.com/urunler/pazaryeri).

### A.3 Definitions that trigger licensing

- **"Fonların elinde tutulması"** (fund safeguarding): payment/e-money institutions must "ödeme hizmeti kapsamında veya elektronik para karşılığında tutulan fonların korunmasını sağlamak" — protect funds held under a payment-service or e-money arrangement. If your platform receives customer money and later forwards it, you *are* holding funds and need a license. [Iyzico Blog](https://www.iyzico.com/blog/odeme-hizmetleri-ve-elektronik-para-kanunu-neler-getirdi).
- **"Ödeme aracılık hizmeti"**: operating payment accounts, transferring funds in them, payment-instrument issuance/acquiring, bill-payment intermediation. Triggered when a third party effects payments between parties via accounts under its operational control. [TCMB — 6493](https://www.tcmb.gov.tr/wps/wcm/connect/TR/TCMB+TR/Main+Menu/Banka+Hakkinda/Mevzuat/6493/).
- Exact wording of Madde 12/2-(b)/(h) was not retrievable via web fetch on 2026-05-02 `[unverified 2026-05-02]`. Pull consolidated text from [Lexpera](https://www.lexpera.com.tr/mevzuat/kanunlar/odeme-ve-menkul-kiymet-mutabakat-sistemleri-odeme-hizmetleri-ve-elektronik-para-kuruluslari-hakkinda) before signing legal opinions.

---

## B. Iyzico Pazaryeri — Deep Dive

### B.1 End-to-end flow

1. **Sub-merchant creation** (server-to-server): platform calls Iyzico API with kuaför data, receives `subMerchantKey`. [Alt Üye](https://docs.iyzico.com/urunler/pazaryeri/pazaryeri-entegrasyonu/alt-uye-olusturma).
2. **Booking + payment**: customer pays in-app via Iyzico (3D Secure). Request carries `paymentItems[]` with `subMerchantKey` and `subMerchantPrice` per line.
3. **Funds land in Iyzico escrow** — never platform balance.
4. **Split**: Iyzico calculates platform commission = `total – subMerchantPrice` and credits platform/barber sub-balances respectively.
5. **Approval**: items start in "pending merchant confirmation" (status 1). Platform calls `approve` after service rendered (or auto-approve T+N); `disapprove` reverses. [Pazaryeri Ödemesi](https://docs.iyzico.com/urunler/pazaryeri/pazaryeri-entegrasyonu/pazaryeri-odemesi).
6. **Refund**: returns funds from escrow to customer; no commission deducted. [Iyzico Yardım — Pazaryeri](https://www.iyzico.com/destek/yardim-merkezi/urunler-ve-ozellikler/pazaryeri-odeme-cozumu).

### B.2 Sub-merchant onboarding fields

Source: [Alt Üye İşyeri API](https://docs.iyzico.com/on-hazirliklar/api-reference-beta/pazaryeri/alt-uye-isyeri).

| Field | Bireysel (PERSONAL) | Şahıs Şti (PRIVATE_COMPANY) | Limited/A.Ş. |
|---|---|---|---|
| email, gsm, address | required | required | required |
| identityNumber (TCKN) | **required** | optional | optional |
| taxOffice | — | **required** | **required** |
| taxNumber | — | optional | **required** |
| legalCompanyTitle | — | **required** | **required** |
| IBAN | optional at create, **required before approval** | same | same |
| subMerchantExternalId | required | required | required |

**Practical takeaway for kuaförler:** A registered **şahıs şirketi (esnaf)** is the sweet spot — needs only TCKN, vergi dairesi, IBAN, and vergi-levhası ad-soyad match. **Bireysel** is also accepted for sub-merchants — Iyzico's "company required" rule applies to the *platform's* master account, not its sub-merchants. **No sıhhi müessese ruhsatı, no signature circular, no trade-registry gazette** is required from sub-merchants. [Iyzico Başvuru — Gerekli Evraklar](https://www.iyzico.com/destek/yardim-merkezi/basvuru/gerekli-evraklar).

### B.3 Fees (2026)

Iyzico does not publish a marketplace-specific tariff; standard merchant pricing applies to the platform's master account, then commission is split.

- **MDR single payment**: **%4.29 + 0.25 TL** per txn (BSMV included). Negotiated corporate rate: from **%2.49 + 0.25 TL** for higher volume. [Iyzico Sanal POS Komisyon](https://www.iyzico.com/blog/sanal-pos-komisyon-oranlari-hakkinda-merak-ettikleriniz). A 2026 promo of **%1.95** is reported `[unverified 2026-05-02]` ([Poskomisyon](https://poskomisyon.com/pos/iyzico-sanal/)).
- **Installment**: ~%3.99 + 0.25 TL.
- **Sub-merchant fees**: **none** — no service/integration/access fee. MDR comes off the *total transaction*, then platform commission and barber payout flow to their respective balances. [Iyzico Pazaryeri](https://docs.iyzico.com/urunler/pazaryeri).
- **Refund**: no MDR deducted.

Your 5–15% platform take is *layered on top of* Iyzico's MDR. Customer pays X → Iyzico keeps ~%4.29+0.25 → remaining ~95.5% splits per `subMerchantPrice`.

### B.4 Settlement timing

- **Default**: weekly batch. Mon–Sun transactions settle to platform/sub-merchant **bank IBAN** the **following Wednesday**.
- **Holdback**: a **3-week** hold applies before bank release (fraud/chargeback protection). Effectively **T+3 weeks** to bank; balance visible immediately inside Iyzico.
- **Per-sub-merchant configuration**: commission rate is per-sub-merchant; payout cadence is set at master-account level. T+0 / T+1 not standard, requires special agreement. [Iyzico Yardım — Üye İşyeri](https://www.iyzico.com/destek/yardim-merkezi/uye-is-yeri-odemeleri).

### B.5 KYC turnaround

Platform onboarding: **24h–48h**. Sub-merchant: minutes when IBAN/TCKN match cleanly; hours if manual review. No formal SLA published `[unverified 2026-05-02]`.

### B.6 Limits

Iyzico **does not publish** hard min/max per-transaction or monthly volume caps. Card-network and risk-based fraud limits apply (3D Secure required above thresholds, BKM rules). Get the risk-rule sheet from Iyzico onboarding `[unverified 2026-05-02]`.

---

## C. Alternatives

### C.1 PayTR Marketplace
- Marketplace product **exists** as "Pazaryeri Çözümü". A "PayTR İşim" branded product was **not found** in 2026 PayTR docs `[unverified 2026-05-02]`.
- Same sub-merchant model; configurable per-seller commissions; no fee to sub-merchant. [PayTR Marketplace](https://www.paytr.com/en/marketplace-solution).
- **Fees**: dynamic **%0.99–%4.00** based on volume, capped near TCMB %3.56 single-payment limit. [PayTR Pazaryeri Komisyon](https://www.paytr.com/blog/pazaryeri-komisyon-oranlari).
- **Settlement: T+1** to bank — *materially better than Iyzico's T+3 weeks* for cash flow.
- Application approval: <2 hours within business hours.

### C.2 Param Marketplace
- Product exists: [Param Pazaryeri](https://dev.param.com.tr/tr/pazaryeri/alt-uye-isyeri). Sub-merchant types and required fields (TCKN, IBAN, MCC, vergi dairesi) parallel Iyzico.
- **Fees**: standard members **%2.25 single**, ParamKart Business members **%0.99**, installments up to **%3.40 (12 ay)**, no setup/monthly fee. [Param 2026 komisyon](https://poskomisyonlari.com/pos-firmalari/param-pos-komisyon-oranlari).
- Settlement specifics not fully published `[unverified 2026-05-02]`.

### C.3 Stripe Connect Türkiye
- **Not supported in Turkey in 2026.** Turkey is not on Stripe's supported-countries list. Turkish entities cannot create Stripe accounts directly. Sources: [Stripe Global](https://stripe.com/global), [Stripe Connect](https://stripe.com/connect).
- Workarounds (UK/US incorporation + Stripe Atlas) push you outside the 6493 framework, create FX/TCMB capital-movement issues, and don't help you onboard Turkish barbers as sub-merchants. **Do not use.**

### C.4 Comparison

| | Iyzico | PayTR | Param | Stripe |
|---|---|---|---|---|
| TCMB licensed | Yes | Yes | Yes | N/A — TR not supported |
| Single-payment MDR | %4.29 (%2.49 neg.) | %0.99–%4.00 dynamic | %0.99–%2.25 | — |
| Bank settlement | T+3 weeks (weekly) | **T+1** | configurable | — |
| Sub-merchant types | Bireysel/Şahıs/Şirket | Same | Same | — |
| Documentation | **Best in market** | Good | OK | — |
| Refund commission | None | None | `[unverified]` | — |

---

## D. Recommendation

### D.1 TCMB License — **NO** (with conditions)

You do not need a license **provided you architect so that consumer funds never touch any platform-controlled account.** Operate under Madde 12/2-(b). Required compliance:

1. Customer always pays the licensed PSP, never your IBAN.
2. PSP pays barber's IBAN directly. Your only inflow is your commission, paid to your IBAN by the PSP — that is normal commercial revenue, not payment intermediation.
3. ToS must position the platform explicitly as **"ticari temsilci / aracı hizmet sağlayıcı"** under both 6493 and 6563 (E-Ticaret Kanunu).
4. Track gross GMV (commission-inclusive). Approaching **50M TL / rolling 12 months** → file annual TCMB notification each January.
5. Keep architecture clean from day 1 so a TCMB reclassification doesn't force a code rewrite.

### D.2 Recommended PSP — **Iyzico** (primary), **PayTR** (alt for cash flow)

- **Iyzico**: best docs, broadest API, predictable for v1. Pay the higher MDR for stability; negotiate down once volume is real.
- **PayTR** is the right alternative if barber cash-flow complaints emerge — T+1 vs T+3 weeks is a major UX win for service workers. Build a `PaymentProvider` abstraction so swapping providers is cheap.

### D.3 Onboarding flow (UX) for kuaförler

1. **Sign up**: name, GSM, email, TCKN, IBAN, vergi dairesi, vergi numarası (if registered), selfie + ID upload.
2. **Auto-classify**: vergi numarası present → `PRIVATE_COMPANY` (şahıs) or `LIMITED_OR_JOINT_STOCK_COMPANY`; else `PERSONAL` (with esnaf-registration nudge).
3. **API call** `submerchant/create` to Iyzico; persist `subMerchantKey`.
4. **IBAN verification**: collect; show "Pending verification" until approved.
5. **Status-gate booking**: barber cannot accept paid bookings until sub-merchant approved + IBAN verified.
6. **ToS copy**: "Ödeme işlemleri Iyzico aracılığı ile gerçekleşmektedir. [Platform] yalnızca ticari temsilci olarak hareket eder; fonlar Iyzico'nun emanet hesabında tutulur."

### D.4 Settlement strategy

- **Charge customer at booking** (auth + capture, or auth-only + capture on service date).
- **Auto-approve sub-merchant payment 24h after appointment end** to allow disputes; manual disapprove for no-shows.
- **Refund window**: full refund up to N hours before appointment per cancellation policy; partial after.
- **Barber payouts**: rely on Iyzico's weekly batch (T+3w to bank). For barber retention, explore Iyzico's optional early-payout feature `[unverified 2026-05-02]`.
- Reserve a chargeback buffer; never let barber net balance go negative.

### D.5 Risks / blockers

1. **50M TL threshold creep**: at 5–15% take, you hit 50M TL GMV around ~3.5–10M TL platform revenue — easy by year 2. Have legal counsel ready for the TCMB filing.
2. **Bireysel sub-merchants** (unregistered): fine for the PSP but creates **gelir vergisi + SGK** issues for the kuaför. Disclose platform is not the employer; nudge esnaf registration above ~X TL/month.
3. **Chargebacks**: barber service is intangible; "no-show" chargebacks are common. Build appointment confirmation + photo/QR check-in + 24h auto-approval.
4. **MDR negotiation**: %4.29 is steep — engage Iyzico Sales day 1 about marketplace-volume rates.
5. **Iyzico T+3-weeks**: barber complaints near-guaranteed. Pre-negotiate faster settlement or set realistic expectations in onboarding.
6. **Stripe is a non-option** for the Turkish leg — don't waste cycles.
7. `[unverified 2026-05-02]` items above need PSP-sales confirmation pre-contract, especially: per-sub-merchant settlement timing, transaction min/max, sub-merchant KYC SLAs, exact 2026 promo MDR.

---

## Sources

**Regulatory:** [TCMB 6493 Kanun](https://www.tcmb.gov.tr/wps/wcm/connect/TR/TCMB+TR/Main+Menu/Banka+Hakkinda/Mevzuat/6493/) · [TCMB Ödeme Sistemleri Mevzuatı](https://www.tcmb.gov.tr/wps/wcm/connect/TR/TCMB+TR/Main+Menu/Banka+Hakkinda/Mevzuat/Odeme+Sistemleri/) · [Mevzuat.gov.tr 6493](https://www.mevzuat.gov.tr/mevzuat?MevzuatNo=6493&MevzuatTur=1&MevzuatTertip=5) · [Lexpera 6493 Konsolide](https://www.lexpera.com.tr/mevzuat/kanunlar/odeme-ve-menkul-kiymet-mutabakat-sistemleri-odeme-hizmetleri-ve-elektronik-para-kuruluslari-hakkinda) · [Canpolat Legal — TCMB Ticari Temsilci](https://medium.com/canpolat-legal/tcmbye-yap%C4%B1lacak-ticari-temsilci-ve-s%C4%B1n%C4%B1rl%C4%B1-a%C4%9F-i%CC%87stisnalar%C4%B1-bildirimlerinde-dikkat-edilmesi-2e49bbadc0c5) · [Yüksel Attorneys](https://www.yuksellaw.com.tr/merkez-bankasi-ticari-temsilci-ile-sinirli-ag-istisnalarina-iliskin-bildirim-yukumlulugu-hakkinda-bilgilendirme-rehberini-yayimladi/) · [Fintechtime — Pazaryeri İstisnası](https://fintechtime.com/2021/05/pazaryeri-istisnasi-hukumlerinin-yeni-duzenlemeler-isiginda-degerlendirmesi/) · [Iyzico Blog — 6493 Kanun](https://www.iyzico.com/blog/odeme-hizmetleri-ve-elektronik-para-kanunu-neler-getirdi)

**Iyzico:** [Pazaryeri](https://docs.iyzico.com/urunler/pazaryeri) · [Pazaryeri Ödemesi](https://docs.iyzico.com/urunler/pazaryeri/pazaryeri-entegrasyonu/pazaryeri-odemesi) · [Alt Üye Oluşturma](https://docs.iyzico.com/urunler/pazaryeri/pazaryeri-entegrasyonu/alt-uye-olusturma) · [API Reference Alt Üye](https://docs.iyzico.com/on-hazirliklar/api-reference-beta/pazaryeri/alt-uye-isyeri) · [Yardım — Pazaryeri](https://www.iyzico.com/destek/yardim-merkezi/urunler-ve-ozellikler/pazaryeri-odeme-cozumu) · [Yardım — Üye İşyeri](https://www.iyzico.com/destek/yardim-merkezi/uye-is-yeri-odemeleri) · [Başvuru Evrakları](https://www.iyzico.com/destek/yardim-merkezi/basvuru/gerekli-evraklar) · [Sanal POS Komisyon](https://www.iyzico.com/blog/sanal-pos-komisyon-oranlari-hakkinda-merak-ettikleriniz)

**PayTR:** [Marketplace Solution](https://www.paytr.com/en/marketplace-solution) · [Pazaryeri Komisyon](https://www.paytr.com/blog/pazaryeri-komisyon-oranlari) · [Fiziki POS 2026 Rehberi](https://www.paytr.com/blog/fiziki-pos-komisyon-oranlari-2026-rehberi)

**Param:** [Pazaryeri Alt Üye İşyeri](https://dev.param.com.tr/tr/pazaryeri/alt-uye-isyeri) · [Param 2026 Komisyon](https://poskomisyonlari.com/pos-firmalari/param-pos-komisyon-oranlari)

**Stripe:** [Global Availability](https://stripe.com/global) · [Connect](https://stripe.com/connect)

**Other:** [Poskomisyon — Iyzico 2026](https://poskomisyon.com/pos/iyzico-sanal/) · [Garanti BBVA Pazaryeri Altyapısı](https://garantibbvapos.com.tr/pazaryeri-odeme-ve-para-dagitimi-altyapisi) · [TÖDEB — Genel Bilgiler](https://todeb.org.tr/detay/odeme-hizmetlerine-iliskin-genel-bilgiler/115/)

> `[unverified 2026-05-02]` items must be confirmed via direct PSP sales contact and a fresh consultation with a TR fintech-licensed lawyer before contracts are signed.

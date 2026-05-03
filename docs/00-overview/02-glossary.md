# Sözlük (Glossary)

Domain + teknik + Türk regülasyon kavramları için tek hakikat kaynağı. Yeni terim eklenirse alfabetik konum.

---

| Terim | Tanım |
|---|---|
| **Adisyon** | Müşterinin aldığı hizmetlerin tutarını belgeleyen kayıt; bizim sistemde `Booking` + `BookingService` ile temsil edilir. |
| **Aydınlatma metni** | KVKK m.10 kapsamında veri sahibine bilgilendirme. Avukat onaylı şablon zorunlu. |
| **Açık rıza** | KVKK m.5 — kullanıcının özgür, bilgilendirilmiş, kesin onayı. **Parçalı** olmalı (her amaç için ayrı toggle). |
| **BarberShop** | Sistemde işletme aggregate root. Birden fazla `Stylist`'i tutar. |
| **BDDK / TCMB** | Eskiden BDDK, 2020'den beri TCMB ödeme hizmetleri otoritesi. 6493 sayılı Kanun yetkili mevzuat. |
| **Booking** | Randevu aggregate. State machine: pending → confirmed → started → completed / cancelled / no-show. |
| **Bağ-Kur** | Esnaf/serbest meslek SGK kolu. Founder Ltd. ortağı olarak ay başı ödeme yapar. |
| **CQRS** | Command Query Responsibility Segregation. Yazma + okuma ayrı handler'lar. |
| **Clean Architecture** | Domain → Application → Infrastructure → API katman ayrımı. |
| **Concierge MVP** | Yazılım yerine manuel insan işiyle hipotez test etmek (Phase 0 customer discovery'de). |
| **e-Arşiv fatura** | GİB elektronik fatura sistemi. e-ticaret aracı için 500K TL eşiği. Çift fatura modeli marketplace standard. |
| **EAS Build** | Expo Application Services — Expo build/submit cloud servisi. |
| **Eşik (e-Arşiv)** | 2026 itibarıyla genel 3M TL, e-ticaret aracı için **500K TL**. |
| **ESBİS** | Esnaf Bilgi Sistemi. Kuaför doğrulama için kullanılır (public API yok, web). |
| **GMV** | Gross Merchandise Volume — platform üzerinden geçen toplam işlem hacmi. |
| **Hetzner** | Alman hosting sağlayıcı. Helsinki + Frankfurt veri merkezi. |
| **IPaymentProvider** | PSP soyutlaması. PayTR + Iyzico + Mock implementations. |
| **Idempotency key** | Aynı request birden fazla geldiğinde tek sefer işleme alma anahtarı. PSP webhook + payment komutlarında zorunlu. |
| **Iyzico Pazaryeri** | Iyzico marketplace ürünü. Sub-merchant onboarding + split payment. T+3w settlement. |
| **KVKK** | Kişisel Verilerin Korunması Kanunu (6698 sayılı). |
| **Kademeli iade** | İptal süresi → iade oranı: 24h+ %100, 12-24h %50, 0-12h %0. Yasal uyumlu. |
| **MDR** | Merchant Discount Rate — PSP'nin işletmeden aldığı komisyon yüzdesi. |
| **Marketplace** | İki taraflı platform. Bizim için kuaför + müşteri arasında aracılık. |
| **MediatR** | .NET mediator library. Command/Query/Notification handler pattern. |
| **Modular Monolith** | Tek deploy edilebilen ama modülleri katı sınırlarla ayrılmış uygulama. |
| **MYK** | Mesleki Yeterlilik Kurumu. Kuaför Seviye 4 belgesi 2026/1 Tebliğ ile zorunlu meslek listesinde. |
| **NACE** | Faaliyet kodları sistemi. Şirket kuruluşunda gerekli. |
| **Nilvera** | E-Arşiv entegratörü. REST API + VKN sorgu. |
| **No-show** | Müşteri randevuya gelmedi — iade %0 + müşteri 1 strike. |
| **NPS** | Net Promoter Score — kullanıcı tavsiye eğilimi metriği. Hedef >40. |
| **NVI** | Nüfus İdaresi — il/ilçe/mahalle açık veri kaynağı. |
| **OTP** | One-Time Password. SMS ile telefon doğrulama. |
| **Outbox pattern** | DB transaction'ı ile event publish atomicity garantisi. |
| **PaaS / IaaS** | Platform-as-a-Service / Infrastructure-as-a-Service. Hetzner Cloud IaaS. |
| **PayTR Pazaryeri** | PayTR'ın marketplace ürünü. Sub-merchant + split. T+1 settlement. |
| **PostGIS** | PostgreSQL spatial extension. K-NN sorgu, polygon contain. |
| **PSP** | Payment Service Provider. PayTR, Iyzico vb. |
| **Refresh token** | Access token süresi dolduğunda yenilemek için kullanılan uzun ömürlü token. **Device-bound** + rotated. |
| **RFC** | Request for Comments. Spec değişikliği için açılan tartışma dokümanı. |
| **SaaS** | Software-as-a-Service. Mevcut TR oyuncularının çoğu B2B SaaS. |
| **SLA** | Service Level Agreement. Uptime + response time taahhüdü. |
| **SMMM** | Serbest Muhasebeci Mali Müşavir. Aylık muhasebe + e-defter. |
| **Stylist** | İşletme içindeki tek bir kuaför/berber. `BarberShop`'a bağlı. |
| **Sub-merchant** | PSP altında alt-tüccar. Kuaför, PayTR Pazaryeri'nde sub-merchant olur. |
| **TCMB** | Türkiye Cumhuriyeti Merkez Bankası. Ödeme hizmetleri otoritesi (eski BDDK). |
| **TPMK** | Türk Patent ve Marka Kurumu. Marka tescili. |
| **Ticari temsilci istisnası** | 6493 sk. m.12/2-(b). Marketplace'in TCMB lisanssız çalışmasını sağlayan istisna. |
| **Tier (komisyon)** | Kuaför komisyon seviyesi: standart %10, premium %5+abonelik (V1.5). |
| **VERBİS** | KVKK veri sorumluları sicili. Eşik: 50 çalışan VEYA 100M TL bilanço. |
| **VKN** | Vergi Kimlik Numarası. Tüzel kişiler için. |
| **VUK 509** | Vergi Usul Kanunu 509. Tebliği. E-Arşiv 7 gün SLA. |
| **Walk-in (simplified)** | Statik "boşum" toggle + filtre. Real-time queue **YOK** (V1.5'a). |
| **Walk-in (full)** | Real-time presence + SignalR + canlı queue uzunluğu. V1.5+. |

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/01-research/turkey-compliance.md`](../01-research/turkey-compliance.md)

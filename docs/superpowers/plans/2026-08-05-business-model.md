# RFC-012/013 İş Modeli Implementasyon Planı (Cycle 12)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Davranış detayı RFC-012/013'te — plan yalnız görev sınırları. Ara kararlar koordinatörce onaylandı (2026-08-05): PlatformSettings DB-backed; CommissionPayment sibling aggregate (PayTR iskeleti); itiraz vadeyi durdurmaz; onay zorunluluğu FullOnline dahil; Booking.RequiresOnlinePayment snapshot; anti-spam sayımı NoShow dahil/Cancelled hariç.

**Suite baseline:** 126 domain + 66 application + 121 integration + 116 mobile jest. Paralel: web/ ajanı (dokunulmaz).

### C1+C2: Faturalama omurgası (backend)
- `PlatformSettings` (DB, seed'li tek satır ya da key-value): global CommissionRate (default 0.20) + shop-özel override; admin GET/PUT endpoint'leri (Admin policy + audit).
- `CommissionStatement` aggregate: dönem (yıl-ay), shopId, matrah (o ay **Completed** randevuların TotalAmountMinor toplamı), oran (kesim anında snapshot), tutar, durum `Open→Notified→Paid | Overdue→SuspensionApplied`, dueDate = kapanış+7g, adminNote.
- Hangfire: `MonthlyStatementCloseJob` (ay başında önceki ayı kapatır; idempotent — dönem+shop unique) + `DailyOverdueCheckJob` (vade+geçmiş Open/Notified → Overdue; Overdue+ödenmemiş → shop Suspend + `SuspensionApplied` + owner bildirimi; admin unsuspend mevcut akış).
- Owner endpoint'leri: `GET /v1/owner/billing/statements` (+detay: randevu bazlı satır dökümü) + `POST .../statements/{id}/dispute` (yalnız not düşer, vade DURMAZ).
- Admin endpoint'leri: tahsilat kuyruğu listesi, `mark-paid` (havale onayı), `extend-due`, `adjust` (tutar düzeltme + zorunlu gerekçe) — hepsi audit'li.
- Bildirimler: statement kapanışı (borç+vade), vade yaklaşıyor (T-2g), suspend.
- IBAN/banka bilgisi: PlatformSettings'te (admin girer; owner borç ekranında gösterilir). PayTR online komisyon ödemesi: `CommissionPayment` iskeleti (Initiate/webhook uyarlaması) — provider Mock'la testli, gerçek aktivasyon founder hesabına bloklu.
- Testler: kapanış job'u (yalnız Completed matraha girer; iptal/no-show girmez; idempotent), vade→Overdue→Suspend zinciri, admin mark-paid→(suspend'liyse) unsuspend AYRI admin aksiyonu, oran override, dispute audit.

### C3: BookingPolicy (backend)
- BarberShop'a `BookingPolicy` (DepositEnabled, DepositPercent [admin min-max: PlatformSettings], MaxPendingPerCustomer=2, MinDaysBetweenBookings?, MaxBookingsPerMonth?) + M-migration.
- Owner ayar endpoint'leri (GET/PUT, validasyon + Türkçe hatalar); global `Payment:OnlinePaymentsEnabled=false` iken DepositEnabled=true SET EDİLEBİLİR ama etkisiz (initiate'te iki koşul birlikte aranır) — ayar ekranı bilgi mesajı.
- InitiateBooking enforcement: per-shop kurallar (sayım: NoShow dahil, Cancelled hariç) + mevcut global 3-Pending taban korunur.

### C4: Ödeme-sonra-onay akışı (backend — EN HASSAS)
- `Booking.RequiresOnlinePayment` snapshot (initiate anında: global flag && shop.DepositEnabled).
- PaymentCompletionService: ödeme tamam → booking'i **Confirm ETMEZ**; `DepositPaidAt` işaretler (Pending kalır) → owner'a "onay bekleyen ödenmiş talep" bildirimi.
- OwnerConfirm: her randevuda zorunlu (FullOnline dahil); onayda müşteriye bildirim (zincir mevcut).
- Reddet/timeout: kapora ödenmişse OTOMATİK %100 iade (BarberRequested / PendingTimeout — oto-refund zinciri mevcut; RefundPercentage yolu doğrulanır).
- ExpirePendingBookingsJob 3 dal: (a) RequiresOnlinePayment && !DepositPaidAt → 30dk (ödeme terk); (b) DepositPaidAt || !RequiresOnlinePayment → ScheduledAt'e kadar onay bekler; geçerse iptal+iade.
- Testler: kapora öde→Pending kalır→onay→Confirmed; öde→red→tam iade; öde→timeout→tam iade; kaporasız→onay; FullOnline→onay zorunlu.

### C5: UI (mobile owner ayarları + müşteri metinleri + admin web tahsilat)
- Owner: (barber)/settings ekranı (kapora + anti-spam ayarları); Bugün ekranında "ödenmiş, onay bekliyor" rozetli talepler.
- Müşteri: confirm.tsx per-shop kapora gösterimi (policy'yi public shop DTO'suna minimal ekle: depositEnabled+percent); "kuaför onaylayınca kesinleşir" metinleri; red/iade dürüst iletişim.
- Admin web: /billing sayfası (tahsilat kuyruğu, mark-paid, extend, adjust, statement detayı).

### C6: Kapanış
- docs/09-payment/02-commission-model.md çelişki güncellemesi + GAP + CLAUDE.md Cycle 12 + RFC M1/M4 (public liste RFC delta'sı + yorum düzeltmesi) + memory.

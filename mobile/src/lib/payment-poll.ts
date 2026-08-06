import { isBookingConfirmed } from './format';

export type PaymentPollOutcome = 'confirmed' | 'paidPendingApproval' | 'pending';

/**
 * RFC-013 C4 (backend `PaymentCompletionService.CaptureAndConfirmAsync` +
 * `Booking.MarkPaymentReceived`): capture artık booking'i Confirm ETMİYOR. Booking
 * Pending kalır, yalnızca payment yansıması (`BookingDto.paymentKind`/`paidOnlineMinor`)
 * dolar — onay her zaman ayrı, açık bir owner adımı (`OwnerConfirm`, FullOnline dahil).
 *
 * `booking/payment.tsx`'teki verify-poll bu yüzden İKİ farklı "olumlu" durumu ayırt
 * etmeli, aksi halde ödeme kaydedilmiş ama hâlâ Pending bir randevu için sonsuza kadar
 * "doğrulanıyor" döngüsünde kalıp `pendingWebhook`'a düşer (yanlış: ödeme aslında
 * başarılı, sadece owner onayı bekliyor):
 *  - 'confirmed': booking Confirmed'e geçmiş (owner ödeme sırasında/hemen sonra
 *    onaylamış — payment + onay birlikte gerçekleşmiş, nadir ama mümkün bir yarış).
 *  - 'paidPendingApproval': ödeme backend'e yansımış (`paymentKind` dolu) ama booking
 *    hâlâ Pending — owner onayı bekleniyor, ödeme BAŞARILI sayılır (poll durmalı).
 *  - 'pending': ne biri ne diğeri — webhook/mock-complete henüz backend'e yansımamış
 *    olabilir, poll devam etmeli.
 *
 * `paymentKind` yalnız GERÇEKTEN yakalanmış (Captured/Settled/…) bir payment'ta dolu
 * olur (bkz. backend `BookingPaymentProjection.PaidStatuses` — Initiated/Authorized/
 * Failed sayılmaz), bu yüzden burada ekstra bir durum kontrolüne gerek yok.
 */
export function resolvePaymentPollOutcome(booking: {
  status: unknown;
  paymentKind?: unknown;
}): PaymentPollOutcome {
  if (isBookingConfirmed(booking.status)) return 'confirmed';
  if (booking.paymentKind != null) return 'paidPendingApproval';
  return 'pending';
}

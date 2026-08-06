import { resolvePaymentPollOutcome } from '@/lib/payment-poll';

describe('resolvePaymentPollOutcome', () => {
  it('booking Confirmed ise "confirmed" döner (owner ödeme sırasında/hemen sonra onaylamış)', () => {
    expect(resolvePaymentPollOutcome({ status: 'Confirmed', paymentKind: 'Deposit' })).toBe('confirmed');
    expect(resolvePaymentPollOutcome({ status: 1, paymentKind: null })).toBe('confirmed');
  });

  it('booking Pending ama paymentKind dolu ise "paidPendingApproval" döner (RFC-013 C4 — ödeme onaylamıyor)', () => {
    expect(resolvePaymentPollOutcome({ status: 'Pending', paymentKind: 'Deposit' })).toBe('paidPendingApproval');
    expect(resolvePaymentPollOutcome({ status: 0, paymentKind: 'FullOnline' })).toBe('paidPendingApproval');
  });

  it('booking Pending ve paymentKind yoksa "pending" döner (webhook henüz yansımamış — poll devam etmeli)', () => {
    expect(resolvePaymentPollOutcome({ status: 'Pending', paymentKind: null })).toBe('pending');
    expect(resolvePaymentPollOutcome({ status: 0, paymentKind: undefined })).toBe('pending');
  });

  it('Cancelled + paymentKind null ise "pending" döner (poll caller max deneme sonrası pendingWebhook fazına düşürür)', () => {
    expect(resolvePaymentPollOutcome({ status: 'Cancelled', paymentKind: null })).toBe('pending');
  });
});

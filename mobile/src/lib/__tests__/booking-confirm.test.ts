import { resolveBookingConfirmMode } from '@/lib/booking-confirm';

describe('resolveBookingConfirmMode', () => {
  it('onlinePaymentsEnabled true → online-payment (RFC-006 kapora akışı)', () => {
    expect(resolveBookingConfirmMode(true)).toBe('online-payment');
  });

  it('onlinePaymentsEnabled false → salon-payment (RFC-009 ücretsiz büyüme modu)', () => {
    expect(resolveBookingConfirmMode(false)).toBe('salon-payment');
  });

  it('onlinePaymentsEnabled undefined (henüz hydrate edilmemiş) → fail-closed salon-payment', () => {
    expect(resolveBookingConfirmMode(undefined)).toBe('salon-payment');
  });
});

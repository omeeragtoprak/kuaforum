import { resolveBookingConfirmMode, resolveBookingPromise, resolvePaymentSuccessMessage } from '@/lib/booking-confirm';

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

describe('resolveBookingPromise (RFC-014 §2 D2 madde 6)', () => {
  it('auto-confirm KAPALI → ödeme gerekip gerekmediğinden bağımsız "kuaför onaylar" metni', () => {
    expect(resolveBookingPromise({ autoConfirmEnabled: false, requiresPayment: false })).toBe(
      'Kuaför onayladığında bildirim alacaksın.',
    );
    expect(resolveBookingPromise({ autoConfirmEnabled: false, requiresPayment: true })).toBe(
      'Kuaför onayladığında bildirim alacaksın.',
    );
  });

  it('auto-confirm AÇIK + ödeme gerekmiyor → "anında kesinleşir"', () => {
    expect(resolveBookingPromise({ autoConfirmEnabled: true, requiresPayment: false })).toBe(
      'Randevun anında kesinleşir.',
    );
  });

  it('auto-confirm AÇIK + ödeme gerekiyor (kapora/tam) → ödeme sonrası kesinleşir, kuaför onayı beklenmez', () => {
    expect(resolveBookingPromise({ autoConfirmEnabled: true, requiresPayment: true })).toBe(
      'Ödemen alınınca randevun anında kesinleşir — kuaför onayı beklemezsin.',
    );
  });
});

describe('resolvePaymentSuccessMessage', () => {
  it('auto-confirm KAPALI iken kuaförün onayladığını söyler (gerçekten owner aksiyonu var)', () => {
    expect(resolvePaymentSuccessMessage(false)).toBe('Ödemen alındı ve kuaförün onayladı.');
  });

  it('auto-confirm AÇIK iken kuaföre yanlış atıf yapmaz — "otomatik onaylandı" der', () => {
    expect(resolvePaymentSuccessMessage(true)).toBe('Ödemen alındı, randevun otomatik onaylandı.');
  });
});

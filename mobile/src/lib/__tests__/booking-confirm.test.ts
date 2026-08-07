import {
  resolveBookingConfirmMode,
  resolveBookingPromise,
  resolvePaymentSuccessMessage,
  formatConflictConfirmMessage,
  formatConflictAfterCreateMessage,
} from '@/lib/booking-confirm';
import { formatDateTime } from '@/lib/format';
import type { ConflictingBooking } from '@/api/booking';

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

describe('formatConflictConfirmMessage / formatConflictAfterCreateMessage (RFC-014 §2 D4)', () => {
  const conflict: ConflictingBooking = {
    bookingId: 'b-1',
    barberShopName: 'Ahmet Kuaför',
    scheduledAt: '2026-08-07T11:30:00+03:00',
    scheduledEndsAt: '2026-08-07T12:00:00+03:00',
  };

  // Karar tablosu:
  // | fonksiyon                        | girdi                          | çıktı içeriği                                            |
  // |-----------------------------------|---------------------------------|-----------------------------------------------------------|
  // | formatConflictConfirmMessage      | conflict (salon adı + tarih)    | salon adı + formatDateTime(scheduledAt) + soru cümlesi    |
  // | formatConflictAfterCreateMessage  | conflict (salon adı + tarih)    | salon adı + formatDateTime(scheduledAt), SORU YOK          |

  it('formatConflictConfirmMessage: salon adı + tarih/saat içerir, "yine de" sorusuyla biter', () => {
    const msg = formatConflictConfirmMessage(conflict);
    expect(msg).toContain('Ahmet Kuaför');
    expect(msg).toContain(formatDateTime(conflict.scheduledAt));
    expect(msg).toContain('Yine de bu randevuyu almak istiyor musun?');
  });

  it('formatConflictConfirmMessage: "emin misiniz" DEMEZ — direktif kuralı (ne olacağını yazar)', () => {
    expect(formatConflictConfirmMessage(conflict)).not.toMatch(/emin misin/i);
  });

  it('formatConflictAfterCreateMessage: salon adı + tarih/saat içerir, soru İÇERMEZ', () => {
    const msg = formatConflictAfterCreateMessage(conflict);
    expect(msg).toContain('Ahmet Kuaför');
    expect(msg).toContain(formatDateTime(conflict.scheduledAt));
    expect(msg).not.toContain('?');
  });

  it('farklı salon adı ve zaman doğru yansır (sabit metin değil, gerçekten girdiye göre üretiliyor)', () => {
    const other: ConflictingBooking = {
      bookingId: 'b-2',
      barberShopName: 'Zeynep Güzellik Salonu',
      scheduledAt: '2026-12-25T09:00:00+03:00',
      scheduledEndsAt: '2026-12-25T09:30:00+03:00',
    };
    const msg = formatConflictConfirmMessage(other);
    expect(msg).toContain('Zeynep Güzellik Salonu');
    expect(msg).toContain(formatDateTime(other.scheduledAt));
    expect(msg).not.toContain('Ahmet Kuaför');
  });
});
